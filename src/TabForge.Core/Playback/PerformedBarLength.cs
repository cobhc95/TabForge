using TabForge.Models;

namespace TabForge.Playback;

// Owns: the one rule for how long a performed bar lasts when an imported bar holds less than its time signature.
// Does not own: the written content measurement (ScoreToMidiCompiler.ContentSlots).
// Tests: the existing MIDI export timing tests.

/// <summary>An imported bar shorter than its time signature plays only as long as its written content.</summary>
internal static class PerformedBarLength
{
    internal static bool IsShort(double contentSlots, int slots) =>
        contentSlots > 0.25 && contentSlots < slots - 0.01;

    internal static double Slots(SongProject song, int bar, int slots)
    {
        if (song.ImportedFrom is null) return slots;
        var content = ScoreToMidiCompiler.ContentSlots(song, bar);
        return IsShort(content, slots) ? content : slots;
    }
}
