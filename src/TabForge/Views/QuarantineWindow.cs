using System.Windows;
using System.Windows.Controls;

namespace TabForge.Views;

/// <summary>
/// Settings > Audio &amp; Plug-ins > "Plug-ins switched off after a crash": the quarantine list, read-only, with
/// Allow again per row. The caller removes the path from the live settings and re-syncs the engine.
/// </summary>
internal static class QuarantineWindow
{
    public static void Show(Window owner, Func<List<string>> current, Action<string> allowAgain)
    {
        var w = new Window
        {
            Title = "Plug-ins switched off after a crash", Owner = owner, Width = 640, Height = 360, MinWidth = 420, MinHeight = 220,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var dock = new DockPanel { Margin = new Thickness(12) };
        var caption = new TextBlock
        {
            Text = "These plug-ins crashed and are not loaded. Allow again loads one on the next playback; trust and approval stay as they are.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(caption, Dock.Top);
        var close = new Button { Content = "Close", IsCancel = true, IsDefault = true, Padding = new Thickness(14, 3, 14, 3), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom);
        close.Click += (_, _) => w.Close();
        var rows = new StackPanel();
        var scroller = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dock.Children.Add(caption); dock.Children.Add(close); dock.Children.Add(scroller);
        w.Content = dock;

        void Fill()
        {
            rows.Children.Clear();
            var list = current();
            if (list.Count == 0)
            {
                var none = new TextBlock { Text = "No plug-ins are switched off." };
                none.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                rows.Children.Add(none);
                return;
            }
            foreach (var path in list)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var allow = new Button { Content = "Allow again", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Load this plug-in again on the next playback" };
                var captured = path;
                allow.Click += (_, _) => { allowAgain(captured); Fill(); };
                DockPanel.SetDock(allow, Dock.Right);
                row.Children.Add(allow);
                row.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path,VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path });
                rows.Children.Add(row);
            }
        }
        Fill();
        DialogHost.ShowModal(w);
    }
}
