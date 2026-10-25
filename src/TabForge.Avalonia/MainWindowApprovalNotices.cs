using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the two approval notices on the top bar (linked audio waiting for approval, plug-ins not approved) for the song shown, and the
//     review windows they open, with the save and the refresh after each change.
// Does not own: the approval rules (MediaAccess, PluginTrust), the look of the notice row (StatusNoticeBarView, NotificationBar), the lifetime of the window (MainWindow).
// Tests: TabForge.Avalonia.Tests.ApprovalReviewWindowsTests, TabForge.Avalonia.Tests.LinkedAudioReviewDataTests, TabForge.Avalonia.Tests.NotificationBarTests.
/// <summary>The two approval notices of the main window and what each opens: the notice first, then the review window.</summary>
public sealed class MainWindowApprovalNotices
{
    public const string ReviewButtonText = "Review…";
    public const string LinkedAudioNoticeName = "Linked audio notice";
    public const string PluginNoticeName = "Plug-in notice";

    private readonly Window _owner;
    private readonly MainWindowTabs _songs;
    private readonly MainWindowSettings? _settings;
    private readonly PluginTrust _trust;
    private readonly IPluginFileTrust _pluginFiles = new TabForge.Platform.Windows.WindowsPluginFileTrust();
    private readonly Action? _syncEngine;
    private NotificationBar? _topBar;

    /// <param name="trust">The app's plug-in trust state (the engine's load gate asks the same one); null makes one (tests).</param>
    /// <param name="syncEngine">Brings the engine in line with the song shown (a lifted quarantine loads the plug-in again); null does nothing.</param>
    public MainWindowApprovalNotices(Window owner, MainWindowTabs songs, MainWindowSettings? settings, Action? syncEngine, PluginTrust trust)
    {
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _owner = owner;
        _songs = songs;
        _settings = settings;
        _syncEngine = syncEngine;
        MediaNotice = new StatusNoticeBarView(LinkedAudioNoticeName, ReviewButtonText, () => _ = ShowLinkedAudioReviewAsync(_owner));
        TrustNotice = new StatusNoticeBarView(PluginNoticeName, ReviewButtonText, () => _ = ShowPluginReviewAsync());
        DockPanel.SetDock(MediaNotice, Dock.Bottom);
        DockPanel.SetDock(TrustNotice, Dock.Bottom);
        songs.ActiveChanged += Refresh;
        Refresh();
    }

    public StatusNoticeBarView MediaNotice { get; }

    public StatusNoticeBarView TrustNotice { get; }

    /// <summary>Routes both notices to the unified top bar (sticky Warn with Review…); null keeps the bottom rows (tests).</summary>
    public void AttachNotificationBar(NotificationBar? bar)
    {
        _topBar = bar;
        Refresh();
    }

    /// <summary>Shows or hides both notices for the song now shown.</summary>
    public void Refresh()
    {
        RefreshMediaNotice();
        RefreshTrustNotice();
    }

    private void RefreshMediaNotice()
    {
        var song = _songs.Active;
        var waiting = MediaAccess.UnapprovedNoWait(song.Project.Tracks.SelectMany(t => t.AudioClips), song.Media);
        if (_topBar is not null)
        {
            if (waiting.Count == 0) _topBar.HideSticky(NotificationBar.ApprovalMediaSource);
            else _topBar.Show(LinkedAudioApprovalReview.MediaNoticeText(waiting), NotificationKind.Warn,
                (ReviewButtonText, () => _ = ShowLinkedAudioReviewAsync(_owner)),
                source: NotificationBar.ApprovalMediaSource, tooltip: LinkedAudioApprovalReview.MediaNoticeToolTip(waiting));
            MediaNotice.Hide();
            return;
        }
        if (waiting.Count == 0) { MediaNotice.Hide(); return; }
        MediaNotice.Show(LinkedAudioApprovalReview.MediaNoticeText(waiting), LinkedAudioApprovalReview.MediaNoticeToolTip(waiting));
    }

    private void RefreshTrustNotice()
    {
        var details = _trust.UntrustedDetails(_songs.Active.Project, PluginSettingsOfStore(), _pluginFiles);
        if (_topBar is not null)
        {
            if (PluginApprovalNoticePresenter.Notice(details, PluginTrust.ReasonChanged) is not { } topNotice) _topBar.HideSticky(NotificationBar.ApprovalTrustSource);
            else _topBar.Show(topNotice.Text, NotificationKind.Warn,
                (ReviewButtonText, () => _ = ShowPluginReviewAsync()),
                source: NotificationBar.ApprovalTrustSource, tooltip: topNotice.ToolTip);
            TrustNotice.Hide();
            return;
        }
        if (PluginApprovalNoticePresenter.Notice(details, PluginTrust.ReasonChanged) is not { } notice) { TrustNotice.Hide(); return; }
        TrustNotice.Show(notice.Text, notice.ToolTip);
    }

    /// <summary>Media.ManageApprovals: the linked-audio review for the shown song, over the owner window.</summary>
    public void Register(MainWindowCommands commands) =>
        commands.Add("Media.ManageApprovals", () => _ = ShowLinkedAudioReviewAsync(_owner));

    /// <summary>The linked-audio review for the shown song, over <paramref name="owner"/>; returns when it closes.</summary>
    public async Task ShowLinkedAudioReviewAsync(Window owner)
    {
        var song = _songs.Active;
        var stored = (_settings?.Settings ?? SettingsDefaults.Fallback()).Audio.ApprovedMedia.ToList();
        var review = LinkedAudioApprovalReview.Begin(song.Project.Tracks.SelectMany(t => t.AudioClips), song.Media, stored);
        var window = new LinkedAudioReviewWindow(review.Waiting, review.AllowedForThisSong, review.AllowedForOtherSongs, review.Older,
            decisions => { review.Allow(decisions); SaveAndRefresh(); },
            approvals => { review.Revoke(approvals); SaveAndRefresh(); });
        await window.ShowDialog(owner);
    }

    /// <summary>The plug-ins switched off after a crash, over <paramref name="owner"/>: Allow again takes the path off the saved list, syncs the engine, saves and refreshes the notices.</summary>
    public async Task ShowQuarantineAsync(Window owner)
    {
        var quarantined = PluginSettingsOfStore().Quarantined;
        var window = new QuarantineWindow(quarantined, path => AllowAgain(quarantined, path));
        await window.ShowDialog(owner);
    }

    /// <summary>Takes <paramref name="path"/> off the quarantine list; false (nothing done) when it was not listed.</summary>
    internal bool AllowAgain(List<string> quarantined, string path)
    {
        if (TabForge.Plugins.PluginQuarantine.AllowAgain(quarantined, path) == 0) return false;
        _syncEngine?.Invoke();   // the chain key no longer says Skip: the engine loads the plug-in again
        SaveAndRefresh();
        return true;
    }

    private async Task ShowPluginReviewAsync()
    {
        var untrusted = _trust.UntrustedDetails(_songs.Active.Project, PluginSettingsOfStore(), _pluginFiles);
        if (untrusted.Count == 0) { Refresh(); return; }
        var review = new PluginApprovalReview(PluginSettingsOfStore(), _trust, _pluginFiles, untrusted);
        var window = new PluginReviewWindow(review.Untrusted, paths => { review.Approve(paths); SaveAndRefresh(); });
        await window.ShowDialog(_owner);
    }

    private PluginSettings PluginSettingsOfStore() => _settings?.Settings.Plugins ?? new PluginSettings();

    private void SaveAndRefresh()
    {
        _settings?.SaveSettings();
        Refresh();
    }
}
