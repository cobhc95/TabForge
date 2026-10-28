using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Shell.Win32;
using TabForge.Platform;
using TabForge.Shell;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the native hit-test mapping (point -> HT code) and the hook's attach and detach (NcHitTestHook).</summary>
public class NcHitTestHookTests
{
    // A 1000 x 600 DIP window: caption 0..32 high, a tab at 0..200, min/max/close 46 wide at the right end.
    private sealed class Bar : ICaptionButtonRects
    {
        public IReadOnlyList<(UiRect Bounds, ChromeHit Kind)> CaptionButtons { get; } = new[]
        {
            (new UiRect(862, 0, 46, 32), ChromeHit.MinButton),
            (new UiRect(908, 0, 46, 32), ChromeHit.MaxButton),
            (new UiRect(954, 0, 46, 32), ChromeHit.CloseButton),
        };
        public IReadOnlyList<UiRect> TabBounds { get; } = new[] { new UiRect(0, 0, 200, 32) };
        public IReadOnlyList<UiRect> TabActionBounds { get; } = Array.Empty<UiRect>();
        public UiRect CaptionBounds => new(0, 0, 1000, 32);
        public void SetHot(ChromeHit? button) { }
        public void SetPressed(ChromeHit? button) { }
        public void NativeMiddleClick() { }
        public bool MiddleClickEnabled { get; set; } = true;
    }

    // Maximised window-relative rects: the content (and its buttons) sit 8 DIPs inside the outer window
    // because window.Padding = OffScreenMargin; the native window rect overhangs the monitor by the same inset.
    private sealed class MaximisedBar : ICaptionButtonRects
    {
        public IReadOnlyList<(UiRect Bounds, ChromeHit Kind)> CaptionButtons { get; } = new[]
        {
            (new UiRect(870, 8, 46, 32), ChromeHit.MinButton),
            (new UiRect(916, 8, 46, 32), ChromeHit.MaxButton),
            (new UiRect(962, 8, 46, 32), ChromeHit.CloseButton),
        };
        public IReadOnlyList<UiRect> TabBounds { get; } = new[] { new UiRect(8, 8, 200, 32) };
        public IReadOnlyList<UiRect> TabActionBounds { get; } = Array.Empty<UiRect>();
        public UiRect CaptionBounds => new(8, 8, 1000, 32);
        public void SetHot(ChromeHit? button) { }
        public void SetPressed(ChromeHit? button) { }
        public void NativeMiddleClick() { }
    }

    // Points in DIPs from the window origin, mapped to physical screen pixels for a window at (100, 50).
    [Theory]
    [InlineData(1.0, 2, 300, NativeHitCodes.HtLeft)]
    [InlineData(1.0, 2, 2, NativeHitCodes.HtTopLeft)]
    [InlineData(1.0, 998, 598, NativeHitCodes.HtBottomRight)]
    [InlineData(1.0, 500, 2, NativeHitCodes.HtTop)]
    [InlineData(1.0, 500, 20, NativeHitCodes.HtCaption)]
    [InlineData(1.0, 100, 20, NativeHitCodes.HtClient)]
    [InlineData(1.0, 880, 20, NativeHitCodes.HtMinButton)]
    [InlineData(1.0, 930, 20, NativeHitCodes.HtMaxButton)]
    [InlineData(1.0, 970, 20, NativeHitCodes.HtClose)]
    [InlineData(1.0, 500, 300, NativeHitCodes.HtClient)]
    [InlineData(1.5, 5, 300, NativeHitCodes.HtLeft)]       // 7.5 px < 9 px border at 150%
    [InlineData(1.5, 7, 300, NativeHitCodes.HtClient)]     // 10.5 px: past the border
    [InlineData(1.5, 930, 20, NativeHitCodes.HtMaxButton)]
    [InlineData(1.5, 500, 20, NativeHitCodes.HtCaption)]
    [InlineData(1.5, 996, 300, NativeHitCodes.HtRight)]
    [InlineData(1.0, 100, 0, NativeHitCodes.HtTop)]        // top row over a tab resizes, as in WPF
    [InlineData(1.0, 880, 0, NativeHitCodes.HtMinButton)]  // caption buttons win over the resize border
    [InlineData(1.0, 999, 0, NativeHitCodes.HtClose)]      // the top-right corner pixel is Close
    [InlineData(1.0, 999, 20, NativeHitCodes.HtClose)]
    [InlineData(1.5, 999, 0, NativeHitCodes.HtClose)]
    public void NormalWindowMapsPointToCode(double scale, double x, double y, int expected)
    {
        var rect = new PixelRect(100, 50, (int)(1000 * scale), (int)(600 * scale));
        var screen = new PixelPoint(100 + (int)(x * scale), 50 + (int)(y * scale));
        Assert.Equal(expected, NcHitTestHook.HitCode(screen, rect, scale, false, default, new Bar()));
    }

