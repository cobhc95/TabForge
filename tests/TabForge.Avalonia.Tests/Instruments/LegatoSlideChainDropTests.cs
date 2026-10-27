using System;
using System.IO;
using System.Linq;
using TabForge.Playback;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: probe for dropped notes after a legato slide (Aces High bar 66: 5 slide to 7, then 7 x5 on one string).
// Env: TABFORGE_SAMPLES_DIR = the sample folder; without it the test returns at once.
public class LegatoSlideChainDropTests
{
    [Theory]
    [InlineData("iron_maiden/aces_high_8.gp5")]
    [InlineData("iron_maiden/aces_high_9.gp5")]
    public void Notes_after_a_legato_slide_target_are_each_attacked(string relative)
    {
        AlphaTabWarmUp.EnsureReady();
        var dir = Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR");
        var path = string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, relative);
        if (path is null || !File.Exists(path)) return;
        var song = GuitarProImporter.Import(path, new ImportContext());
        var timeline = MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });
        var cells = timeline.Notes.Where(n => n.TrackIndex == 0 && n.Bar == 65 && n.VoiceIndex == 0).Select(n => n.Cell).OrderBy(c => c).ToList();
        // Cell 2 is the slide target (not re-picked); the later 7s are separate picks.
        Assert.Equal(new[] { 0, 6, 10, 12, 14 }, cells);
    }
}
