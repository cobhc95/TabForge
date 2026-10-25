using System;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the section button feedback's rules on a manual clock: the hover names one section, a press sinks to full depth and eases back after the
/// release, and the confirm flash starts at full strength and fades out.
/// Does not own: the strip and row wiring (SectionButtonTests), the jump (SectionLinkTests).
/// Tests: TabForge.Avalonia.Tests.SectionButtonFeedbackTests.
/// </summary>
public class SectionButtonFeedbackTests
{
    [AvaloniaFact]
    public void HoverNamesTheSectionUnderThePointerUntilItLeaves()
    {
        var section = DemoSongFactory.Create().Markers[0];
        var redraws = 0;
        var feedback = new SectionButtonFeedback(() => redraws++, AvaloniaUiThread.ForApplication(), () => TimeSpan.Zero);

        feedback.SetHovered(section);
        Assert.Same(section, feedback.Hovered);
        Assert.Equal(1, redraws);

        feedback.SetHovered(section);
        Assert.Equal(1, redraws);

        feedback.SetHovered(null);
        Assert.Null(feedback.Hovered);
    }

    [AvaloniaFact]
    public void PressSinksAndReleaseEasesBackAndTheFlashFades()
    {
        UiMotion.Configure(false, 1);
        var section = DemoSongFactory.Create().Markers[0];
        var now = TimeSpan.Zero;
        var feedback = new SectionButtonFeedback(() => { }, AvaloniaUiThread.ForApplication(), () => now);

        feedback.Press(section);
        Assert.Equal(1, feedback.PressDepth(section), 6);
        Assert.Same(section, feedback.Release());
        Assert.Null(feedback.Release());

        now = TimeSpan.FromMilliseconds(SectionButtonFeedback.ReleaseMilliseconds / 2);
        Assert.InRange(feedback.PressDepth(section), 0.01, 0.99);
        now = TimeSpan.FromMilliseconds(SectionButtonFeedback.ReleaseMilliseconds);
        Assert.Equal(0, feedback.PressDepth(section), 6);

        feedback.Flash(section);
        Assert.Equal(1, feedback.FlashAlpha(section), 6);
        now += TimeSpan.FromMilliseconds(SectionButtonFeedback.FlashMilliseconds / 2);
        Assert.InRange(feedback.FlashAlpha(section), 0.01, 0.99);
        now += TimeSpan.FromMilliseconds(SectionButtonFeedback.FlashMilliseconds);
        Assert.Equal(0, feedback.FlashAlpha(section), 6);
    }
}
