using TabForge.Documents;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Controllers;

/// <summary>What the approval notices and their review windows need from the window.</summary>
internal interface IApprovalHost
{
    /// <summary>The song the window shows now.</summary>
    DocumentSession ActiveDocument { get; }
    AppSettings Settings { get; }
    void SaveSettings();
    void SyncAudioEngine();
    void RefreshArrangement();
    void RefreshMixerWindow();
    /// <summary>Queues work for the window's thread; dropped when the window has closed by then.</summary>
    void Post(Action work);
    void ShowLinkedAudioReview(LinkedAudioReview review);
    void ShowPluginReview(PluginReview review);
}

// Owns: the two approval notices above the status bar (linked audio on a network or removable location, native plug-ins from
//     unapproved locations) and the windows that list what they ask about.
// Does not own: the approval rules and stored approvals (MediaAccess, MediaPathPolicy, PluginTrust).
// Tests: TestClosedDocumentChainsReleased.
/// <summary>
/// The two notices above the status bar that ask for an approval: linked audio on a network location or a removable drive (never opened until the
/// user allows that folder for this song; see <see cref="MediaAccess"/>) and native plug-ins from locations the user has not approved (never
/// loaded until allowed; see <see cref="PluginTrust"/>). Also builds what the window behind each notice shows. <see cref="Dispose"/> detaches from the
/// shared approval events.
/// </summary>
internal sealed class ApprovalNoticeController : IDisposable
{
    private readonly IApprovalHost _host;
    private readonly IStatusNotice _mediaBar;
    private readonly IStatusNotice _trustBar;
    private bool _disposed;

    public ApprovalNoticeController(IApprovalHost host, IStatusNotices notices)
    {
        _host = host;
        _mediaBar = notices.Create(null, "Review…", () => _host.ShowLinkedAudioReview(BeginLinkedAudioReview()));
        _trustBar = notices.Create(null, "Review…", ReviewUntrustedPlugins);
    }

    /// <summary>Approvals changed (by any window): refresh this window's notice, unless the window has closed by the time the dispatcher runs it.</summary>
    public void Start()
    {
        MediaAccess.Changed += OnMediaApprovalsChanged;
        MediaAccess.Resolved += OnMediaApprovalsChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MediaAccess.Changed -= OnMediaApprovalsChanged;
        MediaAccess.Resolved -= OnMediaApprovalsChanged;
    }

    private void OnMediaApprovalsChanged() => _host.Post(UpdateMediaApprovalBar);

    public void UpdateMediaApprovalBar()
    {
        var doc = _host.ActiveDocument;
        var waiting = MediaAccess.UnapprovedNoWait(doc.Project.Tracks.SelectMany(t => t.AudioClips), doc.Media);   // UI thread: no file-system call (a still unresolved path appears when MediaAccess.Resolved arrives)
        if (waiting.Count == 0) { _mediaBar.Hide(); return; }
        var text = waiting.Count == 1
            ? waiting[0].Message + " Until then the clips show as not loaded."
            : $"This song links audio in {waiting.Count} network or removable folders you haven't allowed. Until then the clips show as not loaded.";
        _mediaBar.Show(text, string.Join("\n", waiting.Select(d => d.Verdict.FullPath)));
    }

    public void UpdatePluginTrustBar()
    {
        var details = PluginTrust.UntrustedDetails(_host.ActiveDocument.Project, _host.Settings.Plugins);
        var untrusted = details.Select(d => d.Path).ToList();
        if (untrusted.Count == 0) { _trustBar.Hide(); return; }
        var names = string.Join(", ", untrusted.Take(3).Select(p => System.IO.Path.GetFileName(p.TrimEnd('\\', '/'))));
        var changed = details.Any(d => d.Reason == PluginTrust.ReasonChanged);
        _trustBar.Show(
            $"This song uses {untrusted.Count} plug-in{(untrusted.Count == 1 ? "" : "s")} {(changed ? "you haven't approved or that changed since you approved them" : "from locations you haven't approved")}: {names}{(untrusted.Count > 3 ? ", …" : "")}. They are not loaded.",
            string.Join("\n", details.Select(d => $"{d.Path}  ({d.Reason})")));
    }

