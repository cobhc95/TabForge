using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabForge.Avalonia.Views;

namespace TabForge.Avalonia;

// Owns: handing navigation and transport keys pressed in this main window's tool windows to its key table, in the same two steps the main window uses:
//   Space before the window's controls (so a focused button never takes it as a click), every other key after them (so a control or the window's own table keeps what it uses).
// Does not own: which commands are forwarded (NavigationHotkeys), the text-box rule and the commands (MainWindowKeyboard), plug-in editors (PluginEditorHotkeys).
// Tests: TabForge.Avalonia.Tests.NavigationHotkeyForwardingTests.
/// <summary>One per main window, from attach to the window's Closed. UI thread only.</summary>
public sealed class ToolWindowKeyForwarding : IDisposable
{
    private readonly Window _main;
    private readonly MainWindowKeyboard _keyboard;
    private readonly IDisposable _beforeControls, _afterControls;

    public ToolWindowKeyForwarding(Window main, MainWindowKeyboard keyboard)
    {
        _main = main;
        _keyboard = keyboard;
        _beforeControls = InputElement.KeyDownEvent.AddClassHandler<Window>(OnBeforeControls, RoutingStrategies.Tunnel);
        _afterControls = InputElement.KeyDownEvent.AddClassHandler<Window>(OnAfterControls, RoutingStrategies.Bubble);
    }

    /// <summary>True for the windows that keep every key until the user leaves them: the settings windows, and the command palette, which is typed into.</summary>
    public static bool KeepsItsKeys(Type window) =>
        window == typeof(PreferencesWindow) || window == typeof(FretboardSettingsWindow) || window == typeof(CommandPaletteWindow);

    private void OnBeforeControls(Window window, KeyEventArgs e)
    {
        if (e.Key == Key.Space) Forward(window, e);
    }

    private void OnAfterControls(Window window, KeyEventArgs e) => Forward(window, e);

    private void Forward(Window window, KeyEventArgs e)
    {
        if (e.Handled || !IsToolWindowOfMine(window)) return;
        e.Handled = _keyboard.RouteNavigation(e.Key, e.KeyModifiers, window.FocusManager?.GetFocusedElement());
    }

    // A modal dialog keeps its keys. A window with no main window among its owners is served by whichever main window hears it first.
    private bool IsToolWindowOfMine(Window window)
    {
        if (window is MainWindow || window.IsDialog || KeepsItsKeys(window.GetType())) return false;
        var root = window;
        while (root.Owner is Window owner) root = owner;
        return root is not MainWindow || ReferenceEquals(root, _main);
    }

    public void Dispose()
    {
        _beforeControls.Dispose();
        _afterControls.Dispose();
    }
}
