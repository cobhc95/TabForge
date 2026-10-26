using TabForge.Models;

namespace TabForge.Features.Recording.Rules;

// Owns: the MIDI one armed track receives during one recording: its passes (one per loop lap), its held notes, and the MIDI clips they
//   become when the recording stops.
// Does not own: where the messages come from or what song time they were heard at (the recording controller of each app), placing the
//   clips on lanes (RecordedTakes), the undo step.
// Tests: TabForge.Avalonia.Tests.MidiTakeTests, CountInRecordingTests.
public sealed class MidiTake
{
    /// <summary>Each pass: its song start and end in seconds and its notes (start relative to the pass start).</summary>
    public List<(double Start, double End, List<ClipNote> Notes)> Passes { get; } = new();
    /// <summary>Notes held now, by pitch: when they began (song seconds) and their velocity.</summary>
    public Dictionary<int, (double Start, int Velocity)> Held { get; } = new();
    public double LastSec { get; private set; } = double.NaN;

    /// <summary>
    /// Adds one channel message heard at song second <paramref name="sec"/> to pass <paramref name="lap"/> (0 without a loop).
    /// <paramref name="recordStartSec"/> gives the first pass's start from the first message's time.
    /// </summary>
    public void Keep(int type, int pitch, int velocity, double sec, int lap, (double Start, double End)? loop, Func<double, double> recordStartSec)
    {
        while (Passes.Count <= lap)
        {
            if (Passes.Count > 0)
            {
                // The previous lap ends at the loop end; notes still held end there.
                CloseHeld(loop?.End ?? LastSec);
                var previous = Passes[^1];
                Passes[^1] = (previous.Start, loop?.End ?? previous.End, previous.Notes);
            }
            var start = Passes.Count == 0 ? recordStartSec(sec) : loop?.Start ?? sec;
            Passes.Add((start, start, new List<ClipNote>()));
        }
        LastSec = sec;
        var pass = Passes[lap];
        if (type == 0x90 && velocity > 0) Held[pitch] = (sec, velocity);
        else if (type is 0x80 or 0x90 && Held.Remove(pitch, out var held))
            pass.Notes.Add(new ClipNote(held.Start - pass.Start, Math.Max(0.01, sec - held.Start), pitch, held.Velocity));
        Passes[lap] = (pass.Start, Math.Max(pass.End, sec), pass.Notes);
    }

    /// <summary>Ends every held note at <paramref name="at"/> in the last pass.</summary>
    public void CloseHeld(double at)
    {
        if (Passes.Count == 0) { Held.Clear(); return; }
        var pass = Passes[^1];
        foreach (var (pitch, held) in Held)
            pass.Notes.Add(new ClipNote(held.Start - pass.Start, Math.Max(0.01, at - held.Start), pitch, held.Velocity));
        Held.Clear();
    }

    /// <summary>The recording stopped at <paramref name="endSec"/> (NaN: unknown): every pass with notes becomes its own separate
    /// ordinary MIDI clip (one per lap, no take group). All but the newest clip are muted; the newest plays.</summary>
    public List<AudioClip> Finish(string trackName, double endSec, bool looped = false)
    {
        if (Passes.Count == 0) return new List<AudioClip>();
        CloseHeld(double.IsNaN(endSec) ? LastSec : endSec);
        var last = Passes[^1];
        Passes[^1] = (last.Start, Math.Max(last.End, double.IsNaN(endSec) ? last.End : endSec), last.Notes);
        var recordingId = Passes.Count > 1 ? Guid.NewGuid().ToString("N") : null;
        var clips = Passes.Where(p => p.Notes.Count > 0).Select((p, i) => new AudioClip
        {
            Name = Passes.Count > 1 ? $"{trackName} MIDI take {i + 1}" : $"{trackName} MIDI",
            StartSec = Math.Max(0, p.Start), SourceLengthSec = Math.Max(0.1, p.End - p.Start),
            Notes = p.Notes.OrderBy(n => n.StartSec).ToList(),
            Muted = true, LoopRecordingId = recordingId,
        }).ToList();
        if (clips.Count > 0) clips[^1].Muted = false;   // the newest lap plays; older laps stay muted
        return clips;
    }
}
