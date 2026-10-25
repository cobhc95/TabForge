using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

// Owns: the one answer to "which tempo is sounding here": at a time on the compiled (performed-order) timeline, and at a written bar and cell when stopped.
// Does not own: the tempo carry itself (ScoreToMidiCompiler writes each performed bar's start tempo into ScoreBar.Tempo; MusicTime holds the in-bar ramps).
// Tests: TabForge.Avalonia.Tests.LiveTempoTests.
// Threads: any thread; allocation free.

/// <summary>The live tempo of a playing or stopped song, from the same tempo map playback applies.</summary>
public static class PlaybackTempo
{
    /// <summary>The tempo at <paramref name="ms"/> of the compiled <paramref name="timeline"/>: the performed bar's start tempo (repeats, jumps and carried changes included) moved on by that bar's mid-bar changes and ramps.</summary>
    public static int AtTime(SongProject project, ScoreTimeline timeline, double ms)
    {
        if (timeline.Bars.Count == 0) return Math.Clamp(project.Tempo, 20, 400);
        var bar = timeline.BarAt(ms);
        var slot = bar.SlotFraction(ms) * bar.Slots;
        return MusicTime.TempoAtSlot(MusicTime.BarOf(project, bar.Bar), slot, bar.Tempo);
    }

    /// <summary>The tempo at a written bar and cell while nothing plays (the cursor's tempo): the tempo in force at the bar, moved on by its mid-bar changes up to the cell.</summary>
    public static int AtCell(SongProject project, int bar, int cell) =>
        MusicTime.TempoAtSlot(MusicTime.BarOf(project, bar), Math.Max(0, cell), MusicTime.TempoAt(project, bar));
}
