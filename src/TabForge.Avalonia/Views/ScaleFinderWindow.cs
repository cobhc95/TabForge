using TabForge.Avalonia.Platform;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Scale finder (Tools > Scale finder…): "Likely scales" analyses the notes of the selection or the whole song and lists the scales they fit,
/// best first; "All scales" picks any root and scale. "Show on fretboard" sets <see cref="Highlight"/> to the chosen "Root Scale".
/// Owns: the controls, their click and double-click wiring and the Highlight result. Does not own: the search, the texts and the choice
/// rules (ScaleFinderPresenter), the scale tables (MusicTheoryService) and the colours (AvaloniaPalette keys, named below).
/// Size: the constructor (under 80 lines) lays the window out once. The scope radios, the result lists, the root box and the buttons read
/// each other's state and are readonly fields set only there, so it is kept whole rather than split.
/// Tests: TabForge.Avalonia.Tests.ScaleFinderWindowTests.
/// </summary>
public sealed class ScaleFinderWindow : Window
{
    private readonly SongProject _project;
    private readonly TrackModel? _track;
    private readonly ScaleFinderPresenter.SearchRange? _selection;
    private readonly RadioButton _inSelection;
    private readonly RadioButton _inSong;
    private readonly CheckBox _allTracks;
    private readonly TabControl _tabs;
    private readonly ListBox _likely;
    private readonly ListBox _allScales;
    private readonly ComboBox _rootBox;
    private readonly TextBlock _status;

    /// <summary>
    /// The highlight the window closed with: "Root Scale" for Show on fretboard, "Off" for Clear highlight, null when closed without a choice.
    /// </summary>
    public string? Highlight { get; private set; }

    /// <summary>The track the finder searches (the selected track the window opened on); the tests read it to check the search scope.</summary>
    internal TrackModel? SearchedTrack => _track;

    /// <param name="selection">The passage the "Selection" scope searches, or null when nothing is selected.</param>
    /// <param name="current">The saved "Root Scale" highlight, or null when none is set.</param>
    public ScaleFinderWindow(SongProject project, TrackModel? track, ScaleFinderPresenter.SearchRange? selection, string? current)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _project = project;
        _track = track;
        _selection = selection;
        Title = "Scale finder"; Width = 560; Height = 600; MinWidth = 460; MinHeight = 420;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        _inSelection = new RadioButton
        {
            Name = "InSelection", Content = ScaleFinderPresenter.SelectionLabel(selection?.Label), GroupName = "Scope",
            IsEnabled = selection is not null, IsChecked = selection is not null, Margin = new Thickness(0, 4, 0, 0),
        };
        _inSong = new RadioButton
        {
            Name = "InSong", Content = "Entire song", GroupName = "Scope", IsChecked = selection is null, Margin = new Thickness(0, 4, 0, 0),
        };
        _allTracks = new CheckBox
        {
            Name = "AllTracks", Content = ScaleFinderPresenter.AllTracksLabel(track?.Name),
            IsChecked = ScaleFinderPresenter.AllTracksStartsChecked(track), IsEnabled = track is not null, Margin = new Thickness(0, 6, 0, 0),
        };
        var source = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        source.Children.Add(ToolWindowParts.Small("Search the notes in"));
        source.Children.Add(_inSelection);
        source.Children.Add(_inSong);
        source.Children.Add(_allTracks);
        DockPanel.SetDock(source, Dock.Top);

        var buttons = BuildButtons(out var clear, out var show, out var close);

        _status = ToolWindowParts.Status("StatusText");
        DockPanel.SetDock(_status, Dock.Bottom);

        _likely = new ListBox { Name = "LikelyList", BorderThickness = new Thickness(0), [!ListBox.BackgroundProperty] = new DynamicResourceExtension("PanelBackground") };
        _rootBox = new ComboBox { Name = "RootBox", Width = 90, ItemsSource = ScaleFinderPresenter.RootChoices };
        _allScales = new ListBox
        {
            Name = "AllScalesList", BorderThickness = new Thickness(0), ItemsSource = ScaleFinderPresenter.ScaleChoices,
            [!ListBox.BackgroundProperty] = new DynamicResourceExtension("PanelBackground"),
        };
        _tabs = BuildTabs();

        var root = new DockPanel { Margin = new Thickness(14) };
        root.Children.Add(source);
        root.Children.Add(buttons);
        root.Children.Add(_status);
        root.Children.Add(_tabs);
        Content = root;

