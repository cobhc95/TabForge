using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Shell.Win32;
using TabForge.Platform;
using TabForge.Shell;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the title bar's native-hit-test rectangles, the native hot and pressed visuals and the single click. Tests: Shell/TitleBarView, NcHitTestHook.HitCode.</summary>
public class TitleBarRectsTests
{
    private static (MainWindow Window, TitleBarView Bar) Open(double width, double height)
    {
        var window = new MainWindow { Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.GetVisualDescendantsOf<TitleBarView>().Single());
    }

    private static UiRect Laid(Control c, Window w)
    {
        var origin = c.TranslatePoint(new Point(0, 0), w)!.Value;
        var corner = c.TranslatePoint(new Point(c.Bounds.Width, c.Bounds.Height), w)!.Value;
        return new UiRect(origin.X, origin.Y, corner.X - origin.X, corner.Y - origin.Y);
    }

    private static bool Has(Button b, string pseudoClass) => b.Classes.Any(c => c == pseudoClass);

    private static Button Button(TitleBarView bar, string name) => bar.GetVisualDescendantsOf<Button>().Single(b => b.Name == name);

    private static void AssertMatchesLayout(Window window, TitleBarView bar)
    {
        Assert.Equal(Laid(bar, window), bar.CaptionBounds);
        foreach (var (rect, kind) in bar.CaptionButtons)
        {
            var name = kind switch { ChromeHit.MinButton => "Minimise", ChromeHit.MaxButton => "Maximise", _ => "Close" };
            Assert.Equal(Laid(Button(bar, name), window), rect);
        }
        var tabs = bar.GetVisualDescendantsOf<Control>().Where(c => c.GetVisualParent() is StackPanel { Name: "Tabs" }).Select(c => Laid(c, window)).ToList();
        Assert.NotEmpty(tabs);
        Assert.Equal(tabs, bar.TabBounds);
        foreach (var name in new[] { "NewTab", "Settings", "Record", "MonitorFx" }) Assert.Contains(Laid(Button(bar, name), window), bar.TabActionBounds);
    }

    [AvaloniaFact]
    public void RectanglesMatchTheLayoutAtOpenResizeAndMaximise()
    {
        var (window, bar) = Open(1320, 900);
        AssertMatchesLayout(window, bar);
        Assert.Equal(1320, bar.CaptionBounds.Width, 1);
        window.Width = 1000;
        Dispatcher.UIThread.RunJobs();
        AssertMatchesLayout(window, bar);
        window.WindowState = WindowState.Maximized;
        Dispatcher.UIThread.RunJobs();
        AssertMatchesLayout(window, bar);
        window.Close();
    }

    [AvaloniaFact]
    public void RectanglesMatchTheLayoutAtUiScale()
    {
        var (window, bar) = Open(1320, 900);
        window.GetVisualDescendantsOf<LayoutTransformControl>().First().LayoutTransform = new ScaleTransform(0.8, 0.8);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AssertMatchesLayout(window, bar);
        window.Close();
    }

