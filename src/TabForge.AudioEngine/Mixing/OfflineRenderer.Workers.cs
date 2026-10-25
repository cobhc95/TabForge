using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Mixing;

// Owns: the offline render's event loading and dispatch, the worker loop and the per-block step with its transport, split from OfflineRenderer.cs.
// Does not own: the render run, progress and sink (OfflineRenderer).
public sealed partial class OfflineRenderer
{
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
            _threads.MarkOfflineWorker();
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
                // Bounded idle wait (not a spin): the mixer wakes workers per block; 20 ms bounds wake latency without burning CPU.
                if (!progressed) worker.Wake.WaitOne(20);
            }
        }
        catch (Exception ex)
        {
            _threads.Log.Write($"render worker {index} failed: {ex}");
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
        return new TransportInfo { Tempo = bpm, PpqPosition = p.Ppq + (t - p.Frame) / (double)_rate * bpm / 60.0, Playing = playing, SongSec = t / (double)_rate, Speed = 1 };
    }

    private TransportInfo TransportAt(Job job, long t, bool playing)
    {
        var tempo = _tempo;
        var i = job.TempoPos;
        while (i + 1 < tempo.Length && tempo[i + 1].Frame <= t) i++;
        job.TempoPos = i;
        var p = tempo[i];
        var bpm = p.Tempo > 0 ? p.Tempo : 120;
        return new TransportInfo { Tempo = bpm, PpqPosition = p.Ppq + (t - p.Frame) / (double)_rate * bpm / 60.0, Playing = playing, SongSec = t / (double)_rate, Speed = 1 };
    }
}