    // Maximised: the window overhangs its monitor by 8 DIPs per side; no resize edge; points and rects share window-relative space.
    [Theory]
    [InlineData(1.0, 2, 300, NativeHitCodes.HtClient)]
    [InlineData(1.0, 500, 2, NativeHitCodes.HtCaption)]
    [InlineData(1.0, 930, 2, NativeHitCodes.HtMaxButton)]
    [InlineData(1.0, 990, 2, NativeHitCodes.HtClose)]
    [InlineData(1.0, 100, 0, NativeHitCodes.HtClient)]     // top row over a tab reaches the tab
    [InlineData(1.0, 999, 0, NativeHitCodes.HtClose)]
    [InlineData(1.5, 930, 2, NativeHitCodes.HtMaxButton)]
    [InlineData(1.5, 2, 2, NativeHitCodes.HtClient)]
    [InlineData(1.5, 500, 40, NativeHitCodes.HtClient)]
    public void MaximisedWindowMapsPointToCode(double scale, double x, double y, int expected)
    {
        var inset = (int)(8 * scale);
        var rect = new PixelRect(-inset, -inset, (int)(1000 * scale) + 2 * inset, (int)(600 * scale) + 2 * inset);
        var screen = new PixelPoint((int)(x * scale), (int)(y * scale));
        Assert.Equal(expected, NcHitTestHook.HitCode(screen, rect, scale, true, new PixelPoint(inset, inset), new MaximisedBar()));
    }

    // Maximised edge strips: the left/top ~8 px of each caption button still hit that button (old inset subtraction mis-hit them).
    [Fact]
    public void MaximisedEdgeStripsHitOwnButton()
    {
        var bar = new MaximisedBar();
        var inset = 8;
        var rect = new PixelRect(-inset, -inset, 1000 + 2 * inset, 600 + 2 * inset);
        int Code(double x, double y) => NcHitTestHook.HitCode(new PixelPoint((int)x, (int)y), rect, 1.0, true, new PixelPoint(inset, inset), bar);
        var cases = new[]
        {
            (new UiRect(870, 8, 46, 32), NativeHitCodes.HtMinButton),
            (new UiRect(916, 8, 46, 32), NativeHitCodes.HtMaxButton),
            (new UiRect(962, 8, 46, 32), NativeHitCodes.HtClose),
        };
        foreach (var (bounds, expected) in cases)
        {
            // Left strip: x = Left+1 (outer-relative) -> screen x = Left+1-8; sweep y through the button height.
            for (var dy = 1; dy < 32; dy += 5)
            {
                var outerX = bounds.X + 1;
                var outerY = bounds.Y + dy;
                Assert.Equal(expected, Code(outerX - 8, outerY - 8));
            }
            // Top strip: y = Top+1 (outer-relative); sweep x through the button width.
            for (var dx = 1; dx < 46; dx += 5)
            {
                var outerX = bounds.X + dx;
                var outerY = bounds.Y + 1;
                Assert.Equal(expected, Code(outerX - 8, outerY - 8));
            }
        }
        // Regression points from the report: Close left edge+1 is Close (not Max); Max top edge+1 is Max.
        Assert.Equal(NativeHitCodes.HtClose, Code(962 + 1 - 8, 24 - 8));
        Assert.Equal(NativeHitCodes.HtMaxButton, Code(916 + 23 - 8, 8 + 1 - 8));
    }

    // Middle-click spots: empty caption incl. the top resize band (restored) and the top row (maximised); tabs and buttons are not.
    [Theory]
    [InlineData(500, 20, true)]     // E1: gap right of the tabs
    [InlineData(500, 2, true)]      // E2: top resize band, which answers HTTOP
    [InlineData(500, 0, true)]
    [InlineData(2, 20, false)]      // the leftmost pixels are the first tab
    [InlineData(100, 20, false)]    // a tab
    [InlineData(880, 20, false)]    // E5: minimise
    [InlineData(500, 300, false)]   // client
    public void MiddleClickSpotRule(double x, double y, bool expected)
    {
        var rect = new PixelRect(100, 50, 1000, 600);
        Assert.Equal(expected, NcHitTestHook.IsMiddleClickSpot(new PixelPoint(100 + (int)x, 50 + (int)y), rect, 1, new Bar()));
    }

    [Fact]
    public void MiddleClickSpotMaximisedTopRowIsCaption()
    {
        var inset = 8;
        var rect = new PixelRect(-inset, -inset, 1000 + 2 * inset, 600 + 2 * inset);
        Assert.True(NcHitTestHook.IsMiddleClickSpot(new PixelPoint(500, 0), rect, 1, new MaximisedBar()));
    }

