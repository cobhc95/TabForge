using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

// Owns: the one place every "Manage presets…" row opens the manager from: the single app-wide window, the kind and key it jumps to, and
//   the stores it was configured with (the settings that hold the bend and tremolo presets). One replaceable opener is the seam a test records instead of opening a window.
// Does not own: the window and its data (PresetManagerWindow, IPresetCatalogue), the stores, or any row's own contents.
// Tests: PresetManagerOpenTests, PresetManagerWindowTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>The app's single preset manager instance and the one way to open it.</summary>
public static class PresetManager
{
    private static PresetManagerWindow? _open;
    private static Func<IPresetStoreFactory?> _stores = () => null;

    /// <summary>Opens the manager; tests replace this to record the owner, kind and key instead of opening a window.</summary>
    internal static Action<Window, PresetCatalogKind, string?> Opener { get; set; } = OpenWindow;

    /// <summary>True while the manager window is open (the drop-down rows re-read their folder when the window comes back).</summary>
    internal static bool IsOpen => _open is { IsVisible: true };

    /// <summary>Takes the stores of every preset kind from the main window, once, when the command is registered (null: the folders only).</summary>
    public static void Configure(Func<IPresetStoreFactory?>? stores) => _stores = stores ?? (() => null);

    /// <summary>Opens the manager on a kind (and plug-in folder or processor type), or brings the open one forward and jumps it there.</summary>
    public static void Open(Window owner, PresetCatalogKind kind, string? key) => Opener(owner, kind, key);

    // The remembered dialog folders of the main window that owns this one (through the owner chain); null without one.
    private static TabForge.Services.DialogFolderMemory? FoldersOf(Window owner)
    {
        for (WindowBase? window = owner; window is not null; window = window.Owner)
            if (window is MainWindow main) return main.Settings?.DialogFolders;
        return null;
    }

    private static void OpenWindow(Window owner, PresetCatalogKind kind, string? key)
    {
        if (_open is { IsVisible: true } shown)
        {
            shown.Show(kind, key);
            if (shown.WindowState == WindowState.Minimized) shown.WindowState = WindowState.Normal;
            shown.Activate();
            return;
        }
        var stores = _stores();
        var window = new PresetManagerWindow(new AvaloniaDialogHost(), kind, key, stores is null ? null : new PresetCataloguePresenter(stores), stores) { Folders = FoldersOf(owner) };
        _open = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_open, window)) _open = null; };
        window.Show(owner);
    }
}
