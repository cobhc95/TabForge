using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Visualization;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the fretboard, keyboard and drum-pad right-click menu: its rows (the per-track view, the scale, the practice toggles, the lock,
//   the top or bottom position and the Fretboard settings door) built from the live settings each time it opens, the command ids those
//   rows run, and writing each toggle or choice to the settings and saving them, so SettingsChanged applies it live.
// Does not own: the menu item widgets (ContextMenuBuilder), the settings file (MainWindowSettings), the scale finder (the Scales button
//   runs it, reached through the find-scale callback), the Preferences window (the settings callback opens it).
// Tests: TabForge.Avalonia.Tests.FretboardContextMenuTests.
public sealed class FretboardContextMenu : IMenuCommandRunner
{
    public const string ViewId = "Fretboard.View", ScaleId = "Fretboard.Scale", FindScaleId = "Fretboard.FindScale", ClearScaleId = "Fretboard.ClearScale", SettingsId = HotkeyCatalog.FretboardSettings;

    private const string ShowThisTrackAs = "Show this track as", Scale = "Scale", FindScale = "Find scale…", ClearScale = "Clear scale";
    private const string Settings = "Fretboard settings…";

    private readonly Func<AppSettings> _settings;
    private readonly Action _save;
    private readonly Action _openSettings;
    private readonly Func<TrackModel?> _selectedTrack;
    /// <summary>The view each track chose from "Show this track as". Key: the track id (an undo replaces the TrackModel, not its id); ceiling: one entry per track the user changed this session; thread: the UI thread.</summary>
    private readonly Dictionary<Guid, string> _viewOfTrack = new();
    private Action _findScale = () => { };

    /// <param name="settings">The live settings the rows read and the commands write.</param>
    /// <param name="save">Saves the settings and raises SettingsChanged.</param>
    /// <param name="openSettings">Opens the Fretboard settings window (which links on to Preferences).</param>
    /// <param name="selectedTrack">The track the panel shows; "Show this track as" changes that track only (null: no track, nothing changes).</param>
    public FretboardContextMenu(Func<AppSettings> settings, Action save, Action openSettings, Func<TrackModel?> selectedTrack)
    {
        _settings = settings;
        _save = save;
        _openSettings = openSettings;
        _selectedTrack = selectedTrack;
    }

    /// <summary>Raised when a track's view changes, so the panel shows it.</summary>
    public event Action? TrackViewChanged;

    /// <summary>Saves the fretboard's horizontal place: a dragged board lands here, as the Position rows set it.</summary>
    public void SetFretboardPosition(FretboardHorizontalPosition place)
    {
        _settings().Appearance.FretboardPosition = place.ToString();
        _save();
    }

    /// <summary>The view a track's panel shows: the view the track chose from the menu, else the Fretboard settings' default.</summary>
    public string ViewFor(TrackModel? track, string settingDefault) =>
        track is not null && _viewOfTrack.TryGetValue(track.Id, out var own) ? own : settingDefault;

    /// <summary>View > Switch instrument view: the selected track shows the next view (fretboard, keyboard, drum pads, match the instrument).</summary>
    public void CycleInstrumentView()
    {
        var order = new[] { InstrumentViews.Fretboard, InstrumentViews.Keyboard, InstrumentViews.Drums, InstrumentViews.MatchInstrument };
        var track = _selectedTrack();
        var at = Array.IndexOf(order, ViewFor(track, _settings().Editing.InstrumentView));
        Run(ViewId, order[(at + 1) % order.Length]);
    }

    /// <summary>
    /// Right-click on <paramref name="target"/> opens the menu for the panel's kind; <paramref name="findScale"/> is the Scales button's action
    /// (null keeps the one an earlier attach gave). <paramref name="track"/> names the track this target shows when it is not the pane's own
    /// selected track: a Band card passes its own track, so "Show this track as" acts on that card alone.
    /// </summary>
    public void Attach(Control target, Func<InstrumentPanelKind> kind, Action? findScale = null, Func<TrackModel?>? track = null)
    {
        if (findScale is not null) _findScale = findScale;
        IMenuCommandRunner owner = track is null ? this : new TrackMenu(this, track);
        target.ContextRequested += (_, e) =>
        {
            if (!e.TryGetPosition(target, out var _)) return;
            ContextMenuBuilder.Show(target, RowsFor(kind(), track is null ? _selectedTrack() : track()), owner);
            e.Handled = true;
        };
    }

    /// <summary>The rows for a panel of <paramref name="kind"/>, each checkable row showing its setting's current value.</summary>
    public IReadOnlyList<MenuSpec> RowsFor(InstrumentPanelKind kind) => RowsFor(kind, _selectedTrack());

    /// <summary>The rows for a panel of <paramref name="kind"/> showing <paramref name="track"/>: the view rows mark that track's own view.</summary>
    internal IReadOnlyList<MenuSpec> RowsFor(InstrumentPanelKind kind, TrackModel? track)
    {
        var settings = _settings();
        var editing = settings.Editing;
        var rows = new List<MenuSpec>
        {
            new() { Header = ShowThisTrackAs, Children = InstrumentViews.All.Select(v => Choice(ViewId, v, v, v == ViewFor(track, editing.InstrumentView))).ToList() },
        };
        // Drum tracks have no scale: the drum map's menu keeps the view rows only.
        if (kind != InstrumentPanelKind.Drums) rows.Add(new() { Header = Scale, Children = ScaleRows(editing.ScaleHighlight) });
        // Note names, the look-ahead toggles, left-handed, lock, position and dock live in the Fretboard settings window now, once.
        rows.Add(MenuSpec.Separator());
        rows.Add(new MenuSpec { Id = SettingsId, Header = Settings, ToolTip = "Open the Fretboard settings window: look, notes and markers, layout, strings and capo, scale highlight" });
        return rows;
    }

    /// <summary>The scale rows: each root's submenu of scales, then Find scale and Clear scale.</summary>
    private List<MenuSpec> ScaleRows(string? current)
    {
        var rows = ScaleFinderPresenter.RootChoices.Select(root => new MenuSpec
        {
            Header = root,
            Children = ScaleFinderPresenter.ScaleChoices.Select(name => Choice(ScaleId, name, $"{root} {name}", $"{root} {name}" == current)).ToList(),
        }).ToList();
        rows.Add(MenuSpec.Separator());
        rows.Add(new MenuSpec { Id = FindScaleId, Header = FindScale });
        rows.Add(new MenuSpec { Id = ClearScaleId, Header = ClearScale, Enabled = current is not null });
        return rows;
    }

    private static MenuSpec Choice(string id, string header, string arg, bool selected) =>
        new() { Id = id, Header = header, Arg = arg, Checkable = true, Checked = selected, Radio = true };

    public bool Handles(string commandId) => commandId.StartsWith("Fretboard.", StringComparison.Ordinal);

    public void Run(string commandId, string? argument) => RunFor(_selectedTrack(), commandId, argument);

    /// <summary>Runs a row for <paramref name="track"/>: only "Show this track as" reads it (the view of that one track).</summary>
    internal void RunFor(TrackModel? track, string commandId, string? argument)
    {
        var settings = _settings();
        var editing = settings.Editing;
        switch (commandId)
        {
            case ViewId:
                if (track is { } shown && argument is not null)
                {
                    _viewOfTrack[shown.Id] = argument;
                    TrackViewChanged?.Invoke();
                }
                break;
            case ScaleId: editing.ScaleHighlight = argument; break;
            case ClearScaleId: editing.ScaleHighlight = null; break;
            case FindScaleId: _findScale(); return;
            case SettingsId: _openSettings(); return;
            default: throw new InvalidOperationException($"No handler for command '{commandId}'.");
        }
        _save();
    }

    /// <summary>One attached target's own track: the shared menu builds and runs its rows for that track, so a Band card's board acts on that card.</summary>
    private sealed class TrackMenu(FretboardContextMenu menu, Func<TrackModel?> track) : IMenuCommandRunner
    {
        public bool Handles(string commandId) => menu.Handles(commandId);

        public void Run(string commandId, string? argument) => menu.RunFor(track(), commandId, argument);
    }
}