    private static readonly PixelRect Win = new(100, 50, 1000, 600);
    private static PixelPoint At(double x, double y) => new(100 + (int)x, 50 + (int)y);

    [AvaloniaFact]
    public void NativeMiddleClickFiresOnReleaseWithinSlopOnEmptyCaption()
    {
        var hook = NcHitTestHook.AttachIfWindows(new Window())!;
        var bar = new Bar();
        Assert.True(hook.MiddleDown(At(500, 20), Win, 1, bar));
        Assert.True(hook.MiddleUp(At(505, 20), Win, 1, bar));
        Assert.False(hook.MiddleUp(At(505, 20), Win, 1, bar)); // the press is used up
    }

    [AvaloniaFact]
    public void NativeMiddleReleaseOntoTabButtonOrClientDoesNothing()
    {
        var hook = NcHitTestHook.AttachIfWindows(new Window())!;
        var bar = new Bar();
        foreach (var (x, y) in new[] { (100.0, 20.0), (880.0, 20.0), (500.0, 300.0) })
        {
            Assert.True(hook.MiddleDown(At(500, 20), Win, 1, bar));
            Assert.False(hook.MiddleUp(At(x, y), Win, 1, bar));
        }
    }

    [AvaloniaFact]
    public void NativeMiddlePressDoesNothingWhenSettingOff()
    {
        var hook = NcHitTestHook.AttachIfWindows(new Window())!;
        var bar = new Bar { MiddleClickEnabled = false };
        Assert.False(hook.MiddleDown(At(500, 20), Win, 1, bar));
        Assert.False(hook.MiddleUp(At(500, 20), Win, 1, bar));
    }

    // 8 DIPs at 150% is 12 physical pixels: 12 px away still clicks, 13 px does not.
    [AvaloniaFact]
    public void NativeMiddleSlopIsEightDipsAtScale()
    {
        var hook = NcHitTestHook.AttachIfWindows(new Window())!;
        var bar = new Bar();
        var rect = new PixelRect(100, 50, 1500, 900);
        Assert.True(hook.MiddleDown(new PixelPoint(850, 80), rect, 1.5, bar));
        Assert.True(hook.MiddleUp(new PixelPoint(862, 80), rect, 1.5, bar));
        Assert.True(hook.MiddleDown(new PixelPoint(850, 80), rect, 1.5, bar));
        Assert.False(hook.MiddleUp(new PixelPoint(863, 80), rect, 1.5, bar));
    }

    [AvaloniaFact]
    public void TwoWindowsKeepIndependentMiddleClickState()
    {
        var a = NcHitTestHook.AttachIfWindows(new Window())!;
        var b = NcHitTestHook.AttachIfWindows(new Window())!;
        var bar = new Bar();
        Assert.True(a.MiddleDown(At(500, 20), Win, 1, bar));
        Assert.False(b.MiddleUp(At(500, 20), Win, 1, bar)); // b never saw a press
        Assert.True(a.MiddleUp(At(500, 20), Win, 1, bar));
    }

    [Fact]
    public void WithoutTitleBarOnlyEdgesAreAnswered()
    {
        var rect = new PixelRect(0, 0, 1000, 600);
        Assert.Equal(NativeHitCodes.HtLeft, NcHitTestHook.HitCode(new PixelPoint(1, 300), rect, 1, false, default, null));
        Assert.Equal(0, NcHitTestHook.HitCode(new PixelPoint(500, 10), rect, 1, false, default, null));
    }

    // Full screen: the hidden caption row's stale rectangles must not claim the top strip, the caption buttons or the edges.
    [Theory]
    [InlineData(500, 20)]   // caption
    [InlineData(970, 20)]   // Close
    [InlineData(930, 20)]   // Maximise (Snap Layouts)
    [InlineData(500, 2)]    // top resize edge
    [InlineData(1, 300)]    // left resize edge
    [InlineData(999, 0)]    // top-right corner
    public void FullScreenAnswersClientEverywhere(int x, int y)
    {
        var rect = new PixelRect(100, 50, 1000, 600);
        var point = new PixelPoint(100 + x, 50 + y);
        Assert.Equal(NativeHitCodes.HtClient, NcHitTestHook.HitCode(point, rect, 1, false, default, new Bar(), true));
        Assert.NotEqual(NativeHitCodes.HtClient, NcHitTestHook.HitCode(point, rect, 1, false, default, new Bar())); // leaving full screen restores the hit test
    }

