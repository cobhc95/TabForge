using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Shell.Win32;
using TabForge.Platform;
using TabForge.Shell;

namespace TabForge.Avalonia.Shell;

// Owns: the one place that picks the operating system mode and the IWindowChrome for a window: native move and resize on Windows,
//       the system buttons on macOS, client-side decorations on Linux.
// Does not own: the implementations or the title bar.
// Tests: TabForge.Avalonia.Tests.TitleBarRectsTests, ChromeModeTests.
/// <summary>Creates the window chrome for the running operating system.</summary>
public static class WindowChromeFactory
{
    public static ChromeOs CurrentOs { get; } =
        OperatingSystem.IsWindows() ? ChromeOs.Windows : OperatingSystem.IsMacOS() ? ChromeOs.MacOs : ChromeOs.Linux;

    // Key: the window (weak); ceiling: one adapter per open window; UI thread.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, IWindowChrome> Chromes = new();

    /// <summary>The window's chrome: the same instance for every caller, so the title bar and the hit-test hook share it.</summary>
    public static IWindowChrome Create(Window window) => Chromes.GetValue(window, Make);

    private static IWindowChrome Make(Window window) => CurrentOs switch
    {
        ChromeOs.Windows => new WindowsChromeAdapter(window),
        ChromeOs.MacOs => new MacChromeAdapter(window),
        _ => new LinuxChromeAdapter(window),
    };
}
