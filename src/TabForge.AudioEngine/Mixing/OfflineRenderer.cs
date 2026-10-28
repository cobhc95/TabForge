using System.Diagnostics;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Mixing;

// Owns: faster-than-realtime render of the loaded track chains (per-chain worker queues, whole chains per worker).

/// <summary>
/// Faster-than-realtime render of the engine's loaded track chains.
/// Chains are independent offline, so each worker thread renders whole chains ahead into bounded per-chain block queues
/// (a chain always runs on the same worker, one crash breadcrumb per worker); one mixer thread pops the blocks in
/// lockstep, sums the master (fixed order: bit-identical whatever the thread count), writes master and stems in the
/// same pass and reports progress. Plug-in delay compensation: a chain with latency L is fed L frames ahead and its
/// first L output frames are discarded, so every stem lines up with the timeline. The MIDI reaches each chain exactly
/// as in playback (own events and the fan-out to routed instruments; the chain's MIDI processors run inside
/// <see cref="TrackChain.Render"/>).
/// </summary>
public sealed partial class OfflineRenderer
{
    private const int Queue = 8;
    /// <summary>Tail bound (all modes): caps render length, file size and worker time when a plug-in reports a huge tail.</summary>
    private const int MaxTailMs = 30_000;
    private const double SilenceDb = -90;

    private struct Ev { public long Frame; public byte Status, Data1, Data2; public bool Routed; }

    private sealed class Job
    {
        public required TrackChain Chain;
        public int Slot, Latency, Weight;
        public int Dest = -1;                    // group bus slot (-1: straight to the master)
        public bool InMaster;
        public string StemPath = "";
        public Ev[] Events = Array.Empty<Ev>();
        public int EventPos;
        public bool EndSent;
        public int TempoPos;
        public float[][][] Buffers = null!;   // [Queue][2][block]
        public float[] Peaks = new float[Queue];
        public float[][] Scratch = null!;
        public SemaphoreSlim Free = new(Queue), Ready = new(0);
        public int PrerollSteps;
        public long Step;                        // steps done: warm-up excluded, pre-roll then output blocks
        public RenderWavSink? Stem;
        public Audio.ClipPlayer[]? OldClipsReal;
        public (int Volume, int Pan) OldMix;
        public bool OldSilent;
        public Audio.ClipPlayer[]? OfflineClips;
    }

    private sealed class Worker
    {
        public readonly List<Job> Jobs = new();
        public readonly AutoResetEvent Wake = new(false);
        public Thread? Thread;
        public int Weight;
    }

    private readonly RenderSpec _spec;
    private readonly MixEngine _mix;
    private readonly SharedBlock _shared;
    private readonly int _rate, _block;
    private readonly Func<bool> _cancelled;
    private readonly Action<RenderProgressInfo> _progress;
    private readonly List<Job> _jobs = new();
    private readonly List<Worker> _workers = new();
    private RenderTempoPoint[] _tempo = Array.Empty<RenderTempoPoint>();
    private long _mainFrames, _hardTotal, _limitBlocks;
    private volatile bool _stop;
    private Exception? _error;
    private readonly List<IPluginInstance> _offlinePlugins = new();
    // Routing graph (see MixEngine.RenderGraph): bus chains and the master chain run on the mixer thread after the track chains.
    private readonly List<TrackChain> _buses = new();
    private TrackChain? _master;
    private readonly TrackChain?[] _peers = new TrackChain?[MixEngine.TotalSlots];
    private bool _linked;
    private int _mixTempoPos;

    private readonly EngineThreads _threads;

    public int WorkerCount => _workers.Count;

    public OfflineRenderer(RenderSpec spec, MixEngine mix, SharedBlock shared, int sampleRate, int maxBlock, EngineThreads threads, Func<bool> cancelled, Action<RenderProgressInfo> progress)
    {
        _spec = spec; _mix = mix; _shared = shared; _threads = threads; _rate = sampleRate; _cancelled = cancelled; _progress = progress;
        _block = Math.Clamp(maxBlock, 16, 512);
    }

