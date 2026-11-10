using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Features.LeftStrip;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.LeftStrip;

// Owns: the strip: the slim vertical icon strip (the icons the owner picked, LeftStripItems; Open, Save, Save as, Render, then the Composer
//   toggle by default) left or right of the score pane, the same icons as a row beside the menus, their size, tooltips and labels, the sidebar toggle
//   icon at the left end of the menu bar (moved into the compact / Band mode bar while that bar replaces the menus), the strip's right-click menu
//   (Place beside menus, Keep vertical with the current one ticked, Customize strip…, Hide strip), the Composer panel's showing (on its own, strip
//   hidden or not), and the commands View.ToggleLeftStrip, View.ToggleComposer, View.ShowStripButton and View.ShowStripOnStart with their saved state.
// Does not own: the commands the icons run (MainWindowCommands), the icon look (MenuRowToggles, QuickActionsGroup), the catalog (LeftStripItems),
//   the customise window (LeftStripCustomizeWindow), the panel's content (ComposerPanel), the saved values and start rules (Core LeftStripSettings),
//   the menu rows (MainMenuTable).
// Tests: TabForge.Avalonia.Tests.LeftStripSettingsTests.
public sealed class LeftStripFeature
{
    public const string ToggleStripId = "View.ToggleLeftStrip", ToggleComposerId = "View.ToggleComposer";
    public const string ShowButtonId = "View.ShowStripButton", ShowOnStartId = "View.ShowStripOnStart";

    /// <summary>The commands the strip registers (the menu tests discover this list).</summary>
    public static readonly string[] Commands = { ToggleStripId, ToggleComposerId, ShowButtonId, ShowOnStartId };

    private const string StripIcon = "M3,4 H21 V20 H3 Z M9,4 V20";
    private const double LargeScale = 1.4;

    private readonly MainWindowCommands _commands;
    private readonly MainWindowSettings? _settings;
    private readonly LeftStripSettings _state;
    private readonly List<ToggleButton> _composerToggles = new();
    private readonly List<ContentControl> _tipButtons = new();
    private void OnDeactivated(object? sender, System.EventArgs e) => CloseTips();
    private bool _visible, _composerOpen;
    private Border? _vertical;
    private StackPanel? _row, _host;
    private ToggleButton? _toggle;
    private Border? _composer;

    /// <summary>Registers the commands; call before the menu is built so its rows are enabled.</summary>
    public LeftStripFeature(MainWindowCommands commands, MainWindowSettings? settings)
    {
        _commands = commands;
        _settings = settings;
        _state = settings?.Settings.LeftStrip ?? new LeftStripSettings();
        _visible = _state.ShownAtStart;
        _composerOpen = _state.ComposerOpenAtStart;
        commands.Add(ToggleStripId, () => SetVisible(!_visible));
        commands.Add(ToggleComposerId, ToggleComposer);
        commands.Add(ShowButtonId, () => { _state.ShowButton = !_state.ShowButton; Apply(); });
        commands.Add(ShowOnStartId, () => { _state.ShowOnStart = !_state.ShowOnStart; Save(); });
        commands.AddCheckState(ShowButtonId, () => _state.ShowButton);
        commands.AddCheckState(ToggleStripId, () => _visible);
        commands.AddCheckState(ToggleComposerId, () => _composerOpen);
        commands.AddCheckState(ShowOnStartId, () => _state.ShowOnStart);
    }

    /// <summary>The left region (the vertical strip when at the left, and the Composer panel); the window puts it in the workspace's left rail: under the menu or mode bar, beside the fretboard row too, down to the top of the timeline.</summary>
    public DockPanel Left { get; } = new() { LastChildFill = false };

    /// <summary>The right region (the vertical strip when at the right); the window puts it in the workspace's right rail.</summary>
    public DockPanel Right { get; } = new() { LastChildFill = false };

    /// <summary>The saved state the customise window edits.</summary>
    internal LeftStripSettings State => _state;

