using System;
using System.Linq;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the check that each shared setting range has one value everywhere: the settings catalog row offers SettingRanges' bounds, and
//   SettingsValidator clamps a loaded value to the same bounds (below the least gives the least, above the most gives the most).
// Does not own: the defaults or what the settings do (their own test classes).
// Tests: this class.
public sealed class SettingRangesTests
{
    private sealed record Range(string Key, int Min, int Max, Action<AppSettings, int> Set, Func<AppSettings, int> Get);

    private static readonly Range[] Ranges =
    {
        new("audio.countinbars", SettingRanges.CountInBarsMin, SettingRanges.CountInBarsMax, (s, v) => s.Audio.CountInBars = v, s => s.Audio.CountInBars),
        new("audio.previewlen", SettingRanges.PreviewLengthMsMin, SettingRanges.PreviewLengthMsMax, (s, v) => s.Audio.PreviewLengthMs = v, s => s.Audio.PreviewLengthMs),
        new("audio.letring", SettingRanges.LetRingCapMsMin, SettingRanges.LetRingCapMsMax, (s, v) => s.Audio.LetRingCapMs = v, s => s.Audio.LetRingCapMs),
        new("appearance.tracktint", SettingRanges.TrackTintPercentMin, SettingRanges.TrackTintPercentMax, (s, v) => s.Appearance.TrackTintPercent = v, s => s.Appearance.TrackTintPercent),
        new("appearance.mutedtrackdim", SettingRanges.MutedTrackDimPercentMin, SettingRanges.MutedTrackDimPercentMax, (s, v) => s.Appearance.MutedTrackDimPercent = v, s => s.Appearance.MutedTrackDimPercent),
        new("editing.scorewheel", SettingRanges.ScoreWheelScrollPixelsMin, SettingRanges.ScoreWheelScrollPixelsMax, (s, v) => s.Editing.ScoreWheelScrollPixels = v, s => s.Editing.ScoreWheelScrollPixels),
        new("fretboard.markersize", SettingRanges.FretMarkerSizePercentMin, SettingRanges.FretMarkerSizePercentMax, (s, v) => s.Editing.FretMarkerSizePercent = v, s => s.Editing.FretMarkerSizePercent),
        new("band.rowsperscreen", SettingRanges.BandRowsPerScreenMin, SettingRanges.BandRowsPerScreenMax, (s, v) => s.Timeline.Band.RowsPerScreen = v, s => s.Timeline.Band.RowsPerScreen),
        new("timeline.mastertrackopacity", SettingRanges.MasterTrackOpacityPercentMin, SettingRanges.MasterTrackOpacityPercentMax, (s, v) => s.Timeline.MasterTrackOpacityPercent = v, s => s.Timeline.MasterTrackOpacityPercent),
    };

    [Fact]
    public void TheValuesAreTheOnesTheAppHasAlwaysUsed()
    {
        Assert.Equal((1, 4), (SettingRanges.CountInBarsMin, SettingRanges.CountInBarsMax));
        Assert.Equal((60, 1200), (SettingRanges.PreviewLengthMsMin, SettingRanges.PreviewLengthMsMax));
        Assert.Equal((400, 6000), (SettingRanges.LetRingCapMsMin, SettingRanges.LetRingCapMsMax));
        Assert.Equal((0, 60), (SettingRanges.TrackTintPercentMin, SettingRanges.TrackTintPercentMax));
        Assert.Equal((0, 100), (SettingRanges.MutedTrackDimPercentMin, SettingRanges.MutedTrackDimPercentMax));
        Assert.Equal((12, 96), (SettingRanges.ScoreWheelScrollPixelsMin, SettingRanges.ScoreWheelScrollPixelsMax));
        Assert.Equal((60, 160), (SettingRanges.FretMarkerSizePercentMin, SettingRanges.FretMarkerSizePercentMax));
        Assert.Equal((1, 5), (SettingRanges.BandRowsPerScreenMin, SettingRanges.BandRowsPerScreenMax));
        Assert.Equal((1, 40), (SettingRanges.SnapDistancePxMin, SettingRanges.SnapDistancePxMax));
        Assert.Equal((5, 50), (SettingRanges.MasterTrackOpacityPercentMin, SettingRanges.MasterTrackOpacityPercentMax));
        Assert.Equal("#8B939F", new TimelineSettings().MasterTrackColour);
    }

    [Fact]
    public void TheCatalogRowOffersTheSharedRange()
    {
        var rows = SettingsCatalog.Build(new AppSettings()).ToDictionary(r => r.Key);
        foreach (var range in Ranges)
        {
            Assert.True(rows.TryGetValue(range.Key, out var row), range.Key + " has no catalog row");
            Assert.Equal((range.Min, range.Max), ((int)row!.Min, (int)row.Max));
        }
    }

    [Fact]
    public void TheValidatorClampsToTheSharedRange()
    {
        foreach (var range in Ranges)
        {
            var low = new AppSettings();
            range.Set(low, range.Min - 1);
            Assert.Equal(range.Min, range.Get(SettingsValidator.Normalize(low)));
            var high = new AppSettings();
            range.Set(high, range.Max + 1);
            Assert.Equal(range.Max, range.Get(SettingsValidator.Normalize(high)));
        }
        var snapLow = new AppSettings();
        snapLow.Timeline.Snap.DistancePx = 0;
        Assert.Equal(SettingRanges.SnapDistancePxMin, SettingsValidator.Normalize(snapLow).Timeline.Snap.DistancePx);
        var snapHigh = new AppSettings();
        snapHigh.Timeline.Snap.DistancePx = 999;
        Assert.Equal(SettingRanges.SnapDistancePxMax, SettingsValidator.Normalize(snapHigh).Timeline.Snap.DistancePx);
        var badColour = new AppSettings();
        badColour.Timeline.MasterTrackColour = "not a colour";
        Assert.Equal("#8B939F", SettingsValidator.Normalize(badColour).Timeline.MasterTrackColour);
    }

    [Fact]
    public void ATypedSnapDistanceIsClampedToTheSharedRange()
    {
        var snap = new SnapSettings();
        Assert.True(SnapSettingsPresenter.CommitDistance(snap, "0"));
        Assert.Equal(SettingRanges.SnapDistancePxMin, snap.DistancePx);
        Assert.True(SnapSettingsPresenter.CommitDistance(snap, "500"));
        Assert.Equal(SettingRanges.SnapDistancePxMax, snap.DistancePx);
    }
}