    /// <summary>Main thread, device stopped, before <see cref="Run"/>: picks the chains, switches plug-ins to offline mode, applies mixer levels and clips. <see cref="Restore"/> undoes it (always call it).</summary>
    public void Prepare()
    {
        var seen = new HashSet<int>();
        foreach (var rs in _spec.Slots.OrderBy(s => s.Slot))
        {
            if (!seen.Add(rs.Slot)) continue;
            // The monitor chain (Monitor FX: speaker / room correction) is heard live only: a render never runs it, even when asked for its slot.
            if (rs.Slot == MixEngine.MonitorSlot) { _threads.Log.Write("render: the monitor chain is live only, skipped"); continue; }
            var chain = _mix.ChainAt(rs.Slot);
            if (chain is null) { _threads.Log.Write($"render: slot {rs.Slot} has no chain, skipped"); continue; }
            if (!rs.InMaster && string.IsNullOrEmpty(rs.StemPath)) continue;   // neither in the master nor a stem: nothing to render
            var job = new Job { Chain = chain, Slot = rs.Slot, InMaster = rs.InMaster, StemPath = rs.StemPath ?? "" };
            job.OldMix = chain.GetMix();
            job.OldSilent = chain.Silent;
            chain.Silent = false;   // the render's own rules decide: InMaster (the shared mute/solo rule) for the mix, stems ignore mute
            _jobs.Add(job);
            if (rs.Volume >= 0 || rs.Pan >= 0) chain.SetMix(rs.Volume >= 0 ? rs.Volume : job.OldMix.Volume, rs.Pan >= 0 ? rs.Pan : job.OldMix.Pan);
            // Audio clips: always the synchronous offline reader (the live players stream from the disk thread).
            var specs = rs.Clips ?? chain.Clips.Select(p => p.Spec).ToList();
            job.OldClipsReal = chain.Clips;
            job.OfflineClips = specs.Where(c => File.Exists(c.File)).Select(c => new Audio.ClipPlayer(c, _rate, _threads.Log, offline: true)).ToArray();
            chain.Clips = job.OfflineClips;
        }
        if (_jobs.Count == 0) throw new RenderException("None of the tracks to render is loaded in the audio engine.");
        // Same graph as playback: track chains (sidechain / MIDI-forward sources first), then the group buses, then the master chain.
        var graph = _mix.Graph;
        var rank = new Dictionary<int, int>();
        for (var i = 0; i < graph.Order.Length; i++) rank[graph.Order[i]] = i;
        _jobs.Sort((a, b) =>
        {
            var c = rank.GetValueOrDefault(a.Slot, int.MaxValue).CompareTo(rank.GetValueOrDefault(b.Slot, int.MaxValue));
            return c != 0 ? c : a.Slot.CompareTo(b.Slot);
        });
        foreach (var job in _jobs) { job.Dest = job.Slot < graph.Dest.Length ? graph.Dest[job.Slot] : -1; _peers[job.Slot] = job.Chain; }
        foreach (var bus in graph.Buses) if (_mix.ChainAt(bus) is { } busChain) { _buses.Add(busChain); _peers[bus] = busChain; }
        foreach (var job in _jobs) if (job.Dest >= 0 && _peers[job.Dest] is null) job.Dest = -1;
        _master = _mix.ChainAt(MixEngine.MasterSlot);
        // Chains linked by a sidechain or MIDI forward render on one worker, in graph order, one block per round.
        foreach (var job in _jobs)
            foreach (var e in job.Chain.Effects)
                if ((e.SideSlot is >= 0 and < MixEngine.MaxSlots && _peers[e.SideSlot] is not null)
                    || (e.ForwardSlot is >= 0 and < MixEngine.MaxSlots && _peers[e.ForwardSlot] is not null)) _linked = true;
        foreach (var chain in BusChains())
            foreach (var plugin in chain.Plugins)
            {
                try { plugin.SetOfflineMode(true); _offlinePlugins.Add(plugin); }
                catch (Exception ex) { _threads.Log.Write($"render: offline mode failed for {plugin.Path}: {ex.Message}"); }
            }
        foreach (var job in _jobs)
        {
            foreach (var plugin in job.Chain.Plugins)
            {
                try { plugin.SetOfflineMode(true); _offlinePlugins.Add(plugin); }
                catch (Exception ex) { _threads.Log.Write($"render: offline mode failed for {plugin.Path}: {ex.Message}"); }
            }
            if (job.Chain.MidiSynth is { } synth && !_offlinePlugins.Contains(synth)) { try { synth.SetOfflineMode(true); _offlinePlugins.Add(synth); } catch (Exception) { } // swallow: best-effort offline switch on a plug-in that may not support it
 }
        }
    }

    /// <summary>The group bus chains and the master chain that take part in this render.</summary>
    private IEnumerable<TrackChain> BusChains() => _master is null ? _buses : _buses.Append(_master);

