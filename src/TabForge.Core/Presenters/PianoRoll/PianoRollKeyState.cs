namespace TabForge.Presenters;

// Owns: which keys a MIDI keyboard holds down right now, for the piano roll's live key highlight: note-on presses a key, note-off (or a note-on
//       with velocity 0) releases it, All notes off and All sound off release every key. Pressed twice (two devices) a key needs two releases.
//       Also which pitches the track's notes sound at a playhead beat. Messages arrive on the MIDI driver thread; reads are on the UI thread.
// Does not own: the MIDI input (MidiInputHub, the app's PianoRollView) or the drawing (the app's PianoRollPlayheadLayer).
// Tests: TabForge.Avalonia.Tests.PianoRollKeyStateTests.
public sealed class PianoRollKeyState
{
    private const int AllSoundOff = 120, AllNotesOff = 123;
    private readonly object _gate = new();
    private readonly int[] _held = new int[128];   // presses per pitch; ceiling 128 entries; guarded by _gate

    /// <summary>Applies one MIDI message; true when a key changed (the caller repaints once for many).</summary>
    public bool Message(int status, int data1, int data2)
    {
        var pitch = data1 & 0x7F;
        switch (status & 0xF0)
        {
            case 0x90 when data2 > 0: return Press(pitch);
            case 0x80:
            case 0x90: return Release(pitch);
            case 0xB0 when data1 is AllSoundOff or AllNotesOff: return Clear();
            default: return false;
        }
    }

    public bool Press(int pitch)
    {
        lock (_gate) { _held[pitch & 0x7F]++; }
        return true;
    }

    public bool Release(int pitch)
    {
        lock (_gate)
        {
            if (_held[pitch & 0x7F] == 0) return false;
            _held[pitch & 0x7F]--;
            return true;
        }
    }

    /// <summary>Releases every key (the input closed, All notes off); true when one was down.</summary>
    public bool Clear()
    {
        lock (_gate)
        {
            var any = _held.Any(h => h > 0);
            Array.Clear(_held);
            return any;
        }
    }

    public bool IsDown(int pitch)
    {
        lock (_gate) { return _held[pitch & 0x7F] > 0; }
    }

    /// <summary>Fills <paramref name="down"/> (128 entries) with the held keys, for one paint.</summary>
    public void CopyTo(bool[] down)
    {
        lock (_gate) { for (var i = 0; i < 128 && i < down.Length; i++) down[i] = _held[i] > 0; }
    }

    /// <summary>Marks in <paramref name="sounding"/> (128 entries) the pitches of the unmuted notes that sound at <paramref name="beat"/>.</summary>
    public static void Sounding(IReadOnlyList<PianoRollNote> notes, double beat, bool[] sounding)
    {
        Array.Clear(sounding);
        foreach (var note in notes)
            if (!note.Muted && note.Start <= beat && note.End > beat) sounding[PianoRollEdits.ClampPitch(note.Pitch)] = true;
    }
}
