using System.Windows;
using System.Windows.Controls;
using TabForge.Plugins;
using TabForge.Views;

namespace TabForge;

// MainWindow: non-modal notice when the open song names native plug-ins from locations the user has not approved
// (they are never loaded until allowed; see PluginTrust).
public partial class MainWindow
{
    private Border? _trustBar;
    private TextBlock? _trustText;

    private void UpdatePluginTrustBar()
    {
        var details = PluginTrust.UntrustedDetails(_project, _settings.Plugins);
        var untrusted = details.Select(d => d.Path).ToList();
        if (untrusted.Count == 0) { if (_trustBar is not null) _trustBar.Visibility = Visibility.Collapsed; return; }
        if (_trustBar is null)
        {
            if (MainStatusBar.Parent is not DockPanel dock) return;
            _trustText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _trustText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var review = new Button { Content = "Review…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(10, 0, 0, 0) };
            review.Click += (_, _) => ReviewUntrustedPlugins();
            DockPanel.SetDock(review, Dock.Right);
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 3, 8, 3) };
            row.Children.Add(review);
            row.Children.Add(_trustText);
            _trustBar = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) };
            _trustBar.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            _trustBar.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
            DockPanel.SetDock(_trustBar, Dock.Bottom);
            dock.Children.Insert(dock.Children.IndexOf(MainStatusBar) + 1, _trustBar);
        }
        var names = string.Join(", ", untrusted.Take(3).Select(p => System.IO.Path.GetFileName(p.TrimEnd('\\', '/'))));
        var changed = details.Any(d => d.Reason == PluginTrust.ReasonChanged);
        _trustText!.Text = $"This song uses {untrusted.Count} plug-in{(untrusted.Count == 1 ? "" : "s")} {(changed ? "you haven't approved or that changed since you approved them" : "from locations you haven't approved")}: {names}{(untrusted.Count > 3 ? ", …" : "")}. They are not loaded.";
        _trustText.ToolTip = string.Join("\n", details.Select(d => $"{d.Path}  ({d.Reason})"));
        _trustBar.Visibility = Visibility.Visible;
    }

    private void ReviewUntrustedPlugins()
    {
        var untrusted = PluginTrust.UntrustedDetails(_project, _settings.Plugins);
        if (untrusted.Count == 0) { UpdatePluginTrustBar(); return; }
        var w = new Window
        {
            Title = "Review plug-ins", Owner = this, Width = 720, Height = 420, MinWidth = 420, MinHeight = 240, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(12) };
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "Loading a plug-in runs its code with your user rights. Running plug-ins in their own process only contains crashes; it is not a security sandbox. Tick only plug-ins you trust and click Allow selected: the approval covers this exact file, and you are asked again if it changes. The others stay disabled." };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var list = new StackPanel();
        var boxes = new List<(CheckBox Box, string Path)>();
        foreach (var (path, reason) in untrusted)
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
        foreach (var p in chosen) PluginTrust.Approve(_settings.Plugins, p);
        SaveSettings();
        SyncAudioEngine();
        RefreshMixerWindow();
    }
}
