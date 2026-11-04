using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the FX chain window's layout at the UI scales a user can pick: the window is sized to the scale, the menu row sits at the top and fills the
/// window's inner width, and the strip's groups are not pushed onto a second row by a rounding difference.
/// Does not own: the scale helper (WindowUiScaleTests).
/// Tests: this class.
/// </summary>
public class FxChainWindowScaleTests
{
    private static LayoutTransformControl? _scaleRoot;
    private readonly ITestOutputHelper _out;
    public FxChainWindowScaleTests(ITestOutputHelper output) => _out = output;

    private static void SetScale(double scale)
    {
        WindowUiScale.CurrentAppearance.UiScale = scale;
        _scaleRoot ??= new LayoutTransformControl();
        ThemeSettingsBinding.ApplyWindow(new Window(), _scaleRoot, WindowUiScale.CurrentAppearance);
        RunQueuedUiWork();
    }

    [AvaloniaFact]
    public void TheStripStaysOnOneRowAtEveryScaleStep()
    {
        var wrapped = new System.Collections.Generic.List<string>();
        for (var percent = 70; percent <= 150; percent++)
        {
            FxChainWindow? window = null;
            try
            {
                SetScale(percent / 100.0);
                window = FxTestKit.Window();
                window.Show();
                RunQueuedUiWork();
                var row = Named<Control>(window, "MenuRow");
                if (row.Bounds.Height > 40) wrapped.Add($"{percent}% (row {row.Bounds.Height:0.##} high, {row.Bounds.Width:0.##} wide)");
            }
            finally
            {
                window?.Close();
                SetScale(1.0);
            }
        }
        _out.WriteLine("wrapped at: " + string.Join(", ", wrapped));
        Assert.Empty(wrapped);
    }

    [AvaloniaTheory]
    [InlineData(1.0, 0.75)] [InlineData(1.0, 0.8)] [InlineData(1.0, 0.85)] [InlineData(1.0, 0.9)] [InlineData(0.8, 0.75)] [InlineData(0.75, 0.85)]
    public void AnOpenWindowFollowsALiveScaleChange(double from, double to)
    {
        FxChainWindow? window = null;
        try
        {
            SetScale(from);
            window = FxTestKit.Window();
            window.Show();
            RunQueuedUiWork();
            SetScale(to);
            RunQueuedUiWork();
            var row = Named<Control>(window, "MenuRow");
            var body = Named<Control>(window, "ChainList");
            var top = row.TranslatePoint(default, window)!.Value;
            var listBottom = body.TranslatePoint(new Point(0, body.Bounds.Height), window)!.Value.Y;
            _out.WriteLine($"{from}->{to}: window {window.Width:0.##}x{window.Height:0.##} bounds {window.Bounds.Width:0.##}x{window.Bounds.Height:0.##} row top {top.Y:0.##} list bottom {listBottom:0.##} (client {window.ClientSize.Height:0.##}) list h {body.Bounds.Height:0.##}");
            Assert.Equal(980 * to, window.Width, 3);
            Assert.True(top.Y < 8, $"menu row at {top.Y}");
            Assert.True(listBottom > window.ClientSize.Height * 0.7, $"the chain list ends at {listBottom} of {window.ClientSize.Height}");
        }
        finally
        {
            window?.Close();
            SetScale(1.0);
        }
    }

    [AvaloniaTheory]
    [InlineData(1.0)] [InlineData(0.9)] [InlineData(0.85)] [InlineData(0.8)] [InlineData(0.75)] [InlineData(0.7)] [InlineData(1.25)] [InlineData(1.5)]
    public void TheMenuRowStaysAtTheTopAndTheStripKeepsOneRowWhenItFits(double scale)
    {
        FxChainWindow? window = null;
        try
        {
            SetScale(scale);
            window = FxTestKit.Window();
            window.Show();
            RunQueuedUiWork();
            var row = Named<Control>(window, "MenuRow");
            var groups = Named<Panel>(window, "LiveStripGroups");
            var top = row.TranslatePoint(default, window)!.Value;
            var lines = 1 + groups.Children.Zip(groups.Children.Skip(1), (a, b) => b.Bounds.X < a.Bounds.X ? 1 : 0).Sum();
            _out.WriteLine($"scale {scale}: window {window.Width:0.##}x{window.Height:0.##} bounds {window.Bounds.Width:0.##}x{window.Bounds.Height:0.##} row top {top.Y:0.##} h {row.Bounds.Height:0.##} w {row.Bounds.Width:0.##} strip lines {lines} groups w {groups.Bounds.Width:0.##}");
            foreach (var child in groups.Children) _out.WriteLine($"   {child.Name} x {child.Bounds.X:0.##} y {child.Bounds.Y:0.##} w {child.Bounds.Width:0.##} desired {child.DesiredSize.Width:0.##}");
            Assert.Equal(980 * scale, window.Width, 3);
            Assert.True(top.Y < 8, $"menu row at {top.Y}");
            Assert.Equal(1, lines);
        }
        finally
        {
            window?.Close();
            SetScale(1.0);
        }
    }
}
