using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>The "Linked audio" window: folders waiting for approval (Allow selected) and the approvals already given (Revoke selected).</summary>
internal static class LinkedAudioReviewWindow
{
    public static void Show(Window owner, LinkedAudioReview review)
    {
        var w = new Window
        {
            Title = "Linked audio", Owner = owner, Width = 720, Height = 480, MinWidth = 420, MinHeight = 260, ShowInTaskbar = false,
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
        if (review.Waiting.Count > 0) list.Children.Add(new TextBlock { Text = "Waiting for approval", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var d in review.Waiting)
        {
            var box = new CheckBox { Margin = new Thickness(0, 2, 0, 2), Content = d.Verdict.Folder + (d.Verdict.Location == MediaLocation.Network ? "  (network)" : "  (removable)"), ToolTip = d.Verdict.FullPath };
            allowBoxes.Add((box, d));
            list.Children.Add(box);
        }
        void AddGiven(string title, IReadOnlyList<MediaApproval> items)
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
        AddGiven("Allowed for this song", review.AllowedForThisSong);
        AddGiven("Allowed for other songs", review.AllowedForOtherSongs);
        AddGiven("Older approvals for unsaved songs (no longer used)", review.Older);
        if (allowBoxes.Count == 0 && revokeBoxes.Count == 0) list.Children.Add(new TextBlock { Text = "No linked audio folders are waiting or allowed." });
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var allow = new Button { Content = "Allow selected", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0), IsDefault = true, IsEnabled = allowBoxes.Count > 0 };
        var revoke = new Button { Content = "Revoke selected", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0), IsEnabled = revokeBoxes.Count > 0 };
        var close = new Button { Content = "Close", Padding = new Thickness(12, 3, 12, 3), IsCancel = true };
        allow.Click += (_, _) => { review.Allow(allowBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.Decision).ToList()); w.Close(); };
        revoke.Click += (_, _) => { review.Revoke(revokeBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.Approval).ToList()); w.Close(); };
        buttons.Children.Add(allow);
        buttons.Children.Add(revoke);
        buttons.Children.Add(close);
        w.Content = root;
        DialogHost.ShowModal(w);
    }
}

/// <summary>The "Review plug-ins" window: tick the plug-ins to trust, then Allow selected.</summary>
internal static class PluginReviewWindow
{
    public static void Show(Window owner, PluginReview review)
    {
        var w = new Window
        {
            Title = "Review plug-ins", Owner = owner, Width = 720, Height = 420, MinWidth = 420, MinHeight = 240, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(12) };
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = TabForge.Plugins.PluginTrust.RightsNotice + " Tick the plug-ins you trust and click Allow selected: the approval covers this exact file, and you are asked again if it changes. The others stay disabled." };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var list = new StackPanel();
        var boxes = new List<(CheckBox Box, string Path)>();
        foreach (var (path, reason) in review.Untrusted)
        {
            var found = System.IO.File.Exists(path) || System.IO.Directory.Exists(path);
            var box = new CheckBox { Margin = new Thickness(0, 2, 0, 2), Content = path + (found ? $"  ({reason})" : "  (not found)"), ToolTip = $"{path}\n{(found ? reason : "not found")}" };
            boxes.Add((box, path));
            list.Children.Add(box);
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var allow = new Button { Content = "Allow selected", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        var keep = new Button { Content = "Keep disabled", Padding = new Thickness(12, 3, 12, 3), IsCancel = true };
        allow.Click += (_, _) => { w.DialogResult = true; };
        keep.Click += (_, _) => { w.DialogResult = false; };
        buttons.Children.Add(allow);
        buttons.Children.Add(keep);
        w.Content = root;
        if (DialogHost.ShowModal(w) != true) return;
        var chosen = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Path).ToList();
        if (chosen.Count == 0) return;
        review.Approve(chosen);
    }
}
