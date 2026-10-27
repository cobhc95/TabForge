using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views.NoteMaps;
using TabForge.Avalonia.Views.Presets;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the drum map editor's map drop-down: a new map opens with its default name and passing checks, the drop-down ends with the
///   Manage row (which opens the drum-map manager and is never picked as a map), and Save still writes the map with that row present.
/// Does not own: the window (DrumMapEditorWindow), the checks (DrumMapDraftPresenter) or the map files (DrumMapLibrary).
/// Tests: this class.
/// </summary>
public class DrumMapEditorWindowTests
{
    [AvaloniaFact]
    public void NewMapOpensWithDefaultNameAndPassesItsChecks()
    {
        using var scratch = Scratch();
        var window = new DrumMapEditorWindow(null, null);

        Assert.Equal("My drum map", DialogTestKit.Named<TextBox>(window, "NameBox").Text);
        Assert.True(DialogTestKit.Named<Button>(window, "SaveButton").IsEnabled);
        Assert.True(window.Check());
        window.Close();
    }

    [AvaloniaFact]
    public void MapDropDownEndsWithManageThatOpensDrumMaps()
    {
        using var scratch = Scratch();
        var window = new DrumMapEditorWindow(null, null);
        window.Show();
        var box = DialogTestKit.Named<ComboBox>(window, "MapBox");
        var last = box.Items.Cast<object>().Last();
        Assert.True(PresetRowText.IsManage(last));
        Assert.Equal(PresetRowText.Manage, ((ComboBoxItem)last).Content);

        var calls = new List<(PresetCatalogKind Kind, string? Key)>();
        var previous = PresetManager.Opener;
        PresetManager.Opener = (_, kind, key) => calls.Add((kind, key));
        try
        {
            box.SelectedItem = last;
            DialogTestKit.RunQueuedUiWork();
        }
        finally { PresetManager.Opener = previous; }

        Assert.Equal((PresetCatalogKind.DrumMap, (string?)null), Assert.Single(calls));
        Assert.True(window.Check());   // the editor still holds its map, not the manage row
        window.Close();
    }

    [AvaloniaFact]
    public void SaveStillWritesTheMapWithTheManageRowPresent()
    {
        using var scratch = Scratch();
        var window = new DrumMapEditorWindow(null, null);
        window.Show();
        DialogTestKit.Named<TextBox>(window, "NameBox").Text = "Manage line kit";
        Assert.True(window.Check());

        FxTestKit.PressButton(window, "SaveButton");
        DialogTestKit.RunQueuedUiWork();

        Assert.NotNull(DrumMapLibrary.Find("Manage line kit"));
    }

    private static IDisposable Scratch()
    {
        var scratch = FxTestKit.ScratchLibrary();
        DrumMapLibrary.Reload();
        return scratch;
    }
}