    /// <summary>Main thread, workers stopped: plug-ins, levels and clips go back to how playback needs them.</summary>
    public void Restore()
    {
        foreach (var job in _jobs)
        {
            job.Chain.CrumbWorker = -1;
            if (job.OfflineClips is not null)
            {
                job.Chain.Clips = job.OldClipsReal ?? Array.Empty<Audio.ClipPlayer>();
                foreach (var c in job.OfflineClips) { try { c.Dispose(); } catch (Exception) { } // swallow: best-effort dispose of an offline clip
 }
                job.OfflineClips = null;
            }
            job.Chain.SetMix(job.OldMix.Volume, job.OldMix.Pan);
            job.Chain.Silent = job.OldSilent;
            job.Chain.Panic();
        }
        foreach (var chain in BusChains()) chain.Panic();
        foreach (var plugin in _offlinePlugins) { try { plugin.SetOfflineMode(false); } catch (Exception ex) { _threads.Log.Write($"render: realtime mode failed for {plugin.Path}: {ex.Message}"); } }
        _offlinePlugins.Clear();
    }

    /// <summary>Render thread: renders everything and writes the files. Throws <see cref="RenderException"/> on failure or cancel.</summary>
    public RenderResult Run()
    {
        var clock = Stopwatch.StartNew();
        var sinks = new List<RenderWavSink>();
        try
        {
            var end = _spec.EndFrame;
            var tailFrames = PlanRender(end);
            var block = _block;
            var master = OpenOutputs(sinks, block);
            StartWorkers(block, tailFrames);
            var run = new MixRun(this, master, sinks, clock, end);
            for (long b = 0; b < _limitBlocks; b++)
                if (!MixBlock(run, b)) break;
            return FinishRun(run);
        }
        catch (Exception ex)
        {
            _stop = true;
            foreach (var w in _workers) w.Wake.Set();
            foreach (var job in _jobs) { try { job.Free.Release(Queue); } catch (SemaphoreFullException) { } // swallow: the release queue is already full
 }
            JoinWorkers();
            foreach (var sink in sinks) sink.Abort();
            if (ex is RenderException) throw;
            _threads.Log.Write($"render failed: {ex}");
            throw new RenderException($"The render failed: {ex.GetBaseException().Message}");
        }
    }

    /// <summary>Plans the render: events, tempo, chain latencies and weights, and the length of the tail. Returns the tail in frames.</summary>
    private long PlanRender(long end)
    {
        _mainFrames = end - _spec.StartFrame;
        var events = LoadEvents();
        foreach (var job in _jobs) { job.Latency = job.Chain.LatencySamples; job.Weight = 1 + job.Chain.Plugins.Count * 4 + (job.Chain.MidiSynth is null ? 0 : 2) + (job.Chain.Clips.Length > 0 ? 1 : 0); }
        DispatchEvents(events, end);
        _tempo = _spec.Tempo.Count > 0 ? _spec.Tempo.OrderBy(t => t.Frame).ToArray() : new[] { new RenderTempoPoint(0, 120, 0) };
        var pluginTail = Math.Min((long)MaxTailMs * _rate / 1000, _jobs.Select(j => j.Chain).Concat(BusChains()).Max(c => (long)c.TailSamples));
        var capFrames = (long)(_spec.TailMs <= 0 || _spec.TailMs > MaxTailMs ? MaxTailMs : _spec.TailMs) * _rate / 1000;
        long tailFrames = _spec.TailMode switch
        {
            RenderTailMode.None => 0,
            RenderTailMode.Fixed => Math.Min(Math.Max((long)Math.Max(0, _spec.TailMs) * _rate / 1000, pluginTail), (long)MaxTailMs * _rate / 1000),
            _ => capFrames,
        };
        _hardTotal = _mainFrames + tailFrames;
        _limitBlocks = (_hardTotal + _block - 1) / _block;
        return tailFrames;
    }

    /// <summary>Opens the master file and the stem files; returns the master sink (null when no master is wanted).</summary>
    private RenderWavSink? OpenOutputs(List<RenderWavSink> sinks, int block)
    {
        RenderWavSink? master = null;
        if (_spec.MasterPath.Length > 0) { master = new RenderWavSink(_spec.MasterPath, -1, _rate, _spec.SampleRate, _spec.Channels, _spec.Format, block); sinks.Add(master); }
        foreach (var job in _jobs)
            if (job.StemPath.Length > 0) { job.Stem = new RenderWavSink(job.StemPath, job.Slot, _rate, _spec.SampleRate, _spec.Channels, _spec.Format, block); sinks.Add(job.Stem); }
        if (sinks.Count == 0) throw new RenderException("Nothing to write: no master and no stems.");
        return master;
    }

