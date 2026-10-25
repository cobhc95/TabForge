using System;
using System.Collections.Generic;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia;

// Owns: the Range hotkeys that act on the bars selected in the score, on every track: Range.Clear (empty the bars, the bar count stays),
//   Range.Remove (delete the bars and close the gap), Range.InsertBefore and Range.InsertAfter (insert empty bars beside the selection).
//   Each one is one undo step through the score editor's RunEdit, and the fill-with-rests choice is the editor's own tool state.
// Does not own: the gap rules (Core TimelineRangeEdit, the one path timeline and score share; refusals and the meter rule live there), the selection (ScorePreviewEditor), Range.Delete (its question stays with the
//   Delete flow, not handled here), the score view (ScorePreviewView).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (every id in Commands is handled).
public static class MainWindowRangeCommands
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Range.Clear", "Range.Remove", "Range.InsertBefore", "Range.InsertAfter" };

    /// <summary>The track index that makes a gap edit run on every track.</summary>
    private const int AllTracks = -1;

    /// <param name="commands">The window's command table; each id is added once.</param>
    /// <param name="score">The score the commands act on when they run (the one pressed last, like the clipboard), read at each run.</param>
    /// <param name="notice">Shows a refusal (a Remove that would cut a repeat in two).</param>
    public static void Register(MainWindowCommands commands, Func<ScorePreviewView> score, Func<SelectionModel> selection, Action<string> notice)
    {
        commands.Add("Range.Clear", () => RunOnSelection(score(), selection(), notice, BarRangeAction.Clear));
        commands.Add("Range.Remove", () => RunOnSelection(score(), selection(), notice, BarRangeAction.Remove));
        commands.Add("Range.InsertBefore", () => RunOnSelection(score(), selection(), notice, BarRangeAction.InsertBefore));
        commands.Add("Range.InsertAfter", () => RunOnSelection(score(), selection(), notice, BarRangeAction.InsertAfter));
    }

    /// <summary>Runs one bar-range edit over the loop region, else the white selection's bars (SelectionBars) of the shown song through TimelineRangeEdit, the same path as the timeline; nothing happens when no bars are selected.</summary>
    internal static void RunOnSelection(ScorePreviewView score, SelectionModel selection, Action<string> notice, BarRangeAction action)
    {
        var project = score.Editor.Project;
        var span = project is null ? null : SelectionBars.For(selection, project)
            ?? (score.Editor.SelectedBarRange() is { } own ? new BarSpan(own.First, own.Last, null, false) : null);
        if (span is null) return;
        var result = default(TimelineRangeResult);
        // The loop or the score bars act on every track; selected clips' bars act on those clips' tracks only (one undo step for all of them).
        score.Edit(editor => editor.Project is { } song && editor.RunEdit(() =>
        {
            var changed = false;
            foreach (var track in span.Tracks ?? new[] { AllTracks })
            {
                var one = TimelineRangeEdit.Apply(song, span.Start, span.End, action, track, editor.Tools.FillBarsWithRests);
                changed |= one.Changed;
                if (one.Refusal is not null) result = one;
            }
            return changed;
        }));
        if (result.Refusal is { } refusal) notice(refusal);
    }
}
