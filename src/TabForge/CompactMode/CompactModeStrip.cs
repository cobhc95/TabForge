using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Views;
using TabForge.Views.Band;

namespace TabForge.CompactMode;

// Owns: the header strip shown under the top bar while compact mode is on: the "COMPACT MODE" caption, one pill per track with
//   exactly the selected track lit, and the × that leaves compact mode. While compact mode is on the window's menu row is hidden; a
//   right-click on the strip, or a click on its caption, opens that row's icons and menus, and so does the Band view's header while it stands in for the strip. It hooks itself to its window when loaded.
// Does not own: the pill, caption and close-button looks (TrackPillStrip), the track selection (the window's track list, which the
//   strip reads and sets), the command (CompactModeFeatureModule.cs), the right-click menu's content (CompactModeMenu.cs).
// Tests: TestCompactMode.
public sealed class CompactModeStrip : DockPanel
{
    private readonly WrapPanel _pills = new() { Margin = new Thickness(4, 4, 4, 2) };
    private readonly Button _close = TrackPillStrip.CloseButton("Close compact mode", CompactModeFeatureModule.CommandId);
    private Selector? _tracks;
    private Func<bool> _otherHeaderShown = () => false;
    private ITrackListDock? _dock;
    private FrameworkElement? _menuRow;
    private CompactModeMenu? _menu;
    private readonly Button _menuButton = BandLayoutMenu.Button();
    private BandView? _band;
    /// <summary>The chevron after the caption.</summary>
    internal Button MenuButton => _menuButton;
    /// <summary>Tests fill the menu without showing a pop-up.</summary>
    internal bool SuppressPopups { get; set; }
    private bool _on;

    public CompactModeStrip()
    {
        Visibility = Visibility.Collapsed;
        SetResourceReference(BackgroundProperty, "PanelBrush");
        var caption = TrackPillStrip.Caption("COMPACT MODE");
        SetDock(caption, Dock.Left);
        Children.Add(caption);
        // The caption and its chevron open the same menu a right-click on the strip opens, under the caption.
        BandLayoutMenu.AlsoOpens(caption, _menuButton);
        foreach (var target in new FrameworkElement[] { caption, _menuButton }) TooltipShortcuts.Bind(target, "Menus and tools", null);
        System.Windows.Automation.AutomationProperties.SetName(_menuButton, "Menus and tools");
        _menuButton.Click += (_, _) =>
        {
            if (_menu is null) return;
            _menu.Fill(ContextMenu!);
            ContextMenu!.PlacementTarget = caption; ContextMenu.Placement = PlacementMode.Bottom;
            if (!SuppressPopups) ContextMenu.IsOpen = true;
        };
        SetDock(_menuButton, Dock.Left);
        Children.Add(_menuButton);
        _close.Click += (_, _) => CloseRequested?.Invoke();
        SetDock(_close, Dock.Right);
        Children.Add(_close);
        Children.Add(TrackPillStrip.PillScroll(_pills));
        System.Windows.Automation.AutomationProperties.SetName(this, "Compact mode");
        Loaded += (_, _) => HookWindow();
        // Right-click anywhere on the strip: the icons and menus of the hidden menu row.
        ContextMenu = new ContextMenu();
        if (Application.Current?.TryFindResource(typeof(ContextMenu)) is Style style) ContextMenu.Style = style;
        ContextMenuOpening += (_, e) => { if (_menu is null) e.Handled = true; else { ContextMenu.Placement = PlacementMode.MousePoint; _menu.Fill(ContextMenu); } };
        ContextMenu.Closed += (_, _) => _menu?.Restore();
    }

    /// <summary>The window's menu row (hidden while the strip is the header), its icon row and its main menu (offered by the strip's right-click menu).</summary>
    internal void AttachMenuRow(FrameworkElement menuRow, Panel iconRow, Menu mainMenu)
    {
        _menuRow = menuRow;
        _menu = new CompactModeMenu(iconRow, mainMenu);
    }

