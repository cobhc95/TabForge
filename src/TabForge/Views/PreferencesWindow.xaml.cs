using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfPath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>Native, searchable settings pages backed directly by the framework-neutral descriptors.</summary>
public partial class PreferencesWindow : Window
{
    private sealed record PageRow(string Category, SettingDescriptor? Setting, HotkeyAction? Hotkey, Border Element);
    private sealed record HotkeyControl(HotkeyAction Action, Button Gesture, TextBlock Feedback, Button Reassign, Button Reset);

    private readonly Action<AppSettings>? _apply;
    private readonly Action<AppSettings>? _preview;
    private readonly Dictionary<string, SettingDescriptor> _descriptorsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SettingDescriptor> _defaultsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PageRow> _rows = new();
    private readonly List<(SettingDescriptor Descriptor, FrameworkElement Row, Button Reset)> _settingControls = new();
    private readonly List<HotkeyControl> _hotkeyControls = new();
    private readonly Dictionary<string, (Button Button, SettingsNavigationIcon Icon)> _navigation = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings;
    private AppSettings _baseline;
    private List<SettingDescriptor> _descriptors;
    private string _selectedCategory = TakeInitialCategory();
    /// <summary>The page the last Settings window was left on; a plain open (F12, the menu) returns there. Kept in memory only.</summary>
    private static string? _lastPage;
    /// <summary>Groups whose "More options" the user opened (or a search or deep link opened) in this window: "page|group".</summary>
    private readonly HashSet<string> _openMore = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _statusTimer;

    /// <summary>The page the next Settings window opens on (a page id or name, e.g. "audio" from the mixer); used once.</summary>
    internal static string? InitialCategory { get; set; }
    /// <summary>The row the next Settings window scrolls to and highlights (see <see cref="SetTarget"/>); used once.</summary>
    internal static string? InitialRow { get; set; }

    /// <summary>
    /// Deep link for the "... settings..." entries of the right-click menus: the next Settings window opens on page
    /// <paramref name="pageKey"/> (a <see cref="SettingsCatalog"/> category) and, when <paramref name="rowKey"/> is given,
    /// scrolls to that row's group and highlights the row. Both are used once, so a later plain open shows the last page again.
    /// </summary>
    internal static void SetTarget(string pageKey, string? rowKey = null)
    {
        InitialCategory = pageKey;
        InitialRow = rowKey;
    }

    private string? _pendingRow = TakeInitialRow();

    private static string? TakeInitialRow()
    {
        var row = InitialRow;
        InitialRow = null;
        return row;
    }

    private static string TakeInitialCategory()
    {
        // An unknown id opens Common settings (and says so in the debug output); a plain open returns to the last page.
        var requested = InitialCategory;
        var category = SettingsCatalog.ResolvePage(requested);
        if (requested is not null && category is null) System.Diagnostics.Debug.WriteLine($"Preferences: unknown page '{requested}', opening {SettingsCatalog.Home}");
        category ??= requested is null ? _lastPage ?? SettingsCatalog.Home : SettingsCatalog.Home;
        InitialCategory = null;
        return category;
    }

    /// <summary>Set by the main window: applies the default instrument view to every track of the open song.</summary>
    internal static Action<string>? ShowAllTracksAs { get; set; }
    private string? _recordingActionId;
    private (string ActionId, string Gesture, string ConflictingActionId)? _pendingConflict;
    private bool _dirty;
    private bool _allowClose;
    private bool _rebuilding;
    private bool _wasSearching;
    private int _cardIndex;
    private readonly DispatcherTimer _searchDebounce;
    private static readonly Dictionary<string, string> HotkeySearchIndex = new(StringComparer.Ordinal);

    public event Action<AppSettings>? SettingsApplied;
    public event Action<AppSettings>? SettingsPreviewed;

    /// <summary>Creates an unowned settings window for in-process tests.</summary>
    public PreferencesWindow(AppSettings current) : this(current, null, null, null) { }

