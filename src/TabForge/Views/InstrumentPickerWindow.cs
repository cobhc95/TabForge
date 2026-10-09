using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Instrument catalogue: every sound with its badge, grouped by family, with a search box that filters
/// as you type. Double-click or Select picks the instrument. With <c>focusFamily</c> (the track-row instrument icon) only the
/// current instrument's family is open; the other family headers open with a click, and a search shows matches in every family.
/// </summary>
public static class InstrumentPickerWindow
{
    /// <param name="preview">Called with the tile's name on every click (a live audition); the caller ends it after the dialog closes.</param>
    public static string? Show(Window? owner, string? current, Color trackColour, bool focusFamily = false, Action<string>? preview = null)
    {
        var w = new Window
        {
            Title = "Choose instrument", Width = 760, Height = 620, MinWidth = 480, MinHeight = 360,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner, ShowInTaskbar = false,
        };
        w.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(14) };

        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(6, 4, 6, 4), FontSize = 14 };
        System.Windows.Automation.AutomationProperties.SetName(search, "Search instruments");
        var searchHint = new TextBlock { Text = "Search instruments (e.g. \"bass\", \"strings\", \"drum\")…", IsHitTestVisible = false,
            Margin = new Thickness(10, 5, 0, 0), Opacity = 0.55 };
        var searchHost = new Grid();
        searchHost.Children.Add(search);
        searchHost.Children.Add(searchHint);
        DockPanel.SetDock(searchHost, Dock.Top);
        root.Children.Add(searchHost);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var selectedText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), FontWeight = FontWeights.SemiBold };
        var ok = new Button { Content = "Select", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        buttons.Children.Add(selectedText); buttons.Children.Add(ok); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var host = new StackPanel();
        scroll.Content = host;
        root.Children.Add(scroll);

        string? chosen = current;
        Border? chosenTile = null;
        var tiles = new List<(InstrumentEntry Entry, Border Tile)>();
        var headers = new List<(string Family, TextBlock Header, WrapPanel Panel)>();
        var currentFamily = InstrumentCatalog.Find(current)?.Category;
        var open = InstrumentCatalog.Categories.ToDictionary(f => f, f => !focusFamily || currentFamily is null || f == currentFamily);

        void Select(InstrumentEntry entry, Border tile)
        {
            if (chosenTile is not null) chosenTile.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
            chosenTile = tile;
            tile.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            chosen = entry.Name;
            selectedText.Text = entry.Name;
        }

        foreach (var family in InstrumentCatalog.Categories)
        {
            var header = new TextBlock { Text = family, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 10, 0, 4) };
            header.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            if (focusFamily)
            {
                header.Cursor = Cursors.Hand;
                var name = family;
                header.MouseLeftButtonDown += (_, _) => { open[name] = !open[name]; Refresh(); };
            }
            var panel = new WrapPanel();
            host.Children.Add(header);
            host.Children.Add(panel);
            headers.Add((family, header, panel));
            foreach (var entry in InstrumentCatalog.All.Where(e => e.Category == family))
            {
                var stack = new StackPanel { Width = 112 };
                stack.Children.Add(InstrumentIcon.Element(entry, 56));
                stack.Children.Add(new TextBlock { Text = entry.Name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                    FontSize = 11, Margin = new Thickness(0, 4, 0, 0), MaxHeight = 30 });
                var tile = new Border { Child = stack, Padding = new Thickness(4, 6, 4, 6), Margin = new Thickness(3), CornerRadius = new CornerRadius(6),
                    Cursor = Cursors.Hand, ToolTip = $"{entry.Name}  ·  {(entry.IsDrumKit ? "drum kit, channel 10" : $"GM program {entry.Program + 1}")}" };
                tile.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
                tile.MouseEnter += (_, _) => { if (tile != chosenTile) tile.SetResourceReference(Border.BackgroundProperty, "HoverBrush"); };
                tile.MouseLeave += (_, _) => { if (tile != chosenTile) tile.SetResourceReference(Border.BackgroundProperty, "Panel2Brush"); };
                tile.MouseLeftButtonDown += (_, e) =>
                {
                    Select(entry, tile);
                    if (e.ClickCount == 1) preview?.Invoke(entry.Name);
                    if (e.ClickCount == 2) w.DialogResult = true;
                };
                panel.Children.Add(tile);
                tiles.Add((entry, tile));
                if (string.Equals(entry.Name, current, StringComparison.OrdinalIgnoreCase)) Select(entry, tile);
            }
        }

        // Live filter: name or family contains every typed word; while searching, matches show in every family.
        void Refresh()
        {
            var words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (entry, tile) in tiles)
            {
                var hay = entry.Name + " " + entry.Category;
                tile.Visibility = words.All(wd => hay.Contains(wd, StringComparison.OrdinalIgnoreCase)) ? Visibility.Visible : Visibility.Collapsed;
            }
            foreach (var (family, header, panel) in headers)
            {
                var any = panel.Children.OfType<UIElement>().Any(c => c.Visibility == Visibility.Visible);
                var shown = any && (words.Length > 0 || open[family]);
                header.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
                panel.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
                if (focusFamily) header.Text = (shown ? "▾ " : "▸ ") + family;
            }
        }
        search.TextChanged += (_, _) => { Refresh(); scroll.ScrollToTop(); };
        Refresh();
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var first = tiles.FirstOrDefault(t => t.Tile.Visibility == Visibility.Visible);
            if (first.Tile is not null) { Select(first.Entry, first.Tile); w.DialogResult = true; }
        };
        ok.Click += (_, _) => w.DialogResult = true;
        w.Content = root;
        FocusManager.SetFocusedElement(w, search);   // the search box is ready to type into
        w.Loaded += (_, _) =>
        {
            search.Focus();
            chosenTile?.BringIntoView();
        };
        return DialogHost.ShowModal(w) == true ? chosen : null;
    }
}
