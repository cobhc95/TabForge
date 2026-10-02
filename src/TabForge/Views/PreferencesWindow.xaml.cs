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
using static TabForge.Views.PreferencesCards;

namespace TabForge.Views;

/// <summary>Native, searchable settings pages backed directly by the framework-neutral descriptors.</summary>
public partial class PreferencesWindow : Window, IPreferencesHost
{

    private readonly SettingEditors _editors;
    private readonly HotkeyPage _hotkeys;
    private readonly CommonPages _pages;
    private readonly Action<AppSettings>? _apply;
    private readonly Action<AppSettings>? _preview;
    private readonly Dictionary<string, SettingDescriptor> _descriptorsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SettingDescriptor> _defaultsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PageRow> _rows = new();
    private readonly List<(SettingDescriptor Descriptor, FrameworkElement Row, Button Reset)> _settingControls = new();
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

    /// <summary>The actions of the main window that opened this dialog (Linked audio, quarantine, "Apply to all tracks"); null for an unowned test dialog.</summary>
    private readonly SettingsWindowActions? _actions;
    private bool _dirty;
    private bool _allowClose;
    private bool _rebuilding;
    private bool _wasSearching;
    private int _cardIndex;
    private readonly DispatcherTimer _searchDebounce;

    public event Action<AppSettings>? SettingsApplied;
    public event Action<AppSettings>? SettingsPreviewed;

    /// <summary>Creates an unowned settings window for in-process tests.</summary>
    public PreferencesWindow(AppSettings current) : this(current, null, null, null, null) { }

    internal PreferencesWindow(AppSettings current, Window? owner, Action<AppSettings>? apply, Action<AppSettings>? preview, SettingsWindowActions? actions)
    {
        InitializeComponent();
        Title = $"TabForge Settings - {AppInfo.DisplayVersion}";
        VersionLabel.Text = AppInfo.VersionLine;
        if (owner is not null) Owner = owner;
        _actions = actions;
        _editors = new SettingEditors(this);
        _hotkeys = new HotkeyPage(this);
        _pages = new CommonPages(this);
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
        PreviewKeyDown += (_, e) => _hotkeys.OnPreviewKeyDown(e);
        // Esc = Cancel. Not IsCancel on the button: that closes the dialog even after "keep editing" in the discard question
        // (and asks it twice). Bubbling KeyDown, so an open combo box, the search box and shortcut capture handle Esc first.
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            e.Handled = true;
            CancelButton_Click(this, new RoutedEventArgs());
        };
        PreviewMouseDown += (_, e) => _hotkeys.OnPreviewMouseDown(e);
        SizeChanged += (_, _) => UpdateSearchAreaWidth();
        SearchHeaderBorder.SizeChanged += (_, _) => UpdateSearchAreaWidth();
        Closing += Window_Closing;
        SourceInitialized += (_, _) => ApplyDarkCaption();
        Loaded += (_, _) =>
        {
            UpdateSearchAreaWidth();
            // A deep link names a row: rebuild its page (already built by the constructor) to scroll to it and flash it.
            var row = _pendingRow;
            _pendingRow = null;
            if (row is not null && _descriptorsByKey.ContainsKey(row)) RebuildPage(focusSettingKey: row);
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

    internal static (string Name, string Hex)[] ColourPresets => SettingEditors.ColourPresets;

    Window IPreferencesHost.Dialog => this;
    AppSettings IPreferencesHost.Settings => _settings;
    AppSettings IPreferencesHost.Baseline => _baseline;
    Dictionary<string, SettingDescriptor> IPreferencesHost.DescriptorsByKey => _descriptorsByKey;
    Dictionary<string, SettingDescriptor> IPreferencesHost.DefaultsByKey => _defaultsByKey;
    SettingsWindowActions? IPreferencesHost.Actions => _actions;
    void IPreferencesHost.SettingChanged(SettingDescriptor? descriptor, object? value) => SettingChanged(descriptor, value);
    void IPreferencesHost.RebuildPage(string? focusSettingKey, string? focusHotkey) => RebuildPage(focusSettingKey, focusHotkey);
    void IPreferencesHost.RefreshDescriptors() { _descriptors = SettingsCatalog.Build(_settings); ReplaceDescriptorMap(); }
    void IPreferencesHost.SetStatus(string text) => StatusText.Text = text;
    void IPreferencesHost.AddTopCard(Border card, int columns) => AddTopCard(card, columns);
    void IPreferencesHost.AddRow(PageRow row) => _rows.Add(row);
    Border IPreferencesHost.SettingRow(SettingDescriptor descriptor, bool searchResult) => SettingRow(descriptor, searchResult);
    void IPreferencesHost.NavigateToCategory(string category) => NavigateToCategory(category);
    void IPreferencesHost.NavigateToHotkey(string id) => NavigateToHotkey(id);
    void IPreferencesHost.ResetAll() => ResetAll();
    void IPreferencesHost.ResetHotkeyCategory(IReadOnlyCollection<string> categories) => ResetHotkeyCategory(categories);
    void IPreferencesHost.AddRecentColour(string hex) => AddRecentColour(hex);

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
            _hotkeys.Clear();
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
                _hotkeys.BuildPage(categoryFilter);
                SetPageHeader(SettingsCatalog.Hotkeys, Subtitle(SettingsCatalog.Hotkeys));
            }
            else if (!searching && _selectedCategory == SettingsCatalog.Advanced)
            {
                _pages.BuildAdvancedPage();
                SetPageHeader(SettingsCatalog.Advanced, Subtitle(SettingsCatalog.Advanced));
            }
            else if (!searching && _selectedCategory == SettingsCatalog.Home)
            {
                _pages.BuildHomePage();
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
                    (HotkeyPage.HotkeySearchText(action).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     "hotkeys shortcuts commands".Contains(query, StringComparison.OrdinalIgnoreCase))).ToList() : new List<HotkeyAction>();
                foreach (var group in hotkeys.GroupBy(action => action.Category))
                {
                    var card = GroupCard(group.Key + " commands");
                    foreach (var action in group) AddCardContent(card, _hotkeys.HotkeyRow(action, true));
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
        var editor = _editors.BuildEditor(descriptor);
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
            var changed = HotkeyCatalog.All.Count(action => categories.Contains(action.Category) && _hotkeys.IsHotkeyCustomized(action));
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
            var host = DiscardPrompt.HostOf(this);   // the window that opened this dialog
            Dispatcher.BeginInvoke(new Action(() => host?.DisableDiscardWarning()));
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


    private void RefreshResetButtons()
    {
        foreach (var (descriptor, _, reset) in _settingControls)
            reset.Visibility = IsCustomized(descriptor) ? Visibility.Visible : Visibility.Collapsed;
        _hotkeys.RefreshResetButtons();
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



    private Button CreateButton(string text, string styleKey, string? tooltip = null)
    {
        var button = new Button { Content = text, Style = (Style)FindResource(styleKey) };
        if (tooltip is not null) button.ToolTip = tooltip;
        AutomationProperties.SetName(button, text);
        return button;
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


}
