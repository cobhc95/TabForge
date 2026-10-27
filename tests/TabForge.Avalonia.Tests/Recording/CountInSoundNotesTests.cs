using TabForge.Playback;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the count-in sound setting read into the accent and click notes (CountInSoundNotes.Parse).
// Does not own: the engine's click output (PlaybackEngine), the sound presets.
// Tests: this file.
public sealed class CountInSoundNotesTests
{
    [Fact]
    public void CustomPairGivesItsNotesAndAPresetKeepsTheMetronomeNotes()
    {
        Assert.Equal((37, 75), CountInSoundNotes.Parse("37,75"));
        Assert.Equal((-1, -1), CountInSoundNotes.Parse("same"));
    }
}
