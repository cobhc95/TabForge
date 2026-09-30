using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Command palette: type to fuzzy-search every <see cref="HotkeyCatalog"/> command, see its bound key, press
/// Enter to run it. (Not the tool palette in MainWindow.Palette.cs.)
/// </summary>
internal sealed class CommandPalette : Window
{
    internal sealed record Entry(string Id, string Title, string Key, string Description);

    private readonly List<Entry> _all;
    private readonly TextBox _query = new();
    private readonly ListBox _list = new();
    private string? _chosen;
    private bool _closing;

    /// <summary>Screenshot tour: do not close when the window loses focus.</summary>
    internal bool KeepOpen { get; set; }

    /// <summary>The command id the user picked, or null when cancelled.</summary>
    public string? ChosenId => _chosen;

    public CommandPalette(Window owner, HotkeySettings hotkeys)
    {
        Owner = owner;
        Title = "Command palette";
        Width = 520; Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "PanelBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        _all = HotkeyCatalog.All
            .Select(a => new Entry(a.Id, a.Category + ": " + a.Name, HotkeyCatalog.Display(HotkeyCatalog.GestureFor(hotkeys, a.Id)), a.Description))
            .ToList();

        AutomationProperties.SetName(_query, "Search commands");
        AutomationProperties.SetName(_list, "Matching commands");
        _query.Margin = new Thickness(8, 8, 8, 4);
        _query.Padding = new Thickness(4);
        _query.TextChanged += (_, _) => Refresh();
        _list.Margin = new Thickness(8, 4, 8, 8);
        _list.SetResourceReference(BackgroundProperty, "PanelBrush");
        _list.SetResourceReference(ForegroundProperty, "TextBrush");
        _list.ItemTemplate = BuildTemplate();
        _list.MouseDoubleClick += (_, _) => Choose();
        _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_list, 1);
        grid.Children.Add(_query);
        grid.Children.Add(_list);
        Content = grid;

        PreviewKeyDown += OnKey;
        Loaded += (_, _) => { _query.Focus(); Refresh(); };
        // Closing deactivates the window; closing it again from Deactivated throws, so close only once.
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) => { if (IsVisible && !_closing && !KeepOpen) Close(); };
    }

    private static DataTemplate BuildTemplate()
    {
        var template = new DataTemplate();
        var panel = new FrameworkElementFactory(typeof(DockPanel));
        var key = new FrameworkElementFactory(typeof(TextBlock));
        key.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Entry.Key)));
        key.SetValue(DockPanel.DockProperty, Dock.Right);
        key.SetValue(UIElement.OpacityProperty, 0.75);
        key.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 0, 0));
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Entry.Title)));
        title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        panel.AppendChild(key);
        panel.AppendChild(title);
        template.VisualTree = panel;
        return template;
    }

    private void Refresh()
    {
        var q = _query.Text.Trim();
        var shown = q.Length == 0
            ? _all.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ToList()
            : _all.Select(e => (Entry: e, Score: Score(q, e.Title)))
                  .Where(x => x.Score > 0)
                  .OrderByDescending(x => x.Score).ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
                  .Select(x => x.Entry).ToList();
        _list.ItemsSource = shown;
        if (shown.Count > 0) _list.SelectedIndex = 0;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Enter: Choose(); e.Handled = true; break;
            case Key.Down: Move(1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
        }
    }

    private void Move(int delta)
    {
        if (_list.Items.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
        _list.ScrollIntoView(_list.SelectedItem);
    }

    private void Choose()
    {
        if (_list.SelectedItem is not Entry entry) return;
        _chosen = entry.Id;
        Close();
    }

    /// <summary>
    /// Fuzzy score: 0 = no match. Every query character must appear in order (case-insensitive); consecutive
    /// runs and word starts score higher, and earlier/tighter matches beat scattered ones.
    /// </summary>
    internal static int Score(string query, string text)
    {
        if (query.Length == 0) return 1;
        var score = 0;
        var t = 0;
        var run = 0;
        foreach (var c in query)
        {
            if (char.IsWhiteSpace(c)) { run = 0; continue; }
            var found = false;
            while (t < text.Length)
            {
                var same = char.ToLowerInvariant(text[t]) == char.ToLowerInvariant(c);
                var wordStart = t == 0 || !char.IsLetterOrDigit(text[t - 1]);
                t++;
                if (!same) { run = 0; continue; }
                run++;
                score += 1 + run * 2 + (wordStart ? 6 : 0);
                found = true;
                break;
            }
            if (!found) return 0;
        }
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 20;
        return score;
    }
}