    /// <summary>Gives every chain its block queues, spreads the chains over the workers and starts them.</summary>
    private void StartWorkers(int block, long tailFrames)
    {
        foreach (var job in _jobs)
        {
            job.Buffers = new float[Queue][][];
            for (var q = 0; q < Queue; q++) job.Buffers[q] = new[] { new float[block], new float[block] };
            job.Scratch = new[] { new float[block], new float[block] };
            job.PrerollSteps = (job.Latency + block - 1) / block;
        }
        var workerCount = _spec.Threads == RenderThreads.One || _linked ? 1 : Math.Clamp(Environment.ProcessorCount - 1, 1, Math.Min(_jobs.Count, SharedBlock.MaxRenderWorkers));
        for (var i = 0; i < workerCount; i++) _workers.Add(new Worker());
        foreach (var job in _linked ? (IEnumerable<Job>)_jobs : _jobs.OrderByDescending(j => j.Weight))   // balanced by plug-in count; a chain never moves between workers
        {
            var w = _workers.OrderBy(x => x.Weight).First();
            w.Jobs.Add(job); w.Weight += job.Weight;
        }
        for (var i = 0; i < _workers.Count; i++)
        {
            var index = i; var worker = _workers[i];
            worker.Thread = new Thread(() => WorkerMain(index, worker)) { IsBackground = true, Name = $"TabForge render {i}" };
            worker.Thread.Start();
        }
        _threads.Log.Write($"render: {_jobs.Count} chains on {_workers.Count} workers, {_mainFrames} + {tailFrames} frames, PDC max {_jobs.Max(j => j.Latency)}");
    }

    /// <summary>The state of the mixer loop: the mix buffers, the tail and limiter bookkeeping and the progress counters.</summary>
    private sealed class MixRun
    {
        public readonly RenderWavSink? Master;
        public readonly List<RenderWavSink> Sinks;
        public readonly Stopwatch Clock;
        public readonly long End;
        public readonly int Block;
        public readonly bool Auto;
        public readonly float[] MixL, MixR, MasterInL, MasterInR;
        public readonly Dictionary<TrackChain, float[][]> BusIn = new(ReferenceEqualityComparer.Instance);
        public readonly float SilenceFloor;
        public readonly float MasterGain;
        public readonly SafetyLimiter? Limiter;
        public int LimiterSkip;
        public long Written;
        public long LastReport = Stopwatch.GetTimestamp();
        public long SilentFrames;
        public long LastAudibleEnd;
        public long FinalFrames;
        public long KeptEnd;          // input frames up to the end of the last block that was not held back (what the stems keep)
        public bool Trimmed;          // the auto tail ended the render early: the held silence was dropped

        public MixRun(OfflineRenderer owner, RenderWavSink? master, List<RenderWavSink> sinks, Stopwatch clock, long end)
        {
            Master = master; Sinks = sinks; Clock = clock; End = end;
            Block = owner._block;
            Auto = owner._spec.TailMode == RenderTailMode.Auto;
            MixL = new float[Block]; MixR = new float[Block];
            MasterInL = new float[Block]; MasterInR = new float[Block];
            foreach (var bus in owner._buses) BusIn[bus] = new[] { new float[Block], new float[Block] };
            SilenceFloor = (float)Gain.FromDb(SilenceDb);
            LastAudibleEnd = owner._mainFrames;
            FinalFrames = owner._hardTotal;
            MasterGain = owner._spec.MasterGain;
            // A7-A01: transparent master safety limiter, after the master chain and the master level (Monitor FX never render). It
            // lags by its lookahead: the first output frames (its latency) are dropped and the same number is flushed at the end,
            // so the master file lines up with the stems and keeps its length. Stems are never limited.
            Limiter = owner._spec.SafetyLimiter && master is not null ? new SafetyLimiter(owner._rate) : null;
            LimiterSkip = Limiter?.Latency ?? 0;
        }

