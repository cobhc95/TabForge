using TabForge.Models;

namespace TabForge.Services;

// Owns: the ONE table that decides which command a Copy/Cut/Paste/Paste special/Delete/Duplicate press runs, from the focus target and the selection.
// Does not own: the selection data (SelectionModel), the key maps, running the commands or the track-delete confirmation (the host asks when NeedsConfirm).
// Tests: EditRoutingTests.

/// <summary>The edit actions that route by focus.</summary>
public enum EditVerb { Copy, Cut, Paste, PasteSpecial, Delete, Duplicate, ClearBars, RemoveBars, InsertBarsBefore, InsertBarsAfter }

/// <summary>What exists right now: which selections are held and what the clipboard holds.</summary>
public sealed record EditSelectionState(
    bool HasScoreSelection = false, bool HasRange = false, bool HasClips = false, bool HasTrackRow = false,
    bool ClipboardHasBars = false, bool ClipboardHasClips = false, bool ClipboardHasTrack = false, bool ClipboardHasRange = false, bool HasHighlight = false);

/// <summary>The routed command, or why there is none; <see cref="NeedsConfirm"/> = the host must ask before running it (track removal).</summary>
public sealed record RoutedEdit(string? CommandId, string? NotAvailableReason, bool NeedsConfirm = false)
{
    public bool IsAvailable => CommandId is not null;
    public static RoutedEdit Run(string commandId, bool needsConfirm = false) => new(commandId, null, needsConfirm);
    public static RoutedEdit NotAvailable(string reason) => new(null, reason);
}

/// <summary>
/// The single routing table: (focus target, verb) -> command id. <see cref="Resolve"/> adds the availability and confirmation rules,
/// <see cref="Classify"/> reads the table backwards, and <see cref="Pick"/> chooses among the commands one gesture is bound to, so a
/// rebound key still routes by focus. Keyboard, menus and context menus all ask this class; none keeps its own key map for these verbs.
/// </summary>
public sealed class EditRouting
{
    /// <summary>The timeline-range bar duplicate (BarRangeEditor.Duplicate); the id has no key binding by default.</summary>
    public const string RangeDuplicateId = "Range.Duplicate";

    /// <summary>The timeline-range copy (RangeCapture -> clipboard) and cut (copy, then the Delete question and delete as one undo step).</summary>
    public const string RangeCopyId = "Range.Copy", RangeCutId = "Range.Cut";

    /// <summary>The timeline-range paste (Overwrite at the insert point, RangePaste) and Paste special (its window); with no copied range they fall back to the score paste.</summary>
    public const string RangePasteId = "Range.Paste", RangePasteSpecialId = "Range.PasteSpecial";

    // The score has the Edit.* commands; the loop region (Loop focus, set by a ruler press) has its own copy, cut, paste, paste special and delete; clips and track rows have their own.
    private readonly Dictionary<(FocusTarget, EditVerb), string> _table = new()
    {
        [(FocusTarget.ScoreSelection, EditVerb.Copy)] = "Edit.Copy",
        [(FocusTarget.ScoreSelection, EditVerb.Cut)] = "Edit.Cut",
        [(FocusTarget.ScoreSelection, EditVerb.Paste)] = "Edit.Paste",
        [(FocusTarget.ScoreSelection, EditVerb.PasteSpecial)] = "Edit.PasteSpecial",
        [(FocusTarget.Loop, EditVerb.Copy)] = RangeCopyId,
        [(FocusTarget.Loop, EditVerb.Cut)] = RangeCutId,
        [(FocusTarget.Loop, EditVerb.Paste)] = RangePasteId,
        [(FocusTarget.Loop, EditVerb.PasteSpecial)] = RangePasteSpecialId,
        [(FocusTarget.Loop, EditVerb.Delete)] = "Range.Delete",
        [(FocusTarget.Loop, EditVerb.Duplicate)] = RangeDuplicateId,
        // The bar-range edits (Clear / Remove / Insert gap) act on the selected bars, in the score or on the timeline; both run TimelineRangeEdit.
        [(FocusTarget.ScoreSelection, EditVerb.ClearBars)] = "Range.Clear",
        [(FocusTarget.ScoreSelection, EditVerb.RemoveBars)] = "Range.Remove",
        [(FocusTarget.ScoreSelection, EditVerb.InsertBarsBefore)] = "Range.InsertBefore",
        [(FocusTarget.ScoreSelection, EditVerb.InsertBarsAfter)] = "Range.InsertAfter",
        [(FocusTarget.Loop, EditVerb.ClearBars)] = "Range.Clear",
        [(FocusTarget.Loop, EditVerb.RemoveBars)] = "Range.Remove",
        [(FocusTarget.Loop, EditVerb.InsertBarsBefore)] = "Range.InsertBefore",
        [(FocusTarget.Loop, EditVerb.InsertBarsAfter)] = "Range.InsertAfter",
        // With clips selected (and no loop) the bar edits act on the clips' bar span on their tracks (SelectionBars).
        [(FocusTarget.Clips, EditVerb.ClearBars)] = "Range.Clear",
        [(FocusTarget.Clips, EditVerb.RemoveBars)] = "Range.Remove",
        [(FocusTarget.Clips, EditVerb.InsertBarsBefore)] = "Range.InsertBefore",
        [(FocusTarget.Clips, EditVerb.InsertBarsAfter)] = "Range.InsertAfter",
        [(FocusTarget.Clips, EditVerb.Copy)] = "Clip.Copy",
        [(FocusTarget.Clips, EditVerb.Cut)] = "Clip.Cut",
        [(FocusTarget.Clips, EditVerb.Paste)] = "Clip.Paste",
        [(FocusTarget.Clips, EditVerb.Delete)] = "Clip.Delete",
        [(FocusTarget.Clips, EditVerb.Duplicate)] = "Clip.Duplicate",
        [(FocusTarget.TrackRow, EditVerb.Copy)] = "TrackRow.Copy",
        [(FocusTarget.TrackRow, EditVerb.Cut)] = "TrackRow.Cut",
        [(FocusTarget.TrackRow, EditVerb.Paste)] = "TrackRow.Paste",
        [(FocusTarget.TrackRow, EditVerb.Delete)] = "TrackRow.Delete",
        [(FocusTarget.TrackRow, EditVerb.Duplicate)] = "TrackRow.Duplicate",
        // The timeline bar highlight (grey cells) has its own copy, cut, paste and delete; nothing else applies to it.
        [(FocusTarget.BarHighlight, EditVerb.Copy)] = "Highlight.Copy",
        [(FocusTarget.BarHighlight, EditVerb.Cut)] = "Highlight.Cut",
        [(FocusTarget.BarHighlight, EditVerb.Paste)] = "Highlight.Paste",
        [(FocusTarget.BarHighlight, EditVerb.Delete)] = "Highlight.Delete",
    };

