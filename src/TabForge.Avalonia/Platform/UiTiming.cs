// Owns: the named UI durations the Avalonia windows and their timers use, all in milliseconds in one table, each named for its purpose. Two
//   entries may hold the same number when their purposes differ (a 16 ms animation frame is not a 400 ms debounce): the table gives every
//   duration one name, not fewer numbers.
// Does not own: the timers and their priorities (IUiTimer, AvaloniaUiTimer, AvaloniaUiThread), the logic a duration feeds (each owner) or
//   the Core animation scaling (UiMotion).
// Tests: TabForge.Avalonia.Tests.UiTimingTests.
namespace TabForge.Avalonia.Platform;

/// <summary>The one named source of the app's UI durations, in milliseconds.</summary>
internal static class UiTiming
{
    /// <summary>The settings store's save debounce: a changed setting is written once the pointer has rested.</summary>
    public const double SettingsSaveDebounceMs = 400;

    /// <summary>How often a live settings preview reaches the score while a slider is dragged.</summary>
    public const double SettingsPreviewIntervalMs = 100;

    /// <summary>The automatic update check's delay after start-up.</summary>
    public const double UpdateCheckStartDelayMs = 8_000;

    /// <summary>The least time between two automatic update checks (once a day).</summary>
    public const double UpdateCheckIntervalMs = 86_400_000;

    /// <summary>The update request's timeout before it counts as unreachable.</summary>
    public const double UpdateCheckTimeoutMs = 15_000;

    /// <summary>How often the autosave clock looks at the autosave interval.</summary>
    public const double AutosaveClockIntervalMs = 20_000;

    /// <summary>The margin after the registry's missing-song grace before the song is looked for again.</summary>
    public const double AutosaveRecheckDelayMs = 250;

    /// <summary>How long the pointer rests on a submenu parent before its submenu opens.</summary>
    public const double MenuOpenDelayMs = 120;

    /// <summary>How long an open submenu survives the pointer leaving it.</summary>
    public const double MenuCloseDelayMs = 350;

    /// <summary>The section button feedback's frame timer while a release or flash fades.</summary>
    public const double SectionFeedbackFrameMs = 16;

    /// <summary>The section drag animator's frame timer while sections move.</summary>
    public const double SectionDragFrameMs = 16;

    /// <summary>The timer frame source's period when no window drives the playhead (about 125 Hz).</summary>
    public const double PlayheadFallbackFrameMs = 8;

    /// <summary>The armed row's input level poll period (about 30 Hz).</summary>
    public const double ArmedRowMeterIntervalMs = 33;

    /// <summary>The tuner window's live reading poll period (about 30 Hz).</summary>
    public const double TunerPollIntervalMs = 33;

    /// <summary>The one meter tick the armed-row levels and the tuner readings share (about 30 Hz).</summary>
    public const double SharedMeterTickMs = 33;

    /// <summary>How long typing rests before a palette, preset or browser filter re-applies (the leading edge applies at once, so the first keystroke and tests see it).</summary>
    public const double FilterDebounceMs = 100;

    /// <summary>The dock layout's save debounce after a layout change.</summary>
    public const double LayoutChangeDebounceMs = 400;

    /// <summary>How long a neighbouring tab eases into its reorder slot.</summary>
    public const double TabReorderSlideMs = 150;

    /// <summary>How long a dropped tab eases from where it was released into its slot.</summary>
    public const double TabDropSettleMs = 130;

    /// <summary>A sideways offset change smaller than this (DIPs) is no scroll: the playhead follower keeps the offset and the footer bar keeps its value.</summary>
    public const double TimelineScrollEpsilonDips = 1;

    /// <summary>The scrolled-in span is unknown while the viewport is at most this wide (DIPs): the timeline draws every bar, not the visible span.</summary>
    public const double TimelineMinViewportDips = 1;
}
