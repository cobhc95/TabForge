using TabForge.Models;

namespace TabForge.Documents;

/// <summary>The stored values of the "When clips overlap on a lane" setting (EditingSettings.ClipOverlap).</summary>
public static class ClipOverlapChoice
{
    public const string Ask = "Ask", Move = "Move", Keep = "Keep";

    /// <summary>The Preferences labels, in the order of <see cref="Values"/>.</summary>
    public static string[] Labels => new[] { "Ask each time", "Move the existing clip", "Keep overlapping" };
    public static string[] Values => new[] { Ask, Move, Keep };

    /// <summary>The stored value for any text: an unknown or missing value asks.</summary>
    public static string Normalize(string? stored) => stored is Move or Keep ? stored : Ask;

    public static string ToLabel(string? stored) => Labels[Array.IndexOf(Values, Normalize(stored))];

    public static string FromLabel(string label) { var i = Array.IndexOf(Labels, label); return i < 0 ? Ask : Values[i]; }
}

/// <summary>What the prompt answered: move the existing clip (or keep the overlap) and whether to remember the choice.</summary>
public readonly record struct ClipOverlapAnswer(bool Move, bool Remember);

/// <summary>What the prompt shows: the edited clip, the existing clip it overlaps and the lane (1-based) they share.</summary>
public sealed record ClipOverlapQuestion(string Edited, string Existing, int Lane);

/// <summary>How a check ended: no overlap, the existing clips moved, the overlap kept, or the user cancelled and the edit was undone.</summary>
public enum ClipOverlapOutcome { None, Moved, Kept, Cancelled }

// Owns: the decision after an edit that may have made clips overlap on a lane: the stored setting, the injectable prompt, Remember, the lane moves as
//   part of the edit's own undo step, and undoing the edit on Cancel.
// Does not own: finding and moving the clips (ClipLaneOverlap), the prompt window (Views/ClipOverlapPromptWindow), the stored setting or the edits.
// Tests: TabForge.Avalonia.Tests.ClipLaneOverlapTests.
/// <summary>Resolves clip overlaps after a committed edit. Without a prompt, an "Ask" setting keeps the overlap (the old behaviour).</summary>
public sealed class ClipOverlapPolicy
{
    /// <summary>The setting's stored value, read at each check; null asks.</summary>
    public Func<string>? Setting { get; set; }

    /// <summary>Stores the value a ticked "Remember my choice" gives (Move or Keep).</summary>
    public Action<string>? Remember { get; set; }

    /// <summary>Asks the question; a null answer is Cancel.</summary>
    public Func<ClipOverlapQuestion, Task<ClipOverlapAnswer?>>? Prompt { get; set; }

    /// <summary>The name a clip is called in the prompt.</summary>
    public static string NameOf(AudioClip clip) =>
        !string.IsNullOrWhiteSpace(clip.Name) ? clip.Name : !string.IsNullOrWhiteSpace(clip.File) ? Path.GetFileNameWithoutExtension(clip.File) : clip.IsMidi ? "MIDI clip" : "Audio clip";

    /// <summary>
    /// Checks the edit that just committed (<paramref name="edited"/> are its clips). With a remembered setting the task is already complete when this returns;
    /// the lane moves join the edit's undo step. Cancel undoes that step (the document's project is then replaced: callers reload from it).
    /// </summary>
    public async Task<ClipOverlapOutcome> ResolveAsync(IEditableDocument document, IReadOnlyList<AudioClip> edited)
    {
        var overlaps = ClipLaneOverlap.Find(document.Project, edited);
        if (overlaps.Count == 0) return ClipOverlapOutcome.None;
        var move = ClipOverlapChoice.Normalize(Setting?.Invoke()) switch { ClipOverlapChoice.Move => true, ClipOverlapChoice.Keep => false, _ => (bool?)null };
        var stale = false;
        if (move is null)
        {
            if (Prompt is null) return ClipOverlapOutcome.Kept;
            var first = overlaps[0];
            var project = document.Project;
            var asked = document.Undo.Latest?.Order;
            var answer = await Prompt(new ClipOverlapQuestion(NameOf(first.Edited), NameOf(first.Existing), first.Edited.Lane + 1));
            if (!ReferenceEquals(project, document.Project)) return ClipOverlapOutcome.None;   // the tab changed under the prompt
            // Another edit landed while the question was open: the newest undo step is no longer this edit's, so Cancel must not undo (it would undo that later edit) and Move is its own step.
            stale = document.Undo.Latest?.Order != asked;
            if (answer is not { } picked)
            {
                if (stale) return ClipOverlapOutcome.Kept;
                DocumentEdits.Undo(document);
                return ClipOverlapOutcome.Cancelled;
            }
            if (picked.Remember) Remember?.Invoke(picked.Move ? ClipOverlapChoice.Move : ClipOverlapChoice.Keep);
            move = picked.Move;
            overlaps = ClipLaneOverlap.Find(document.Project, edited);
        }
        if (move != true || overlaps.Count == 0) return ClipOverlapOutcome.Kept;
        // The lane moves join the edit's step: the newest undo state is the one before the edit, and handing it back stores no second step.
        DocumentEdits.Run(document, _ => ClipLaneOverlap.Move(overlaps) > 0, before: stale ? null : document.Undo.Latest, label: "Move clip to a free lane");
        return ClipOverlapOutcome.Moved;
    }
}