        // Perf: the scale lists are virtualized (the "All scales" list was already an ItemsSource; both ride a
        // VirtualizingStackPanel with a recycling template). The "Likely" list keeps direct items: tests read their tags.
        _likely.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _allScales.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        _allScales.ItemTemplate = new FuncDataTemplate<string>((scale, _) => new TextBlock { Text = scale ?? "" }, supportsRecycling: true);

        // The current highlight preselects the All scales tab.
        var (currentRoot, currentScale) = ScaleFinderPresenter.Split(current);
        _rootBox.SelectedItem = ScaleFinderPresenter.RootOrDefault(currentRoot);
        if (currentScale is not null) _allScales.SelectedItem = currentScale;

        _inSelection.IsCheckedChanged += (_, _) => Analyse();
        _inSong.IsCheckedChanged += (_, _) => Analyse();
        _allTracks.IsCheckedChanged += (_, _) => Analyse();
        _likely.DoubleTapped += (_, _) => ShowOnFretboard();
        _allScales.DoubleTapped += (_, _) => ShowOnFretboard();
        show.Click += (_, _) => ShowOnFretboard();
        clear.Click += (_, _) => ClearHighlight();
        close.Click += (_, _) => Close();
        Analyse();
    }

    /// <summary>The Clear highlight, Show on fretboard (the default, in the accent look) and Close buttons, docked to the bottom.</summary>
    private static StackPanel BuildButtons(out Button clear, out Button show, out Button close)
    {
        clear = ButtonNamed("Clear highlight", "ClearButton");
        show = ButtonNamed("Show on fretboard", "ShowButton");
        show.IsDefault = true;
        show.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        show[!Button.ForegroundProperty] = new DynamicResourceExtension("OnAccentBrush");   // the accent fill takes its own ink, not the dialog text colour
        close = ButtonNamed("Close", "CloseButton");
        close.IsCancel = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = DialogMetrics.FooterGap };
        buttons.Children.Add(clear);
        buttons.Children.Add(show);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        return buttons;
    }

    /// <summary>The Likely scales tab and the All scales tab (the key box above the full list).</summary>
    private TabControl BuildTabs()
    {
        var rootRow = ToolWindowParts.Row(ToolWindowParts.Label("Key (root)"), _rootBox);
        DockPanel.SetDock(rootRow, Dock.Top);
        var browse = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        browse.Children.Add(rootRow);
        browse.Children.Add(_allScales);
        var tabs = new TabControl { Name = "Tabs" };
        tabs.Items.Add(new TabItem { Header = "Likely scales", Content = _likely });
        tabs.Items.Add(new TabItem { Header = "All scales", Content = browse });
        return tabs;
    }

    private void Analyse()
    {
        _likely.Items.Clear();
        var search = ScaleFinderPresenter.Search(_project, _track, _allTracks.IsChecked == true, _inSelection.IsChecked == true, _selection);
        _status.Text = search.Status;
        if (search.NoPitchedNotes)
        {
            _tabs.SelectedIndex = 1;
            return;
        }
        foreach (var row in search.Rows) _likely.Items.Add(LikelyItem(row));
        if (_likely.Items.Count > 0) _likely.SelectedIndex = 0;
    }

    private void ShowOnFretboard()
    {
        Highlight = ScaleFinderPresenter.Chosen(_tabs.SelectedIndex == 0,
            (_likely.SelectedItem as ListBoxItem)?.Tag as string,
            _rootBox.SelectedItem as string,
            _allScales.SelectedItem as string);
        Close();
    }

    private void ClearHighlight()
    {
        Highlight = ScaleFinderPresenter.OffHighlight;
        Close();
    }

    private static ListBoxItem LikelyItem(ScaleFinderPresenter.SearchRow row)
    {
        var line = new StackPanel { Margin = new Thickness(4, 5, 4, 5) };
        line.Children.Add(new TextBlock { Text = row.Highlight, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        line.Children.Add(new TextBlock
        {
            Text = row.Detail, TextWrapping = TextWrapping.Wrap,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary"),
        });
        return new ListBoxItem { Content = line, Tag = row.Highlight };
    }

    // Button text takes the palette text colour: the Core button look paints dark surfaces in both themes.
    private static Button ButtonNamed(string text, string name) => new()
    {
        Name = name, Content = text, Padding = new Thickness(12, 4), Margin = new Thickness(0, 0, 8, 0),
        [!Button.ForegroundProperty] = new DynamicResourceExtension("TextPrimary"),
    };
}