    /// <summary>The focus target and verb of a command id, or null when the id is not a routed edit. A shared Edit.* id reports the score.</summary>
    public (FocusTarget Focus, EditVerb Verb)? Classify(string commandId)
    {
        foreach (var target in new[] { FocusTarget.ScoreSelection, FocusTarget.Loop, FocusTarget.Clips, FocusTarget.TrackRow, FocusTarget.BarHighlight })
            foreach (var verb in Enum.GetValues<EditVerb>())
                if (_table.TryGetValue((target, verb), out var id) && id == commandId) return (target, verb);
        return null;
    }

    /// <summary>The command for <paramref name="verb"/> under <paramref name="focus"/>, or why it is not available.</summary>
    public RoutedEdit Resolve(FocusTarget focus, EditSelectionState state, EditVerb verb)
    {
        if (focus == FocusTarget.None) return RoutedEdit.NotAvailable("Nothing is focused");
        if (!_table.TryGetValue((focus, verb), out var id)) return RoutedEdit.NotAvailable($"{verb} does not apply to {focus}");
        var hasSelection = focus switch
        {
            FocusTarget.ScoreSelection => state.HasScoreSelection,
            FocusTarget.Loop => state.HasRange,
            FocusTarget.Clips => state.HasClips,
            FocusTarget.BarHighlight => state.HasHighlight,
            _ => state.HasTrackRow,
        };
        var isPaste = verb is EditVerb.Paste or EditVerb.PasteSpecial;
        // A copied range on a focused track header pastes as a range starting at that track; a copied track keeps the whole-track paste.
        if (isPaste && focus == FocusTarget.TrackRow && state.ClipboardHasRange && hasSelection) return RoutedEdit.Run(_table[(FocusTarget.Loop, verb)]);
        if (!isPaste && !hasSelection) return RoutedEdit.NotAvailable("Nothing is selected");
        if (isPaste)
        {
            var hasClipboard = focus switch
            {
                FocusTarget.Clips => state.ClipboardHasClips,
                FocusTarget.TrackRow => state.ClipboardHasTrack,
                FocusTarget.BarHighlight => state.ClipboardHasRange,   // the highlight pastes copied cells only
                _ => state.ClipboardHasBars,
            };
            if (!hasClipboard) return RoutedEdit.NotAvailable("The clipboard has nothing to paste here");
        }
        return RoutedEdit.Run(id, focus == FocusTarget.TrackRow && verb is EditVerb.Cut or EditVerb.Delete);
    }

    /// <summary>Of the commands one gesture is bound to, the one that applies under <paramref name="focus"/>, or null (a rebound key still routes).</summary>
    public string? Pick(FocusTarget focus, IEnumerable<string> candidateIds)
    {
        foreach (var id in candidateIds)
            if (Classify(id) is { } c && _table.TryGetValue((focus, c.Verb), out var routed) && routed == id) return id;
        return null;
    }
}