    [AvaloniaFact]
    public void NativeHotAndPressedChangeTheButtonVisuals()
    {
        var (window, bar) = Open(1320, 900);
        var max = Button(bar, "Maximise");
        bar.SetHot(ChromeHit.MaxButton);
        Assert.True(Has(max, ":pointerover"));
        Assert.False(Has(max, ":pressed"));
        bar.SetPressed(ChromeHit.MaxButton);
        Assert.True(Has(max, ":pressed"));
        bar.SetPressed(null);
        bar.SetHot(null);
        Assert.False(Has(max, ":pointerover") || Has(max, ":pressed"));
        Assert.False(Has(Button(bar, "Close"), ":pointerover"));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaxButtonHotRenders(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var (window, bar) = Open(1320, 200);
        bar.SetHot(ChromeHit.MaxButton);
        DialogTestKit.SavePng(window, nameof(TitleBarRectsTests), "title-bar-max-hot-" + (light ? "light" : "dark") + ".png");
        window.Close();
    }

    [AvaloniaFact]
    public void ANativePressSkipsTheAvaloniaClickAndARealPressClearsIt()
    {
        var calls = 0;
        var chrome = new CountingChrome(() => calls++);
        var bar = new TitleBarView(new TabForge.Avalonia.Views.TabStripView(new TabForge.Services.TabSettings()), chrome, () => { }, () => { });
        var window = new Window { Width = 900, Height = 200, Content = bar };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        bar.SetPressed(ChromeHit.MaxButton); // the hook runs the action itself
        bar.SetPressed(null);
        DialogTestKit.Click(Button(bar, "Maximise"));
        Assert.Equal(0, calls);
        var point = Button(bar, "Maximise").TranslatePoint(new Point(10, 10), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(1, calls);
        window.Close();
    }

    [AvaloniaFact]
    public void HitTestWithTheRealTitleBarAnswersMaxButtonAndCaption()
    {
        var (window, bar) = Open(1320, 900);
        var rect = new PixelRect(0, 0, 1320, 900);
        var max = bar.CaptionButtons.Single(b => b.Kind == ChromeHit.MaxButton).Bounds;
        int Code(double x, double y) => NcHitTestHook.HitCode(new PixelPoint((int)x, (int)y), rect, 1, false, default, bar);
        Assert.Equal(NativeHitCodes.HtMaxButton, Code(max.X + max.Width / 2, max.Y + max.Height / 2));
        var tabsEnd = bar.TabActionBounds.Concat(bar.TabBounds).Where(r => r.X < 600).Max(r => r.X + r.Width);
        var buttonsStart = bar.TabActionBounds.Where(r => r.X >= 600).Min(r => r.X);
        Assert.True(buttonsStart > tabsEnd + 10, "empty caption exists between the tabs and the buttons");
        Assert.Equal(NativeHitCodes.HtCaption, Code((tabsEnd + buttonsStart) / 2, 22));
        Assert.Equal(NativeHitCodes.HtClient, Code(bar.TabBounds[0].X + 20, 22));
        window.Close();
    }

    // The window's edge answers: its caption-button hook runs before its resize hook, so the top row and the right column
    // over Minimise, Maximise and Close press the button; elsewhere a normal window's 6 px border resizes, and a maximised
    // window (no resize border) gives the top row to the tab, New tab, Record and Settings under it.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EdgePixelsHitTheTargetsWpfHits(bool maximised)
    {
        var (window, bar) = Open(1320, 900);
        var inset = maximised ? 8 : 0;
        // Maximised, the real window pads its content by the off-screen margin (TitleBarView.KeepInsideScreen), so bar rects carry the inset.
        window.Padding = new global::Avalonia.Thickness(inset);
        window.UpdateLayout();
        var rect = new PixelRect(-inset, -inset, 1320, 900); // the window overhangs the screen by the inset on every side
        // x, y are screen pixels; the visible screen is the window minus the inset on each side.
        int Code(double x, double y) => NcHitTestHook.HitCode(new PixelPoint((int)x, (int)y), rect, 1, maximised, new PixelPoint(inset, inset), bar);
        UiRect Action(string name) => Laid(Button(bar, name), window);
        double Mid(UiRect r) => r.X + r.Width / 2 - inset;
        var top = maximised ? NativeHitCodes.HtClient : NativeHitCodes.HtTop;
        Assert.Equal(inset, bar.TabBounds[0].Y);
        Assert.Equal(top, Code(Mid(bar.TabBounds[0]), 0));
        Assert.Equal(inset, Action("NewTab").Y);
        Assert.Equal(top, Code(Mid(Action("NewTab")), 0));
        Assert.Equal(top, Code(Mid(Action("Record")), 0));
        Assert.Equal(top, Code(Mid(Action("Settings")), 0));
        foreach (var (bounds, kind) in bar.CaptionButtons)
            Assert.Equal(kind switch { ChromeHit.MinButton => NativeHitCodes.HtMinButton, ChromeHit.MaxButton => NativeHitCodes.HtMaxButton, _ => NativeHitCodes.HtClose },
                Code(Mid(bounds), 0));
        Assert.Equal(NativeHitCodes.HtClose, Code(1319 - 2 * inset, 0));
        Assert.Equal(NativeHitCodes.HtClose, Code(1319 - 2 * inset, 22));
        Assert.Equal(maximised ? NativeHitCodes.HtClient : NativeHitCodes.HtRight, Code(1319 - 2 * inset, 300));
        window.Close();
    }

    private sealed class CountingChrome : IWindowChrome
    {
        private readonly System.Action _onMaximise;
        public CountingChrome(System.Action onMaximise) => _onMaximise = onMaximise;
        public WindowChromeState State => WindowChromeState.Normal;
        public event System.EventHandler? StateChanged { add { } remove { } }
        public void BeginMove() { }
        public void BeginResize(ResizeEdge edge) { }
        public void Minimize() { }
        public void ToggleMaximise() => _onMaximise();
        public void Close() { }
    }
}
