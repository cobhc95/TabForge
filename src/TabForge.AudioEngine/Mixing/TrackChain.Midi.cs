using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Mixing;

// Owns: a track chain's MIDI input side: the route, the routed, forwarded and own events, their ordering and merge, the MIDI processors stage and the panic, split from TrackChain.cs.
// Does not own: the audio render or the plug-in list (TrackChain).
public sealed partial class TrackChain : IDisposable
{
    /// <summary>Audio thread: MIDI from another track that routes to this chain's instrument (already known to come from the chosen source).</summary>
    public void AddRouted(int frame, byte status, byte data1, byte data2)
    {
        var route = _route;
        if ((route >> 16) - 2 < 0) return;   // this chain does not take another track's MIDI
        AddInstrument(route, frame, status, data1, data2);
    }

    private void AddInstrument(int route, int frame, byte status, byte data1, byte data2)
    {
        if (status < 0xF0 && (route & (1 << (status & 0x0F))) == 0) return;   // channel filter (system messages always pass)
        if (_instCount < _instEvents.Length) _instEvents[_instCount++] = new BlockMidi { Frame = frame, Status = status, Data1 = data1, Data2 = data2 };
        else Interlocked.Increment(ref MidiDropped);
    }

    /// <summary>Audio thread: queues a MIDI message for the next <see cref="Render"/>.</summary>
    public void AddEvent(int frame, byte status, byte data1, byte data2)
    {
        var kind = status & 0xF0;
        // The General MIDI synth handles volume / pan itself, exactly like the Windows synth; a VST instrument gets
        // them as the chain's level and pan instead (many instruments ignore CC7 / CC10).
        if (kind == 0xB0 && data1 == 7) { _volume = data2 / 127f; return; }
        if (kind == 0xB0 && data1 == 10) { _pan = data2 / 127f; return; }
        if (_eventCount < _events.Length) _events[_eventCount++] = new BlockMidi { Frame = frame, Status = status, Data1 = data1, Data2 = data2 };
        else Interlocked.Increment(ref MidiDropped);
        var route = _route;
        if ((route >> 16) == 1) AddInstrument(route, frame, status, data1, data2);   // own MIDI
    }

    /// <summary>Audio thread: MIDI forwarded from another chain's plug-in output (delivered for this block; CC7 / CC10 are not taken as the track level).</summary>
    internal void AddForwarded(int frame, byte status, byte data1, byte data2)
    {
        if (_eventCount < _events.Length) _events[_eventCount++] = new BlockMidi { Frame = frame, Status = status, Data1 = data1, Data2 = data2 };
        else Interlocked.Increment(ref MidiDropped);
        var route = _route;
        if ((route >> 16) == 1) AddInstrument(route, frame, status, data1, data2);
        _unsorted = true;
    }

    /// <summary>Audio thread, after every chain rendered: hands captured plug-in MIDI output to the destination chains (heard next block).</summary>
    public void DeliverForwards(TrackChain?[] chains)
    {
        foreach (var e in _effects)
        {
            var count = e.FwdCount;
            if (count == 0) continue;
            e.FwdCount = 0;
            var dest = e.ForwardSlot;
            if (dest < 0 || dest >= chains.Length || chains[dest] is not { } target || ReferenceEquals(target, this)) continue;
            for (var i = 0; i < count; i++) target.AddForwarded(e.Fwd[i].Frame, e.Fwd[i].Status, e.Fwd[i].Data1, e.Fwd[i].Data2);
        }
    }

    /// <summary>Stable insertion sort by frame (small lists, no allocation).</summary>
    private static void SortByFrame(BlockMidi[] items, int count)
    {
        for (var i = 1; i < count; i++)
        {
            var x = items[i]; var j = i - 1;
            while (j >= 0 && items[j].Frame > x.Frame) { items[j + 1] = items[j]; j--; }
            items[j + 1] = x;
        }
    }

