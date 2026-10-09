using System.Diagnostics;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Mixing;

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
public sealed class OfflineRenderer
{
    private const int Queue = 8;
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

    public int WorkerCount => _workers.Count;

    public OfflineRenderer(RenderSpec spec, MixEngine mix, SharedBlock shared, int sampleRate, int maxBlock, Func<bool> cancelled, Action<RenderProgressInfo> progress)
    {
        _spec = spec; _mix = mix; _shared = shared; _rate = sampleRate; _cancelled = cancelled; _progress = progress;
        _block = Math.Clamp(maxBlock, 16, 512);
    }

    /// <summary>Main thread, device stopped, before <see cref="Run"/>: picks the chains, switches plug-ins to offline mode, applies mixer levels and clips. <see cref="Restore"/> undoes it (always call it).</summary>
    public void Prepare()
    {
        var seen = new HashSet<int>();
        foreach (var rs in _spec.Slots.OrderBy(s => s.Slot))
        {
            if (!seen.Add(rs.Slot)) continue;
            var chain = _mix.ChainAt(rs.Slot);
            if (chain is null) { EngineLog.Write($"render: slot {rs.Slot} has no chain, skipped"); continue; }
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
            job.OfflineClips = specs.Where(c => File.Exists(c.File)).Select(c => new Audio.ClipPlayer(c, _rate, offline: true)).ToArray();
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
                catch (Exception ex) { EngineLog.Write($"render: offline mode failed for {plugin.Path}: {ex.Message}"); }
            }
        foreach (var job in _jobs)
        {
            foreach (var plugin in job.Chain.Plugins)
            {
                try { plugin.SetOfflineMode(true); _offlinePlugins.Add(plugin); }
                catch (Exception ex) { EngineLog.Write($"render: offline mode failed for {plugin.Path}: {ex.Message}"); }
            }
            if (job.Chain.MidiSynth is { } synth && !_offlinePlugins.Contains(synth)) { try { synth.SetOfflineMode(true); _offlinePlugins.Add(synth); } catch (Exception) { } }
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
                foreach (var c in job.OfflineClips) { try { c.Dispose(); } catch (Exception) { } }
                job.OfflineClips = null;
            }
            job.Chain.SetMix(job.OldMix.Volume, job.OldMix.Pan);
            job.Chain.Silent = job.OldSilent;
            job.Chain.Panic();
        }
        foreach (var chain in BusChains()) chain.Panic();
        foreach (var plugin in _offlinePlugins) { try { plugin.SetOfflineMode(false); } catch (Exception ex) { EngineLog.Write($"render: realtime mode failed for {plugin.Path}: {ex.Message}"); } }
        _offlinePlugins.Clear();
    }

    /// <summary>Render thread: renders everything and writes the files. Throws <see cref="RenderException"/> on failure or cancel.</summary>
    public RenderResult Run()
    {
        var clock = Stopwatch.StartNew();
        var sinks = new List<RenderWavSink>();
        RenderWavSink? master = null;
        try
        {
            // ---- plan
            var end = _spec.EndFrame;
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
            var block = _block;
            _limitBlocks = (_hardTotal + block - 1) / block;
            var auto = _spec.TailMode == RenderTailMode.Auto;

            // ---- outputs
            if (_spec.MasterPath.Length > 0) { master = new RenderWavSink(_spec.MasterPath, -1, _rate, _spec.SampleRate, _spec.Channels, _spec.Format, block); sinks.Add(master); }
            foreach (var job in _jobs)
                if (job.StemPath.Length > 0) { job.Stem = new RenderWavSink(job.StemPath, job.Slot, _rate, _spec.SampleRate, _spec.Channels, _spec.Format, block); sinks.Add(job.Stem); }
            if (sinks.Count == 0) throw new RenderException("Nothing to write: no master and no stems.");

            // ---- workers
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
            EngineLog.Write($"render: {_jobs.Count} chains on {_workers.Count} workers, {_mainFrames} + {tailFrames} frames, PDC max {_jobs.Max(j => j.Latency)}");

            // ---- mixer
            var mixL = new float[block]; var mixR = new float[block];
            var masterInL = new float[block]; var masterInR = new float[block];
            var busIn = new Dictionary<TrackChain, float[][]>(ReferenceEqualityComparer.Instance);
            foreach (var bus in _buses) busIn[bus] = new[] { new float[block], new float[block] };
            long written = 0; var lastReport = Stopwatch.GetTimestamp();
            var silenceFloor = (float)Gain.FromDb(SilenceDb);
            long silentFrames = 0, lastAudibleEnd = _mainFrames;
            var finalFrames = _hardTotal;
            var masterGain = _spec.MasterGain;
            // A7-A01: transparent master safety limiter, after the master chain and the master level (Monitor FX never render). It
            // lags by its lookahead: the first `limiterSkip` output frames are dropped and the same number is flushed at the end,
            // so the master file lines up with the stems and keeps its length. Stems are never limited.
            var limiter = _spec.SafetyLimiter && master is not null ? new SafetyLimiter(_rate) : null;
            var limiterSkip = limiter?.Latency ?? 0;
            long keptEnd = 0;          // input frames up to the end of the last block that was not held back (what the stems keep)
            var trimmed = false;       // the auto tail ended the render early: the held silence was dropped
            // Limits mixL / mixR in place (n frames) and returns how many frames of it are written (after the drop).
            int MasterOut(int frames)
            {
                if (limiter is null) return frames;
                limiter.Process(mixL, mixR, frames);
                if (limiterSkip <= 0) return frames;
                var drop = Math.Min(limiterSkip, frames);
                limiterSkip -= drop;
                var left = frames - drop;
                if (left > 0) { Array.Copy(mixL, drop, mixL, 0, left); Array.Copy(mixR, drop, mixR, 0, left); }
                return left;
            }
            for (long b = 0; b < _limitBlocks; b++)
            {
                var n = (int)Math.Min(block, _hardTotal - b * block);
                if (n <= 0) break;
                var q = (int)(b % Queue);
                foreach (var job in _jobs) WaitReady(job);
                Array.Clear(mixL, 0, n); Array.Clear(mixR, 0, n);
                var blockPeak = 0f;
                foreach (var bufs in busIn.Values) { Array.Clear(bufs[0], 0, n); Array.Clear(bufs[1], 0, n); }
                foreach (var job in _jobs)
                {
                    var l = job.Buffers[q][0]; var r = job.Buffers[q][1];
                    // Stems are these buffers (post-fader, pre-bus); the master mix goes through the group bus first.
                    var (dl, dr) = job.Dest >= 0 && _peers[job.Dest] is { } busChain ? (busIn[busChain][0], busIn[busChain][1]) : (mixL, mixR);
                    if (job.InMaster) for (var i = 0; i < n; i++) { dl[i] += l[i]; dr[i] += r[i]; }
                    blockPeak = MathF.Max(blockPeak, job.Peaks[q]);
                }
                if (_buses.Count > 0 || _master is not null)
                {
                    var t0 = _spec.StartFrame + b * block;
                    var transport = MixTransport(t0, playing: t0 < end);
                    foreach (var bus in _buses) bus.Render(mixL, mixR, 0, n, in transport, _shared, null, null, busIn[bus][0], busIn[bus][1]);
                    if (_master is { } masterChain)
                    {
                        Array.Copy(mixL, masterInL, n); Array.Copy(mixR, masterInR, n);
                        Array.Clear(mixL, 0, n); Array.Clear(mixR, 0, n);
                        masterChain.Render(mixL, mixR, 0, n, in transport, _shared, null, null, masterInL, masterInR);
                    }
                    for (var i = 0; i < n; i++) blockPeak = MathF.Max(blockPeak, MathF.Max(MathF.Abs(mixL[i]), MathF.Abs(mixR[i])));   // bus / master tails keep the auto tail going
                }
                if (masterGain != 1f) for (var i = 0; i < n; i++) { mixL[i] *= masterGain; mixR[i] *= masterGain; }

                // Auto tail: silent blocks past the end are held back; 1 s of them ends the render there.
                var inTail = auto && b * block >= _mainFrames;
                var hold = inTail && blockPeak < silenceFloor;
                if (inTail)
                {
                    if (hold) silentFrames += n; else { silentFrames = 0; lastAudibleEnd = b * block + n; }
                }
                if (master is not null) Put(master, mixL, mixR, MasterOut(n), hold);
                foreach (var job in _jobs) if (job.Stem is { } stem) Put(stem, job.Buffers[q][0], job.Buffers[q][1], n, hold);
                written = b * block + n;
                if (!hold) keptEnd = written;
                foreach (var job in _jobs) job.Free.Release();
                foreach (var w in _workers) w.Wake.Set();
                if (inTail && silentFrames >= _rate)
                {
                    finalFrames = Math.Max(_mainFrames, lastAudibleEnd);
                    foreach (var sink in sinks)
                    {
                        // The limited master lags by its lookahead: the start of its held output still carries the last audible
                        // frames. Keep exactly enough of it to end where the stems end; the rest (and the limiter's delay) is silence.
                        if (limiter is not null && ReferenceEquals(sink, master)) sink.FlushHeldFirst(keptEnd - sink.WrittenFrames);
                        else sink.DropHeld();
                    }
                    trimmed = true;
                    break;
                }
                if (_spec.RealtimePace)
                {
                    var due = written * 1000.0 / _rate;
                    while (clock.Elapsed.TotalMilliseconds < due && !_cancelled()) Thread.Sleep(1);
                }
                if (_cancelled()) throw new RenderException("Render cancelled.", cancelled: true);
                if (Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds >= 100)
                {
                    lastReport = Stopwatch.GetTimestamp();
                    ReportProgress(written, auto ? _mainFrames : _hardTotal, clock);
                }
                finalFrames = written;
            }
            foreach (var sink in sinks) sink.FlushHeld();   // the cap was reached inside the tail: what was held is still part of the file
            if (limiter is not null && master is not null && !trimmed)
            {
                // The last `Latency` frames are still inside the limiter's delay: push silence through and write them.
                for (var left = limiter.Latency; left > 0;)
                {
                    var chunk = Math.Min(block, left);
                    Array.Clear(mixL, 0, chunk); Array.Clear(mixR, 0, chunk);
                    var written2 = MasterOut(chunk);
                    if (written2 > 0) master.Write(mixL, mixR, written2);
                    left -= chunk;
                }
            }
            _stop = true;
            foreach (var w in _workers) w.Wake.Set();
            foreach (var job in _jobs) { job.Free.Release(Queue); }
            JoinWorkers();
            if (_error is not null) throw _error;
            var files = new List<RenderFileResult>();
            foreach (var sink in sinks) files.Add(sink.Finish(finalFrames));
            sinks.Clear();
            ReportProgress(finalFrames, finalFrames, clock, done: true);
            return new RenderResult(finalFrames, finalFrames / (double)_rate, clock.Elapsed.TotalSeconds, _workers.Count, files);
        }
        catch (Exception ex)
        {
            _stop = true;
            foreach (var w in _workers) w.Wake.Set();
            foreach (var job in _jobs) { try { job.Free.Release(Queue); } catch (SemaphoreFullException) { } }
            JoinWorkers();
            foreach (var sink in sinks) sink.Abort();
            if (ex is RenderException) throw;
            EngineLog.Write($"render failed: {ex}");
            throw new RenderException($"The render failed: {ex.GetBaseException().Message}");
        }
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
        foreach (var w in _workers) if (w.Thread is { } t && !t.Join(10_000)) EngineLog.Write("render: a worker did not stop");
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
                EngineLog.Write($"render: no block from slot {job.Slot} for 30 s; ending the engine so TabForge can recover");
                EngineHost.HardExit(70);
            }
        }
    }

    private RenderEvent[] LoadEvents()
    {
        var events = RenderEventFile.Read(_spec.EventFile);
        var sorted = true;
        for (var i = 1; i < events.Length; i++) if (events[i].Frame < events[i - 1].Frame) { sorted = false; break; }
        if (sorted) return events;
        var order = Enumerable.Range(0, events.Length).ToArray();
        Array.Sort(order, (a, b) => events[a].Frame != events[b].Frame ? events[a].Frame.CompareTo(events[b].Frame) : a.CompareTo(b));
        return order.Select(i => events[i]).ToArray();
    }

    /// <summary>Splits the timeline into per-chain event lists with the playback's own rules: a track's MIDI goes to its chain, and to every instrument routed to take it.</summary>
    private void DispatchEvents(RenderEvent[] events, long end)
    {
        var fanout = _mix.Fanout;
        var bySlot = _jobs.ToDictionary(j => j.Slot);
        var lists = _jobs.ToDictionary(j => j.Slot, _ => new List<Ev>());
        foreach (var e in events)
        {
            if (e.Frame >= end || e.Slot is < 0 or >= MixEngine.MaxSlots) continue;
            if (lists.TryGetValue(e.Slot, out var own)) own.Add(new Ev { Frame = e.Frame, Status = e.Status, Data1 = e.Data1, Data2 = e.Data2 });
            if (fanout[e.Slot] is { } destinations)
                foreach (var d in destinations)
                    if (lists.TryGetValue(d, out var routed)) routed.Add(new Ev { Frame = e.Frame, Status = e.Status, Data1 = e.Data1, Data2 = e.Data2, Routed = true });
        }
        foreach (var (slot, list) in lists) bySlot[slot].Events = list.ToArray();
    }

    private void WorkerMain(int index, Worker worker)
    {
        try
        {
            EngineThreads.MarkOfflineWorker();
            foreach (var job in worker.Jobs) job.Chain.CrumbWorker = index;
            // Panic + one silent block first: clears whatever the last playback left (the panic drops events queued in the same block).
            foreach (var job in worker.Jobs) { job.Chain.Panic(); Step(job, job.Scratch, _block, _spec.StartFrame, deliver: false); }
            while (!_stop)
            {
                var active = false; var progressed = false;
                // Linked chains: output steps advance together, one block per round in graph order, so a sidechain / MIDI-forward
                // source's block is always rendered before its destination's (after every chain's pre-roll).
                var roundReady = true;
                if (_linked)
                    foreach (var job in worker.Jobs)
                        if (job.Step < job.PrerollSteps || (job.Step - job.PrerollSteps < Volatile.Read(ref _limitBlocks) && job.Free.CurrentCount == 0)) { roundReady = false; break; }
                foreach (var job in worker.Jobs)
                {
                    if (job.Step < job.PrerollSteps)
                    {
                        active = true; progressed = true;
                        var t0 = _spec.StartFrame + job.Step * _block;
                        Step(job, job.Scratch, (int)Math.Min(_block, job.Latency - job.Step * _block), t0, deliver: true);
                        job.Step++;
                        continue;
                    }
                    var outBlock = job.Step - job.PrerollSteps;
                    if (outBlock >= Volatile.Read(ref _limitBlocks)) continue;
                    active = true;
                    if (_linked && !roundReady) continue;
                    if (!job.Free.Wait(0)) continue;
                    var q = (int)(outBlock % Queue);
                    Step(job, job.Buffers[q], _block, _spec.StartFrame + job.Latency + outBlock * _block, deliver: true);
                    job.Peaks[q] = BlockPeak(job.Buffers[q], _block);
                    job.Ready.Release();
                    job.Step++;
                    progressed = true;
                }
                if (!active) break;
                if (!progressed) worker.Wake.WaitOne(20);
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"render worker {index} failed: {ex}");
            Interlocked.CompareExchange(ref _error, new RenderException($"A track failed while rendering: {ex.GetBaseException().Message}"), null);
            _stop = true;
        }
    }

    private static float BlockPeak(float[][] buffer, int n)
    {
        var peak = 0f;
        for (var c = 0; c < 2; c++) { var b = buffer[c]; for (var i = 0; i < n; i++) { var a = MathF.Abs(b[i]); if (a > peak) peak = a; } }
        return peak;
    }

    /// <summary>Renders one chain block at chain time <paramref name="t0"/> (frames on the song timeline, already advanced by the chain's latency).</summary>
    private void Step(Job job, float[][] dest, int n, long t0, bool deliver)
    {
        var chain = job.Chain;
        Array.Clear(dest[0], 0, _block); Array.Clear(dest[1], 0, _block);
        var stepEnd = t0 + n;
        var end = _spec.EndFrame;
        if (deliver)
        {
            var ev = job.Events;
            while (job.EventPos < ev.Length && ev[job.EventPos].Frame < stepEnd)
            {
                ref var e = ref ev[job.EventPos++];
                var frame = (int)Math.Clamp(e.Frame - t0, 0, n - 1);
                if (e.Routed) chain.AddRouted(frame, e.Status, e.Data1, e.Data2); else chain.AddEvent(frame, e.Status, e.Data1, e.Data2);
            }
            if (!job.EndSent && end < stepEnd)
            {
                job.EndSent = true;   // notes off at the end bound; what they leave ringing is the tail
                var frame = (int)Math.Clamp(end - t0, 0, n - 1);
                for (var ch = 0; ch < 16; ch++) chain.AddEvent(frame, (byte)(0xB0 | ch), 123, 0);
            }
        }
        var transport = TransportAt(job, t0, playing: deliver && t0 < end);
        chain.Render(dest[0], dest[1], 0, n, in transport, _shared, null, _peers);
        chain.DeliverForwards(_peers);   // linked chains only (one worker); a forward to a chain not rendered is dropped
    }

    /// <summary>Mixer thread: transport for the bus / master chains at timeline frame <paramref name="t"/>.</summary>
    private TransportInfo MixTransport(long t, bool playing)
    {
        var tempo = _tempo;
        while (_mixTempoPos + 1 < tempo.Length && tempo[_mixTempoPos + 1].Frame <= t) _mixTempoPos++;
        var p = tempo[_mixTempoPos];
        var bpm = p.Tempo > 0 ? p.Tempo : 120;
        return new TransportInfo { Tempo = bpm, PpqPosition = p.Ppq + (t - p.Frame) / (double)_rate * bpm / 60.0, Playing = playing, SongSec = t / (double)_rate };
    }

    private TransportInfo TransportAt(Job job, long t, bool playing)
    {
        var tempo = _tempo;
        var i = job.TempoPos;
        while (i + 1 < tempo.Length && tempo[i + 1].Frame <= t) i++;
        job.TempoPos = i;
        var p = tempo[i];
        var bpm = p.Tempo > 0 ? p.Tempo : 120;
        return new TransportInfo { Tempo = bpm, PpqPosition = p.Ppq + (t - p.Frame) / (double)_rate * bpm / 60.0, Playing = playing, SongSec = t / (double)_rate };
    }
}