    [AvaloniaFact]
    public void FullScreenIgnoresMoveAndMaximiseAndExitRestoresThem()
    {
        var window = new Window { WindowState = WindowState.FullScreen };
        var chrome = new TabForge.Avalonia.Platform.AvaloniaWindowChrome(window);
        chrome.ToggleMaximise();
        Assert.Equal(WindowState.FullScreen, window.WindowState);
        window.WindowState = WindowState.Normal;
        chrome.ToggleMaximise();
        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    [AvaloniaFact]
    public void FullScreenHidesTheCaptionAreaAndExitRestoresIt()
    {
        var window = new Window();
        TitleBarView.For(window, new TabForge.Avalonia.Views.TabStripView(new TabForge.Services.TabSettings()), new MainWindowCommands(window, () => { }));
        window.WindowState = WindowState.FullScreen;
        Assert.Equal(0, window.ExtendClientAreaTitleBarHeightHint);
        window.WindowState = WindowState.Normal;
        Assert.Equal(TabForge.Platform.Theme.ChromeMetrics.CaptionRowHeight, window.ExtendClientAreaTitleBarHeightHint);
    }

    [AvaloniaFact]
    public void HookDetachesOnCloseAndIsCollectable()
    {
        // The window holds no reference to the hook after close (headless keeps closed windows itself, so the hook is what is watched).
        var (hooked, attachedAfterClose, weak) = OpenAndClose();
        for (var i = 0; i < 5 && weak.IsAlive; i++) { global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.False(weak.IsAlive);
        Assert.True(hooked);
        Assert.False(attachedAfterClose); // headless has no HWND; on Windows a real window detaches here too
    }

    [Theory]
    [InlineData(1000, 1200, true)]
    [InlineData(1000, 1500, true)]
    [InlineData(1000, 3000, true)]
    [InlineData(1000, 3001, false)]
    [InlineData(1000, 999, false)]
    [InlineData(-1, 5000, false)]
    public void LoneMiddleRelease_IsAcceptedOnlyShortlyAfterActivation(long activatedAt, long now, bool accepted) =>
        Assert.Equal(accepted, NcHitTestHook.AcceptsLoneRelease(now, activatedAt));

    // A press on empty caption opens the tab at once (first try, even right after activation): the matching release only swallows, never fires again.
    [AvaloniaFact]
    public void MiddlePressOpensTabOnceAndReleaseSwallows()
    {
        var hook = NcHitTestHook.AttachIfWindows(new Window())!;
        var bar = new Bar();
        Assert.True(hook.NoteMiddlePress(At(500, 20), Win, 1, bar)); // E1: gap right of the tabs opens on press
        Assert.False(hook.NoteMiddlePress(At(500, 20), Win, 1, bar)); // E2: one press fires once, even if two messages arrive
        Assert.True(hook.NoteMiddleRelease()); // the release is swallowed, not fired
        Assert.False(hook.NoteMiddleRelease()); // nothing pending: nothing to swallow
        Assert.True(hook.NoteMiddlePress(At(500, 2), Win, 1, bar)); // E2: top resize band is empty caption too
        Assert.True(hook.NoteMiddleRelease());
    }

    [AvaloniaFact]
    public void MiddlePressOnTabButtonOrClientOpensNothing()
    {
        var hook = NcHitTestHook.AttachIfWindows(new Window())!;
        var bar = new Bar();
        foreach (var (x, y) in new[] { (100.0, 20.0), (880.0, 20.0), (500.0, 300.0) })
        {
            Assert.False(hook.NoteMiddlePress(At(x, y), Win, 1, bar)); // E3: tab, button, client stay untouched
            Assert.False(hook.NoteMiddleRelease());
        }
    }

    // Both hit paths read one rule: an empty-caption pixel answers HtCaption (or a resize edge on the border) and is a middle-click spot.
    [Theory]
    [InlineData(500, 20, true)]
    [InlineData(500, 2, true)]
    [InlineData(100, 20, false)]
    [InlineData(880, 20, false)]
    public void EmptyCaptionRuleAgreesWithMiddleClickSpot(double x, double y, bool expected)
    {
        var bar = new Bar();
        var point = new UiPoint(x, y);
        Assert.Equal(expected, ChromeHitRules.IsEmptyCaption(point, bar.TabBounds, bar.TabActionBounds, bar.CaptionButtons, bar.CaptionBounds));
        Assert.Equal(expected, NcHitTestHook.IsMiddleClickSpot(At(x, y), Win, 1, bar));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (bool Hooked, bool AttachedAfterClose, WeakReference Hook) OpenAndClose()
    {
        var window = new Window();
        var hook = NcHitTestHook.AttachIfWindows(window);
        window.Show();
        window.Close();
        return (hook is not null, hook?.IsAttached ?? true, new WeakReference(hook));
    }
}