    /// <summary>The "Linked audio" review for the song shown now. The song is fixed here: a later focus or tab change cannot retarget Allow / Revoke.</summary>
    public LinkedAudioReview BeginLinkedAudioReview()
    {
        var doc = _host.ActiveDocument;
        var media = doc.Media;
        var waiting = MediaAccess.Unapproved(doc.Project.Tracks.SelectMany(t => t.AudioClips), media);
        var given = _host.Settings.Audio.ApprovedMedia.ToList();
        var here = MediaAccess.ApprovalsOf(media);   // this song's stored approvals (its path) or its session's (unsaved song)
        // Approvals stored with an empty path belong to no song any more (they were shared by every unsaved song): listed so they can be revoked, never honoured.
        var legacy = given.Where(a => string.IsNullOrEmpty(a.Project)).ToList();
        var elsewhere = given.Where(a => !string.IsNullOrEmpty(a.Project) && !here.Contains(a)).ToList();
        return new LinkedAudioReview(this, media, waiting, here, elsewhere, legacy);
    }

    private void ReviewUntrustedPlugins()
    {
        var untrusted = PluginTrust.UntrustedDetails(_host.ActiveDocument.Project, _host.Settings.Plugins);
        if (untrusted.Count == 0) { UpdatePluginTrustBar(); return; }
        _host.ShowPluginReview(new PluginReview(this, untrusted));
    }

    internal void CommitMediaChange()
    {
        _host.SaveSettings();
        _host.SyncAudioEngine();
        UpdateMediaApprovalBar();
        _host.RefreshArrangement();
    }

    internal void CommitPluginApprovals(IEnumerable<string> paths)
    {
        foreach (var p in paths) PluginTrust.Approve(_host.Settings.Plugins, p);
        _host.SaveSettings();
        _host.SyncAudioEngine();
        _host.RefreshMixerWindow();
    }
}

/// <summary>The "Linked audio" review: folders waiting for approval and the approvals already given, for one song. Allow and Revoke act on that song.</summary>
internal sealed class LinkedAudioReview
{
    private readonly ApprovalNoticeController _owner;
    private readonly MediaContext _media;

    public IReadOnlyList<MediaDecision> Waiting { get; }
    public IReadOnlyList<MediaApproval> AllowedForThisSong { get; }
    public IReadOnlyList<MediaApproval> AllowedForOtherSongs { get; }
    public IReadOnlyList<MediaApproval> Older { get; }

    public LinkedAudioReview(ApprovalNoticeController owner, MediaContext media, IReadOnlyList<MediaDecision> waiting,
        IReadOnlyList<MediaApproval> here, IReadOnlyList<MediaApproval> elsewhere, IReadOnlyList<MediaApproval> older)
    {
        _owner = owner;
        _media = media;
        Waiting = waiting;
        AllowedForThisSong = here;
        AllowedForOtherSongs = elsewhere;
        Older = older;
    }

    public void Allow(IEnumerable<MediaDecision> chosen)
    {
        foreach (var d in chosen) MediaAccess.Approve(d.Verdict, _media);
        _owner.CommitMediaChange();
    }

    public void Revoke(IEnumerable<MediaApproval> chosen)
    {
        foreach (var a in chosen) MediaAccess.Revoke(a, _media);
        _owner.CommitMediaChange();
    }
}

/// <summary>The plug-in review: the plug-ins of the song that are not approved. Approving covers the exact file.</summary>
internal sealed class PluginReview
{
    private readonly ApprovalNoticeController _owner;

    public IReadOnlyList<(string Path, string Reason)> Untrusted { get; }

    public PluginReview(ApprovalNoticeController owner, IReadOnlyList<(string Path, string Reason)> untrusted)
    {
        _owner = owner;
        Untrusted = untrusted;
    }

    public void Approve(IReadOnlyList<string> paths) => _owner.CommitPluginApprovals(paths);
}