        /// <summary>Limits MixL / MixR in place (frames) and returns how many frames of it are written (after the drop).</summary>
        public int MasterOut(int frames)
        {
            if (Limiter is null) return frames;
            Limiter.Process(MixL, MixR, frames);
            if (LimiterSkip <= 0) return frames;
            var drop = Math.Min(LimiterSkip, frames);
            LimiterSkip -= drop;
            var left = frames - drop;
            if (left > 0) { Array.Copy(MixL, drop, MixL, 0, left); Array.Copy(MixR, drop, MixR, 0, left); }
            return left;
        }
    }

    /// <summary>Mixes and writes one block. False when the render is over (no frames left, or the auto tail ended it).</summary>
    private bool MixBlock(MixRun run, long b)
    {
        var block = run.Block;
        var n = (int)Math.Min(block, _hardTotal - b * block);
        if (n <= 0) return false;
        var q = (int)(b % Queue);
        foreach (var job in _jobs) WaitReady(job);
        Array.Clear(run.MixL, 0, n); Array.Clear(run.MixR, 0, n);
        foreach (var bufs in run.BusIn.Values) { Array.Clear(bufs[0], 0, n); Array.Clear(bufs[1], 0, n); }
        var blockPeak = SumJobs(run, q, n);
        blockPeak = RenderBusesAndMaster(run, b, n, blockPeak);
        var masterGain = run.MasterGain;
        if (masterGain != 1f) for (var i = 0; i < n; i++) { run.MixL[i] *= masterGain; run.MixR[i] *= masterGain; }

        // Auto tail: silent blocks past the end are held back; 1 s of them ends the render there.
        var inTail = run.Auto && b * block >= _mainFrames;
        var hold = inTail && blockPeak < run.SilenceFloor;
        if (inTail)
        {
            if (hold) run.SilentFrames += n; else { run.SilentFrames = 0; run.LastAudibleEnd = b * block + n; }
        }
        if (run.Master is not null) Put(run.Master, run.MixL, run.MixR, run.MasterOut(n), hold);
        foreach (var job in _jobs) if (job.Stem is { } stem) Put(stem, job.Buffers[q][0], job.Buffers[q][1], n, hold);
        run.Written = b * block + n;
        if (!hold) run.KeptEnd = run.Written;
        foreach (var job in _jobs) job.Free.Release();
        foreach (var w in _workers) w.Wake.Set();
        if (inTail && run.SilentFrames >= _rate)
        {
            EndRenderInTail(run);
            return false;
        }
        if (_spec.RealtimePace)
        {
            var due = run.Written * 1000.0 / _rate;
            while (run.Clock.Elapsed.TotalMilliseconds < due && !_cancelled()) Thread.Sleep(1);
        }
        if (_cancelled()) throw new RenderException("Render cancelled.", cancelled: true);
        if (Stopwatch.GetElapsedTime(run.LastReport).TotalMilliseconds >= 100)
        {
            run.LastReport = Stopwatch.GetTimestamp();
            ReportProgress(run.Written, run.Auto ? _mainFrames : _hardTotal, run.Clock);
        }
        run.FinalFrames = run.Written;
        return true;
    }

    /// <summary>Adds every chain block to the mix (or to its group bus) and returns the peak of the block.</summary>
    private float SumJobs(MixRun run, int q, int n)
    {
        var blockPeak = 0f;
        foreach (var job in _jobs)
        {
            var l = job.Buffers[q][0]; var r = job.Buffers[q][1];
            // Stems are these buffers (post-fader, pre-bus); the master mix goes through the group bus first.
            var (dl, dr) = job.Dest >= 0 && _peers[job.Dest] is { } busChain ? (run.BusIn[busChain][0], run.BusIn[busChain][1]) : (run.MixL, run.MixR);
            if (job.InMaster) for (var i = 0; i < n; i++) { dl[i] += l[i]; dr[i] += r[i]; }
            blockPeak = MathF.Max(blockPeak, job.Peaks[q]);
        }
        return blockPeak;
    }

