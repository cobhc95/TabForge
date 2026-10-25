using Avalonia.Controls;
using Avalonia.Platform;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Shell;

// Owns: the macOS window set-up: the native traffic lights stay (system chrome in a thick title bar) and the window actions are Avalonia's.
// Does not own: the tab inset or hiding our caption buttons (TitleBarView, from ChromeHitRules.LeadingInset) or the title row height.
// Tests: TabForge.Avalonia.Tests.ChromeModeTests. Real macOS behaviour is unproven until phase 7.
/// <summary>The window chrome on macOS.</summary>
public sealed class MacChromeAdapter : AvaloniaWindowChrome
{
    public MacChromeAdapter(Window window) : base(window)
    {
        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome | ExtendClientAreaChromeHints.OSXThickTitleBar;
    }
}
