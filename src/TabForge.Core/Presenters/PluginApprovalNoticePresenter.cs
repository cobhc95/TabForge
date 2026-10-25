namespace TabForge.Presenters;

/// <summary>The text and tool tip of one status notice.</summary>
public sealed record NoticeText(string Text, string ToolTip);

/// <summary>
/// The plug-in approval notice: what it says about the song's plug-ins that are not approved.
/// Owns: the notice text (count, first three names, why) and its tool tip (every path with its reason); no notice when nothing is waiting.
/// Does not own: which plug-ins are trusted or approving them (PluginTrust: an unapproved plug-in is never loaded) or the notice row (ApprovalNoticeController).
/// Tests: no dedicated test class.
/// </summary>
public static class PluginApprovalNoticePresenter
{
    /// <summary>The notice for <paramref name="untrusted"/>; null (no notice) when the list is empty. <paramref name="changedReason"/> marks a plug-in changed since its approval.</summary>
    public static NoticeText? Notice(IReadOnlyList<(string Path, string Reason)> untrusted, string changedReason)
    {
        if (untrusted.Count == 0) return null;
        var names = string.Join(", ", untrusted.Take(3).Select(d => Path.GetFileName(d.Path.TrimEnd('\\', '/'))));
        var changed = untrusted.Any(d => d.Reason == changedReason);
        return new NoticeText(
            $"This song uses {untrusted.Count} plug-in{(untrusted.Count == 1 ? "" : "s")} {(changed ? "you haven't approved or that changed since you approved them" : "from locations you haven't approved")}: {names}{(untrusted.Count > 3 ? ", …" : "")}. They are not loaded.",
            string.Join("\n", untrusted.Select(d => $"{d.Path}  ({d.Reason})")));
    }
}