    /// <summary>Runs the group buses and the master chain over the mix; returns the block peak including their tails.</summary>
    private float RenderBusesAndMaster(MixRun run, long b, int n, float blockPeak)
    {
        if (_buses.Count == 0 && _master is null) return blockPeak;
        var mixL = run.MixL; var mixR = run.MixR;
        var t0 = _spec.StartFrame + b * run.Block;
        var transport = MixTransport(t0, playing: t0 < run.End);
        foreach (var bus in _buses) bus.Render(mixL, mixR, 0, n, in transport, _shared, null, null, run.BusIn[bus][0], run.BusIn[bus][1]);
        if (_master is { } masterChain)
        {
            Array.Copy(mixL, run.MasterInL, n); Array.Copy(mixR, run.MasterInR, n);
            Array.Clear(mixL, 0, n); Array.Clear(mixR, 0, n);
            masterChain.Render(mixL, mixR, 0, n, in transport, _shared, null, null, run.MasterInL, run.MasterInR);
        }
        for (var i = 0; i < n; i++) blockPeak = MathF.Max(blockPeak, MathF.Max(MathF.Abs(mixL[i]), MathF.Abs(mixR[i])));   // bus / master tails keep the auto tail going
        return blockPeak;
    }

    /// <summary>One second of silence past the end: the render ends there and the held silence is dropped.</summary>
    private void EndRenderInTail(MixRun run)
    {
        run.FinalFrames = Math.Max(_mainFrames, run.LastAudibleEnd);
        foreach (var sink in run.Sinks)
        {
            // The limited master lags by its lookahead: the start of its held output still carries the last audible
            // frames. Keep exactly enough of it to end where the stems end; the rest (and the delay of the limiter) is silence.
            if (run.Limiter is not null && ReferenceEquals(sink, run.Master)) sink.FlushHeldFirst(run.KeptEnd - sink.WrittenFrames);
            else sink.DropHeld();
        }
        run.Trimmed = true;
    }

    /// <summary>Flushes what was held and the delay of the limiter, stops the workers, finishes the files and reports.</summary>
    private RenderResult FinishRun(MixRun run)
    {
        foreach (var sink in run.Sinks) sink.FlushHeld();   // the cap was reached inside the tail: what was held is still part of the file
        if (run.Limiter is not null && run.Master is not null && !run.Trimmed)
        {
            // The last Latency frames are still inside the delay of the limiter: push silence through and write them.
            for (var left = run.Limiter.Latency; left > 0;)
            {
                var chunk = Math.Min(run.Block, left);
                Array.Clear(run.MixL, 0, chunk); Array.Clear(run.MixR, 0, chunk);
                var written2 = run.MasterOut(chunk);
                if (written2 > 0) run.Master.Write(run.MixL, run.MixR, written2);
                left -= chunk;
            }
        }
        _stop = true;
        foreach (var w in _workers) w.Wake.Set();
        foreach (var job in _jobs) { job.Free.Release(Queue); }
        JoinWorkers();
        if (_error is not null) throw _error;
        var finalFrames = run.FinalFrames;
        var files = new List<RenderFileResult>();
        foreach (var sink in run.Sinks) files.Add(sink.Finish(finalFrames));
        run.Sinks.Clear();
        ReportProgress(finalFrames, finalFrames, run.Clock, done: true);
        return new RenderResult(finalFrames, finalFrames / (double)_rate, run.Clock.Elapsed.TotalSeconds, _workers.Count, files);
    }

    private static void Put(RenderWavSink sink, float[] l, float[] r, int n, bool hold)
    {
        if (hold) sink.Hold(l, r, n);
        else { sink.FlushHeld(); sink.Write(l, r, n); }
    }

    private void ReportProgress(long written, long total, Stopwatch clock, bool done = false)
    {
        var seconds = written / (double)_rate;
        var elapsed = Math.Max(0.001, clock.Elapsed.TotalSeconds);
        var fraction = done ? 1 : Math.Min(0.995, written / (double)Math.Max(1, total));
        _progress(new RenderProgressInfo(fraction, seconds, seconds / elapsed));
    }

    private void JoinWorkers()
    {
        foreach (var w in _workers) if (w.Thread is { } t && !t.Join(10_000)) _threads.Log.Write("render: a worker did not stop");
    }

    /// <summary>Mixer: waits for a chain's next block. 30 s without one = a plug-in hung; end the engine (TabForge quarantines it from the breadcrumb and restarts), as the audio watchdog does.</summary>
    private void WaitReady(Job job)
    {
        var waited = Stopwatch.StartNew();
        while (!job.Ready.Wait(50))
        {
            if (_error is not null) throw _error;
            if (_cancelled()) throw new RenderException("Render cancelled.", cancelled: true);
            if (waited.Elapsed.TotalSeconds > 30)
            {
                _threads.Log.Write($"render: no block from slot {job.Slot} for 30 s; ending the engine so TabForge can recover");
                EngineHost.HardExit(70);
            }
        }
    }
}