    /// <summary>
    /// Audio thread: runs one stage's MIDI processors on the current stream (_cur), in place. A bypassed stage runs none, and a
    /// processor list that goes away (bypass, removed) first releases every note it let through, so nothing hangs.
    /// Returns true when the stream went through processors (or released notes).
    /// </summary>
    private bool RunProcessors(Effect e, int frames, in TransportInfo transport)
    {
        var wanted = e.Bypass || e.Misbehaved ? null : e.Processors;
        if (wanted is null && e.ProcActive is null) return false;
        _procOut.Clear();
        if (!ReferenceEquals(wanted, e.ProcActive))
        {
            if (wanted is null) e.ProcActive!.ReleaseAll(_procOut); else wanted.TakeOver(e.ProcActive, _procOut);
            e.ProcActive = wanted;
        }
        var input = new ReadOnlySpan<BlockMidi>(_cur, 0, _curCount);
        if (e.ProcActive is not null) e.ProcActive.Process(input, _procOut, frames, transport, MidiLog, _a[0], _a[1]);
        else foreach (var m in input) _procOut.Add(m);
        if (_procOut.Unsorted) _procOut.Sort();
        _curCount = Math.Min(_procOut.Count, _cur.Length);
        _procOut.Span[.._curCount].CopyTo(_cur);
        return true;
    }

    /// <summary>
    /// Audio thread, no allocation: the block's queue after a panic = all notes off + all sound off on every channel at frame 0, then the
    /// program changes, controllers and pitch bends that were queued (notes and other messages are dropped).
    /// </summary>
    internal static void PanicKeepingState(BlockMidi[] buffer, ref int count)
    {
        var kept = 0;
        for (var i = 0; i < count; i++)
        {
            var e = buffer[i];
            var kind = e.Status & 0xF0;
            if (kind == 0xC0 || kind == 0xE0 || (kind == 0xB0 && e.Data1 < 120)) buffer[kept++] = e;
        }
        const int PanicEvents = 32;
        if (buffer.Length < PanicEvents) { count = 0; return; }
        if (kept > buffer.Length - PanicEvents) kept = buffer.Length - PanicEvents;
        Array.Copy(buffer, 0, buffer, PanicEvents, kept);   // overlapping copy is handled
        for (var ch = 0; ch < 16; ch++)
        {
            buffer[2 * ch] = new BlockMidi { Frame = 0, Status = (byte)(0xB0 | ch), Data1 = 123 };
            buffer[2 * ch + 1] = new BlockMidi { Frame = 0, Status = (byte)(0xB0 | ch), Data1 = 120 };
        }
        count = kept + PanicEvents;
    }

    /// <summary>Any thread: silence every note at the next block.</summary>
    public void Panic() => _panic = true;

    /// <summary>Audio thread: silence now, dropping the notes this block holds so far (events added after this call are kept).</summary>
    public void PanicNow()
    {
        _panic = false;
        _unsorted = false;
        foreach (var e in _effects) e.AutoPitch.Reset();
        foreach (var e in _effects) e.ProcActive?.Reset();   // held notes and delayed events are gone with the panic
        // The panic silences notes but must not eat channel state: a song start is "reset, then program / volume / bend setup", and the
        // setup travels by the shared ring, so the panic can land in the block that already holds the setup. Dropping it left the synth on
        // piano (GM program 0) until the setup was sent again (the first cold play).
        PanicKeepingState(_events, ref _eventCount);
        PanicKeepingState(_instEvents, ref _instCount);
    }

    /// <summary>Merges two frame-ordered event lists into <paramref name="dst"/> (first list wins ties); returns the count. No allocation.</summary>
    private static int MergeMidi(BlockMidi[] dst, ReadOnlySpan<BlockMidi> first, ReadOnlySpan<BlockMidi> second)
    {
        int i = 0, j = 0, n = 0;
        while (n < dst.Length && (i < first.Length || j < second.Length))
            dst[n++] = j >= second.Length || (i < first.Length && first[i].Frame <= second[j].Frame) ? first[i++] : second[j++];
        return n;
    }
}
