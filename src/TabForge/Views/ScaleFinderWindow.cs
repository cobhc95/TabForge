using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Scale finder: "Likely scales" analyses the notes of the selection (or the whole song) and lists the
/// scales and keys they fit, best first; "All scales" lets you pick any root and scale without searching.
/// The chosen scale is highlighted on the fretboard. Colours come from the theme resources.
/// </summary>
public static class ScaleFinderWindow
{
    /// <summary>Passage to analyse when the user picks "Selection": bars/cells inclusive.</summary>
    public sealed record Range(int StartMeasure, int StartCell, int EndMeasure, int EndCell, string Label);

    /// <summary>Returns "Root Scale" to highlight, "Off" to clear, or null when closed without a choice.</summary>
    public static string? Show(Window? owner, SongProject project, TrackModel? track, Range? selection, string? current,
        string? highlightStyle = null, string? highlightColour = null, Action<string?, string?>? appearanceChanged = null)
    {
        string? result = null;
        var w = new Window
        {
            Title = "Scale finder", Width = 560, Height = 600, MinHeight = 420, MinWidth = 460,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner, ShowInTaskbar = false,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");

        var root = new DockPanel { Margin = new Thickness(14) };

        // ---- where to look ----
        var source = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(source, Dock.Top);
        source.Children.Add(Muted("Search the notes in"));
        var inSelection = new RadioButton
        {
            Content = selection is null ? "Selection (nothing selected)" : $"Selection ({selection.Label})",
            IsEnabled = selection is not null, IsChecked = selection is not null, Margin = new Thickness(0, 4, 0, 0), GroupName = "scope"
        };
        var inSong = new RadioButton { Content = "Entire song", IsChecked = selection is null, Margin = new Thickness(0, 4, 0, 0), GroupName = "scope" };
        var allTracks = new CheckBox
        {
            Content = track is null ? "All tracks" : $"All tracks (not only {track.Name})",
            // A drum track has no pitches to analyse: start with every track instead.
            IsChecked = track is null || track.Kind == TrackKind.Drums || track.MidiChannel == 9,
            IsEnabled = track is not null, Margin = new Thickness(0, 6, 0, 0)
        };
        source.Children.Add(inSelection);
        source.Children.Add(inSong);
        source.Children.Add(allTracks);
        root.Children.Add(source);

        // ---- buttons ----
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var clear = new Button { Content = "Clear highlight", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button { Content = "Show on fretboard", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        apply.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
        apply.Foreground = Brushes.White;
        var close = new Button { Content = "Close", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
        buttons.Children.Add(clear);
        buttons.Children.Add(apply);
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        // ---- highlight appearance (applies at once and is saved for every song) ----
        if (appearanceChanged is not null)
        {
            var look = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(look, Dock.Bottom);
            look.Children.Add(new TextBlock { Text = "Highlight style", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var styleCombo = new ComboBox { Width = 150, ItemsSource = ScaleHighlightStyles.All, SelectedItem = highlightStyle ?? ScaleHighlightStyles.Shaded };
            look.Children.Add(styleCombo);
            look.Children.Add(new TextBlock { Text = "Colour", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
            var colourCombo = new ComboBox { Width = 110, ItemsSource = ScaleHighlightStyles.Colours, SelectedItem = highlightColour ?? "Blue" };
            look.Children.Add(colourCombo);
            styleCombo.SelectionChanged += (_, _) => appearanceChanged(styleCombo.SelectedItem as string, null);
            colourCombo.SelectionChanged += (_, _) => appearanceChanged(null, colourCombo.SelectedItem as string);
            root.Children.Add(look);
        }

        var status = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        // ---- likely scales / all scales ----
        var tabs = new TabControl();
        var likely = new ListBox { BorderThickness = new Thickness(0) };
        likely.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        tabs.Items.Add(new TabItem { Header = "Likely scales", Content = likely });

        var browse = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var rootRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(rootRow, Dock.Top);
        rootRow.Children.Add(new TextBlock { Text = "Key (root)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var rootCombo = new ComboBox { Width = 90 };
        foreach (var n in MusicTheoryService.NoteNames) rootCombo.Items.Add(n);
        rootRow.Children.Add(rootCombo);
        browse.Children.Add(rootRow);
        var allScales = new ListBox { BorderThickness = new Thickness(0) };
        allScales.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        browse.Children.Add(allScales);
        tabs.Items.Add(new TabItem { Header = "All scales", Content = browse });
        root.Children.Add(tabs);

        // Current highlight preselects the browse tab.
        var (currentRoot, currentScale) = Split(current);
        rootCombo.SelectedItem = currentRoot ?? "C";
        foreach (var name in MusicTheoryService.Scales.Keys) allScales.Items.Add(new ListBoxItem { Content = name, Tag = name });
        if (currentScale is not null)
            allScales.SelectedItem = allScales.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == currentScale);

        void Analyse()
        {
            likely.Items.Clear();
            var tracks = allTracks.IsChecked == true || track is null ? project.Tracks : new List<TrackModel> { track };
            var useSelection = inSelection.IsChecked == true && selection is not null;
            var summary = useSelection
                ? ScaleDetector.Collect(tracks, selection!.StartMeasure, selection.StartCell, selection.EndMeasure, selection.EndCell)
                : ScaleDetector.Collect(tracks, 0, 0, int.MaxValue - 1, int.MaxValue - 1);
            if (summary.NoteCount == 0)
            {
                status.Text = "No pitched notes to analyse here. Pick a scale on the All scales tab instead.";
                tabs.SelectedIndex = 1;
                return;
            }
            var used = Enumerable.Range(0, 12).Where(pc => summary.Weights[pc] > 0).Select(pc => MusicTheoryService.NoteNames[pc]);
            status.Text = $"{summary.NoteCount} notes analysed · notes used: {string.Join(" ", used)}. Usually several scales fit; pick one.";
            foreach (var c in ScaleDetector.Rank(summary).Take(14))
            {
                var row = new StackPanel { Margin = new Thickness(4, 5, 4, 5) };
                row.Children.Add(new TextBlock { Text = c.Highlight, FontWeight = FontWeights.SemiBold, FontSize = 14 });
                var detail = new TextBlock
                {
                    Text = $"{c.Coverage:P0} of the notes fit" + (c.UnusedScaleNotes > 0 ? $" · {c.UnusedScaleNotes} scale note(s) not played" : " · every scale note is played") +
                           $"   ({c.NoteList})",
                    TextWrapping = TextWrapping.Wrap
                };
                detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                row.Children.Add(detail);
                likely.Items.Add(new ListBoxItem { Content = row, Tag = c.Highlight });
            }
            likely.SelectedIndex = 0;
        }

        string? Chosen() => tabs.SelectedIndex == 0
            ? (likely.SelectedItem as ListBoxItem)?.Tag as string
            : allScales.SelectedItem is ListBoxItem { Tag: string scale } && rootCombo.SelectedItem is string r ? $"{r} {scale}" : null;

        inSelection.Checked += (_, _) => Analyse();
        inSong.Checked += (_, _) => Analyse();
        allTracks.Click += (_, _) => Analyse();
        likely.MouseDoubleClick += (_, _) => apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        allScales.MouseDoubleClick += (_, _) => apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        apply.Click += (_, _) =>
        {
            var chosen = Chosen();
            if (chosen is null) { status.Text = "Select a scale first."; return; }
            result = chosen;
            w.DialogResult = true;
        };
        clear.Click += (_, _) => { result = "Off"; w.DialogResult = true; };

        w.Content = root;
        Analyse();
        return DialogHost.ShowModal(w) == true ? result : null;
    }

    private static TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }

    private static (string? Root, string? Scale) Split(string? highlight)
    {
        if (string.IsNullOrWhiteSpace(highlight) || highlight == "Off") return (null, null);
        var space = highlight.IndexOf(' ');
        return space <= 0 ? (null, null) : (highlight[..space], highlight[(space + 1)..]);
    }
}