    /// <summary>Builds the strip's parts and places the toggle icon in the menu row, or in the mode bar while the menu row is hidden.</summary>
    public void Attach(Control menuRow, CompactTrackStrip modeBar)
    {
        _vertical = new Border { MinWidth = ChromeMetrics.LeftStripWidth };
        _vertical[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        _vertical[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        _vertical.ContextMenu = StripMenu();
        _vertical.AttachedToVisualTree += (_, _) => { if (TopLevel.GetTopLevel(_vertical) is Window w) { w.Deactivated -= OnDeactivated; w.Deactivated += OnDeactivated; } };
        _composer = ComposerPanel.Build();
        DockPanel.SetDock(_vertical, Dock.Left);
        DockPanel.SetDock(_composer, Dock.Left);
        Left.Children.Add(_composer);
        _row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, ContextMenu = StripMenu() };
        _toggle = MenuRowToggles.Button(new MenuRowToggles.Toggle(ToggleStripId, "Show / hide the left strip", StripIcon), _commands);
        _host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        _host.Children.Add(_toggle);
        _host.Children.Add(_row);
        DockPanel.SetDock(_host, Dock.Left);
        void Place()
        {
            (_host.Parent as Panel)?.Children.Remove(_host);
            var target = menuRow.IsVisible && menuRow is Border { Child: DockPanel row } ? row : (Panel)modeBar;
            target.Children.Insert(0, _host);
        }
        menuRow.PropertyChanged += (_, e) => { if (e.Property == Visual.IsVisibleProperty) Place(); };
        Place();
        Rebuild();
    }

    /// <summary>Rebuilds the icons from the saved state (after the customise window applies) and shows the parts as it says.</summary>
    internal void Rebuild()
    {
        if (_vertical is null || _row is null) return;
        _composerToggles.Clear();
        _tipButtons.Clear();
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6) };
        Fill(column, vertical: true);
        _vertical.Child = column;
        _row.Children.Clear();
        Fill(_row, vertical: false);
        Apply();
    }

    private void Fill(StackPanel panel, bool vertical)
    {
        foreach (var id in LeftStripItems.Resolve(_state.Items))
        {
            if (LeftStripItems.Find(id) is not { } item) { panel.Children.Add(Line(vertical)); continue; }
            var size = IconSizing.Current * (_state.Large ? LargeScale : 1);
            Button? plain = null;
            ToggleButton? toggle = null;
            if (item.IsToggle) _composerToggles.Add(toggle = MenuRowToggles.Button(new MenuRowToggles.Toggle(item.Id, item.Label, item.Icon), _commands));
            else plain = QuickActionsGroup.Button(() => _commands, item.Id, item.Label, item.Icon);
            var button = (ContentControl?)plain ?? toggle!;
            var icon = MenuRowToggles.Icon(item.Icon, size);
            button.Content = _state.Labels ? Labelled(icon, item.Label, vertical) : icon;
            if (_state.Large || _state.Labels) button.Height = double.NaN;
            if (_state.Large) button.MinHeight = UiMetrics.QuickButtonHeight * LargeScale;
            ToolTip.SetServiceEnabled(button, _state.Tooltips);
            ConfigureTip(button, vertical);
            panel.Children.Add(button);
        }
    }

    // The default tip opens at the pointer, over the icon below, and is dropped only by a pointer exit; so: a fixed side placement clear of the
    // neighbours (right of a left strip, left of a right strip, under the beside-menus row), a short delay with none carried over from the last tip,
    // and closed at once on press and when the window loses focus.
    private void ConfigureTip(ContentControl button, bool vertical)
    {
        ToolTip.SetPlacement(button, !vertical ? PlacementMode.Bottom : _state.RightSide ? PlacementMode.Left : PlacementMode.Right);
        ToolTip.SetShowDelay(button, UiTiming.StripTooltipDelayMs);
        ToolTip.SetBetweenShowDelay(button, 0);
        button.AddHandler(InputElement.PointerPressedEvent, (_, _) => ToolTip.SetIsOpen(button, false), RoutingStrategies.Tunnel, handledEventsToo: true);
        button.PointerExited += (_, _) => ToolTip.SetIsOpen(button, false);
        _tipButtons.Add(button);
    }

    private void CloseTips()
    {
        foreach (var button in _tipButtons) ToolTip.SetIsOpen(button, false);
    }

    private static Control Labelled(Control icon, string label, bool vertical)
    {
        var text = new TextBlock { Text = label, FontSize = ChromeMetrics.TextCaption, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = vertical ? 1 : 4, Margin = vertical ? new Thickness(0, 2) : default,
            Children = { icon, text },
        };
    }

    private static Control Line(bool vertical)
    {
        if (!vertical) return MenuRowToggles.Separator();
        var line = new Border { Height = ChromeMetrics.LeftStripSeparatorHeight, Width = ChromeMetrics.LeftStripSeparatorWidth, Margin = new Thickness(0, 4), Opacity = ChromeMetrics.LeftStripSeparatorOpacity, IsHitTestVisible = false };
        line[!Border.BackgroundProperty] = new DynamicResourceExtension("TextMuted");
        return line;
    }

    private ContextMenu StripMenu()
    {
        MenuItem Item(string header, System.Action run)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => run();
            return item;
        }
        // The placement rows are ticked as the app's other checkable rows (ContextMenuBuilder): exactly one, re-read each time the menu opens.
        var beside = Item("Place beside menus", () => { _state.BesideMenus = true; Apply(); });
        var vertical = Item("Keep vertical", () => { _state.BesideMenus = false; Apply(); });
        beside.ToggleType = vertical.ToggleType = MenuItemToggleType.CheckBox;
        var menu = new ContextMenu
        {
            Items = { beside, vertical, new Separator(), Item("Customize strip…", Customize), Item("Hide strip", () => SetVisible(false)) },
        };
        void Tick() { beside.IsChecked = _state.BesideMenus; vertical.IsChecked = !_state.BesideMenus; }
        Tick();
        menu.Opening += (_, _) => Tick();
        return menu;
    }

    private void Customize()
    {
        var owner = _vertical is null ? null : TopLevel.GetTopLevel(_vertical) as Window;
        var window = new LeftStripCustomizeWindow(_state, Rebuild);
        if (owner is not null) window.Show(owner); else window.Show();
    }

    private void SetVisible(bool visible)
    {
        _visible = visible;
        Apply();
    }

    private void ToggleComposer()
    {
        // The Composer opens on its own (docked left without the strip when the strip is hidden); hiding the strip leaves it open.
        _composerOpen = !_composerOpen;
        Apply();
    }

    /// <summary>Shows the parts as the state says and lights the toggles, then saves the state.</summary>
    private void Apply()
    {
        Paint();
        Save();
    }

    private void Paint()
    {
        if (_vertical is null || _row is null || _toggle is null || _composer is null || _host is null) return;
        var side = _state.RightSide ? Right : Left;
        if (_vertical.Parent != side)
        {
            (_vertical.Parent as Panel)?.Children.Remove(_vertical);
            DockPanel.SetDock(_vertical, _state.RightSide ? Dock.Right : Dock.Left);
            side.Children.Insert(0, _vertical);   // at the left, the strip sits outside the Composer panel
        }
        _vertical.BorderThickness = _state.RightSide ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        _vertical.IsVisible = _visible && !_state.BesideMenus;
        _row.IsVisible = _visible && _state.BesideMenus;
        _composer.IsVisible = _composerOpen;
        _toggle.IsVisible = _state.ShowButton;
        _toggle.IsChecked = _visible;
        if (_toggle.Content is Control icon) icon.Opacity = _visible ? 1 : ChromeMetrics.DimTextOpacity;   // grey while hidden
        foreach (var toggle in _composerToggles) toggle.IsChecked = _composer.IsVisible;
        _host.IsVisible = _toggle.IsVisible || _row.IsVisible;
    }

    private void Save()
    {
        _state.Visible = _visible;
        _state.ComposerOpen = _composerOpen;
        _settings?.SaveSettingsDeferred();
    }
}
