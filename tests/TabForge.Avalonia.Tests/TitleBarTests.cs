using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the title row checks (size, order, button actions, caption pointer rules, glyph contrast). Tests: Shell/TitleBarView, CaptionButtons and AvaloniaWindowChrome.</summary>
public class TitleBarTests
{
    private sealed class FakeChrome : IWindowChrome
    {
        public List<string> Calls { get; } = new();
        public WindowChromeState State { get; set; }
        public event EventHandler? StateChanged;
        public void BeginMove() => Calls.Add("move");
        public void BeginResize(ResizeEdge edge) => Calls.Add("resize");
        public void Minimize() => Calls.Add("min");
        public void ToggleMaximise() { Calls.Add("max"); State = State == WindowChromeState.Maximised ? WindowChromeState.Normal : WindowChromeState.Maximised; StateChanged?.Invoke(this, EventArgs.Empty); }
        public void Close() => Calls.Add("close");
    }

    private static (Window Window, TitleBarView Bar, FakeChrome Chrome, List<string> Actions) Open()
    {
        var chrome = new FakeChrome();
        var actions = new List<string>();
        var bar = new TitleBarView(new TabStripView(new TabForge.Services.TabSettings()), chrome, () => actions.Add("record"), () => actions.Add("settings"));
        var window = new Window { Width = 900, Height = 300, Content = new DockPanel { LastChildFill = false, Children = { bar } } };
        DockPanel.SetDock(bar, Dock.Top);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, bar, chrome, actions);
    }

    private static Button Button(TitleBarView bar, string name) => bar.GetVisualDescendantsOf<Button>().Single(b => b.Name == name);

    // The tab bar measures: tabs 8 px in from the left, top-aligned, 36 px tall on the active-tab colour; a 36 px transparent (+) button
    // with a 14 px glyph centred in it; a 20 px close button with a 10 px glyph.
    [AvaloniaFact]
    public void TabStripMatchesWpfGeometryAndPlusGlyphIsCentred()
    {
        var (window, bar, _, _) = Open();
        var strip = bar.GetVisualDescendantsOf<TabStripView>().Single();
        strip.Show(new[] { new TabStripDocument(null, "Song") }, 0, 600);
        Dispatcher.UIThread.RunJobs();
        Rect In(global::Avalonia.Controls.Control c) => new(c.TranslatePoint(default, window)!.Value, c.Bounds.Size);
        var tab = In(strip.Tabs[0]);
        Assert.Equal(8, tab.X, 0.5);
        Assert.Equal(0, tab.Y, 0.5);
        Assert.Equal(36, tab.Height, 0.5);
        Assert.True(window.TryFindResource("TabActiveBrush", window.ActualThemeVariant, out var active));
        Assert.Equal(((global::Avalonia.Media.ISolidColorBrush)active!).Color,((global::Avalonia.Media.ISolidColorBrush)((Border)strip.Tabs[0]).Background!).Color);
        var plus = Button(bar, "NewTab");
        var plusRect = In(plus);
        Assert.Equal(36, plusRect.Width, 0.5);
        Assert.Equal(36, plusRect.Height, 0.5);
        Assert.Equal(tab.Right + 6, plusRect.X, 0.5);
        var glyph = In(plus.GetVisualDescendantsOf<global::Avalonia.Controls.Shapes.Path>().Single());
        Assert.Equal(14, glyph.Width, 0.5);
        Assert.Equal(plusRect.Center.X, glyph.Center.X, 0.5);
        Assert.Equal(plusRect.Center.Y, glyph.Center.Y, 0.5);
        var close = In(Button(bar, "TabClose"));
        Assert.Equal(20, close.Width, 0.5);
        Assert.Equal(tab.Center.Y, close.Center.Y, 0.5);
        window.Close();
    }

    // The Record video button: a 20 px glyph box centred in the 40 x 44 button, the camera outline in a 17 x 14 box at its left / centre
    //, and the recording dot at the box's top right. The outline is scaled through a Viewbox, so its bounds are
    // fitted and centred: measured on the headless renderer, the drawn outline leaves equal gaps above and below (it sat 2 px high before).
    [AvaloniaFact]
    public void RecordGlyphIsCentredInItsBoxWithTheDotAtItsTopRight()
    {
        var (window, bar, _, _) = Open();
        var record = Button(bar, "Record");
        bar.ShowRecording(true);                                          // the dot is hidden until a recording runs, and a hidden control lays out at zero size
        Dispatcher.UIThread.RunJobs();
        Rect In(global::Avalonia.Controls.Control c) => new(c.TranslatePoint(default, window)!.Value, c.Bounds.Size);
        var box = (Grid)record.Content!;
        var glyph = box.Children.OfType<Viewbox>().Single();
        var dot = box.Children.OfType<global::Avalonia.Controls.Shapes.Ellipse>().Single();
        var recordRect = In(record);
        var boxRect = In(box);
        var glyphRect = In(glyph);
        var dotRect = In(dot);
        Assert.Equal(20, boxRect.Width, 0.5);
        Assert.Equal(20, boxRect.Height, 0.5);
        Assert.Equal(recordRect.Center.X, boxRect.Center.X, 0.5);
        Assert.Equal(recordRect.Center.Y, boxRect.Center.Y, 0.5);
        Assert.Equal(17, glyphRect.Width, 0.5);
        Assert.Equal(14, glyphRect.Height, 0.5);
        Assert.Equal(boxRect.X, glyphRect.X, 0.5);                        // left...
        Assert.Equal(boxRect.Center.Y, glyphRect.Center.Y, 0.5);          // ...and vertically centred in the box
        Assert.Equal(global::Avalonia.Media.Stretch.Uniform, Assert.IsType<global::Avalonia.Controls.Shapes.Path>(glyph.Child).Stretch);
        Assert.Equal(9, dotRect.Width, 0.5);                              // the dot sits at the box's top right...
        Assert.Equal(9, dotRect.Height, 0.5);
        Assert.Equal(boxRect.Right, dotRect.Right, 0.5);
        Assert.Equal(boxRect.Y, dotRect.Y, 0.5);
        Assert.True(dotRect.Right <= recordRect.Right && dotRect.Bottom <= recordRect.Bottom, "the recording dot stays inside the button");
        window.Close();
    }

    [AvaloniaFact]
    public void RowIs44PxWithTabsLeftAndButtonsRightInWpfOrder()
    {
        var (window, bar, _, _) = Open();
        Assert.Equal(44, bar.Bounds.Height);
        var tabs = bar.GetVisualDescendantsOf<TabStripView>().Single();
        var order = new[] { "Record", "MonitorFx", "Settings", "Minimise", "Maximise", "Close" };
        var xs = order.Select(n => Button(bar, n).TranslatePoint(new Point(0, 0), window)!.Value.X).ToArray();
        Assert.Equal(xs.OrderBy(x => x), xs);
        Assert.True(tabs.TranslatePoint(new Point(0, 0), window)!.Value.X < xs[0]);
        Assert.Equal(900, xs[^1] + 46, 1);
        Assert.All(order, n => Assert.Equal(44, Button(bar, n).Bounds.Height));
        window.Close();
    }

    [AvaloniaFact]
    public void ButtonsCallTheChromeAndTheActions()
    {
        var (window, bar, chrome, actions) = Open();
        foreach (var name in new[] { "Record", "Settings", "Minimise", "Maximise", "Close" }) DialogTestKit.Click(Button(bar, name));
        Assert.Equal(new[] { "record", "settings" }, actions);
        Assert.Equal(new[] { "min", "max", "close" }, chrome.Calls);
        window.Close();
    }

    [AvaloniaFact]
    public void PressOnEmptyCaptionMovesAndDoubleClickTogglesMaximise()
    {
        var (window, bar, chrome, _) = Open();
        var empty = new Point(500, 22);
        window.MouseDown(empty, MouseButton.Left);
        window.MouseUp(empty, MouseButton.Left);
        Assert.Equal(new[] { "move" }, chrome.Calls);
        window.MouseDown(empty, MouseButton.Left);
        window.MouseUp(empty, MouseButton.Left);
        Assert.Contains("max", chrome.Calls);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlyphsKeepContrastNormalAndHot(bool light)
    {
        var strip = AvaloniaPalette.Get("ChromeStrip", light);
        var normal = AvaloniaPalette.Get("ChromeGlyph", light);
        Assert.True(ThemeColours.Contrast(Over(normal, strip), strip) >= 4.5, "glyph normal");
        // Hot: the glyph over its hover fill (the close cross is the white MainCloseGlyph over the red fill).
        foreach (var (glyphKey, hoverKey) in new[] { ("ChromeGlyph", "CaptionHover"), ("ChromeGlyph", "CaptionPressed"), ("MainCloseGlyph", "CaptionCloseHover"), ("MainCloseGlyph", "CaptionClosePressed") })
        {
            var hot = Over(AvaloniaPalette.Get(hoverKey, light), strip);
            Assert.True(ThemeColours.Contrast(Over(AvaloniaPalette.Get(glyphKey, light), hot), hot) >= 3.0, $"{glyphKey} over {hoverKey}");
        }
    }

    // The opaque colour of a (possibly translucent) colour painted over an opaque surface.
    private static UiColor Over(UiColor top, UiColor under)
    {
        double a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return new UiColor(255, Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainWindowTopAreaRenders(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new MainWindow();
        DialogTestKit.SavePng(window, nameof(TitleBarTests), "title-bar-" + (light ? "light" : "dark") + ".png");
        Assert.Equal(44, window.GetVisualDescendantsOf<TitleBarView>().Single().Bounds.Height);
        Assert.True(window.ExtendClientAreaToDecorationsHint);
        window.Close();
    }
}

internal static class TitleBarTestExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this global::Avalonia.Visual root) where T : class =>
        global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
