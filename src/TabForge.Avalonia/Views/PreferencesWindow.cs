using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Settings window: the page rail (captions and pages as SettingsCatalog.Bands), the page header, the search box, the cards of the
/// chosen page with their "More options (n)" rows, and the footer (Reset page…, Manage settings, status, Cancel, Apply, OK).
/// Owns: building the controls, one editor per row kind (switch, choice, number with range and unit, colour, text), the enabled state of
/// dependent rows, reset, and the discard question with the list of unsaved changes.
/// Does not own: the rows (PreferencesCatalog), the cards, enable rules and texts (PreferencesPagePresenter), or showing a question (AvaloniaMessageBox).
/// Deviations: every edit copies the working settings into the live <see cref="Saved"/> object and raises <see cref="Previewed"/>; Cancel or closing without OK copies the open-time snapshot back; OK and Apply keep the values, take a new snapshot and raise <see cref="Applied"/>; the owner saves them (MainWindowSettings). Common settings, Shortcuts and Advanced come
/// from PreferencesSpecialPages and the rest are catalogue cards; the button rows, Import and Export are in PreferencesWindow.Buttons.cs and PreferencesWindow.Transfer.cs. Search is SettingsCatalog.Matches over the registry (PreferencesWindow.Search.cs).
/// Size: over 300 lines because the rail, the cards, the editors and the reset questions are one screen. The header and footer are in
/// PreferencesWindow.Chrome.cs; the rules stay in PreferencesPagePresenter.
/// Tests: TabForge.Avalonia.Tests.PreferencesWindowTests.
/// </summary>
public sealed partial class PreferencesWindow : Window, IPreferencesChangesHost
{
    private const string SavedText = "All changes saved";
    private const string WindowTitle = "TabForge Settings";
    /// <summary>The name of the panel holding the page's cards (tests walk it).</summary>
    internal const string PageCardsName = "PageCards";

