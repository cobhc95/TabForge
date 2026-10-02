using System.Windows;
using System.Windows.Controls;
using TabForge.Audio;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow: non-modal notice when the open song links audio on a network location or a removable drive
// (never opened until the user allows that folder for this song; see MediaAccess) and the "Linked audio" window
// that allows folders and revokes earlier approvals.
public partial class MainWindow
{
    private Border? _mediaBar;
    private TextBlock? _mediaText;

    private void WireMediaAccess()
    {
        // Approvals changed (by any window): refresh this window's notice, unless the window has closed by the time the dispatcher runs it.
        Subscribe(h => MediaAccess.Changed += h, h => MediaAccess.Changed -= h, (Action)(() => PostIfOpen(UpdateMediaApprovalBar)));
        Subscribe(h => MediaAccess.Resolved += h, h => MediaAccess.Resolved -= h, (Action)(() => PostIfOpen(UpdateMediaApprovalBar)));
    }

    /// <summary>
    /// What this window's Settings dialogs call back into: bound to this window instance (no static Preferences actions), so a dialog opened
    /// from one window can only ever act on that window, whichever window was created last or has focus.
    /// </summary>
    private SettingsWindowActions CreateSettingsActions() => new()
    {
        ManageLinkedAudio = owner => { ReviewLinkedAudio(owner); return _settings.Audio.ApprovedMedia.ToList(); },
        ManageQuarantine = owner =>
        {
            Views.QuarantineWindow.Show(owner, () => _settings.Plugins.Quarantined.ToList(), path =>
            {
                if (TabForge.Plugins.PluginQuarantine.AllowAgain(_settings.Plugins.Quarantined, path) == 0) return;
                SaveSettings();
                SyncAudioEngine();   // the chain key no longer says Skip: the engine loads it again
                RefreshArrangement();   // the faulted FX icon clears
            });
            return _settings.Plugins.Quarantined.ToList();
        },
        ShowAllTracksAs = view => SetInstrumentView(view, null),   // Settings > Fretboard > Show all tracks as
    };

