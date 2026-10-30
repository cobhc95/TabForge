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
        (FretboardContextMenu.NoteNamesId, null, s => s.Editing.ShowNoteNames),
        (FretboardContextMenu.PreviewId, null, s => s.Editing.PreviewNotesEnabled),
        (FretboardContextMenu.PreviewPreviousId, null, s => s.Editing.PreviewPreviousNotesEnabled),
        (FretboardContextMenu.LeftHandedId, null, s => s.Editing.LeftHanded),
        (FretboardContextMenu.LockId, null, s => s.Appearance.LockInstrumentSize),
        (FretboardContextMenu.PositionId, "Right", s => s.Appearance.FretboardPosition),
        (FretboardContextMenu.DockId, "bottom", s => s.Appearance.FretboardAtBottom),
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
        var before = Flatten(menu.RowsFor(InstrumentPanelKind.Fretboard)).Single(r => r.Id == FretboardContextMenu.NoteNamesId);
        Assert.False(before.Checked);
        menu.Run(FretboardContextMenu.NoteNamesId, null);
        var after = Flatten(menu.RowsFor(InstrumentPanelKind.Fretboard)).Single(r => r.Id == FretboardContextMenu.NoteNamesId);
        Assert.True(after.Checked);
    }

    [AvaloniaFact]
    public void PanelKindDecidesWhichPracticeRowsAppear()
    {
        var menu = MenuFor(new AppSettings(), new Doors());
        var fretboardIds = Flatten(menu.RowsFor(InstrumentPanelKind.Fretboard)).Select(r => r.Id).ToList();
        var keyboardIds = Flatten(menu.RowsFor(InstrumentPanelKind.Keyboard)).Select(r => r.Id).ToList();
        var drumIds = Flatten(menu.RowsFor(InstrumentPanelKind.Drums)).Select(r => r.Id).ToList();
        Assert.Contains(FretboardContextMenu.LeftHandedId, fretboardIds);
        Assert.DoesNotContain(FretboardContextMenu.LeftHandedId, keyboardIds);
        Assert.Contains(FretboardContextMenu.NoteNamesId, keyboardIds);
        Assert.DoesNotContain(FretboardContextMenu.NoteNamesId, drumIds);
    }
}
