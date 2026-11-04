using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Menus;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard right-click menu check: each row runs and flips its setting, the Settings and Find scale rows call their doors,
/// and the checkable rows show the live value.
/// Does not own: the menu widgets (ContextMenuBuilder) or the settings file.
/// Tests: FretboardContextMenu.
/// </summary>
public class FretboardContextMenuTests
{
    /// <summary>Counts what the menu's doors and the settings saves received.</summary>
    private sealed class Doors
    {
        public int Saves;
        public int OpenedSettings;
        public int FoundScale;
    }

    private static FretboardContextMenu MenuFor(AppSettings settings, Doors doors) =>
        new(() => settings, () => doors.Saves++, () => doors.OpenedSettings++, () => null);

    private static List<MenuSpec> Flatten(IEnumerable<MenuSpec> rows) =>
        rows.SelectMany(r => new[] { r }.Concat(r.Children is null ? Array.Empty<MenuSpec>() : Flatten(r.Children))).ToList();

    /// <summary>Each command row that flips a setting, its argument, and how to read it; the rows run in this order. "Show this track as" changes its
    /// track's view, not a setting, so FretboardShowTrackAsTests covers it.</summary>
    private static readonly (string Id, string? Arg, Func<AppSettings, object?> Read)[] Entries =
    {
        (FretboardContextMenu.ScaleId, "C Major", s => s.Editing.ScaleHighlight),
        (FretboardContextMenu.ClearScaleId, null, s => s.Editing.ScaleHighlight),
    };

    [AvaloniaFact]
    public void EachCommandRowFlipsItsSetting()
    {
        var settings = new AppSettings();
        var doors = new Doors();
        var menu = MenuFor(settings, doors);
        foreach (var (id, arg, read) in Entries)
        {
            var before = read(settings);
            menu.Run(id, arg);
            Assert.NotEqual(before, read(settings));
        }
        Assert.Equal(Entries.Length, doors.Saves);
    }

    [AvaloniaFact]
    public void SettingsAndFindScaleRowsCallTheirDoors()
    {
        var settings = new AppSettings();
        var doors = new Doors();
        var menu = new FretboardContextMenu(() => settings, () => doors.Saves++, () => doors.OpenedSettings++, () => null);
        menu.Attach(new Border(), () => InstrumentPanelKind.Fretboard, () => doors.FoundScale++);
        menu.Run(FretboardContextMenu.SettingsId, null);
        menu.Run(FretboardContextMenu.FindScaleId, null);
        Assert.Equal(1, doors.OpenedSettings);
        Assert.Equal(1, doors.FoundScale);
        Assert.Equal(0, doors.Saves);
    }

    [AvaloniaFact]
    public void CheckableRowsShowTheLiveValue()
    {
        var settings = new AppSettings();
        var menu = MenuFor(settings, new Doors());
        var before = Flatten(menu.RowsFor(InstrumentPanelKind.Fretboard)).Single(r => r.Id == FretboardContextMenu.ScaleId && r.Arg == "C Major");
        Assert.False(before.Checked);
        menu.Run(FretboardContextMenu.ScaleId, "C Major");
        var after = Flatten(menu.RowsFor(InstrumentPanelKind.Fretboard)).Single(r => r.Id == FretboardContextMenu.ScaleId && r.Arg == "C Major");
        Assert.True(after.Checked);
    }

    /// <summary>The menu keeps the view, the scale and the door to the Fretboard settings window; everything else (note names, look-ahead,
    /// left-handed, lock, position, dock) lives in that window once, so none of it repeats here.</summary>
    [AvaloniaFact]
    public void TheMenuHoldsOnlyTheViewTheScaleAndTheSettingsDoor()
    {
        var menu = MenuFor(new AppSettings(), new Doors());
        var fretboardIds = Flatten(menu.RowsFor(InstrumentPanelKind.Fretboard)).Select(r => r.Id).ToList();
        var drumIds = Flatten(menu.RowsFor(InstrumentPanelKind.Drums)).Select(r => r.Id).ToList();
        Assert.Contains(FretboardContextMenu.ViewId, fretboardIds);
        Assert.Contains(FretboardContextMenu.ScaleId, fretboardIds);
        Assert.Contains(FretboardContextMenu.SettingsId, fretboardIds);
        Assert.DoesNotContain(FretboardContextMenu.ScaleId, drumIds);
        Assert.All(fretboardIds.Where(id => id is not null), id => Assert.Contains(id, new[]
        {
            FretboardContextMenu.ViewId, FretboardContextMenu.ScaleId, FretboardContextMenu.FindScaleId, FretboardContextMenu.ClearScaleId, FretboardContextMenu.SettingsId,
        }));
    }
}
