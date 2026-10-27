using System;
using System.Collections.Generic;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;
using Avalonia.Headless.XUnit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Preferences rows the score reads live: default note value, Move to the next note after writing, score wheel distance, the Follow rows
/// (mode, axes, look-ahead, trigger, margin, stop at end, frame rate) and playback line thickness, each changed through ScoreSettingsApplier and
/// checked on the score view, plus the pure follow rule (FollowScrollPolicy) for the same rows.
/// Tests: ScoreSettingsApplier, ScorePreviewView, FollowScrollPolicy, FollowFrameGate.
/// </summary>
public class FollowSettingsTests
{
    private static readonly FollowViewport Viewport = new(0, 0, 800, 600);
    private static readonly FollowExtent Content = new(4000, 4000);

    [AvaloniaFact]
    public void SettingChangeReachesTheScoreView()
    {
        var cases = new List<(string Row, Action<AppSettings> Change, Func<ScorePreviewView, bool> Holds)>
        {
            ("editing.duration", s => s.Editing.DefaultDuration = 8, v => v.Editor.Tools.DurationDenominator == 8),
            ("editing.advance", s => s.Editing.AutoAdvance = true, v => v.Editor.Tools.AutoAdvanceAfterEntry),
            ("editing.scorewheel", s => s.Editing.ScoreWheelScrollPixels = 60, v => v.WheelScrollPixels == 60),
            ("follow.playback", s => s.Follow.FollowPlayback = false, v => !v.Follow.FollowPlayback),
            ("follow.playhead.thickness", s => s.Follow.PlayheadThickness = 3.5, v => v.Probe.Appearance.PlayheadThickness == 3.5),
        };
        foreach (var (row, change, holds) in cases)
        {
            var score = new ScorePreviewView();
            var applier = new ScoreSettingsApplier(score);
            var settings = new AppSettings();
            applier.Apply(settings);
            change(settings);
            applier.Apply(settings);
            Assert.True(holds(score), $"Row {row} did not reach the score view.");
        }
    }

    [AvaloniaFact]
    public void FollowRowsDecideWhereTheScoreScrolls()
    {
        var playedLow = new FollowLine(100, 700, 760);   // below the 80% trigger of an 600-high viewport
        var cases = new List<(string Row, Action<FollowSettings> Change, FollowLine Line, bool Moves)>
        {
            ("follow.playback off", f => f.FollowPlayback = false, playedLow, false),
            ("follow.mode Jump", f => f.Mode = FollowModes.Jump, playedLow, true),
            ("follow.vertical off", f => f.VerticalFollow = false, playedLow, false),
            ("follow.horizontal off", f => f.HorizontalFollow = false, new FollowLine(900, 0, 40), false),
        };
        foreach (var (row, change, line, moves) in cases)
        {
            var follow = new FollowSettings();
            change(follow);
            var target = FollowScrollPolicy.Target(follow, Viewport, line, 200, Content);
            Assert.True((target is not null) == moves, $"Row {row}: expected moves={moves}.");
        }

        var margin = new FollowSettings { MarginPercent = 50 };
        var parked = FollowScrollPolicy.Target(margin, Viewport, playedLow, 200, Content);
        Assert.Equal(700 - 600 * 0.5, parked!.Value.Y);
        var trigger = new FollowSettings { VerticalTriggerPercent = 95 };
        Assert.Null(FollowScrollPolicy.Target(trigger, Viewport, new FollowLine(100, 500, 560), 200, Content));
        var atEnd = new FollowViewport(0, 3400, 800, 600);   // the last 600 units of a 4000-high score
        Assert.Null(FollowScrollPolicy.Target(new FollowSettings(), atEnd, playedLow with { Top = 3700, Bottom = 3760 }, 200, Content));
        var lookAhead = new FollowSettings { AnticipationBars = 2 };
        Assert.NotNull(FollowScrollPolicy.Target(lookAhead, Viewport, new FollowLine(700, 0, 40), 200, Content));
        Assert.Null(FollowScrollPolicy.Target(new FollowSettings { AnticipationBars = 0 }, Viewport, new FollowLine(700, 0, 40), 200, Content));
    }

    /// <summary>A system whose bottom is below the viewport scrolls down even when stop at end holds the view (the extent reaches the viewport bottom).</summary>
    [Fact]
    public void SystemWithItsBottomBelowTheViewportScrollsDownAtTheEnd()
    {
        var follow = new FollowSettings { StopAtEnd = true, VerticalFollow = true };
        var atEnd = new FollowExtent(4000, Viewport.Bottom);
        var target = FollowScrollPolicy.Target(follow, Viewport, new FollowLine(100, 560, 680), 200, atEnd);

        Assert.NotNull(target);
        Assert.True(target!.Value.Y > Viewport.Y);
    }

    [Fact]
    public void FrameGateCapsTheSmoothFrameRate()
    {
        var gate = new FollowFrameGate();
        Assert.True(gate.Allow(240));
        Assert.False(gate.Allow(10));   // at most one step per 100 ms at 10 fps
    }
}
