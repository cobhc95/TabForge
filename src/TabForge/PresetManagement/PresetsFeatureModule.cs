using System.Windows;
using TabForge.Services;
using TabForge.Services.Features;
using TabForge.Views.Presets;

namespace TabForge.PresetManagement;

// Owns: what the preset manager adds to the central tables: the Tools.PresetManager hotkey row (no default key), its command and the
//   Tools menu row.
// Does not own: the window (PresetManagerWindow) or the drop-downs' own "Manage presets…" rows (PresetRows).
// Tests: TestPresetManagerWindow, TestFeatureModuleContributions, TestMainMenuTreeGolden.
public sealed class PresetsFeatureModule : IFeatureModule
{
    public const string CommandId = "Tools.PresetManager";

    public string Name => "Preset manager";

    public IReadOnlyList<FeatureHotkey> Hotkeys { get; } = new FeatureHotkey[]
    {
        new(new(CommandId, HotkeyCatalog.CategoryView, "Preset manager", "",
            "Open the preset manager: every saved plug-in preset, FX chain, container, MIDI chain, MIDI processor preset and drum map in one window, to rename, copy, reorder, delete and search (same as Tools > Preset manager)."), "Tools.Tuner"),
    };

    public IReadOnlyList<FeatureMenuRow> MenuRows { get; } = new FeatureMenuRow[]
    {
        new("_Tools", "Tools.Tuner", "Preset manager…", CommandId),
    };

    public IEnumerable<FeatureCommand> Commands(IFeatureHost host)
    {
        yield return new(CommandId, () => PresetManager.Open(host as Window, PresetKind.PluginPreset, null));
    }
}
