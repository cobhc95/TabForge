using System.Windows;
using TabForge.Plugins;

namespace TabForge.Views.Presets;

/// <summary>The kinds of user presets. The key of a kind that has one is the plug-in's preset folder name (PluginPreset) or the processor type (MidiProcessor).</summary>
internal enum PresetKind { PluginPreset, FxChain, Container, MidiChain, MidiProcessor, DrumMap }

// Owns: the entry "Manage presets…" at the end of every preset drop-down and the Preset manager command: the store of a preset kind
//   (and key) without any window instance, and opening the one preset manager window on a kind.
// Does not own: the window (PresetManagerWindow), the files (PresetFolderStore, DrumMapStore) or loading a preset.
// Tests: TestPresetManagePanel (StoreOf), TestPresetManagerWindow.
internal static class PresetManager
{
    /// <summary>The store of a kind; null when the kind needs a key and none is given.</summary>
    internal static IPresetStore? StoreOf(PresetKind kind, string? key) => kind switch
    {
        PresetKind.PluginPreset => key is { Length: > 0 } ? PresetFolderStore.Plugin(key) : null,
        PresetKind.FxChain => PresetFolderStore.FxChains(),
        PresetKind.Container => PresetFolderStore.Containers(),
        PresetKind.MidiChain => PresetFolderStore.MidiChains(),
        PresetKind.MidiProcessor => key is { Length: > 0 } ? PresetFolderStore.MidiProcessor(key) : null,
        PresetKind.DrumMap => new DrumMapStore(),
        _ => null,
    };

    /// <summary>Opens the preset manager on that kind (and plug-in or processor type); an open manager comes to the front and jumps there.</summary>
    internal static void Open(Window? owner, PresetKind kind, string? key)
    {
        if (Application.Current?.Windows.OfType<PresetManagerWindow>().FirstOrDefault() is { } open)
        {
            open.Show(kind, key);
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        new PresetManagerWindow(owner, kind, key).Show();
    }
}