    /// <summary>
    /// The Band view's header stands in for the strip while both are on: a right-click on it opens the same menu, and its
    /// roll-down gets the same rows below its own. Neither happens while compact mode is off.
    /// </summary>
    internal void AttachBandHeader(BandView band)
    {
        _band = band;
        band.Header.ContextMenu = ContextMenu;
        band.Header.ContextMenuOpening += (_, e) => { if (!_on || _menu is null) e.Handled = true; else { ContextMenu!.Placement = PlacementMode.MousePoint; _menu.Fill(ContextMenu); } };
        band.LayoutMenuOpening += menu =>
        {
            if (!_on || _menu is not { } rows) return;
            rows.Append(menu);
            menu.Closed += (_, _) => rows.Restore();
        };
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if ((child as T ?? Find<T>(child)) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>The right-click menu, filled as a right-click fills it; <see cref="CloseMenu"/> hands the main menus' items back.</summary>
    internal ContextMenu OpenMenu() { _menu?.Fill(ContextMenu); return ContextMenu; }
    internal void CloseMenu() => _menu?.Restore();

    /// <summary>Raised by the ×: leave compact mode.</summary>
    internal event Action? CloseRequested;
    internal Button CloseButton => _close;
    internal IReadOnlyList<ToggleButton> Pills => _pills.Children.OfType<ToggleButton>().ToList();

    /// <summary>The track list the pills mirror: its items are the tracks, its selection is the selected track.</summary>
    internal void Attach(Selector tracks, Func<bool>? otherHeaderShown = null)
    {
        _tracks = tracks;
        _otherHeaderShown = otherHeaderShown ?? _otherHeaderShown;
        tracks.SelectionChanged += (_, _) => Sync();
        ((INotifyCollectionChanged)tracks.Items).CollectionChanged += (_, _) => Rebuild();
    }

    /// <summary>Compact mode turned on or off.</summary>
    internal void SetOn(bool on) { _on = on; Refresh(); }

    /// <summary>Shows the strip while compact mode is on, unless another header (the Band view's) takes the place.</summary>
    internal void Refresh()
    {
        Visibility = _on && !_otherHeaderShown() ? Visibility.Visible : Visibility.Collapsed;
        // The menu row is hidden whenever compact mode is on, whichever header shows.
        if (_menuRow is not null) _menuRow.Visibility = _on ? Visibility.Collapsed : Visibility.Visible;
        if (!_on) _menu?.Restore();
        if (_on && _band is null && Visibility != Visibility.Visible && Window.GetWindow(this) is { } window && Find<BandView>(window) is { } band) AttachBandHeader(band);
        Rebuild();
    }

    private void Rebuild()
    {
        if (Visibility != Visibility.Visible || _tracks is null) return;
        _pills.Children.Clear();
        var index = 0;
        foreach (var track in _tracks.Items.OfType<TrackModel>())
        {
            var at = index++;
            var pill = TrackPillStrip.Pill(track, track.Name, "Select " + track.Name);
            pill.Click += (_, _) => { _tracks.SelectedIndex = at; Sync(); };   // the same selection a click on the track's row makes
            _pills.Children.Add(pill);
        }
        Sync();
    }

    /// <summary>Lights the selected track's pill and no other.</summary>
    private void Sync()
    {
        if (_tracks is null) return;
        for (var i = 0; i < _pills.Children.Count; i++) ((ToggleButton)_pills.Children[i]).IsChecked = i == _tracks.SelectedIndex;
    }

    /// <summary>In the main window: follows the View > Compact mode row, the window's track list and the dock (the Band view's header wins).</summary>
    private void HookWindow()
    {
        if (_tracks is not null || Window.GetWindow(this) is not { } window) return;
        if (window.FindName("TrackMixerGrid") is not Selector tracks || window.FindName("MainMenu") is not Menu menu) return;
        if (MenuHotkey.Find(menu.Items, CompactModeFeatureModule.CommandId) is not { } row) return;
        var host = window as ITrackListFitHost;
        Attach(tracks, () => (host?.Dock as DockWorkspace)?.IsPanelVisible("band") == true);
        if (window.FindName("MainToolbar") is FrameworkElement menuRow && window.FindName("ToolbarTempoGroup") is Panel iconRow) AttachMenuRow(menuRow, iconRow, menu);
        CloseRequested += () => row.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        row.Unchecked += (_, _) => SetOn(false);
        row.Checked += (_, _) =>
        {
            if (_dock is null && (_dock = host?.Dock) is { } dock) dock.LayoutChanged += (_, _) => Refresh();
            SetOn(true);
        };
    }
}
