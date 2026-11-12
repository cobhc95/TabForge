namespace TabForge.Services;

// Owns: the one copy of the bounds of the numeric settings that more than one place enforces: the settings catalog row (what the Settings window offers),
//   SettingsValidator (what a loaded settings file is clamped to) and the windows, popups or playback code that clamp the same value again.
// Does not own: the defaults (the settings classes), the bounds that only one place uses (still written at that place), or what a value does.
// Tests: SettingRangesTests.
public static class SettingRanges
{
    /// <summary>Count-in length in bars (Settings, the metronome popup, the compiler and the scheduler).</summary>
    public const int CountInBarsMin = 1, CountInBarsMax = 4;

    /// <summary>Note preview length in milliseconds.</summary>
    public const int PreviewLengthMsMin = 60, PreviewLengthMsMax = 1_200;

    /// <summary>Longest let-ring sustain in milliseconds. Since 0.9.6 playback honours the whole range (SustainResolver no longer floors it at 600).</summary>
    public const int LetRingCapMsMin = 400, LetRingCapMsMax = 6_000;

    /// <summary>Track colour tint on the timeline rows, in percent.</summary>
    public const int TrackTintPercentMin = 0, TrackTintPercentMax = 60;

    /// <summary>How much a muted track's row is dimmed, in percent.</summary>
    public const int MutedTrackDimPercentMin = 0, MutedTrackDimPercentMax = 100;

    /// <summary>Score mouse-wheel scroll distance in pixels.</summary>
    public const int ScoreWheelScrollPixelsMin = 12, ScoreWheelScrollPixelsMax = 96;

    /// <summary>Fretboard note marker size in percent (Settings and the fretboard settings window).</summary>
    public const int FretMarkerSizePercentMin = 60, FretMarkerSizePercentMax = 160;

    /// <summary>Band view rows per screen (Settings, the Band view's own +/- commands).</summary>
    public const int BandRowsPerScreenMin = 1, BandRowsPerScreenMax = 5;

    /// <summary>Timeline snap distance in pixels (the snap settings window and popup).</summary>
    public const int SnapDistancePxMin = 1, SnapDistancePxMax = 40;

    /// <summary>Master track line strength in percent (Settings and the master lane strip).</summary>
    public const int MasterTrackOpacityPercentMin = 5, MasterTrackOpacityPercentMax = 50;
}
