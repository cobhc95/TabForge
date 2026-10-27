using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: opening the scale finder from the Tools menu and from the fretboard's Scales button: the search scope (the score's selection, else the loop
//   area, else the whole song), the track it searches (the track the score shows, which is the selected track), the "Root Scale" highlight it
//   preselects, and writing the chosen highlight to the editing settings and saving them.
// Does not own: the finder window and its search (ScaleFinderWindow, ScaleFinderPresenter), the menu rows (FretboardContextMenu, MainMenuTable),
//   the highlight's drawing (InstrumentPanelView).
// Tests: TabForge.Avalonia.Tests.ScaleFinderWindowTests, TabForge.Avalonia.Tests.FretboardScaleFinderTests.
// Threads: UI thread only.
public sealed class MainWindowScaleFinder
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Tools.ScaleFinder" };

    private readonly MainWindowTabs _songs;
    private readonly ScorePreviewView _score;
    private readonly Func<SelectionModel> _loop;
    private readonly Func<EditingSettings> _editing;
    private readonly Action _save;
    private readonly Window _owner;

    /// <param name="songs">The tabs; the finder searches the shown song.</param>
    /// <param name="score">The score; its editor gives the selected track and the selection.</param>
    /// <param name="loop">The shown song's selection, whose loop region is searched when the score has no selection.</param>
    /// <param name="editing">The editing settings the highlight is read from and written to.</param>
    /// <param name="save">Saves the settings after a highlight is chosen.</param>
    /// <param name="owner">The window the finder opens over.</param>
    public MainWindowScaleFinder(MainWindowTabs songs, ScorePreviewView score, Func<SelectionModel> loop, Func<EditingSettings> editing, Action save, Window owner)
    {
        _songs = songs;
        _score = score;
        _loop = loop;
        _editing = editing;
        _save = save;
        _owner = owner;
    }

    /// <summary>Registers the Tools menu's Scale finder command.</summary>
    public void Register(MainWindowCommands commands) => commands.Add("Tools.ScaleFinder", () => _ = OpenAsync());

    /// <summary>Opens the finder on the shown song and the selected track, preselecting the current highlight; a chosen highlight is saved.</summary>
    public async Task OpenAsync()
    {
        var project = _songs.Active.Project;
        var track = project.Tracks.Count > 0 ? _score.Editor.Track : null;
        var finder = new ScaleFinderWindow(project, track, SearchRange(), _editing().ScaleHighlight);
        await finder.ShowDialog(_owner);
        if (ApplyChoice(_editing(), finder.Highlight)) _save();
    }

    /// <summary>Writes the highlight the finder closed with: "Off" clears it, a "Root Scale" sets it, null (closed without a choice) changes nothing.</summary>
    /// <returns>True when the editing settings changed.</returns>
    internal static bool ApplyChoice(EditingSettings editing, string? chosen)
    {
        if (chosen is null) return false;
        editing.ScaleHighlight = chosen == ScaleFinderPresenter.OffHighlight ? null : chosen;
        return true;
    }

    /// <summary>The passage the finder searches: the score's selection, else the loop region, else null (the whole song).</summary>
    internal ScaleFinderPresenter.SearchRange? SearchRange()
    {
        if (_score.Editor.HasSelection)
        {
            var (startMeasure, startCell, endMeasure, endCell) = _score.Editor.SelectionCellRange;
            return new ScaleFinderPresenter.SearchRange(startMeasure, startCell, endMeasure, endCell, BarsLabel(startMeasure, endMeasure));
        }
        if (_loop().LoopBars is { } bars)
            return new ScaleFinderPresenter.SearchRange(bars.Start, 0, bars.End, int.MaxValue - 1, BarsLabel(bars.Start, bars.End));
        return null;
    }

    /// <summary>The scope's name as the finder shows it: "bar 3" or "bars 3–5" (bar numbers count from one).</summary>
    private static string BarsLabel(int startMeasure, int endMeasure) =>
        startMeasure == endMeasure ? $"bar {startMeasure + 1}" : $"bars {startMeasure + 1}–{endMeasure + 1}";
}