    private void UpdateMediaApprovalBar()
    {
        var waiting = MediaAccess.UnapprovedNoWait(_project.Tracks.SelectMany(t => t.AudioClips), Doc.Media);   // UI thread: no file-system call (a still unresolved path appears when MediaAccess.Resolved arrives)
        if (waiting.Count == 0) { if (_mediaBar is not null) _mediaBar.Visibility = Visibility.Collapsed; return; }
        if (_mediaBar is null)
        {
            if (MainStatusBar.Parent is not DockPanel dock) return;
            _mediaText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _mediaText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var review = new Button { Content = "Review…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(10, 0, 0, 0) };
            review.Click += (_, _) => ReviewLinkedAudio(this);
            DockPanel.SetDock(review, Dock.Right);
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 3, 8, 3) };
            row.Children.Add(review);
            row.Children.Add(_mediaText);
            _mediaBar = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) };
            _mediaBar.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            _mediaBar.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
            DockPanel.SetDock(_mediaBar, Dock.Bottom);
            dock.Children.Insert(dock.Children.IndexOf(MainStatusBar) + 1, _mediaBar);
        }
        _mediaText!.Text = waiting.Count == 1
            ? waiting[0].Message + " Until then the clips show as not loaded."
            : $"This song links audio in {waiting.Count} network or removable folders you haven't allowed. Until then the clips show as not loaded.";
        _mediaText.ToolTip = string.Join("\n", waiting.Select(d => d.Verdict.FullPath));
        _mediaBar.Visibility = Visibility.Visible;
    }

    /// <summary>The "Linked audio" window: folders waiting for approval (Allow selected) and the approvals already given (Revoke selected).</summary>
    private void ReviewLinkedAudio(Window owner)
    {
        // The song this review is about is fixed here, when it opens: a later focus or tab change cannot retarget Allow / Revoke.
        var doc = Doc;
        var media = doc.Media;
        var waiting = MediaAccess.Unapproved(doc.Project.Tracks.SelectMany(t => t.AudioClips), media);
        var given = _settings.Audio.ApprovedMedia.ToList();
        var w = new Window
        {
            Title = "Linked audio", Owner = owner.IsVisible ? owner : this, Width = 720, Height = 480, MinWidth = 420, MinHeight = 260, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(12) };
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "A song can link audio files on network locations or removable drives. TabForge does not read them until you allow the folder (and its subfolders) for this song. Tick folders and choose Allow selected, or tick approvals you gave before and choose Revoke selected." };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var list = new StackPanel();
        var allowBoxes = new List<(CheckBox Box, MediaDecision Decision)>();
        var revokeBoxes = new List<(CheckBox Box, MediaApproval Approval)>();
        if (waiting.Count > 0) list.Children.Add(new TextBlock { Text = "Waiting for approval", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var d in waiting)
        {
            var box = new CheckBox { Margin = new Thickness(0, 2, 0, 2), Content = d.Verdict.Folder + (d.Verdict.Location == MediaLocation.Network ? "  (network)" : "  (removable)"), ToolTip = d.Verdict.FullPath };
            allowBoxes.Add((box, d));
            list.Children.Add(box);
        }
        var here = MediaAccess.ApprovalsOf(media);   // this song's stored approvals (its path) or its session's (unsaved song)
        // Approvals stored with an empty path belong to no song any more (they were shared by every unsaved song): listed so they can be revoked, never honoured.
        var legacy = given.Where(a => string.IsNullOrEmpty(a.Project)).ToList();
        var elsewhere = given.Where(a => !string.IsNullOrEmpty(a.Project) && !here.Contains(a)).ToList();
        void AddGiven(string title, List<MediaApproval> items)
        {
            if (items.Count == 0) return;
            list.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
            foreach (var a in items)
            {
                var box = new CheckBox { Margin = new Thickness(0, 2, 0, 2), Content = a.Folder + (a.Project.Length == 0 ? "  (older approval, no longer used)" : a.Project.StartsWith("session:", StringComparison.Ordinal) ? "  (this unsaved song)" : $"  (song: {System.IO.Path.GetFileName(a.Project)})"), ToolTip = $"{a.Folder}\n{(a.Project.StartsWith("session:", StringComparison.Ordinal) ? "this unsaved song, until it is closed" : a.Project)}" };
                revokeBoxes.Add((box, a));
                list.Children.Add(box);
            }
        }
        AddGiven("Allowed for this song", here);
        AddGiven("Allowed for other songs", elsewhere);
        AddGiven("Older approvals for unsaved songs (no longer used)", legacy);
        if (allowBoxes.Count == 0 && revokeBoxes.Count == 0) list.Children.Add(new TextBlock { Text = "No linked audio folders are waiting or allowed." });
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var allow = new Button { Content = "Allow selected", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0), IsDefault = true, IsEnabled = allowBoxes.Count > 0 };
        var revoke = new Button { Content = "Revoke selected", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0), IsEnabled = revokeBoxes.Count > 0 };
        var close = new Button { Content = "Close", Padding = new Thickness(12, 3, 12, 3), IsCancel = true };
        allow.Click += (_, _) => { foreach (var (box, d) in allowBoxes.Where(b => b.Box.IsChecked == true)) MediaAccess.Approve(d.Verdict, media); CommitMediaChange(); w.Close(); };
        revoke.Click += (_, _) => { foreach (var (box, a) in revokeBoxes.Where(b => b.Box.IsChecked == true)) MediaAccess.Revoke(a, media); CommitMediaChange(); w.Close(); };
        buttons.Children.Add(allow);
        buttons.Children.Add(revoke);
        buttons.Children.Add(close);
        w.Content = root;
        DialogHost.ShowModal(w);
    }

    private void CommitMediaChange()
    {
        SaveSettings();
        SyncAudioEngine();
        UpdateMediaApprovalBar();
        RefreshArrangement();
    }
}
