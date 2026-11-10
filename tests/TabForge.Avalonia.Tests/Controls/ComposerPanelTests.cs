using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Features.LeftStrip;
using TabForge.Platform.Theme;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Composer panel's drum-sample rows: one row per groove shown under Drums, each fitting the panel's width, and the audition button
//   driving the window's hooks (play, stop, and a stop that comes from the window resetting the button).
// Does not own: the landing rules (DrumSampleDropTests) or the strip's open state (LeftStripSettingsTests).
// Tests: this class.
public sealed class ComposerPanelTests
{
    private static (Window Window, Border Panel) Show(ComposerDrumHooks hooks)
    {
        var panel = ComposerPanel.Build(hooks);
        var window = new Window { Width = 800, Height = 900, Content = panel };
        window.Show();
        return (window, panel);
    }

    private static Border Card(Border panel, string name) =>
        panel.GetLogicalDescendants().OfType<Border>().Single(b => global::Avalonia.Automation.AutomationProperties.GetName(b) == name + " category");

    private static List<Border> Rows(Border panel) =>
        panel.GetLogicalDescendants().OfType<Border>().Where(b => b.Name?.StartsWith(ComposerPanel.SampleRowPrefix, StringComparison.Ordinal) == true).ToList();

    private static void Click(Border card) =>
        card.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });

    [AvaloniaFact]
    public void Drums_ShowsEveryGrooveWithinThePanelWidth()
    {
        var (window, panel) = Show(new ComposerDrumHooks());
        Assert.All(Rows(panel), row => Assert.False(row.IsEffectivelyVisible));

        Click(Card(panel, "Drums"));
        window.UpdateLayout();

        var rows = Rows(panel);
        Assert.Equal(DrumSamples.All.Count, rows.Count);
        foreach (var row in rows)
        {
            Assert.True(row.IsEffectivelyVisible);
            Assert.True(row.Bounds.Width <= ChromeMetrics.ComposerPanelWidth, $"{row.Name} is {row.Bounds.Width} wide");
            Assert.True(row.DesiredSize.Width <= row.Bounds.Width + 0.5, $"{row.Name} wants {row.DesiredSize.Width}, has {row.Bounds.Width}");
            var play = row.GetLogicalDescendants().OfType<Button>().Single();
            var inRow = play.TranslatePoint(new Point(play.Bounds.Width, 0), row);
            Assert.NotNull(inRow);
            Assert.True(inRow!.Value.X <= row.Bounds.Width, $"{row.Name}'s play button is cut off");
        }
        window.Close();
    }

    [AvaloniaFact]
    public void PlayButton_PlaysAndStops_AndAWindowStopResetsIt()
    {
        DrumSample? played = null;
        Action<int>? onStep = null;
        var stops = 0;
        var hooks = new ComposerDrumHooks
        {
            Play = (sample, step) => { played = sample; onStep = step; },
            Stop = () => { stops++; onStep?.Invoke(-1); },
        };
        var (window, panel) = Show(hooks);
        Click(Card(panel, "Drums"));
        var first = Rows(panel)[0].GetLogicalDescendants().OfType<Button>().Single();

        first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(DrumSamples.All[0], played);
        Assert.Equal("Stop", ToolTip.GetTip(first));

        first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, stops);
        Assert.Equal("Audition at the song tempo", ToolTip.GetTip(first));

        // The window stops it on its own (a drag starts): the button goes back to Play without a click.
        first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        onStep!(-1);
        Assert.Equal("Audition at the song tempo", ToolTip.GetTip(first));
        window.Close();
    }
}
