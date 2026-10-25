using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

// Owns: the one place every "Manage presets…" row opens the manager from: the single app-wide window, the kind and key it jumps to, and
//   the delete setting it was configured with. One replaceable opener is the seam a test records instead of opening a window.
// Does not own: the window and its data (PresetManagerWindow, IPresetCatalogue), the stores, or any row's own contents.
// Tests: PresetManagerOpenTests, PresetManagerWindowTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>The app's single preset manager instance and the one way to open it.</summary>
public static class PresetManager
{
    private static PresetManagerWindow? _open;
    private static Func<bool> _confirmDeletePresets = () => true;
    private static Action<bool> _rememberDeletePresets = _ => { };

    /// <summary>Opens the manager; tests replace this to record the owner, kind and key instead of opening a window.</summary>
    internal static Action<Window, PresetCatalogKind, string?> Opener { get; set; } = OpenWindow;

    /// <summary>True while the manager window is open (the drop-down rows re-read their folder when the window comes back).</summary>
    internal static bool IsOpen => _open is { IsVisible: true };

    /// <summary>The configured "Ask before deleting presets" read and write, for the drop-down rows.</summary>
    internal static Func<bool> ConfirmDeletePresets => _confirmDeletePresets;
    internal static Action<bool> RememberDeletePresets => _rememberDeletePresets;

    /// <summary>Takes the delete setting's read and write from the main window, once, when the command is registered.</summary>
    public static void Configure(Func<bool>? confirmDeletePresets, Action<bool>? rememberDeletePresets)
    {
        _confirmDeletePresets = confirmDeletePresets ?? (() => true);
        _rememberDeletePresets = rememberDeletePresets ?? (_ => { });
    }

    /// <summary>Opens the manager on a kind (and plug-in folder or processor type), or brings the open one forward and jumps it there.</summary>
    public static void Open(Window owner, PresetCatalogKind kind, string? key) => Opener(owner, kind, key);

    private static void OpenWindow(Window owner, PresetCatalogKind kind, string? key)
    {
        if (_open is { IsVisible: true } shown)
        {
            shown.Show(kind, key);
            if (shown.WindowState == WindowState.Minimized) shown.WindowState = WindowState.Normal;
            shown.Activate();
            return;
        }
        var window = new PresetManagerWindow(new AvaloniaDialogHost(), kind, key,
            confirmDeletePresets: _confirmDeletePresets, rememberDeletePresets: _rememberDeletePresets);
        _open = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_open, window)) _open = null; };
        window.Show(owner);
    }
}
