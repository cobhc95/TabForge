using System.Windows;

namespace TabForge;

/// <summary>
/// CI-parity screen mode. The GitHub Windows runner has a small desktop (about 1024x768) at 100% scale; a developer PC is
/// often 4K at 150%. A top-level window that is larger than the work area is clamped by Windows, so a layout test written
/// on the big screen can pass locally and fail on the runner (it happened). Every test window goes through
/// <c>ShowTestWindow</c>, which calls <see cref="CheckWindowFitsWorkArea"/>: a window larger than the work area fails with a clear message
/// (lay the control out at an explicit size without a top-level window instead, as <c>FitStage</c> does).
///
/// Set <c>TABFORGE_SELFTEST_SMALLSCREEN=1</c> to judge against the runner's screen (<see cref="CiWorkAreaWidth"/> x
/// <see cref="CiWorkAreaHeight"/> device-independent pixels, scale 100%) whatever the real screen is. CI sets it.
/// The real display scale cannot be changed from inside the process, so the mode checks window sizes; it cannot make
/// WPF render at 100% on a 150% screen.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Runs an action when disposed (cleanup of generated stand-in files).</summary>
    private sealed class DisposeAction : IDisposable
    {
        private Action? _action;
        public DisposeAction(Action action) => _action = action;
        public void Dispose() { _action?.Invoke(); _action = null; }
    }

    public const string SmallScreenVariable = "TABFORGE_SELFTEST_SMALLSCREEN";
    public const double CiWorkAreaWidth = 1024;
    public const double CiWorkAreaHeight = 728;   // 1024x768 minus the taskbar

    private static bool SmallScreenMode => Environment.GetEnvironmentVariable(SmallScreenVariable) is "1" or "true" or "TRUE";

    /// <summary>The work area test windows are judged against, in device-independent pixels.</summary>
    private static Size TestWorkArea => SmallScreenMode ? new Size(CiWorkAreaWidth, CiWorkAreaHeight)
        : new Size(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);

    /// <summary>The failure text for a window of the given size, or null when it fits.</summary>
    private static string? WindowTooLargeMessage(string owner, double width, double height, Size workArea) =>
        width <= workArea.Width + 0.5 && height <= workArea.Height + 0.5 ? null
        : $"{owner} opens a {width:0}x{height:0} window but the {(SmallScreenMode ? "CI" : "real")} work area is {workArea.Width:0}x{workArea.Height:0}: Windows clamps a window that is larger, so the test passes on a big screen and fails on the CI runner. Lay the control out at an explicit size without a top-level window (see FitStage) or use a smaller window.";

    /// <summary>Records a failure when a shown test window is larger than the work area (nothing is recorded when it fits).</summary>
    private static void CheckWindowFitsWorkArea(Window window, string owner)
    {
        // The main window's own minimum size (1080 wide) is larger than the CI screen by design; the tests that open it check lifetime and
        // ownership, not layout, and the window keeps its minimum on any screen.
        if (window is MainWindow) return;
        // The size the test asked for: after Show the OS may already have clamped ActualWidth/ActualHeight to the screen.
        var width = double.IsNaN(window.Width) ? window.ActualWidth : window.Width;
        var height = double.IsNaN(window.Height) ? window.ActualHeight : window.Height;
        var message = WindowTooLargeMessage(owner, width, height, TestWorkArea);
        if (message is not null) Check($"test window fits the {(SmallScreenMode ? "CI" : "screen")} work area ({owner})", false, message);
    }

    /// <summary>The guard itself: sizes against a work area, the CI default and the message.</summary>
    private static void TestScreenGuard()
    {
        Check("a window inside the work area is accepted", WindowTooLargeMessage("t", 1000, 700, new Size(1024, 728)) is null);
        Check("a window as large as the work area is accepted", WindowTooLargeMessage("t", 1024, 728, new Size(1024, 728)) is null);
        var tooTall = WindowTooLargeMessage("TestX", 900, 1100, new Size(1024, 728));
        Check("a window taller than the work area is rejected, naming the test and the sizes", tooTall is not null && tooTall.Contains("TestX") && tooTall.Contains("900x1100") && tooTall.Contains("1024x728"), tooTall);
        Check("a window wider than the work area is rejected", WindowTooLargeMessage("t", 1100, 600, new Size(1024, 728)) is not null);
        Check("the CI work area is the runner's 1024x728 at 100%", CiWorkAreaWidth == 1024 && CiWorkAreaHeight == 728);
    }
}
