using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia.Platform;

// Owns: the clip-overlap hook of the timeline: the policy the window gives it and the one call every clip commit point makes after its edit landed.
// Does not own: finding or moving the overlapping clips and asking the user (ClipLaneOverlap, ClipOverlapPolicy in Core), or the post-edit refresh itself (ClipEdited).
// Tests: TabForge.Avalonia.Tests.ClipLaneOverlapTests.
public sealed partial class TimelineView
{
    /// <summary>The window's answer to "clips now overlap on a lane" (setting, prompt, Remember); null in a bare view, where overlaps are left as they are.</summary>
    internal ClipOverlapPolicy? ClipOverlap { get; set; }

    /// <summary>
    /// A clip edit landed (<paramref name="edited"/> are its clips): checks them for overlaps on their lane, then raises <see cref="ClipEdited"/>. When the user
    /// is asked, the edit's refresh shows the overlap first and a second refresh follows the answer (Move changes lanes, Cancel undoes the edit).
    /// </summary>
    internal void ClipEditCommitted(IEditableDocument document, IReadOnlyList<AudioClip>? edited) => CheckOverlaps(document, edited, refresh: true);

    /// <summary>As <see cref="ClipEditCommitted"/> for an edit whose own refresh already ran (a section move): <see cref="ClipEdited"/> is raised again only when the answer changed the song.</summary>
    internal void ClipOverlapsAfter(IEditableDocument document, IReadOnlyList<AudioClip>? edited) => CheckOverlaps(document, edited, refresh: false);

    private void CheckOverlaps(IEditableDocument document, IReadOnlyList<AudioClip>? edited, bool refresh)
    {
        var pending = edited is { Count: > 0 } && ClipOverlap is { } policy ? policy.ResolveAsync(document, edited) : null;
        if (pending is null)
        {
            if (refresh) ClipEdited?.Invoke(document);
            return;
        }
        // Still asking: the edit refreshes now so the overlap shows behind the prompt. An answer already known (a remembered setting) is applied by the time
        // the awaited task continues, which for a completed task is right here, so the single refresh below shows the result.
        var shown = refresh && !pending.IsCompleted;
        if (shown) ClipEdited?.Invoke(document);
        UiTasks.Forget(RefreshAfter(document, pending, always: refresh && !shown), "Resolving the overlapping clips", ClipNotice ?? (_ => { }));
    }

    private async Task RefreshAfter(IEditableDocument document, Task<ClipOverlapOutcome> pending, bool always)
    {
        var outcome = await pending;
        if (always || outcome is ClipOverlapOutcome.Moved or ClipOverlapOutcome.Cancelled) ClipEdited?.Invoke(document);
        InvalidateVisual();
    }
}
