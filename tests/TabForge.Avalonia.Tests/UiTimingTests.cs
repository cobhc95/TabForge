using System;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the values of the named UI durations in <see cref="UiTiming"/>, so moving a timer's number into the table (or the reverse) cannot
/// silently change the behaviour the timers were tuned for.
/// Does not own: the timers that use the durations, their priorities, or the Core animation scaling (UiMotion).
/// Tests: this class.
/// </summary>
public class UiTimingTests
{
    [Fact]
    public void EveryNamedDurationKeepsTheValueItsSitesUsed()
    {
        Assert.Equal(400, UiTiming.SettingsSaveDebounceMs);
        Assert.Equal(100, UiTiming.SettingsPreviewIntervalMs);
        Assert.Equal(8_000, UiTiming.UpdateCheckStartDelayMs);
        Assert.Equal(86_400_000, UiTiming.UpdateCheckIntervalMs);
        Assert.Equal(15_000, UiTiming.UpdateCheckTimeoutMs);
        Assert.Equal(20_000, UiTiming.AutosaveClockIntervalMs);
        Assert.Equal(250, UiTiming.AutosaveRecheckDelayMs);
        Assert.Equal(120, UiTiming.MenuOpenDelayMs);
        Assert.Equal(350, UiTiming.MenuCloseDelayMs);
        Assert.Equal(16, UiTiming.SectionFeedbackFrameMs);
        Assert.Equal(16, UiTiming.SectionDragFrameMs);
        Assert.Equal(8, UiTiming.PlayheadFallbackFrameMs);
        Assert.Equal(33, UiTiming.ArmedRowMeterIntervalMs);
        Assert.Equal(33, UiTiming.TunerPollIntervalMs);
        Assert.Equal(400, UiTiming.LayoutChangeDebounceMs);
        Assert.Equal(150, UiTiming.TabReorderSlideMs);
        Assert.Equal(130, UiTiming.TabDropSettleMs);
        Assert.Equal(1, UiTiming.TimelineScrollEpsilonDips);
        Assert.Equal(1, UiTiming.TimelineMinViewportDips);
    }
}
