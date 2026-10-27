using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;

namespace TabForge.Avalonia;

// Owns: the Bar menu rows that move the cursor (First bar, Last bar, Go to a bar number or section title), the three bar edits (Empty bar,
//   Triplet feel, Clef), Score information (the song's credits and texts dialog) and the screen reader rows (the cursor's bar and position
// announced on the status notice), bound to the shown score.
// Does not own: the menu rows (MainMenuTable in Core), the commands table (MainWindowCommands), the edits (ScorePreviewEditor, the palette tool
//   gp:triplet_feel), the dialog (ScoreInfoWindow).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (every row of the Bar menu is handled or disabled).
/// <summary>The Bar menu's cursor, bar-edit and score information commands.</summary>
public static class MainWindowBarMenu
{
    private const string GoToTitle = "Go to bar";
    private const string GoToLabel = "Bar number or section name:";
    private const string NoMatchText = "No matching bar or section";
    private const string EmptiedText = "Bar emptied";
    private const string ScoreInfoUpdatedText = "Score info updated";
    private const string TripletFeelToolId = "gp:triplet_feel";

    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Bar.First", "Bar.Last", "Bar.GoTo", "Bar.EmptyBar", "Bar.TripletFeel", "Bar.Clef", "Bar.ScoreInfo", ReadBarCommandId, ReadPositionCommandId };

    /// <summary>The screen reader rows: the current bar and the cursor position.</summary>
    private const string ReadBarCommandId = "Reader.ReadBar";
    private const string ReadPositionCommandId = "Reader.ReadPosition";

    /// <param name="score">The score of the shown song.</param>
    /// <param name="owner">The window the Go to prompt and the Score information dialog open over.</param>
    public static void Register(MainWindowCommands commands, ScorePreviewView score, Window owner)
    {
        commands.Add("Bar.First", () => MoveToBar(score, 0));
        commands.Add("Bar.Last", () => MoveToLastWrittenBeat(score));
        commands.Add("Bar.GoTo", () => _ = GoToAsync(score, owner));
        commands.Add("Bar.EmptyBar", () => { score.Editor.Effects.EmptyBar(); score.Editor.Notice?.Invoke(EmptiedText); });
        commands.Add("Bar.TripletFeel", () => score.Run(TripletFeelToolId));
        commands.Add("Bar.Clef", () => score.Edit(editor => editor.CycleClef()));
        commands.Add("Bar.ScoreInfo", () => _ = EditScoreInfoAsync(score, owner));
        commands.Add(ReadBarCommandId, () => score.Editor.Notice?.Invoke(DescribeBar(score)));
        commands.Add(ReadPositionCommandId, () => score.Editor.Notice?.Invoke(DescribePosition(score)));
    }

    // Bar.GoTo: a number is a bar (clamped to the song); any other text is the first section whose title contains it.
    private static async Task GoToAsync(ScorePreviewView score, Window owner)
    {
        var typed = await DialogLauncher.Ask<TextPromptWindow, string?>(owner, () => new TextPromptWindow(GoToTitle, GoToLabel, (score.Editor.SelectedMeasure + 1).ToString()));
        if (typed is null) return;
        if (int.TryParse(typed, out var number))
        {
            MoveToBar(score, Math.Clamp(number - 1, 0, LastBar(score)));
            return;
        }
        var marker = score.Editor.Project!.Markers.FirstOrDefault(m => m.Title.Contains(typed, StringComparison.OrdinalIgnoreCase));
        if (marker is null)
        {
            score.Editor.Notice?.Invoke(NoMatchText);
            return;
        }
        MoveToBar(score, Math.Clamp(marker.MeasureIndex, 0, LastBar(score)));
    }

    // Bar.ScoreInfo: OK writes the edited texts to the song as one edit and reports it; Cancel changes nothing.
    private static async Task EditScoreInfoAsync(ScorePreviewView score, Window owner)
    {
        var project = score.Editor.Project;
        if (project is null) return;
        var values = await DialogLauncher.Ask<ScoreInfoWindow, ScoreInfoValues?>(owner, () => new ScoreInfoWindow(ScoreInfoValues.From(project)));
        if (values is null) return;
        score.Edit(editor => { values.ApplyTo(editor.Project!); return true; });
        score.Editor.Notice?.Invoke(ScoreInfoUpdatedText);
    }

    // The cursor goes to the first cell of the bar on the same string, with no range selected.
    internal static void MoveToBar(ScorePreviewView score, int bar)
    {
        score.Editor.ClearSelection();
        score.Editor.SetPosition(bar, 0, score.Editor.Cursor?.String ?? 0);
    }

    /// <summary>Bar.Last: the cursor goes to the last written beat of the last bar (the first bar when the track has none), as GP5.
    /// Playback keeps playing: moving the cursor only seeks.</summary>
    internal static void MoveToLastWrittenBeat(ScorePreviewView score)
    {
        var editor = score.Editor;
        if (editor.LastWrittenBeat() is { } last)
        {
            editor.ClearSelection();
            editor.SetPosition(last.Measure, last.Cell, editor.Cursor?.String ?? 0);
        }
        else MoveToBar(score, 0);
    }

    private static int LastBar(ScorePreviewView score) => Math.Max(0, score.Editor.BarCount - 1);

    /// <summary>Reader.ReadBar: the cursor's bar with its sounded beats and its section, announced on the status notice.</summary>
    internal static string DescribeBar(ScorePreviewView score)
    {
        var editor = score.Editor;
        var bar = Math.Max(0, editor.SelectedMeasure);
        var beats = 0;
        if (editor.Project?.Tracks is { } tracks && editor.SelectedTrackIndex < tracks.Count)
        {
            var track = tracks[editor.SelectedTrackIndex];
            if (bar < track.Measures.Count) beats = track.Measures[bar].CellsForVoice(editor.ActiveVoiceIndex).Count(c => c.Notes.Count > 0 || c.IsRest);
        }
        var trackName = editor.Track?.Name ?? "";
        var section = SectionAt(editor.Project?.Markers, bar);
        return $"Bar {bar + 1}" + (trackName.Length > 0 ? $", {trackName}" : "") + $": {beats} beat{(beats == 1 ? "" : "s")}" + (section is null ? "" : $", section {section}");
    }

    /// <summary>Reader.ReadPosition: the track, bar, string and section of the cursor, announced on the status notice.</summary>
    internal static string DescribePosition(ScorePreviewView score)
    {
        var editor = score.Editor;
        var bar = Math.Max(0, editor.SelectedMeasure);
        var trackName = editor.Track?.Name ?? "";
        var section = SectionAt(editor.Project?.Markers, bar);
        return (trackName.Length > 0 ? $"Track {trackName}, " : "") + $"bar {bar + 1}, string {editor.SelectedString + 1}" + (section is null ? "" : $", section {section}");
    }

    /// <summary>The title of the section holding <paramref name="bar"/> (the last marker at or before it), or null when there is none.</summary>
    private static string? SectionAt(System.Collections.Generic.IReadOnlyList<TabForge.Models.MarkerModel>? markers, int bar)
    {
        string? title = null;
        if (markers is null) return null;
        foreach (var marker in markers.OrderBy(m => m.MeasureIndex))
        {
            if (marker.MeasureIndex > bar) break;
            title = marker.Title;
        }
        return title;
    }
}
