using System;
using System.Linq;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll scale audit fixes and the Auto-detect wiring (no full UI).
// Tests: PianoRollScale (rows, picks, detection, effective highlight, labels).
public sealed class PianoRollScaleDetectTests
{
    private static PianoRollNote Note(int pitch) => new(0, 1, pitch, 90);

    [Fact]
    public void ScaleFoldWithNoScaleShowsEveryRow()
    {
        Assert.Equal(128, PianoRollScale.Rows(PianoRollScale.FoldScale, null, new[] { Note(60) }).Count);
    }

    [Fact]
    public void UnknownFoldShowsEveryRow()
    {
        Assert.Equal(128, PianoRollScale.Rows("Bogus", PianoRollScale.PitchClasses("C Major"), new[] { Note(60) }).Count);
    }

    [Fact]
    public void UsedFoldWithNoNotesShowsEveryRow()
    {
        Assert.Equal(128, PianoRollScale.Rows(PianoRollScale.FoldUsed, null, Array.Empty<PianoRollNote>()).Count);
    }

    [Fact]
    public void DetectNotesFindsCMajorAndEmptyStaysEmpty()
    {
        var notes = new[] { Note(60), Note(64), Note(67), Note(72) };
        var top = PianoRollScale.DetectNotes(notes);
        Assert.NotEmpty(top);
        Assert.Equal("C Major", top[0].Highlight);
        Assert.Empty(PianoRollScale.DetectNotes(Array.Empty<PianoRollNote>()));
    }

    [Fact]
    public void AutoFollowsDetectionAndManualSticks()
    {
        var notes = new[] { Note(60), Note(64), Note(67) };
        var detected = PianoRollScale.DetectNotes(notes);
        Assert.NotEmpty(detected);
        Assert.Equal(detected[0].Highlight, PianoRollScale.EffectiveHighlight("A Natural Minor", PianoRollScale.Auto, detected));
        Assert.Equal("A Natural Minor", PianoRollScale.EffectiveHighlight("A Natural Minor", "A Natural Minor", detected));
        Assert.Equal("A Natural Minor", PianoRollScale.EffectiveHighlight("A Natural Minor", null, detected));
        Assert.Equal("A Natural Minor", PianoRollScale.EffectiveHighlight("A Natural Minor", "Bogus", detected));
    }

    [Fact]
    public void AutoWithNoNotesFallsBackToTheSetting()
    {
        Assert.Equal("D Major", PianoRollScale.EffectiveHighlight("D Major", PianoRollScale.Auto, Array.Empty<ScaleMatch>()));
        Assert.Null(PianoRollScale.EffectiveHighlight(null, PianoRollScale.Auto, Array.Empty<ScaleMatch>()));
    }

    [Fact]
    public void LockSetFollowsTheEffectiveScale()
    {
        var scale = PianoRollScale.PitchClasses("C Major")!;
        Assert.Equal(62, PianoRollScaleLock.Snap(61, scale));
        Assert.True(PianoRollScale.PitchClasses("Bogus") is null);
        Assert.Equal(61, PianoRollScaleLock.Snap(61, PianoRollScale.PitchClasses("Bogus")));
    }

    [Fact]
    public void ButtonLabelShowsAuto()
    {
        Assert.Equal("C major (auto)", PianoRollScale.DisplayLabel("C Major", auto: true));
        Assert.Equal("C major", PianoRollScale.DisplayLabel("C Major", auto: false));
        Assert.Equal("No scale", PianoRollScale.DisplayLabel("Bogus", auto: false));
        Assert.Equal("No scale", PianoRollScale.DisplayLabel(null, auto: true));
    }
}