    internal PreferencesWindow(AppSettings current, Window? owner, Action<AppSettings>? apply, Action<AppSettings>? preview)
    {
        InitializeComponent();
        Title = $"TabForge Settings - {AppInfo.DisplayVersion}";
        if (owner is not null) Owner = owner;
        _apply = apply;
        _preview = preview;
        _settings = SettingsMigration.Clone(current);
        _baseline = SettingsMigration.Clone(_settings);
        _descriptors = SettingsCatalog.Build(_settings);
        ReplaceDescriptorMap();
        foreach (var descriptor in SettingsCatalog.Build(new AppSettings()))
            _defaultsByKey[descriptor.Key] = descriptor;
        // A deep link to a row also picks the row's page (colour rows live on the Appearance page, whatever the menu says).
        if (_pendingRow is not null && _descriptorsByKey.TryGetValue(_pendingRow, out var target)) _selectedCategory = target.Category;

        CategoryFilter.ItemsSource = new[] { "All categories" }
            .Concat(SettingsCatalog.Categories)
            .Concat(HotkeyCatalog.All.Select(action => action.Category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CategoryFilter.SelectedIndex = 0;
        BuildNavigation();

        _searchDebounce = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(120) };
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); RebuildPage(); };
        SearchBox.TextChanged += (_, _) => { _searchDebounce.Stop(); _searchDebounce.Start(); };
        SearchBox.PreviewKeyDown += (_, e) =>
        {
            // Esc clears the search first (a second Esc cancels the window); Enter must not press OK from here.
            if (e.Key == Key.Enter) { e.Handled = true; return; }
            if (e.Key != Key.Escape || SearchBox.Text.Length == 0) return;
            SearchBox.Clear();
            e.Handled = true;
        };
        CategoryFilter.SelectionChanged += (_, _) => RebuildPage();
        PreviewKeyDown += Window_PreviewKeyDown;
        // Esc = Cancel. Not IsCancel on the button: that closes the dialog even after "keep editing" in the discard question
        // (and asks it twice). Bubbling KeyDown, so an open combo box, the search box and shortcut capture handle Esc first.
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            e.Handled = true;
            CancelButton_Click(this, new RoutedEventArgs());
        };
        PreviewMouseDown += Window_PreviewMouseDown;
        SizeChanged += (_, _) => UpdateSearchAreaWidth();
        SearchHeaderBorder.SizeChanged += (_, _) => UpdateSearchAreaWidth();
        Closing += Window_Closing;
        SourceInitialized += (_, _) => ApplyDarkCaption();
        Loaded += (_, _) =>
        {
            UpdateSearchAreaWidth();
            // A deep link names a row: show its page and scroll to it (the row flashes so the eye finds its group).
            var row = _pendingRow;
            _pendingRow = null;
            RebuildPage(focusSettingKey: row is not null && _descriptorsByKey.ContainsKey(row) ? row : null);
        };

        RebuildPage();
        UpdateNavigationSelection();
    }

    /// <summary>The page the window is showing (self-test hook for the deep links).</summary>
    internal string SelectedCategory => _selectedCategory;

    /// <summary>Keys of the setting rows built on the page now showing, collapsed "More options" rows included (self-test hook).</summary>
    internal IReadOnlyList<string> BuiltRowKeys => _rows.Where(row => row.Setting is not null).Select(row => row.Setting!.Key).ToList();

    /// <summary>The currently staged settings; callers should only adopt this after acceptance.</summary>
    public AppSettings Result => SettingsMigration.Clone(_settings);
    public bool HasChanges => _dirty;

    private static AppSettings Clone(AppSettings settings) => SettingsMigration.Clone(settings);

    private void UpdateSearchAreaWidth()
    {
        if (SearchAreaGrid is null || SearchHeaderBorder is null) return;
        var availableWidth = SearchHeaderBorder.ActualWidth - SearchAreaGrid.Margin.Left - SearchAreaGrid.Margin.Right;
        SearchAreaGrid.Width = Math.Max(0, Math.Min(896, availableWidth));
    }

    private void BuildNavigation()
    {
        NavigationPanel.Children.Clear();
        _navigation.Clear();
        foreach (var (band, pages) in SettingsCatalog.Bands)
        {
            // Non-clickable caption above each band of pages (11 px minimum, muted, not a button).
            if (band.Length > 0)
                NavigationPanel.Children.Add(new TextBlock
                {
                    Text = band, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(18, 12, 0, 4),
                    Foreground = (Brush)Application.Current.FindResource("MutedBrush"), Focusable = false
                });
            foreach (var category in pages) AddNavigationButton(category);
        }
    }

    private void AddNavigationButton(string category)
    {
        {
            var icon = new SettingsNavigationIcon { Kind = IconKind(category), Width = 25, Height = 25,
                VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = category, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(icon);
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            var button = new Button
            {
                Content = content,
                Tag = category,
                Style = (Style)FindResource("SettingsNavigationButton"),
                ToolTip = Subtitle(category),
                Focusable = true
            };
            AutomationProperties.SetName(button, category);
            button.Click += (_, _) => NavigateToCategory(category);
            NavigationPanel.Children.Add(button);
            _navigation[category] = (button, icon);
        }
    }

    private static string IconKind(string category) => category switch
    {
        SettingsCatalog.Home => "Home",
        SettingsCatalog.General => "General",
        SettingsCatalog.Appearance => "Appearance",
        SettingsCatalog.Score => "Score",
        SettingsCatalog.Fretboard => "Fretboard",
        SettingsCatalog.Timeline => "Timeline",
        SettingsCatalog.Editing => "Pencil",
        SettingsCatalog.Playback => "Playback",
        SettingsCatalog.AudioVst => "Audio",
        SettingsCatalog.Recording => "Recording",
        SettingsCatalog.Tabs => "Tabs",
        SettingsCatalog.Hotkeys => "Shortcuts",
        SettingsCatalog.Files => "Files",
        _ => "Advanced"
    };

    private void NavigateToCategory(string category)
    {
        _selectedCategory = _lastPage = category;
        SearchBox.Clear();
        CategoryFilter.SelectedIndex = 0;
        UpdateNavigationSelection();
        RebuildPage();
    }

    private void UpdateNavigationSelection()
    {
        foreach (var (category, entry) in _navigation)
        {
            var selected = category.Equals(_selectedCategory, StringComparison.OrdinalIgnoreCase);
            entry.Button.Style = (Style)FindResource(selected ? "SelectedNavigationButton" : "SettingsNavigationButton");
            entry.Icon.IsSelected = selected;
        }
    }

    private void RebuildPage(string? focusSettingKey = null, string? focusHotkey = null)
    {
        if (_rebuilding || ContentGrid is null) return;
        _searchDebounce?.Stop();
        _rebuilding = true;
        try
        {
            _rows.Clear();
            _settingControls.Clear();
            _hotkeyControls.Clear();
            ContentGrid.Children.Clear();
            ContentGrid.RowDefinitions.Clear();
            ContentGrid.ColumnDefinitions.Clear();
            _cardIndex = 0;

            var query = SearchBox.Text.Trim();
            var categoryFilter = CategoryFilter.SelectedIndex > 0 ? CategoryFilter.SelectedItem?.ToString() : null;
            var searching = query.Length > 0;
            // One centred column: pages are short (Basic rows first, the rest behind "More options").
            const int columns = 1;
            ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ResetPageButton.Visibility = !searching && _selectedCategory is not (SettingsCatalog.Home or SettingsCatalog.Advanced)
                ? Visibility.Visible : Visibility.Collapsed;

            if (!searching && _selectedCategory == SettingsCatalog.Hotkeys)
            {
                BuildHotkeyPage(categoryFilter);
                SetPageHeader(SettingsCatalog.Hotkeys, Subtitle(SettingsCatalog.Hotkeys));
            }
            else if (!searching && _selectedCategory == SettingsCatalog.Advanced)
            {
                BuildAdvancedPage();
                SetPageHeader(SettingsCatalog.Advanced, Subtitle(SettingsCatalog.Advanced));
            }
            else if (!searching && _selectedCategory == SettingsCatalog.Home)
            {
                BuildHomePage();
                SetPageHeader(SettingsCatalog.Home, Subtitle(SettingsCatalog.Home));
            }
            else
            {
                var descriptors = _descriptors.Where(descriptor =>
                    (searching ? SettingsCatalog.Matches(descriptor, query.ToLowerInvariant()) :
                        descriptor.Category.Equals(_selectedCategory, StringComparison.OrdinalIgnoreCase)) &&
                    (categoryFilter is null || descriptor.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                // A search result or a deep link lands on a row that may be inside "More options": open that group.
                if (focusSettingKey is not null && _descriptorsByKey.TryGetValue(focusSettingKey, out var focused))
                    _openMore.Add(MoreKey(focused));

                foreach (var group in descriptors.OrderBy(descriptor => searching ? descriptor.Category : "")
                             .ThenBy(descriptor => descriptor.Order).GroupBy(descriptor => (descriptor.Category, descriptor.Group)))
                {
                    var card = GroupCard(searching ? $"{group.Key.Category} › {group.Key.Group}" : group.Key.Group);
                    var basic = group.Where(descriptor => !descriptor.More).ToList();
                    var more = group.Where(descriptor => descriptor.More).ToList();
                    foreach (var descriptor in basic) AddCardContent(card, SettingRow(descriptor, searching));
                    if (!searching && _selectedCategory == SettingsCatalog.Appearance && group.Key.Group == "Theme")
                        AddThemeReset(card);
                    if (more.Count > 0) AddMoreOptions(card, more, searching);
                    AddTopCard(card, columns);
                }

                var hotkeys = searching ? HotkeyCatalog.All.Where(action =>
                    (categoryFilter is null || categoryFilter.Equals(SettingsCatalog.Hotkeys, StringComparison.OrdinalIgnoreCase) ||
                     categoryFilter.Equals(action.Category, StringComparison.OrdinalIgnoreCase)) &&
                    (HotkeySearchText(action).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     "hotkeys shortcuts commands".Contains(query, StringComparison.OrdinalIgnoreCase))).ToList() : new List<HotkeyAction>();
                foreach (var group in hotkeys.GroupBy(action => action.Category))
                {
                    var card = GroupCard(group.Key + " commands");
                    foreach (var action in group) AddCardContent(card, HotkeyRow(action, true));
                    AddTopCard(card, columns);
                }

                if (searching && descriptors.Count == 0 && hotkeys.Count == 0)
                    AddTopCard(InformationCard("No matching settings", "Try: theme, audio, metronome, shortcut. Or choose a different category."), columns);

                SetPageHeader(searching ? "Search results" : _selectedCategory,
                    searching ? $"{descriptors.Count + hotkeys.Count} results for ‘{query}’" : Subtitle(_selectedCategory));
            }

            if (ContentGrid.RowDefinitions.Count == 0)
                ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            UpdateDependencies();
            RefreshResetButtons();
            ApplyButton.IsEnabled = _dirty;
            if (searching && !_wasSearching) PageScroll.ScrollToTop();
            _wasSearching = searching;
        }
        finally { _rebuilding = false; }

        if (focusSettingKey is not null || focusHotkey is not null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var match = _rows.FirstOrDefault(row => focusSettingKey is not null
                    ? row.Setting?.Key.Equals(focusSettingKey, StringComparison.OrdinalIgnoreCase) == true
                    : row.Hotkey?.Id.Equals(focusHotkey, StringComparison.OrdinalIgnoreCase) == true);
                if (match is null) return;
                match.Element.BringIntoView();
                Highlight(match.Element);
            }));
        }
    }

    private void SetPageHeader(string title, string subtitle)
    {
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
    }

    private void AddTopCard(Border card, int columns)
    {
        var row = _cardIndex / columns;
        var column = _cardIndex % columns;
        if (column == 0) ContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.Margin = new Thickness(0, 0, column + 1 < columns ? 12 : 0, 12);
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        ContentGrid.Children.Add(card);
        _cardIndex++;
    }

    private static Border GroupCard(string title)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Background = (Brush)Application.Current.FindResource("Panel2Brush"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderSoftBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static void AddCardContent(Border card, FrameworkElement row)
    {
        if (card.Child is not StackPanel stack) return;
        if (stack.Children.Count > 1) row.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(row);
    }

    private Border SettingRow(SettingDescriptor descriptor, bool searchResult)
    {
        var rowContent = new Grid { MinHeight = 48 };
        rowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement title;
        if (searchResult)
        {
            var button = new Button
            {
                Content = descriptor.Title,
                Style = (Style)FindResource("SearchResultTitleButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Open this setting in its category."
            };
            AutomationProperties.SetName(button, descriptor.Title);
            button.Click += (_, _) => NavigateToSetting(descriptor.Key);
            title = button;
        }
        else
        {
            title = new TextBlock { Text = descriptor.Title, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        }
        labelStack.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(descriptor.Description))
            labelStack.Children.Add(Note(descriptor.Description, 12, new Thickness(0, 3, 12, 0)));
        rowContent.Children.Add(labelStack);

        var editorAndReset = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var editor = BuildEditor(descriptor);
        editorAndReset.Children.Add(editor);
        var reset = CreateButton("Reset", "ResetActionButton", $"Restore the default value for {descriptor.Title}.");
        reset.Margin = new Thickness(8, 0, 0, 0);
        reset.Visibility = IsCustomized(descriptor) ? Visibility.Visible : Visibility.Collapsed;
        reset.Click += (_, _) => ResetSetting(descriptor);
        // Per-row reset buttons are intentionally not shown; the footer's Reset page / Reset all cover this.
        // A wide editor (long device names, a combo plus a button) would squeeze the label into a sliver of one
        // word per line: it goes under the label instead, at full width of the card.
        editorAndReset.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (editorAndReset.DesiredSize.Width > 215)
        {
            rowContent.ColumnDefinitions.Clear();
            rowContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rowContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editorAndReset.HorizontalAlignment = HorizontalAlignment.Left;
            editorAndReset.Margin = new Thickness(0, 6, 0, 6);
            Grid.SetRow(editorAndReset, 1);
        }
        else Grid.SetColumn(editorAndReset, 1);
        rowContent.Children.Add(editorAndReset);

        var border = new Border
        {
            Padding = new Thickness(3, 6, 3, 6),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Child = rowContent,
            Tag = descriptor.Key
        };
        _rows.Add(new PageRow(descriptor.Category, descriptor, null, border));
        _settingControls.Add((descriptor, border, reset));
        return border;
    }

    private FrameworkElement BuildEditor(SettingDescriptor descriptor)
    {
        return descriptor.Kind switch
        {
            SettingKind.Bool => BuildBoolEditor(descriptor),
            SettingKind.Choice => BuildChoiceEditor(descriptor),
            SettingKind.Number => BuildNumberEditor(descriptor),
            SettingKind.Colour => BuildColourEditor(descriptor),
            SettingKind.Button => BuildRowButton(descriptor),
            _ => BuildTextEditor(descriptor)
        };
    }

    /// <summary>Hook set by the main window: opens the Linked audio window and returns the approvals as they are afterwards.</summary>
    internal static Func<List<MediaApproval>>? ManageLinkedAudio { get; set; }

    /// <summary>The button of a <see cref="SettingKind.Button"/> row: each key has its own action.</summary>
    private FrameworkElement BuildRowButton(SettingDescriptor descriptor)
    {
        switch (descriptor.Key)
        {
            case "score.textfonts":
                return RowButton(descriptor, "Text & fonts…", true, OpenScoreTextFonts);
            case "fretboard.showallas":
                return RowButton(descriptor, "Apply to all tracks", ShowAllTracksAs is not null,
                    () => ShowAllTracksAs?.Invoke(_settings.Editing.InstrumentView));
            case "vst.quarantine":
                return BuildQuarantineButton(descriptor);
            default:
                return BuildLinkedAudioButton(descriptor);
        }
    }

    /// <summary>Hook set by the main window: lists the plug-ins switched off after a crash (live settings) with Allow again; returns the list afterwards.</summary>
    internal static Func<Window, List<string>>? ManageQuarantine { get; set; }

    private FrameworkElement BuildQuarantineButton(SettingDescriptor descriptor)
    {
        var button = RowButton(descriptor, "Plug-ins switched off after a crash…", ManageQuarantine is not null, () =>
        {
            if (ManageQuarantine is not { } open) return;
            // Allow again changes the live settings straight away (like the linked-audio approvals); keep this window's copy and baseline in step.
            var list = open(this);
            _settings.Plugins.Quarantined = list.ToList();
            _baseline.Plugins.Quarantined = list.ToList();
        });
        return button;
    }

    private Button RowButton(SettingDescriptor descriptor, string label, bool enabled, Action click)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 3, 10, 3), ToolTip = descriptor.Tooltip(_settings.Hotkeys), IsEnabled = enabled };
        AutomationProperties.SetName(button, descriptor.Title);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Per-area score text styles, edited on this window's staged settings and previewed live like any other row.</summary>
    private void OpenScoreTextFonts()
    {
        var areas = _settings.Appearance.ScoreTextAreas ??= new();
        var before = JsonSerializer.Serialize(areas);
        if (ScoreTextStyleWindow.Show(this, areas, () => { TabEditorControl.ConfigureTextAreas(areas); SettingChanged(); })) { SettingChanged(); return; }
        var restored = JsonSerializer.Deserialize<Dictionary<string, ScoreTextAreaStyle>>(before) ?? new();
        _settings.Appearance.ScoreTextAreas = restored;
        TabEditorControl.ConfigureTextAreas(restored);
        SettingChanged();
    }

    private FrameworkElement BuildLinkedAudioButton(SettingDescriptor descriptor)
    {
        var button = new Button { Content = "Manage approved folders…", Padding = new Thickness(10, 3, 10, 3), ToolTip = descriptor.Tooltip(_settings.Hotkeys),
            IsEnabled = ManageLinkedAudio is not null };
        AutomationProperties.SetName(button, "Manage approved folders");
        button.Click += (_, _) =>
        {
            if (ManageLinkedAudio is not { } open) return;
            // Approvals change straight away (they are a safety decision); keep this window's copy and its baseline in step
            // so Apply does not bring back a revoked folder and Cancel does not undo one.
            var approvals = open();
            _settings.Audio.ApprovedMedia = approvals.Select(a => new MediaApproval { Project = a.Project, Folder = a.Folder }).ToList();
            _baseline.Audio.ApprovedMedia = approvals.Select(a => new MediaApproval { Project = a.Project, Folder = a.Folder }).ToList();
        };
        return button;
    }

    private FrameworkElement BuildBoolEditor(SettingDescriptor descriptor)
    {
        var check = new CheckBox { IsChecked = descriptor.Get() is true, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = descriptor.Tooltip(_settings.Hotkeys), MinWidth = 24 };
        AutomationProperties.SetName(check, descriptor.Title);
        check.Checked += (_, _) => SettingChanged(descriptor, true);
        check.Unchecked += (_, _) => SettingChanged(descriptor, false);
        return check;
    }

    private FrameworkElement BuildChoiceEditor(SettingDescriptor descriptor)
    {
        var choices = descriptor.Choices;
        // Device names are shown exactly as Windows names them ("Audient iD4", not "Audient i D4").
        string Label(string value) => descriptor.Key is "vst.device" or "vst.input" or "vst.asio.in" or "vst.asio.out" or "vst.asio.outlast" ? value
            : descriptor.Key == "audio.fretboardstyle" && value.StartsWith("GP5: ", StringComparison.Ordinal) ? "Show " + value.Substring(5).ToLowerInvariant()
            : Prettify(value);
        var selected = choices.ToList().FindIndex(value => value.Equals(descriptor.Get()?.ToString(), StringComparison.OrdinalIgnoreCase));
        var combo = new ComboBox
        {
            Width = Math.Clamp(choices.Select(Label).DefaultIfEmpty("").Max(value => value.Length) * 8 + 36, 150, 300),
            ItemsSource = choices.Select(Label).ToArray(),
            SelectedIndex = Math.Max(0, selected),
            ToolTip = descriptor.Tooltip(_settings.Hotkeys),
            MaxDropDownHeight = 330
        };
        AutomationProperties.SetName(combo, descriptor.Title);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0 || combo.SelectedIndex >= choices.Count) return;
            descriptor.Set(choices[combo.SelectedIndex]);
            SettingChanged();
            // A theme preset rewrites many colours: show their new values (after this event finishes).
            if (descriptor.Key == "appearance.thememode") Dispatcher.BeginInvoke(new Action(() => RebuildPage()));
            // A new audio driver has its own devices: refresh the device list and the ASIO-only rows.
            if (descriptor.Key == "vst.driver")
                Dispatcher.BeginInvoke(new Action(() => { _descriptors = SettingsCatalog.Build(_settings); ReplaceDescriptorMap(); RebuildPage(); }));
        };
        if (descriptor.Key == "vst.buffer") return BufferSizeEditor(descriptor, combo, choices);
        if (descriptor.Key != "vst.device" || !string.Equals(_settings.Plugins.Driver, AudioDrivers.Asio, StringComparison.Ordinal)) return combo;
        // ASIO: the driver's own panel sets the buffer size and routing.
        var configure = new Button { Content = "Configure…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2),
            ToolTip = "Open the ASIO driver's own control panel (buffer size, sample rate, routing)" };
        configure.Click += (_, _) =>
        {
            if (!TabForge.Audio.AudioDevices.ShowAsioControlPanel(_settings.Plugins.Device, out var why)) StatusText.Text = why;
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(combo);
        panel.Children.Add(configure);
        return panel;
    }

    /// <summary>Buffer size: type any value (16 to 8192 samples) or pick a usual one from the list.</summary>
    private FrameworkElement BufferSizeEditor(SettingDescriptor descriptor, ComboBox presets, IReadOnlyList<string> choices)
    {
        var box = new TextBox { Width = 64, Text = descriptor.Get()?.ToString() ?? "256", VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Type a buffer size in samples (16 to 8192) and press Enter" };
        AutomationProperties.SetName(box, descriptor.Title);
        void Commit()
        {
            if (!int.TryParse(box.Text.Trim(), out var typed)) { box.Text = descriptor.Get()?.ToString() ?? "256"; return; }
            typed = Math.Clamp(typed, AudioDrivers.MinBuffer, AudioDrivers.MaxBuffer);
            box.Text = typed.ToString();
            if (typed.ToString() == descriptor.Get()?.ToString()) return;
            descriptor.Set(typed.ToString());
            SettingChanged();
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        // The list: picking one fills the box.
        presets.Width = 74;
        presets.SelectedIndex = -1;
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedIndex < 0 || presets.SelectedIndex >= choices.Count) return;
            box.Text = choices[presets.SelectedIndex];
            Commit();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        presets.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(presets);
        return row;
    }

    private FrameworkElement BuildNumberEditor(SettingDescriptor descriptor)
    {
        var value = ToFiniteDouble(descriptor.Get(), descriptor.Min);
        value = Math.Clamp(value, descriptor.Min, descriptor.Max);
        var spinner = BuildNumberSpinner(descriptor, value);
        if (descriptor.Key == "vst.winmidilatency") return WithMeasureButton(descriptor, spinner);
        if (!descriptor.Unit.Equals("%", StringComparison.Ordinal)) return spinner;

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = descriptor.Min,
            Maximum = descriptor.Max,
            Value = value,
            Width = 165,
            TickFrequency = descriptor.Step,
            SmallChange = descriptor.Step,
            LargeChange = descriptor.Step * 10,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = descriptor.Tooltip(_settings.Hotkeys),
            IsSnapToTickEnabled = false
        };
        AutomationProperties.SetName(slider, descriptor.Title);
        panel.Children.Add(slider);
        spinner.Margin = new Thickness(8, 0, 0, 0);
        panel.Children.Add(spinner);
        var syncing = false;
        slider.ValueChanged += (_, args) =>
        {
            if (syncing) return;
            syncing = true;
            SetNumberSpinnerValue(spinner, descriptor, args.NewValue);
            syncing = false;
            descriptor.Set(args.NewValue);
            SettingChanged();
        };
        spinner.Tag = new Action<double>(next =>
        {
            if (syncing) return;
            syncing = true;
            slider.Value = next;
            syncing = false;
            descriptor.Set(next);
            SettingChanged();
        });
        return panel;
    }

    private FrameworkElement BuildNumberSpinner(SettingDescriptor descriptor, double initial)
    {
        var width = descriptor.Unit == "%" ? 82.0 : 118.0;
        var panel = new Grid { Width = width, Height = 32, ToolTip = descriptor.Tooltip(_settings.Hotkeys),
            Tag = descriptor };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var text = new TextBox
        {
            Style = (Style)FindResource("NumericValueTextBox"),
            Text = FormatNumber(initial, descriptor.Decimals),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(text, descriptor.Title);
        Grid.SetRowSpan(text, 2);
        panel.Children.Add(text);
        var up = CreateButton("", "NumericStepButton", $"Increase {descriptor.Title}.");
        var down = CreateButton("", "NumericStepButton", $"Decrease {descriptor.Title}.");
        up.Content = SpinnerArrow(upward: true);
        down.Content = SpinnerArrow(upward: false);
        AutomationProperties.SetName(up, $"Increase {descriptor.Title}");
        AutomationProperties.SetName(down, $"Decrease {descriptor.Title}");
        Grid.SetColumn(up, 1);
        Grid.SetColumn(down, 1);
        Grid.SetRow(down, 1);
        var outer = new Border { Background = (Brush)Application.Current.FindResource("Panel2Brush"), BorderBrush = (Brush)Application.Current.FindResource("BorderSoftBrush"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = panel };
        panel.Children.Add(up);
        panel.Children.Add(down);

        var valid = true;
        var settingText = false;
        void Change(double candidate, bool updateText)
        {
            var next = Math.Clamp(candidate, descriptor.Min, descriptor.Max);
            if (updateText)
            {
                settingText = true;
                text.Text = FormatNumber(next, descriptor.Decimals);
                settingText = false;
            }
            if (outer.Tag is Action<double> callback) callback(next);
            else
            {
                descriptor.Set(next);
                SettingChanged();
            }
        }
        text.TextChanged += (_, _) =>
        {
            if (settingText) return;
            if (double.TryParse(text.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) ||
                double.TryParse(text.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                valid = true;
                text.BorderBrush = Brushes.Transparent;
                if (double.IsFinite(parsed)) Change(parsed, false);
            }
            else
            {
                valid = false;
                text.BorderBrush = Brush("#C04D5B");
            }
        };
        text.LostFocus += (_, _) =>
        {
            var current = Math.Clamp(ToFiniteDouble(descriptor.Get(), descriptor.Min), descriptor.Min, descriptor.Max);
            if (!valid || !double.TryParse(text.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var entered) ||
                !double.IsFinite(entered) || Math.Abs(Math.Clamp(entered, descriptor.Min, descriptor.Max) - entered) > 1e-9)
            {
                settingText = true;
                text.Text = FormatNumber(current, descriptor.Decimals);
                settingText = false;
            }
            valid = true;
            text.BorderBrush = Brushes.Transparent;
        };
        text.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        };
        up.Click += (_, _) => Change(ToFiniteDouble(descriptor.Get(), initial) + descriptor.Step, true);
        down.Click += (_, _) => Change(ToFiniteDouble(descriptor.Get(), initial) - descriptor.Step, true);
        AutomationProperties.SetName(outer, descriptor.Title);
        panel.Tag = null;
        return outer;
    }

    /// <summary>
    /// "Windows MIDI latency (ms)": the number box plus a Measure button. One press plays a very quiet hit five times through the Windows
    /// synth and reads it back (only when nothing else plays); the median is stored only when it is plausible (20-600 ms).
    /// </summary>
    private FrameworkElement WithMeasureButton(SettingDescriptor descriptor, FrameworkElement spinner)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var button = new Button { Content = "Measure", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Plays one very quiet hit five times through the Windows MIDI synth and reads it back from the Windows output. Stop playback first; other audio spoils the reading." };
        AutomationProperties.SetName(button, "Measure the Windows MIDI latency");
        var result = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        panel.Children.Add(spinner);
        panel.Children.Add(button);
        panel.Children.Add(result);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            result.Text = "Measuring (about 5 s)…";
            (double? Ms, string Detail) outcome;
            try { outcome = await Task.Run(() => TabForge.Audio.WindowsMidiLatency.Measure(new TabForge.Playback.SharedMidiOutput())); }
            catch (Exception ex) { outcome = (null, ex.Message); }
            if (outcome.Ms is { } ms)
            {
                var rounded = (int)Math.Round(ms);
                SetNumberSpinnerValue(spinner, descriptor, rounded);
                descriptor.Set(rounded);
                SettingChanged();
                result.Text = $"{rounded} ms stored ({outcome.Detail})";
            }
            else result.Text = $"Not changed: {outcome.Detail}";
            button.IsEnabled = true;
        };
        return panel;
    }

    private static void SetNumberSpinnerValue(FrameworkElement spinner, SettingDescriptor descriptor, double value)
    {
        if (spinner is not Border { Child: Grid grid }) return;
        if (grid.Children.OfType<TextBox>().FirstOrDefault() is { } text)
            text.Text = FormatNumber(value, descriptor.Decimals);
    }

    private static WpfPath SpinnerArrow(bool upward) => new()
    {
        Data = Geometry.Parse(upward ? "M 1,4 L 4,1 L 7,4" : "M 1,1 L 4,4 L 7,1"),
        Stroke = (Brush)Application.Current.FindResource("MutedBrush"),
        StrokeThickness = 1.3,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Width = 8,
        Height = 5,
        Stretch = Stretch.Fill,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private FrameworkElement BuildTextEditor(SettingDescriptor descriptor)
    {
        var text = new TextBox { Width = 210, Text = descriptor.Get()?.ToString() ?? "",
            ToolTip = descriptor.Tooltip(_settings.Hotkeys), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(text, descriptor.Title);
        text.TextChanged += (_, _) =>
        {
            descriptor.Set(text.Text);
            SettingChanged();
        };
        if (descriptor.Key != "vst.folders") return text;
        // Folder lists: browse to a folder instead of typing its path.
        var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Add a plug-in folder", Multiselect = true };
            if (dialog.ShowDialog(this) != true) return;
            var folders = text.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            foreach (var folder in dialog.FolderNames)
                if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) folders.Add(folder);
            text.Text = string.Join("; ", folders);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(text);
        row.Children.Add(browse);
        return row;
    }

    internal static readonly (string Name, string Hex)[] ColourPresets =
    {
        ("Glide blue", "#4C9AFF"), ("Cyan", "#22D3EE"), ("Teal", "#14B8A6"), ("Emerald", "#10B981"),
        ("Green", "#3FB950"), ("Lime", "#84CC16"), ("Violet", "#8B5CF6"), ("Indigo", "#6366F1"),
        ("Magenta", "#D946EF"), ("Rose", "#F43F5E"), ("Crimson", "#DC2626"), ("Orange", "#F97316"),
        ("Amber", "#F59E0B"), ("White", "#F2F4F6"), ("Silver", "#98A1AE"), ("Slate", "#4B5563"),
        ("Charcoal", "#23272D"), ("Black", "#111111")
    };

    /// <summary>
    /// Colour setting: a swatch (click for the full picker with hue and hex) next to a preset dropdown.
    /// The dropdown lists the setting's default, the named presets and, when needed, the current custom value.
    /// </summary>
    private FrameworkElement BuildColourEditor(SettingDescriptor descriptor)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var swatch = new Button
        {
            Style = (Style)FindResource("ColourSwatchButton"),
            Background = SafeBrush(descriptor.Get()?.ToString()),
            ToolTip = "Open the colour picker (hue, shade and hex code)."
        };
        AutomationProperties.SetName(swatch, $"Choose {descriptor.Title}");
        var defaultHex = _defaultsByKey.TryGetValue(descriptor.Key, out var original)
            ? original.Get()?.ToString() ?? "#FFFFFF" : "#FFFFFF";
        var combo = new ComboBox { Width = 170, Margin = new Thickness(8, 0, 0, 0), ToolTip = descriptor.Tooltip(_settings.Hotkeys) };
        AutomationProperties.SetName(combo, descriptor.Title);
        var syncing = false;

        FrameworkElement Entry(string name, string hex)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Tag = hex };
            row.Children.Add(new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 8, 0),
                Background = SafeBrush(hex), BorderBrush = Brush("#55606B"), BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        void Populate(string current)
        {
            syncing = true;
            combo.Items.Clear();
            combo.Items.Add(Entry("Default", defaultHex));
            foreach (var (name, hex) in ColourPresets) combo.Items.Add(Entry(name, hex));
            var match = combo.Items.OfType<FrameworkElement>()
                .FirstOrDefault(item => string.Equals((string)item.Tag, current, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                match = Entry($"Custom ({current.ToUpperInvariant()})", current);
                combo.Items.Add(match);
            }
            combo.SelectedItem = match;
            syncing = false;
        }

        void Apply(string hex)
        {
            if (!TryColour(hex, out _)) return;
            swatch.Background = SafeBrush(hex);
            descriptor.Set(hex);
            SettingChanged();
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (syncing || combo.SelectedItem is not FrameworkElement { Tag: string hex }) return;
            Apply(hex);
        };
        swatch.Click += (_, _) =>
        {
            var current = descriptor.Get()?.ToString() ?? defaultHex;
            var picker = new ColourPickerWindow(current, defaultHex, _settings.Appearance.RecentColours, this);
            if (DialogHost.ShowModal(picker) != true || picker.SelectedColour is null) return;
            AddRecentColour(picker.SelectedColour);
            Apply(picker.SelectedColour);
            Populate(picker.SelectedColour);
        };
        Populate(descriptor.Get()?.ToString() ?? defaultHex);
        panel.Children.Add(swatch);
        panel.Children.Add(combo);
        return panel;
    }
    private void BuildHotkeyPage(string? categoryFilter)
    {
        var matching = HotkeyCatalog.All.Where(action => categoryFilter is null ||
                categoryFilter.Equals(SettingsCatalog.Hotkeys, StringComparison.OrdinalIgnoreCase) ||
                action.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var reset = CreateButton("Reset category", "SecondaryActionButton", "Restore default shortcuts for the displayed category.");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Click += (_, _) => ResetHotkeyCategory(matching.Select(action => action.Category).Distinct().ToList());
        // Preset: switching really rebinds every key to that application's layout. Editing any key
        // afterwards turns the layout into "Custom" (your changes on top of the chosen base).
        var preset = new ComboBox { Width = 250, Margin = new Thickness(0, 0, 10, 0), ToolTip = "Keyboard layout preset", SelectedValuePath = "Tag" };
        foreach (var name in HotkeyPresets.Names)
        {
            var tip = HotkeyPresets.Tooltip(name);
            preset.Items.Add(new ComboBoxItem { Content = HotkeyPresets.DisplayName(name), Tag = name, ToolTip = tip.Length == 0 ? null : tip });
        }
        preset.Items.Add(new ComboBoxItem { Content = HotkeyPresets.Custom, Tag = HotkeyPresets.Custom });
        preset.SelectedValue = HotkeyPresets.Describe(_settings.Hotkeys);
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedValue is not string chosen || chosen == HotkeyPresets.Custom ||
                chosen == HotkeyPresets.Describe(_settings.Hotkeys)) return;
            HotkeyPresets.Apply(_settings.Hotkeys, chosen);
            SettingChanged();
            RebuildPage();
        };
        var presetRow = new StackPanel { Orientation = Orientation.Horizontal };
        presetRow.Children.Add(new TextBlock { Text = "Preset", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        presetRow.Children.Add(preset);
        presetRow.Children.Add(reset);
        AddTopCard(GroupCardWithChildren("Commands", presetRow), 1);
        foreach (var group in matching.GroupBy(action => action.Category))
        {
            var card = GroupCard(group.Key);
            foreach (var action in group) AddCardContent(card, HotkeyRow(action, false));
            AddTopCard(card, 1);
        }
        if (matching.Count == 0)
            AddTopCard(InformationCard("No matching shortcuts", "Choose another category to view its keyboard commands."), 1);
    }

    private Border GroupCardWithChildren(string title, FrameworkElement child)
    {
        var card = GroupCard(title);
        AddCardContent(card, child);
        return card;
    }

    private Border HotkeyRow(HotkeyAction action, bool searchResult)
    {
        var gesture = HotkeyCatalog.GestureFor(_settings.Hotkeys, action.Id);
        var customized = IsHotkeyCustomized(action);
        var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement name;
        if (searchResult)
        {
            var nameButton = CreateButton(action.Name, "SearchResultTitleButton", "Open this command on the Hotkeys page.");
            nameButton.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetName(nameButton, action.Name);
            nameButton.Click += (_, _) => NavigateToHotkey(action.Id);
            name = nameButton;
        }
        else name = new TextBlock { Text = action.Name, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        labelStack.Children.Add(name);
        labelStack.Children.Add(Note(action.Description, 12, new Thickness(0, 3, 8, 0)));
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        tags.Children.Add(Note(action.Category, 11));
        if (customized) tags.Children.Add(new TextBlock { Text = "CUSTOM", FontSize = Services.ThemeService.MinFontSize, Foreground = Brush("#6DBBFF"), Margin = new Thickness(8, 0, 0, 0) });
        labelStack.Children.Add(tags);

        var shortcut = CreateButton(string.IsNullOrWhiteSpace(gesture) ? "Unbound" : HotkeyCatalog.Display(gesture),
            "SecondaryActionButton", "Click, then press the key combination to assign.");
        shortcut.MinWidth = 142;
        shortcut.Height = 34;
        shortcut.HorizontalContentAlignment = HorizontalAlignment.Center;
        shortcut.Click += (_, _) => BeginRecording(action.Id);
        var feedback = new TextBlock { FontSize = 11, Foreground = Brush("#E08484"), TextWrapping = TextWrapping.Wrap,
            Width = 150, Visibility = Visibility.Collapsed };
        var reassign = CreateButton("Reassign", "ResetActionButton", "Move the captured shortcut from its current command to this command.");
        reassign.Visibility = Visibility.Collapsed;
        reassign.Click += (_, _) => ResolveHotkeyConflict(action.Id);
        var clear = CreateButton("Clear", "ResetActionButton", "Unbind this keyboard command.");
        clear.Click += (_, _) =>
        {
            _settings.Hotkeys.Disable(action.Id);
            SettingChanged();
            RebuildPage(focusHotkey: action.Id);
        };
        var reset = CreateButton("Reset", "ResetActionButton", "Restore the default keyboard shortcut.");
        reset.Visibility = customized ? Visibility.Visible : Visibility.Collapsed;
        reset.Click += (_, _) =>
        {
            _settings.Hotkeys.Reset(action.Id);
            SettingChanged();
            RebuildPage(focusHotkey: action.Id);
        };

        var commands = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center };
        shortcut.Margin = new Thickness(0, 0, 5, 0);
        clear.Margin = new Thickness(0, 0, 5, 0);
        reassign.Margin = new Thickness(0, 0, 5, 0);
        commands.Children.Add(shortcut);
        commands.Children.Add(clear);
        commands.Children.Add(reassign);
        commands.Children.Add(reset);
        var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(commands);
        feedback.Margin = new Thickness(0, 4, 0, 0);
        feedback.HorizontalAlignment = HorizontalAlignment.Right;
        controls.Children.Add(feedback);
        var content = new Grid { MinHeight = 57 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(labelStack);
        Grid.SetColumn(controls, 1);
        content.Children.Add(controls);
        var border = new Border
        {
            Padding = new Thickness(3, 7, 3, 7),
            BorderThickness = new Thickness(1),
            BorderBrush = customized ? Brush("#3A5369") : Brushes.Transparent,
            Child = content
        };
        _rows.Add(new PageRow(SettingsCatalog.Hotkeys, null, action, border));
        _hotkeyControls.Add(new HotkeyControl(action, shortcut, feedback, reassign, reset));
        return border;
    }

    private void BeginRecording(string actionId)
    {
        var row = _hotkeyControls.FirstOrDefault(control => control.Action.Id.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (row is null) return;
        CancelRecording();
        _pendingConflict = null;
        _recordingActionId = actionId;
        row.Gesture.Content = "Press a key…";
        row.Feedback.Text = "Press a key combination. Esc cancels.";
        row.Feedback.Visibility = Visibility.Visible;
        row.Reassign.Visibility = Visibility.Collapsed;
        row.Gesture.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingActionId is null) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelRecording();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.None) return;

        var actionId = _recordingActionId;
        var gesture = WpfHotkeyGestureAdapter.FromEvent(e);
        _recordingActionId = null;
        var conflict = HotkeyCatalog.All.FirstOrDefault(other => other.Id != actionId && HotkeyCatalog.SameContext(other.Id, actionId!) &&
            !string.IsNullOrWhiteSpace(HotkeyCatalog.GestureFor(_settings.Hotkeys, other.Id)) &&
            string.Equals(NormalizeGesture(HotkeyCatalog.GestureFor(_settings.Hotkeys, other.Id)),
                NormalizeGesture(gesture), StringComparison.OrdinalIgnoreCase));
        var row = _hotkeyControls.FirstOrDefault(control => control.Action.Id == actionId);
        if (row is null) return;
        if (conflict is not null)
        {
            _pendingConflict = (actionId, gesture, conflict.Id);
            row.Feedback.Text = $"Already used by {conflict.Name} ({conflict.Category}).";
            row.Feedback.Visibility = Visibility.Visible;
            row.Reassign.Visibility = Visibility.Visible;
            row.Gesture.Content = "Conflict";
            return;
        }

        AssignHotkey(actionId, gesture, null);
    }

    private void AssignHotkey(string actionId, string gesture, HotkeyAction? replaced)
    {
        if (replaced is not null) _settings.Hotkeys.Disable(replaced.Id);
        _settings.Hotkeys[actionId] = gesture;
        _pendingConflict = null;
        SettingChanged();
        RebuildPage(focusHotkey: actionId);
        StatusText.Text = replaced is null
            ? $"Bound {HotkeyCatalog.ById(actionId)?.Name ?? actionId} to {HotkeyCatalog.Display(gesture)}"
            : $"Moved {HotkeyCatalog.Display(gesture)} from {replaced.Name} to {HotkeyCatalog.ById(actionId)?.Name ?? actionId}";
    }

    private void ResolveHotkeyConflict(string actionId)
    {
        if (_pendingConflict is not { } pending || pending.ActionId != actionId) return;
        var conflictingAction = HotkeyCatalog.ById(pending.ConflictingActionId);
        if (conflictingAction is null) return;
        AssignHotkey(actionId, pending.Gesture, conflictingAction);
    }

    private void CancelRecording()
    {
        if (_recordingActionId is { } actionId && _hotkeyControls.FirstOrDefault(row => row.Action.Id == actionId) is { } row)
        {
            row.Gesture.Content = GestureLabel(row.Action);
            row.Feedback.Text = "";
            row.Feedback.Visibility = Visibility.Collapsed;
        }
        _recordingActionId = null;
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_recordingActionId is null) return;
        var clicked = e.OriginalSource as DependencyObject;
        for (var current = clicked; current is not null; current = ParentOf(current))
        {
            if (_hotkeyControls.Any(row => ReferenceEquals(current, row.Gesture))) return;
        }
        CancelRecording();
    }

    private string GestureLabel(HotkeyAction action) => GestureForDisplay(_settings.Hotkeys, action);

    private static string MoreKey(SettingDescriptor descriptor) => descriptor.Category + "|" + descriptor.Group;

    /// <summary>The group's advanced rows behind a "More options (n)" toggle; a search or a deep link opens it, and it stays open while this window lives.</summary>
    private void AddMoreOptions(Border card, List<SettingDescriptor> more, bool searching)
    {
        if (card.Child is not StackPanel stack) return;
        var key = MoreKey(more[0]);
        var rows = new StackPanel { Visibility = Visibility.Collapsed };
        foreach (var descriptor in more)
        {
            var row = SettingRow(descriptor, searching);
            if (rows.Children.Count > 0) row.Margin = new Thickness(0, 12, 0, 0);
            rows.Children.Add(row);
        }
        var toggle = new System.Windows.Controls.Primitives.ToggleButton
        {
            Content = $"More options ({more.Count})",
            Style = (Style)FindResource("MoreOptionsToggle"),
            Margin = new Thickness(-6, 8, 0, 0),
            IsChecked = searching || _openMore.Contains(key)
        };
        AutomationProperties.SetName(toggle, $"More options, {more.Count} more settings in this group");
        toggle.ToolTip = "Show or hide the less common settings of this group.";
        void Sync()
        {
            rows.Visibility = toggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            rows.Margin = new Thickness(0, 4, 0, 0);
        }
        toggle.Checked += (_, _) => { if (!searching) _openMore.Add(key); Sync(); };
        toggle.Unchecked += (_, _) => { if (!searching) _openMore.Remove(key); Sync(); };
        Sync();
        stack.Children.Add(toggle);
        stack.Children.Add(rows);
    }

    /// <summary>The Theme group's own button: restores the built-in theme (asks first).</summary>
    private void AddThemeReset(Border card)
    {
        var restore = CreateButton("Reset theme…", "SecondaryActionButton", "Restore the built-in dark palette, sizing, typography and motion defaults (asks first).");
        restore.HorizontalAlignment = HorizontalAlignment.Left;
        restore.Margin = new Thickness(0, 10, 0, 0);
        restore.Click += (_, _) => RestoreDefaultTheme();
        AddCardContent(card, restore);
        AddCardContent(card, Note("Restores the built-in dark palette, sizing, typography and motion defaults.", 12, new Thickness(0, 4, 0, 0)));
    }

    /// <summary>"Common settings": references to the same rows (same keys, same state) for the things people come here for, plus doors to the rest.</summary>
    private void BuildHomePage()
    {
        void Group(string title, string[] keys, params (string Label, Action Click)[] links)
        {
            var card = GroupCard(title);
            foreach (var key in keys)
                if (_descriptorsByKey.TryGetValue(key, out var descriptor)) AddCardContent(card, SettingRow(descriptor, false));
            foreach (var (label, click) in links)
            {
                var button = CreateButton(label, "SecondaryActionButton");
                button.HorizontalAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0, 10, 0, 0);
                button.Click += (_, _) => click();
                AddCardContent(card, button);
            }
            AddTopCard(card, 1);
        }
        Group("Look", new[] { "appearance.thememode", "appearance.uiscale", "appearance.fretboard", "general.toolbar" },
            ("All appearance settings", () => NavigateToCategory(SettingsCatalog.Appearance)));
        Group("Sound", new[] { "vst.driver", "vst.device", "audio.metronome", "audio.metrovolume", "audio.countin", "audio.speed" },
            ("More audio settings", () => NavigateToCategory(SettingsCatalog.AudioVst)),
            ("Playback and practice settings", () => NavigateToCategory(SettingsCatalog.Playback)));
        Group("Score", new[] { "score.defaultnotation", "follow.mode" },
            ("All score settings", () => NavigateToCategory(SettingsCatalog.Score)));
        Group("Files", new[] { "general.autosave", "general.saveformat" },
            ("Files and backups", () => NavigateToCategory(SettingsCatalog.Files)));
        var shortcuts = GroupCard("Shortcuts");
        var open = CreateButton("Open all shortcuts", "SecondaryActionButton");
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 6, 0, 0);
        open.Click += (_, _) => NavigateToCategory(SettingsCatalog.Hotkeys);
        AddCardContent(shortcuts, Note("Pick a preset layout (TabForge or another program's) and change any key on the Shortcuts page.", 12, new Thickness(0, 2, 0, 0)));
        AddCardContent(shortcuts, open);
        AddTopCard(shortcuts, 1);
        AddTopCard(InformationCard("Looking for this song's settings?",
            "Preferences are for the program. A song's own settings are in other windows: Project settings (the File menu) and Track properties (right-click a track)."), 1);
    }

    private void BuildAdvancedPage()
    {
        AddTopCard(InformationCard("Version", $"TabForge {AppInfo.DisplayVersion}"), 1);
        AddTopCard(InformationCard("Settings file",
            UserPaths.SettingsFile), 1);
        AddTopCard(InformationCard("Compatibility",
            "Older flat settings files are migrated into the current settings model when loaded or imported. All fields recognized by the settings model remain available to export; unrecognized JSON properties are ignored."), 1);
        AddTopCard(InformationCard("Arrangement playback",
            "Section edits and reordering always refresh the future playback route at a safe bar boundary. This safety behavior is intentionally not optional."), 1);
        var reset = GroupCard("Reset");
        var resetAll = CreateButton("Reset all settings…", "SecondaryActionButton", "Restore every setting and every shortcut to its default (asks first).");
        resetAll.HorizontalAlignment = HorizontalAlignment.Left;
        resetAll.Margin = new Thickness(0, 10, 0, 0);
        resetAll.Click += (_, _) => ResetAll();
        AddCardContent(reset, Note("Restores every setting and all keyboard shortcuts to their defaults. You can still press Cancel to undo it.", 12, new Thickness(0, 2, 0, 0)));
        AddCardContent(reset, resetAll);
        AddTopCard(reset, 1);
    }

    private static Border InformationCard(string title, string text)
    {
        var card = GroupCard(title);
        AddCardContent(card, Note(text, 12, new Thickness(0, 2, 0, 0)));
        return card;
    }

    /// <summary>Every reset asks first and says how much it will change; the staged change can still be undone with Cancel.</summary>
    private bool ConfirmReset(string title, string message)
    {
        var prompt = new ThemedConfirmDialog(title, message, yesToolTip: "Reset", noToolTip: "Keep the current values", showCancel: false) { Owner = this };
        return DialogHost.ShowModal(prompt) == true && prompt.Result == MessageBoxResult.Yes;
    }

    private void RestoreDefaultTheme()
    {
        if (!ConfirmReset("Reset theme", "Restore the built-in theme: palette, sizing, typography, motion and the score page look?")) return;
        var keys = new[]
        {
            "appearance.thememode", "appearance.uiscale", "appearance.density", "appearance.reduceanimations",
            "appearance.animationspeed", "appearance.accent", "appearance.selection", "appearance.hover",
            "appearance.background", "appearance.panel", "appearance.titlebar", "appearance.tabactive", "appearance.tabhover",
            "appearance.text", "appearance.muted", "appearance.font", "appearance.fontsize", "appearance.iconsize",
            "appearance.toolbaricons", "appearance.scorefont", "appearance.scorefontsize", "appearance.scorebold",
            "appearance.scoreitalic", "appearance.spacing", "appearance.paper", "appearance.scorepaper.dark",
            "appearance.scorepaper.light", "appearance.scoreink.dark", "appearance.scoreink.light",
            "appearance.scorelines.dark", "appearance.scorelines.light", "appearance.cursor",
            "score.systemspacing", "score.measurespacing", "score.ledger", "score.staffopacity",
            "score.hoverintensity", "score.selectionintensity", "score.barnumbers", "score.barnumberfrequency",
            "score.sectionheadings"
        };
        foreach (var key in keys)
            if (_descriptorsByKey.TryGetValue(key, out var target) && _defaultsByKey.TryGetValue(key, out var source))
                target.Set(source.Get());
        SettingChanged();
        RebuildPage();
    }

    private void ManageButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ManageButton, Placement = PlacementMode.Bottom,
            Style = (Style)FindResource("SettingsContextMenu") };
        var import = new MenuItem { Header = "Import settings…", ToolTip = "Load a validated TabForge settings JSON file.",
            Style = (Style)FindResource("SettingsMenuItem") };
        var export = new MenuItem { Header = "Export settings…", ToolTip = "Export the current staged settings to JSON.",
            Style = (Style)FindResource("SettingsMenuItem") };
        var resetPage = new MenuItem { Header = "Reset current page…", ToolTip = "Restore defaults for the selected category (asks first).",
            Style = (Style)FindResource("SettingsMenuItem") };
        var resetAll = new MenuItem { Header = "Reset all settings…", ToolTip = "Restore defaults for every setting and shortcut (asks first).",
            Style = (Style)FindResource("SettingsMenuItem") };
        import.Click += (_, _) => ImportSettings();
        export.Click += (_, _) => ExportSettings();
        resetPage.Click += (_, _) => ResetPage();
        resetAll.Click += (_, _) => ResetAll();
        menu.Items.Add(import);
        menu.Items.Add(export);
        menu.Items.Add(new Separator { Style = (Style)FindResource("SettingsMenuSeparator") });
        menu.Items.Add(resetPage);
        menu.Items.Add(resetAll);
        menu.IsOpen = true;
    }

    private void ResetPageButton_Click(object sender, RoutedEventArgs e) => ResetPage();

    private void ResetPage()
    {
        if (_selectedCategory == SettingsCatalog.Hotkeys)
        {
            // Resets what the page shows: the chosen category, or every category when none is chosen.
            var chosen = CategoryFilter.SelectedIndex > 0 ? CategoryFilter.SelectedItem?.ToString() : null;
            var categories = HotkeyCatalog.All.Where(action => chosen is null || chosen.Equals(SettingsCatalog.Hotkeys, StringComparison.OrdinalIgnoreCase) ||
                chosen.Equals(action.Category, StringComparison.OrdinalIgnoreCase)).Select(action => action.Category).Distinct().ToList();
            var changed = HotkeyCatalog.All.Count(action => categories.Contains(action.Category) && IsHotkeyCustomized(action));
            if (changed == 0) { StatusText.Text = "No shortcuts on this page differ from the defaults."; return; }
            if (!ConfirmReset("Reset shortcuts", $"Restore the default keys for {changed} changed shortcut{(changed == 1 ? "" : "s")}?")) return;
            ResetHotkeyCategory(categories);
            return;
        }
        var page = _descriptors.Where(item => item.Category == _selectedCategory).ToList();
        var customised = page.Count(IsCustomized);
        if (customised == 0) { StatusText.Text = "No settings on this page differ from the defaults."; return; }
        if (!ConfirmReset($"Reset {_selectedCategory}", $"Restore {customised} setting{(customised == 1 ? "" : "s")} on this page to their defaults?")) return;
        foreach (var descriptor in page)
            if (_defaultsByKey.TryGetValue(descriptor.Key, out var source)) descriptor.Set(source.Get());
        SettingChanged();
        RebuildPage();
    }

    private void ResetAll()
    {
        if (!ConfirmReset("Reset all settings", $"Restore every setting and all {HotkeyCatalog.All.Count} keyboard shortcuts to their defaults? Cancel still undoes it until you press OK or Apply.")) return;
        CopySettings(new AppSettings(), _settings);
        _descriptors = SettingsCatalog.Build(_settings);
        ReplaceDescriptorMap();
        SettingChanged();
        RebuildPage();
    }

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        var copy = SettingsMigration.Clone(source);
        target.InstrumentHeight = copy.InstrumentHeight;
        target.ArrangementHeight = copy.ArrangementHeight;
        target.BottomTabsWidth = copy.BottomTabsWidth;
        target.SectionsPanelHeight = copy.SectionsPanelHeight;
        target.ShowInstrument = copy.ShowInstrument;
        target.ShowArrangement = copy.ShowArrangement;
        target.DarkPaper = copy.DarkPaper;
        target.LeftHanded = copy.LeftHanded;
        target.ShowNoteNames = copy.ShowNoteNames;
        target.PreviewNotes = copy.PreviewNotes;
        target.PreviewHorizon = copy.PreviewHorizon;
        target.ScaleHighlight = copy.ScaleHighlight;
        target.FretboardFrets = copy.FretboardFrets;
        target.Notation = copy.Notation;
        target.NotationPreferenceSet = copy.NotationPreferenceSet;
        target.ZoomFactor = copy.ZoomFactor;
        target.Metronome = copy.Metronome;
        target.CountIn = copy.CountIn;
        target.Speed = copy.Speed;
        target.WindowWidth = copy.WindowWidth;
        target.WindowHeight = copy.WindowHeight;
        target.Maximised = copy.Maximised;
        target.Tabs = copy.Tabs;
        target.General = copy.General;
        target.Appearance = copy.Appearance;
        target.Audio = copy.Audio;
        target.Editing = copy.Editing;
        target.Hotkeys = copy.Hotkeys;
        target.Follow = copy.Follow;
        target.Timeline = copy.Timeline;
        target.Workspace = copy.Workspace;
    }

    private void ResetHotkeyCategory(IReadOnlyCollection<string> categories)
    {
        foreach (var action in HotkeyCatalog.All.Where(action => categories.Contains(action.Category)))
            _settings.Hotkeys.Reset(action.Id);
        SettingChanged();
        RebuildPage();
    }

    private void ResetSetting(SettingDescriptor descriptor)
    {
        if (!_defaultsByKey.TryGetValue(descriptor.Key, out var original)) return;
        descriptor.Set(original.Get());
        SettingChanged();
        RebuildPage(focusSettingKey: descriptor.Key);
    }

    private void ImportSettings()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import TabForge settings",
            Filter = "TabForge settings (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            CopySettings(SettingsFileService.Load(dialog.FileName), _settings);
            _descriptors = SettingsCatalog.Build(_settings);
            ReplaceDescriptorMap();
            SettingChanged();
            RebuildPage();
            StatusText.Text = "Settings imported · review, then Apply";
        }
        catch (InvalidDataException ex) { StatusText.Text = $"Import failed: {ex.Message}"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            StatusText.Text = "Import failed: the settings file could not be read.";
        }
    }

    private void ExportSettings()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export TabForge settings",
            Filter = "TabForge settings (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "TabForge-settings.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SettingsFileService.SaveAtomic(dialog.FileName, _settings);
            StatusText.Text = $"Settings exported to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) { StatusText.Text = $"Export failed: {ex.GetBaseException().Message}"; }
    }

    private void ReplaceDescriptorMap()
    {
        _descriptorsByKey.Clear();
        foreach (var descriptor in _descriptors) _descriptorsByKey[descriptor.Key] = descriptor;
    }

    private void SettingChanged(SettingDescriptor? descriptor = null, object? value = null)
    {
        if (descriptor is not null) descriptor.Set(value);
        _dirty = !string.Equals(JsonSerializer.Serialize(_settings), JsonSerializer.Serialize(_baseline), StringComparison.Ordinal);
        ApplyButton.IsEnabled = _dirty;
        _statusTimer?.Stop();
        StatusText.Text = _dirty ? "Previewing changes - not saved yet. OK keeps them, Cancel undoes them." : "All changes saved";
        var snapshot = Clone(_settings);
        SettingsPreviewed?.Invoke(snapshot);
        _preview?.Invoke(snapshot);
        UpdateDependencies();
        RefreshResetButtons();
    }

    private void Commit(bool close)
    {
        var snapshot = Clone(_settings);
        SettingsApplied?.Invoke(snapshot);
        _apply?.Invoke(Clone(snapshot));
        _baseline = Clone(snapshot);
        _dirty = false;
        ApplyButton.IsEnabled = false;
        StatusText.Text = "Applied";
        // "Applied" for two seconds, then back to the resting text.
        _statusTimer?.Stop();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusTimer.Tick += (_, _) => { _statusTimer?.Stop(); if (!_dirty) StatusText.Text = "All changes saved"; };
        _statusTimer.Start();
        if (close)
        {
            _allowClose = true;
            DialogResult = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardPendingChanges()) return;
        RestoreBaselineAndClose();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e) => Commit(close: false);
    private void OkButton_Click(object sender, RoutedEventArgs e) => Commit(close: true);

    private void RestoreBaselineAndClose()
    {
        RestoreBaseline();
        _allowClose = true;
        DialogResult = false;
    }

    private void RestoreBaseline()
    {
        _preview?.Invoke(Clone(_baseline));
        _settings = Clone(_baseline);
        _descriptors = SettingsCatalog.Build(_settings);
        ReplaceDescriptorMap();
        _dirty = false;
        ApplyButton.IsEnabled = false;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_dirty) return;
        if (!ConfirmDiscardPendingChanges())
        {
            e.Cancel = true;
            return;
        }
        RestoreBaseline();
        _allowClose = true;
    }

    private bool ConfirmDiscardPendingChanges()
    {
        if (!_dirty || !_settings.General.ConfirmDiscardSettingsChanges) return true;
        var prompt = new ThemedConfirmDialog(
            "Unsaved preferences",
            "Discard preference changes that have not been applied?",
            yesToolTip: "Discard the unapplied preference changes",
            noToolTip: "Return to settings without discarding",
            details: DescribePendingChanges(), showCancel: false, rememberText: "Don't ask me again") { Owner = this };
        var discard = DialogHost.ShowModal(prompt) == true && prompt.Result == MessageBoxResult.Yes;
        if (discard && prompt.RememberChoice)
        {
            // Cancelling restores the baseline, so record the choice there too; the main window saves it.
            _baseline.General.ConfirmDiscardSettingsChanges = false;
            _settings.General.ConfirmDiscardSettingsChanges = false;
            Dispatcher.BeginInvoke(new Action(() => DiscardPrompt.DisableAndSave()));
        }
        return discard;
    }

    /// <summary>"Category › Setting: old → new" for every setting that differs from the baseline.</summary>
    private List<string> DescribePendingChanges()
    {
        static string Show(object? value) => value switch
        {
            bool b => b ? "On" : "Off",
            double d => d.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture),
            null => "(none)",
            _ => value.ToString() ?? ""
        };
        var before = SettingsCatalog.Build(_baseline).ToDictionary(d => d.Key);
        var changes = new List<string>();
        foreach (var now in SettingsCatalog.Build(_settings))
        {
            if (!before.TryGetValue(now.Key, out var old)) continue;
            var oldText = Show(old.Get());
            var newText = Show(now.Get());
            if (!string.Equals(oldText, newText, StringComparison.Ordinal))
                changes.Add($"{now.Category} › {now.Title}: {oldText} → {newText}");
        }
        if (JsonSerializer.Serialize(_settings.Hotkeys) != JsonSerializer.Serialize(_baseline.Hotkeys))
            changes.Add("Hotkeys › keyboard shortcuts were changed");
        if (changes.Count == 0) changes.Add("Other settings not shown on these pages were changed");
        return changes;
    }

    private bool IsCustomized(SettingDescriptor descriptor) =>
        _defaultsByKey.TryGetValue(descriptor.Key, out var original) && !Equals(descriptor.Get(), original.Get());

    private bool IsHotkeyCustomized(HotkeyAction action) => _settings.Hotkeys.IsDisabled(action.Id) ||
        !string.Equals(NormalizeGesture(HotkeyCatalog.GestureFor(_settings.Hotkeys, action.Id)),
            NormalizeGesture(action.DefaultGesture), StringComparison.OrdinalIgnoreCase);

    private void RefreshResetButtons()
    {
        foreach (var (descriptor, _, reset) in _settingControls)
            reset.Visibility = IsCustomized(descriptor) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in _hotkeyControls)
            row.Reset.Visibility = IsHotkeyCustomized(row.Action) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDependencies()
    {
        foreach (var (descriptor, row, _) in _settingControls)
        {
            var enabled = IsDescriptorEnabled(descriptor, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            row.IsEnabled = enabled;
            row.Opacity = enabled ? 1 : 0.48;
        }
    }

    private bool IsDescriptorEnabled(SettingDescriptor descriptor, HashSet<string> visited)
    {
        if (descriptor.DependsOn is null) return true;
        if (!visited.Add(descriptor.Key) || !_descriptorsByKey.TryGetValue(descriptor.DependsOn, out var parent)) return false;

        var dependencyValueMatches = parent.Kind == SettingKind.Bool
            ? parent.Get() is true
            : descriptor.DependsOnValue is null || descriptor.DependsOnValue.Split('|')
                .Contains(parent.Get()?.ToString(), StringComparer.OrdinalIgnoreCase);
        return dependencyValueMatches && IsDescriptorEnabled(parent, visited);
    }

    private void NavigateToSetting(string key)
    {
        if (!_descriptorsByKey.TryGetValue(key, out var descriptor)) return;
        _selectedCategory = _lastPage = descriptor.Category;
        SearchBox.Clear();
        CategoryFilter.SelectedIndex = 0;
        UpdateNavigationSelection();
        RebuildPage(focusSettingKey: key);
    }

    private void NavigateToHotkey(string id)
    {
        _selectedCategory = _lastPage = SettingsCatalog.Hotkeys;
        SearchBox.Clear();
        CategoryFilter.SelectedIndex = 0;
        UpdateNavigationSelection();
        RebuildPage(focusHotkey: id);
    }

    private void Highlight(Border element)
    {
        element.BorderBrush = (Brush)Application.Current.FindResource("AccentBrush");
        element.Background = (Brush)Application.Current.FindResource("AccentSoftBrush");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            element.BorderBrush = Brushes.Transparent;
            element.Background = Brushes.Transparent;
        };
        timer.Start();
    }

    private void AddRecentColour(string hex)
    {
        _settings.Appearance.RecentColours.RemoveAll(value => value.Equals(hex, StringComparison.OrdinalIgnoreCase));
        _settings.Appearance.RecentColours.Insert(0, hex);
        if (_settings.Appearance.RecentColours.Count > 12)
            _settings.Appearance.RecentColours.RemoveRange(12, _settings.Appearance.RecentColours.Count - 12);
    }

    private static string HotkeySearchText(HotkeyAction action)
    {
        lock (HotkeySearchIndex)
        {
            if (!HotkeySearchIndex.TryGetValue(action.Id, out var text))
                HotkeySearchIndex[action.Id] = text =
                    $"hotkey shortcuts key binding commands {action.Name} {action.Category} {action.Description} {action.DefaultGesture} {action.Id}".ToLowerInvariant();
            return text;
        }
    }

    private static string NormalizeGesture(string gesture) =>
        HotkeyCatalog.TryParse(gesture, out var key, out var modifiers) ? HotkeyCatalog.Format(key, modifiers) : gesture.Trim();

    private static FrameworkElement Note(string text, double fontSize = 12, Thickness? margin = null)
    {
        var label = new TextBlock { Text = text, FontSize = fontSize, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("MutedBrush") };
        return new Border { Child = label, Margin = margin ?? new Thickness(0) };
    }

    private Button CreateButton(string text, string styleKey, string? tooltip = null)
    {
        var button = new Button { Content = text, Style = (Style)FindResource(styleKey) };
        if (tooltip is not null) button.ToolTip = tooltip;
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private static Brush SafeBrush(string? text) => TryColour(text, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

    private static bool TryColour(string? text, out Color color) => TabForge.Visualization.ColourText.TryParseSetting(text, out color);

    private static double ToFiniteDouble(object? value, double fallback)
    {
        try
        {
            var result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(result) ? result : fallback;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return fallback; }
    }

    private static string FormatNumber(double value, int decimals)
    {
        var format = decimals > 0 ? "0." + new string('0', decimals) : "0";
        return value.ToString(format, CultureInfo.CurrentCulture);
    }

    private static string Prettify(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var result = new System.Text.StringBuilder(value.Length + 5);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1])) result.Append(' ');
            result.Append(value[i]);
        }
        return result.ToString();
    }

    private static string Subtitle(string category) => category switch
    {
        SettingsCatalog.Home => "The settings people change most, and where to find the rest",
        SettingsCatalog.General => "Updates, window memory and the questions TabForge asks before it acts",
        SettingsCatalog.Appearance => "How the program looks: theme, size and text, panels, motion and track colours",
        SettingsCatalog.Score => "What the score shows, its spacing, text, page and ink colours, selection and hover",
        SettingsCatalog.Fretboard => "The instrument panel: view, frets, note names, scales, practice aids and its appearance",
        SettingsCatalog.Timeline => "The track list, the timeline display and sections",
        SettingsCatalog.Editing => "Note entry, copy and paste, mouse scrolling",
        SettingsCatalog.Playback => "Scrolling while playing, the playing highlight, speed, metronome, count-in and note preview",
        SettingsCatalog.AudioVst => "Output device, driver, buffer, plug-in folders and plug-in options",
        SettingsCatalog.Recording => "Input device, ASIO inputs and the recording offset",
        SettingsCatalog.Tabs => "Opening, closing and dragging tabs, and how they look",
        SettingsCatalog.Hotkeys => "Search, assign and resolve keyboard shortcuts",
        SettingsCatalog.Files => "Opening and saving, autosave and recovery, Windows file association, linked audio folders",
        SettingsCatalog.Advanced => "Version, settings file, compatibility and reset",
        _ => category
    };

    private static double ToFinite(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    private void ApplyDarkCaption()
    {
        try
        {
            // Caption follows the app theme (dark or light), like every other TabForge window.
            TabForge.Shell.WindowPolish.ApplyTitleBarTheme(this);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, uint valueSize);

    private static DependencyObject? ParentOf(DependencyObject item) => item is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(item)
        : LogicalTreeHelper.GetParent(item);

    private static string GestureForDisplay(HotkeySettings hotkeys, HotkeyAction action)
    {
        var gesture = HotkeyCatalog.GestureFor(hotkeys, action.Id);
        return string.IsNullOrWhiteSpace(gesture) ? "Unbound" : HotkeyCatalog.Display(gesture);
    }

    private sealed class ColourPickerWindow : Window
    {
        private readonly string _default;
        private readonly Slider _hue = new() { Minimum = 0, Maximum = 359, TickFrequency = 1 };
        private readonly Slider _saturation = new() { Minimum = 0, Maximum = 100, TickFrequency = 1 };
        private readonly Slider _value = new() { Minimum = 0, Maximum = 100, TickFrequency = 1 };
        private readonly Slider _alpha = new() { Minimum = 0, Maximum = 100, TickFrequency = 1 };
        private readonly CheckBox _useAlpha = new() { Content = "Include alpha" };
        private readonly Border _preview = new() { Height = 44, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1) };
        private readonly TextBox _hex = new() { Width = 130 };
        private readonly PreferencesWindow _owner;
        private bool _updating;
        private Color _color;

        public string? SelectedColour { get; private set; }

        public ColourPickerWindow(string initial, string defaultValue, IReadOnlyList<string> recent, PreferencesWindow owner)
        {
            _default = defaultValue;
            _owner = owner;
            _color = TryParse(initial, out var parsed) ? parsed : Colors.White;
            Width = 490;
            Height = 630;
            MinWidth = 490;
            MinHeight = 630;
            MaxWidth = 490;
            MaxHeight = 630;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = owner;
            Title = "Choose colour";
            Background = (Brush)Application.Current.FindResource("WindowBrush");
            Foreground = (Brush)Application.Current.FindResource("TextBrush");
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            foreach (var key in owner.Resources.Keys)
                Resources[key] = owner.Resources[key];

            _hex.Text = ColourHex(_color);
            _useAlpha.IsChecked = _color.A != byte.MaxValue;
            _alpha.Value = _color.A * 100.0 / 255;
            LoadHsv(_color);

            var content = new StackPanel { Margin = new Thickness(22) };
            content.Children.Add(new TextBlock { Text = "Colour", FontSize = 20, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10) });
            _preview.Margin = new Thickness(0, 0, 0, 12);
            content.Children.Add(_preview);

            var recentPanel = new WrapPanel();
            foreach (var item in recent.Where(value => TryParse(value, out _)).Take(12))
            {
                var recentButton = new Button
                {
                    Style = (Style)owner.FindResource("ColourSwatchButton"),
                    Width = 30,
                    Height = 28,
                    Margin = new Thickness(2),
                    Background = SafeBrush(item)
                };
                ToolTipService.SetToolTip(recentButton, item);
                recentButton.Click += (_, _) => SetColor(Parse(item));
                recentPanel.Children.Add(recentButton);
            }
            content.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 0, 8), Children =
                { new TextBlock { Text = "Recent colours", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) }, recentPanel } });
            content.Children.Add(SliderRow("Hue", _hue));
            content.Children.Add(SliderRow("Saturation", _saturation));
            content.Children.Add(SliderRow("Brightness", _value));
            var alphaRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4) };
            alphaRow.Children.Add(_useAlpha);
            _alpha.Width = 250;
            _alpha.Margin = new Thickness(12, 0, 0, 0);
            alphaRow.Children.Add(_alpha);
            content.Children.Add(alphaRow);
            var hexRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 12) };
            hexRow.Children.Add(new TextBlock { Text = "Hex", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            _hex.VerticalContentAlignment = VerticalAlignment.Center;
            hexRow.Children.Add(_hex);
            content.Children.Add(hexRow);

            var cancel = DialogButton("Cancel", "SecondaryActionButton");
            var reset = DialogButton("Default", "SecondaryActionButton");
            var use = DialogButton("Use colour", "PrimaryActionButton");
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            reset.Margin = new Thickness(0, 0, 8, 0);
            cancel.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(reset);
            actions.Children.Add(cancel);
            actions.Children.Add(use);
            content.Children.Add(actions);
            Content = content;

            _hue.ValueChanged += (_, _) => UpdateFromHsv();
            _saturation.ValueChanged += (_, _) => UpdateFromHsv();
            _value.ValueChanged += (_, _) => UpdateFromHsv();
            _alpha.ValueChanged += (_, _) => UpdateFromHsv();
            _useAlpha.Checked += (_, _) => UpdateFromHsv();
            _useAlpha.Unchecked += (_, _) => UpdateFromHsv();
            _hex.TextChanged += (_, _) =>
            {
                if (_updating || !TryParse(_hex.Text, out var value)) return;
                _updating = true;
                _color = value;
                _useAlpha.IsChecked = value.A != byte.MaxValue;
                _alpha.Value = value.A * 100.0 / 255;
                LoadHsv(value);
                _hex.Text = ColourHex(value);
                _updating = false;
                UpdatePreview();
            };
            cancel.Click += (_, _) => { SelectedColour = null; DialogResult = false; };
            reset.Click += (_, _) => { SelectedColour = _default; DialogResult = true; };
            use.Click += (_, _) => { SelectedColour = ColourHex(_color, _useAlpha.IsChecked == true); DialogResult = true; };
            UpdatePreview();
        }

        private Button DialogButton(string text, string key)
        {
            var button = new Button { Content = text, Style = (Style)_owner.FindResource(key) };
            AutomationProperties.SetName(button, text);
            return button;
        }

        private static FrameworkElement SliderRow(string label, Slider slider)
        {
            slider.Margin = new Thickness(0, 3, 0, 3);
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            slider.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(slider, 1);
            grid.Children.Add(slider);
            return grid;
        }

        private void UpdateFromHsv()
        {
            if (_updating) return;
            _color = HsvColor(_hue.Value, _saturation.Value / 100, _value.Value / 100,
                _useAlpha.IsChecked == true ? (byte)Math.Round(_alpha.Value * 255 / 100) : byte.MaxValue);
            _updating = true;
            _hex.Text = ColourHex(_color, _useAlpha.IsChecked == true);
            _updating = false;
            UpdatePreview();
        }

        private void SetColor(Color color)
        {
            _updating = true;
            _color = color;
            _useAlpha.IsChecked = color.A != byte.MaxValue;
            _alpha.Value = color.A * 100.0 / 255;
            LoadHsv(color);
            _hex.Text = ColourHex(color);
            _updating = false;
            UpdatePreview();
        }

        private void LoadHsv(Color color)
        {
            var red = color.R / 255.0;
            var green = color.G / 255.0;
            var blue = color.B / 255.0;
            var maximum = Math.Max(red, Math.Max(green, blue));
            var minimum = Math.Min(red, Math.Min(green, blue));
            var delta = maximum - minimum;
            var hue = delta == 0 ? 0 : maximum == red ? 60 * (((green - blue) / delta) % 6) :
                maximum == green ? 60 * ((blue - red) / delta + 2) : 60 * ((red - green) / delta + 4);
            if (hue < 0) hue += 360;
            var wasUpdating = _updating;
            _updating = true;
            _hue.Value = hue;
            _saturation.Value = maximum == 0 ? 0 : delta / maximum * 100;
            _value.Value = maximum * 100;
            _updating = wasUpdating;
        }

        private void UpdatePreview()
        {
            _preview.Background = new SolidColorBrush(_color);
            _preview.BorderBrush = Brush("#53606B");
        }

        private static bool TryParse(string? text, out Color color) => TabForge.Visualization.ColourText.TryParseSetting(text, out color);

        private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
        private static Brush SafeBrush(string value) => TryParse(value, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;
        private static Brush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

        private static Color HsvColor(double hue, double saturation, double value, byte alpha)
        {
            var chroma = value * saturation;
            var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
            var m = value - chroma;
            var (red, green, blue) = hue switch
            {
                < 60 => (chroma, x, 0.0), < 120 => (x, chroma, 0.0), < 180 => (0.0, chroma, x),
                < 240 => (0.0, x, chroma), < 300 => (x, 0.0, chroma), _ => (chroma, 0.0, x)
            };
            return Color.FromArgb(alpha, ToByte(red + m), ToByte(green + m), ToByte(blue + m));
        }

        private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
        private static string ColourHex(Color color, bool includeAlpha = false) => includeAlpha
            ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
