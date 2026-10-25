using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The "Linked audio" window, the Avalonia twin of the WPF ApprovalReviewWindows: folders waiting for approval (Allow selected)
/// and the approvals already given (Revoke selected). The caller passes the lists and the two actions, so the window changes no settings itself.
/// Owns: the layout, the rows and the button wiring. Does not own: the approval rules (MediaAccess, MediaPathPolicy), the song the approvals
/// belong to, or the notice that opens this window. Tests: TabForge.Avalonia.Tests.ApprovalReviewWindowsTests.
/// </summary>
public sealed class LinkedAudioReviewWindow : Window
{
    public const string TitleText = "Linked audio";
    public const string IntroText = "A song can link audio files on network locations or removable drives. TabForge does not read them until you allow the folder (and its subfolders) for this song. Tick folders and choose Allow selected, or tick approvals you gave before and choose Revoke selected.";
    public const string WaitingHeading = "Waiting for approval";
    public const string AllowedThisSongHeading = "Allowed for this song";
    public const string AllowedOtherSongsHeading = "Allowed for other songs";
    public const string OlderHeading = "Older approvals for unsaved songs (no longer used)";
    public const string NoneText = "No linked audio folders are waiting or allowed.";
    public const string AllowSelectedText = "Allow selected";
    public const string RevokeSelectedText = "Revoke selected";
    public const string CloseText = "Close";

    private const string SessionPrefix = "session:";

    private readonly List<(CheckBox Box, MediaDecision Decision)> _allowBoxes = new();
    private readonly List<(CheckBox Box, MediaApproval Approval)> _revokeBoxes = new();

    public LinkedAudioReviewWindow(IReadOnlyList<MediaDecision> waiting, IReadOnlyList<MediaApproval> allowedForThisSong,
        IReadOnlyList<MediaApproval> allowedForOtherSongs, IReadOnlyList<MediaApproval> older,
        Action<IReadOnlyList<MediaDecision>> allow, Action<IReadOnlyList<MediaApproval>> revoke)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = TitleText;
        Width = 720; Height = 480; MinWidth = 420; MinHeight = 260;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        var list = new StackPanel();
        if (waiting.Count > 0) list.Children.Add(Heading(WaitingHeading, 0));
        foreach (var decision in waiting)
        {
            var box = RowBox(decision.Verdict.Folder + (decision.Verdict.Location == MediaLocation.Network ? "  (network)" : "  (removable)"), decision.Verdict.FullPath);
            _allowBoxes.Add((box, decision));
            list.Children.Add(box);
        }
        AddGiven(list, AllowedThisSongHeading, allowedForThisSong);
        AddGiven(list, AllowedOtherSongsHeading, allowedForOtherSongs);
        AddGiven(list, OlderHeading, older);
        if (_allowBoxes.Count == 0 && _revokeBoxes.Count == 0) list.Children.Add(new TextBlock { Text = NoneText });

        var allowButton = ActionButton(AllowSelectedText, "AllowSelectedButton", _allowBoxes.Count > 0);
        allowButton.IsDefault = true;
        allowButton.Click += (_, _) =>
        {
            var chosen = _allowBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.Decision).ToList();
            Close();
            allow(chosen);
        };
        var revokeButton = ActionButton(RevokeSelectedText, "RevokeSelectedButton", _revokeBoxes.Count > 0);
        revokeButton.Click += (_, _) =>
        {
            var chosen = _revokeBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.Approval).ToList();
            Close();
            revoke(chosen);
        };
        var closeButton = ActionButton(CloseText, "CloseButton", true);
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(allowButton);
        buttons.Children.Add(revokeButton);
        buttons.Children.Add(closeButton);

        var intro = new TextBlock { Text = IntroText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var dock = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(intro);
        dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;
    }

    private void AddGiven(StackPanel list, string title, IReadOnlyList<MediaApproval> items)
    {
        if (items.Count == 0) return;
        list.Children.Add(Heading(title, 10));
        foreach (var approval in items)
        {
            var box = RowBox(GivenLabel(approval), GivenToolTip(approval));
            _revokeBoxes.Add((box, approval));
            list.Children.Add(box);
        }
    }

    private static string GivenLabel(MediaApproval approval)
    {
        if (approval.Project.Length == 0) return approval.Folder + "  (older approval, no longer used)";
        if (approval.Project.StartsWith(SessionPrefix, StringComparison.Ordinal)) return approval.Folder + "  (this unsaved song)";
        return approval.Folder + $"  (song: {Path.GetFileName(approval.Project)})";
    }

    private static string GivenToolTip(MediaApproval approval)
    {
        var song = approval.Project.StartsWith(SessionPrefix, StringComparison.Ordinal) ? "this unsaved song, until it is closed" : approval.Project;
        return $"{approval.Folder}\n{song}";
    }

    private static TextBlock Heading(string text, double topMargin) =>
        new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, topMargin, 0, 4) };

    private static CheckBox RowBox(string content, string toolTip)
    {
        var box = new CheckBox { Content = content, Margin = new Thickness(0, 2, 0, 2) };
        ToolTip.SetTip(box, toolTip);
        return box;
    }

    private static Button ActionButton(string text, string name, bool enabled) =>
        new() { Name = name, Content = text, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0), IsEnabled = enabled };
}

/// <summary>
/// The "Review plug-ins" window, the Avalonia twin of the WPF plug-in review: tick the plug-ins to trust, then Allow selected. Untrusted
/// plug-ins that are not on disk show "(not found)". The caller passes the list and the approve action.
/// Owns: the layout and the rows. Does not own: the trust rules (PluginTrust) or the settings change (the approve action does it).
/// Tests: TabForge.Avalonia.Tests.ApprovalReviewWindowsTests.
/// </summary>
public sealed class PluginReviewWindow : Window
{
    public const string TitleText = "Review plug-ins";
    public const string AllowSelectedText = "Allow selected";
    public const string KeepDisabledText = "Keep disabled";
    public const string NotFoundText = "not found";
    public const string IntroTail = " Tick the plug-ins you trust and click Allow selected: the approval covers this exact file, and you are asked again if it changes. The others stay disabled.";

    public PluginReviewWindow(IReadOnlyList<(string Path, string Reason)> untrusted, Action<IReadOnlyList<string>> approve)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = TitleText;
        Width = 720; Height = 420; MinWidth = 420; MinHeight = 240;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        var boxes = new List<(CheckBox Box, string Path)>();
        var list = new StackPanel();
        foreach (var (path, reason) in untrusted)
        {
            var found = File.Exists(path) || Directory.Exists(path);
            var box = new CheckBox { Content = path + (found ? $"  ({reason})" : $"  ({NotFoundText})"), Margin = new Thickness(0, 2, 0, 2) };
            ToolTip.SetTip(box, $"{path}\n{(found ? reason : NotFoundText)}");
            boxes.Add((box, path));
            list.Children.Add(box);
        }

        var allow = new Button { Name = "AllowSelectedButton", Content = AllowSelectedText, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        allow.Click += (_, _) =>
        {
            var chosen = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Path).ToList();
            Close();
            if (chosen.Count > 0) approve(chosen);
        };
        var keep = new Button { Name = "KeepDisabledButton", Content = KeepDisabledText, Padding = new Thickness(12, 3, 12, 3), IsCancel = true };
        keep.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(allow);
        buttons.Children.Add(keep);

        var intro = new TextBlock { Text = PluginTrust.RightsNotice + IntroTail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var dock = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(intro, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(intro);
        dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;
    }
}
