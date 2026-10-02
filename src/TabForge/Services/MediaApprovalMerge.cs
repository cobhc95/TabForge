namespace TabForge.Services;

// Owns: merging a staged settings copy's media approvals with the live approvals.
// Does not own: the approval decisions and the dialogs.
// Tests: TestDocumentContextLeftovers.
/// <summary>
/// Preferences works on a staged copy of the settings while the live approvals keep changing (another window's "Allow", a revoke in the
/// Linked audio window). Handing the staged copy back as-is would drop an approval that moved while the dialog was open, so each
/// preview / apply is merged with the live list instead: what the dialog itself added or removed (compared with what it last saw) is applied
/// to the live list; every other live approval is kept. One instance per open dialog. (Its Linked audio button changes the live list itself and
/// copies the result into the dialog's staged copy, so those changes are the dialog's own view too.)
/// </summary>
public sealed class MediaApprovalMerge
{
    private readonly Func<List<MediaApproval>?> _live;
    private List<MediaApproval> _seen;

    /// <param name="live">The live approvals (the shared settings' list); read at every <see cref="Stage"/>.</param>
    public MediaApprovalMerge(Func<List<MediaApproval>?> live)
    {
        _live = live;
        _seen = Copy(live());
    }

    /// <summary>Replaces <paramref name="staged"/>'s approvals with the merge of the live list and the dialog's own changes.</summary>
    public void Stage(AppSettings staged)
    {
        var live = Copy(_live());
        var mine = staged.Audio.ApprovedMedia ?? new List<MediaApproval>();
        var merged = live.Where(a => !(Contains(_seen, a) && !Contains(mine, a))).ToList();   // the dialog removed it (reset, import): gone; otherwise kept
        foreach (var a in mine)
            if (!Contains(_seen, a) && !Contains(merged, a)) merged.Add(new MediaApproval { Project = a.Project, Folder = a.Folder });   // the dialog added it
        if (merged.Count > 512) merged.RemoveRange(512, merged.Count - 512);
        staged.Audio.ApprovedMedia = merged;
        _seen = Copy(mine);   // the dialog's own view (it never receives the merge): the next call compares its copy with this one
    }

    private static List<MediaApproval> Copy(List<MediaApproval>? list)
    {
        if (list is null) return new();
        lock (list) return list.Select(a => new MediaApproval { Project = a.Project, Folder = a.Folder }).ToList();
    }

    private static bool Contains(List<MediaApproval> list, MediaApproval a) =>
        list.Any(x => string.Equals(x.Project, a.Project, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Folder, a.Folder, StringComparison.OrdinalIgnoreCase));
}