    private readonly AvaloniaDialogHost _host;
    // The main window's actions for the button rows; null (the preview, or no main window) leaves those rows disabled.
    private readonly PreferencesRowActions? _actions;
    private readonly ListBox _rail = new() { Width = ChromeMetrics.SettingsRailWidth, BorderThickness = default };
    private readonly TextBlock _pageTitle = new() { FontSize = 20, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _pageSubtitle = new() { TextWrapping = TextWrapping.Wrap };
    private readonly PreferencesSearchBar _searchBar = new();
    private TextBox _search => _searchBar.Box;
    private readonly StackPanel _cards = new() { Name = PageCardsName, Spacing = 10, Margin = DialogMetrics.PrefsCardsMargin, MaxWidth = 760 };
    private readonly TextBlock _status = ToolWindowParts.Status("Status");
    private readonly PreferencesChangesLink _changes;
    private const double FooterMarginLeft = 20, FooterMarginRight = 26, FooterMarginVertical = 10;
    // A colour row: the colour button width.
    private const double ColourButtonWidth = 200;
    // The status text sits left of the buttons, with this gap before them and wrapping at this width.
    private const double FooterStatusGap = 24, FooterStatusMaxWidth = 470;
    private readonly Button _resetPage = new() { Content = SettingsCatalog.Special(SettingsCatalog.ActionResetPage).Label };
    private readonly Button _manage = new() { Content = SettingsCatalog.Special(SettingsCatalog.ActionManage).Label };
    private readonly HashSet<string> _openMore = new();
    private readonly Dictionary<string, SettingsNavigationIconView> _navigationIcons = new(StringComparer.Ordinal);
    // Perf (H13/H15): the catalogue rows build lazily on the first page that needs them (and the static defaults below
    // are built once per process), so opening Settings pays one catalogue build instead of two or three.
    private List<SettingDescriptor>? _rows;
    /// <summary>The catalogue rows bound to <see cref="Working"/>; built once on first use, dropped when Working is replaced.</summary>
    private List<SettingDescriptor> Rows => _rows ??= PreferencesCatalog.Build(Working);
    private List<SettingsEntry>? _registry;
    /// <summary>The search registry of <see cref="Rows"/> and the shortcuts; built on the first search, dropped when Working is replaced or a shortcut changes.</summary>
    private List<SettingsEntry> Registry => _registry ??= SettingsCatalog.Registry(Rows, Working.Hotkeys);
    private void DropRows() { _rows = null; _registry = null; }
    private void ShortcutsChanged() { _registry = null; MarkChanged(); }
    private readonly List<(SettingDescriptor Row, Control Panel)> _shown = new();
    private string _page = PreferencesCatalog.General;
    private readonly Button _apply = new() { Content = SettingsCatalog.Special(SettingsCatalog.ActionApply).Label, IsEnabled = false };
    private bool _changed;
    // The settings as they were at open or at the last Apply: Cancel and closing without OK copy them back into the live settings.
    private AppSettings _baseline;
    // True after Reset all: the next live write also replaces the dock layout, as the reset does.
    private bool _workspaceReset;
    // True once a write carried the reset layout into the live settings: the Cancel restore must then put the baseline layout back, until Apply keeps it.
    private bool _workspaceWritten;
    // The edits the live settings have not taken yet, and the once-per-frame run that writes them (a click only stores its value and paints).
    private readonly PreferencesLiveWriter _live;
    private readonly NextFrameCoalescer _liveFrame;
    // True when the pending edits include the theme, whose preset rewrites the palette rows: the page is built again to show them.
    private bool _rebuildPage;

    // True while edits are unapplied; Apply is enabled exactly then.
    private bool _dirty { get => _changed; set { _changed = value; _apply.IsEnabled = value; } }

    public PreferencesWindow(AvaloniaDialogHost host, AppSettings saved, PreferencesRowActions? actions = null)
    {
        _host = host;
        _actions = actions;
        Saved = saved;
        _live = new PreferencesLiveWriter(saved);
        _liveFrame = new NextFrameCoalescer(AvaloniaUiThread.ForApplication(), FlushLive, UiPriority.Background);
        Working = PreferencesCatalog.Copy(saved);
        _baseline = PreferencesCatalog.Copy(saved);
        Title = WindowTitle;
        Width = InitialSettingsWidth(saved);
        Height = InitialSettingsHeight(saved);
        MinWidth = ChromeMetrics.SettingsWindowMinWidth;
        MinHeight = ChromeMetrics.SettingsWindowMinHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontSize = DialogMetrics.FontSize;
        _changes = new PreferencesChangesLink(this);
        Content = Layout();
        ShowPage(_page);
        Closed += (_, _) => RestoreBaselineIfUnkept();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty) CaptureSize();
        };
    }

    /// <summary>The settings OK and Apply write to.</summary>
    public AppSettings Saved { get; }

    /// <summary>The settings the editors change until OK or Apply.</summary>
    public AppSettings Working { get; private set; }

    /// <summary>Raised after OK or Apply copied the working settings into <see cref="Saved"/>; the owner saves the settings file.</summary>
    public event Action? Applied;

    /// <summary>Raised on the UI thread after the live <see cref="Saved"/> settings took a previewed edit, a reset or the Cancel restore; the owner tells every settings consumer.</summary>
    public event Action? Previewed;

    /// <summary>Raised on the UI thread after a resize was written into <see cref="Saved"/>; the owner saves the settings file.</summary>
    public event Action? WindowSizeChanged;

    public string Page => _page;
    public string StatusText => _status.Text ?? "";

    /// <summary>The rows built on the page now showing, in page order, "More options" rows included.</summary>
    public IReadOnlyList<SettingDescriptor> ShownRows => _shown.Select(s => s.Row).ToList();

    /// <summary>The editor built for a shown row.</summary>
    public Control EditorOf(string key) => (Control)_shown.Single(s => s.Row.Key == key).Panel.Tag!;

    public bool IsRowEnabled(string key) => _shown.Single(s => s.Row.Key == key).Panel.IsEnabled;

    public void ShowPage(string page)
    {
        _page = page;
        // Opening a page (from a search result, a link or the rail) leaves the search, so the page itself shows and not the results again.
        if (_search.Text is { Length: > 0 }) _search.Text = "";
        foreach (var glyph in _navigationIcons) glyph.Value.IsSelected = glyph.Key == page;
        if (_searchBar.SelectedCategory is not null) _searchBar.Category.SelectedIndex = 0;
        _rail.SelectedItem = _rail.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string?)i.Tag == page);
        Rebuild();
    }

    public void Search(string query) { _search.Text = query; Rebuild(); }

    private Control Layout()
    {
        foreach (var (band, pages) in PreferencesCatalog.Bands)
        {
            if (band.Length > 0)
                _rail.Items.Add(BandHeader(band));
            foreach (var page in pages) _rail.Items.Add(new ListBoxItem { Content = NavigationRow(page), Tag = page, MinHeight = ChromeMetrics.SettingsRailRowHeight, Classes = { AvaloniaFormControlStyles.PrefsNavRowClass } });
        }
        AutomationProperties.SetName(_rail, "Settings pages");
        _rail.Background = null;
        // Perf: the rail rides a VirtualizingStackPanel like the preset and plug-in lists (its rows are few, like the preset tree).
        _rail.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        // The rail's rows stop short of its scroll bar, so the bar never covers a row's highlight.
        _rail.Padding = new Thickness(0, 0, AvaloniaScrollBarStyles.ScrollBarThickness, 0);
        _rail.BorderThickness = DialogMetrics.PrefsRailBorder;
        _rail[!BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        _rail.SelectionChanged += (_, _) => { if (_rail.SelectedItem is ListBoxItem { Tag: string page } && page != _page) ShowPage(page); };

        var header = BuildHeader();
        var footer = BuildFooter();

        var main = new DockPanel();
        DockPanel.SetDock(_searchBar, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        main.Children.Add(_searchBar);
        main.Children.Add(header);
        // The page body is the window colour, so only the section cards stand out as boxes (no grey slab behind them).
        var cardsScroll = new ScrollViewer { Content = _cards };
        cardsScroll[!TemplatedControl.BackgroundProperty] = new DynamicResourceExtension("WindowBrush");
        main.Children.Add(cardsScroll);
        // The title block sits above the rail, so the rail and the title form the left column beside the search strip.
        var title = BuildTitleBlock();
        var sidebar = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        sidebar.Children.Add(title);
        sidebar.Children.Add(_rail);
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(sidebar, Dock.Left);
        root.Children.Add(footer);
        root.Children.Add(sidebar);
        root.Children.Add(main);
        return root;
    }

    /// <summary>A rail caption (BASICS, MUSIC, ...): bold, in the full text brush. The item is disabled, so its brush is set on the item to stop the disabled dimming.</summary>
    private static ListBoxItem BandHeader(string band)
    {
        var header = new ListBoxItem
        {
            Content = band, IsEnabled = false, FontSize = ChromeMetrics.SettingsRailCaptionFontSize, FontWeight = FontWeight.Bold, Margin = default,
            Padding = new Thickness(ChromeMetrics.SettingsRailCaptionIndent, ChromeMetrics.SettingsRailCaptionTop, 0, ChromeMetrics.SettingsRailCaptionBottom),
        };
        header[!TemplatedControl.ForegroundProperty] = new DynamicResourceExtension("TextBrush");
        return header;
    }

    /// <summary>A rail row: the page's glyph, then its name.</summary>
    private Grid NavigationRow(string page)
    {
        var glyph = new SettingsNavigationIconView(page) { Width = ChromeMetrics.SettingsRailIconSize, Height = ChromeMetrics.SettingsRailIconSize, VerticalAlignment = VerticalAlignment.Center };
        _navigationIcons[page] = glyph;
        var label = new TextBlock { Text = page, FontSize = ChromeMetrics.SettingsRailLabelFontSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(ChromeMetrics.SettingsRailLabelGap, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ChromeMetrics.SettingsRailIconSize},*") };
        row.Children.Add(glyph);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    private void Rebuild()
    {
        _cards.Children.Clear();
        _shown.Clear();
        var query = string.Join(' ', (_search.Text ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var searching = query.Length > 0;
        _pageTitle.Text = searching ? "Search results" : _page;
        _pageSubtitle.Text = searching ? "" : PreferencesCatalog.Subtitle(_page);
        _resetPage.IsVisible = !searching && _page is not (PreferencesCatalog.Home or "Advanced");
        if (searching) AddSearchResults(query);
        else if (AddSpecialPage()) return;
        else
        {
            var shown = Rows.Where(row => row.Category == _page).ToList();
            if (shown.Count == 0) _cards.Children.Add(ToolWindowParts.Small(ToolWindowParts.NeedsFullApp($"The {_page} page")));
            foreach (var card in PreferencesPagePresenter.Cards(shown, searching: false)) _cards.Children.Add(RowCard(card, searching: false));
        }
        RefreshEnabled();
    }

    /// <summary>Fills the cards for Common settings, Shortcuts or Advanced; false for a page of plain catalogue cards.</summary>
    private bool AddSpecialPage()
    {
        IEnumerable<Control> cards;
        switch (_page)
        {
            case PreferencesCatalog.Home:
                cards = PreferencesSpecialPages.Home(Rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase), RowPanel, SpecialActions());
                break;
            case SettingsCatalog.Hotkeys:
                cards = PreferencesSpecialPages.Shortcuts(Working.Hotkeys, ShortcutsChanged);
                break;
            case SettingsCatalog.Advanced:
                cards = PreferencesSpecialPages.Advanced(SpecialActions());
                break;
            default:
                return false;
        }
        foreach (var card in cards) _cards.Children.Add(card);
        RefreshEnabled();
        return true;
    }

    private void MarkChanged()
    {
        _dirty = true;
        WriteLive(Working);
        RefreshChanges();
    }

    /// <summary>Refreshes the changes link; the status text is blank while changes wait (the link says so) and "All changes saved" otherwise.</summary>
    private void RefreshChanges()
    {
        _changes.Refresh();
        _status.Text = _changes.Count > 0 ? "" : SavedText;
    }

    /// <summary>Copies <paramref name="source"/> into the live settings (same object, new section values) and raises <see cref="Previewed"/>; the dock layout stays the live one except after Reset all.</summary>
    private void WriteLive(AppSettings source, bool preview = true)
    {
        _live.WriteAll(source, includeWorkspace: _workspaceReset);
        _workspaceWritten |= _workspaceReset;
        _workspaceReset = false;
        _rebuildPage = false;
        if (preview) Previewed?.Invoke();
    }

    /// <summary>The once-per-frame run of the edits made since the last one: only the edited rows reach the live settings, then one preview notice, change list and enabled state.</summary>
    private void FlushLive()
    {
        if (!_dirty || !_live.Pending) return;   // applied, written or cancelled since the edit
        _live.WritePending(Working, Rows, _workspaceReset);
        _workspaceWritten |= _workspaceReset;
        _workspaceReset = false;
        Previewed?.Invoke();
        _dirty = _workspaceWritten || PreferencesPagePresenter.Changes(SavedRows(), Rows).Count > 0;   // an edit put back (a cancelled colour pick) leaves nothing to apply
        RefreshChanges();
        if (_rebuildPage) { _rebuildPage = false; DropRows(); Rebuild(); } else RefreshEnabled();
    }

    /// <summary>The OS or the user closing the window (the X button, Alt+F4) with unapplied edits asks the Cancel question first; a close the code makes (Cancel, OK, a test) does not ask again.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.IsProgrammatic || e.CloseReason is not WindowCloseReason.WindowClosing || !_dirty) return;
        e.Cancel = true;
        Platform.UiTasks.Forget(CloseAskingAsync(), "Close", text => _status.Text = text);
    }

    private bool _asking;

    /// <summary>Asks to discard the unapplied edits (no question without any) and closes the window when the answer allows it; a second call while the question is open does nothing.</summary>
    internal async Task CloseAskingAsync()
    {
        if (_asking) return;
        _asking = true;
        try { if (await ConfirmDiscardAsync()) Close(); }
        finally { _asking = false; }
    }

    /// <summary>Whether closing put the open-time snapshot back (the owner then tells its observers once).</summary>
    internal bool Restored { get; private set; }

    private bool OutputProfilesChanged()
    {
        var now = Saved.Plugins.OutputProfiles;
        var then = _baseline.Plugins.OutputProfiles;
        return now.Count != then.Count || now.Any(p => !then.TryGetValue(p.Key, out var b) || b.BufferSize != p.Value.BufferSize || b.SampleRate != p.Value.SampleRate);
    }

    private void RestoreBaselineIfUnkept()
    {
        // A device switch edits other devices' remembered buffer and rate, which no row shows: they alone can leave the window "clean" yet changed.
        if (!_dirty && !OutputProfilesChanged()) return;
        _dirty = false;
        Restored = true;
        _workspaceReset |= _workspaceWritten;
        WriteLive(_baseline, preview: false);   // the owner tells its observers once, after the window is gone
        _workspaceWritten = false;
    }

    private Control MoreOptions(string page, string group, IReadOnlyList<SettingDescriptor> more, bool searching)
    {
        var key = page + "|" + group;
        var rows = new StackPanel { Spacing = 12, Margin = DialogMetrics.PrefsRowsGap };
        foreach (var row in more) rows.Children.Add(RowPanel(row));
        var toggle = new ToggleButton { Content = $"More options ({more.Count})", IsChecked = searching || _openMore.Contains(key), Margin = DialogMetrics.PrefsMoreToggleMargin };
        toggle.Classes.Add(AvaloniaFormControlStyles.PrefsMoreOptionsClass);
        AutomationProperties.SetName(toggle, $"More options, {more.Count} more settings in this group");
        ToolTip.SetTip(toggle, "Show or hide the less common settings of this group.");
        rows.IsVisible = toggle.IsChecked == true;
        toggle.IsCheckedChanged += (_, _) =>
        {
            rows.IsVisible = toggle.IsChecked == true;
            if (toggle.IsChecked == true) _openMore.Add(key);
        };
        var stack = new StackPanel();
        stack.Children.Add(toggle);
        stack.Children.Add(rows);
        return stack;
    }

    private Control RowPanel(SettingDescriptor row)
    {
        var editor = Editor(row);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = row.Title, TextWrapping = TextWrapping.Wrap });
        if (row.Description.Length > 0) text.Children.Add(ToolWindowParts.Small(row.Description));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,Auto"), Tag = editor };
        Grid.SetColumn(editor, 2);
        editor.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(editor, row.Title);
        TooltipPlacement.SetTip(editor, row.Description.Length > 0 ? row.Description : row.Title);
        grid.Children.Add(text);
        grid.Children.Add(editor);
        _shown.Add((row, grid));
        return grid;
    }

    private Control Editor(SettingDescriptor row)
    {
        switch (row.Kind)
        {
            case SettingKind.Bool:
                var box = new CheckBox { IsChecked = row.Get() is true };
                box.IsCheckedChanged += (_, _) => Changed(row, box.IsChecked == true);
                return box;
            case SettingKind.Choice:
                var combo = new ComboBox { ItemsSource = row.Choices, SelectedItem = row.Get()?.ToString(), MinWidth = 200, Classes = { AvaloniaFormControlStyles.PrefsFieldClass } };
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string v && v != row.Get()?.ToString()) Changed(row, v); };
                RedrawWhenAnswered(row.Choices);
                return combo;
            case SettingKind.Number:
                var number = new NumericUpDown
                {
                    Minimum = (decimal)row.Min, Maximum = (decimal)row.Max, Increment = (decimal)row.Step, Width = 150,
                    Classes = { AvaloniaFormControlStyles.PrefsFieldClass },
                    Value = (decimal)Convert.ToDouble(row.Get(), CultureInfo.InvariantCulture), FormatString = "F" + row.Decimals,
                };
                AutomationProperties.SetName(number, row.Title);
                number.ValueChanged += (_, e) => { if (e.NewValue is { } v) Changed(row, (double)v); };
                return row.Unit == "%" ? ToolWindowParts.Row(number, ToolWindowParts.Label(row.Unit)) : number;
            case SettingKind.Colour:
                return ColourEditor(row);
            case SettingKind.FolderList:
                return FolderListEditor(row);
            case SettingKind.Text:
                var field = new TextBox { Text = row.Get()?.ToString(), Width = 220, Classes = { AvaloniaFormControlStyles.PrefsFieldClass } };
                field.LostFocus += (_, _) => { if (field.Text != row.Get()?.ToString()) Changed(row, field.Text ?? ""); };
                return field;
            default:
                return ButtonEditor(row);
        }
    }

    /// <summary>
    /// Colour setting: one <see cref="ColourButton"/> (swatch and name; click for the colour picker popup, whose Default button restores the setting's default).
    /// </summary>
    private Control ColourEditor(SettingDescriptor row)
    {
        var button = new ColourButton(DefaultColourOf(row.Key), Working.Appearance.RecentColours) { Width = ColourButtonWidth, Classes = { AvaloniaFormControlStyles.PrefsFieldClass } };
        button.Show(row.Get()?.ToString() ?? "");
        AutomationProperties.SetName(button, row.Title);
        button.Previewed += hex => Changed(row, hex);
        button.Picked += hex => Changed(row, hex);
          // every popup click previews the app live; nothing is saved until Apply
        return button;
    }

    // The default colour text of each colour row, read once per process from a default settings copy (the colour popup's Default button).
    // Never Set through these rows: they are the shared defaults every window reads.
    private static readonly object DefaultsSync = new();
    private static Dictionary<string, SettingDescriptor>? _defaultsByKey;
    private static Dictionary<string, string>? _defaultColours;

    private static Dictionary<string, SettingDescriptor> DefaultsByKey()
    {
        lock (DefaultsSync)
        {
            return _defaultsByKey ??= PreferencesCatalog.Build(new AppSettings()).ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>The catalogue default of a colour row, or white when the catalogue has none.</summary>
    private string DefaultColourOf(string key)
    {
        lock (DefaultsSync)
        {
            _defaultColours ??= DefaultsByKey().Where(pair => pair.Value.Kind == SettingKind.Colour)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Get()?.ToString() ?? ThemeColours.FallbackColourHex, StringComparer.OrdinalIgnoreCase);
            return _defaultColours.TryGetValue(key, out var hex) ? hex : ThemeColours.FallbackColourHex;
        }
    }

    /// <summary>Writes a row's new value, as the row's editor does.</summary>
    public void Write(string key, object? value)
    {
        var row = Rows.Single(r => r.Key == key);
        Changed(row, value);
        FlushLive();
    }

    /// <summary>An editor changed <paramref name="row"/>: the value is taken at once and the control paints its new state; the preview, change list and enabled state follow once, after that frame.</summary>
    private void Changed(SettingDescriptor row, object? value)
    {
        row.Set(value);
        _dirty = true;
        _live.Note(row);
        _rebuildPage |= row.Key is "appearance.thememode" or "vst.driver" or "vst.device";   // a new output brings its own buffer, rate and device lists
        _liveFrame.Request();
    }

    private void RefreshEnabled()
    {
        foreach (var (row, panel) in _shown)
            panel.IsEnabled = PreferencesPagePresenter.IsEnabled(row, key => Rows.FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase)));
    }

}

internal static class PreferenceCardExtensions
{
    /// <summary>The page of a card's rows.</summary>
    public static string Category(this PreferenceCard<SettingDescriptor> card) => (card.Basic.Count > 0 ? card.Basic[0] : card.More[0]).Category;
}
