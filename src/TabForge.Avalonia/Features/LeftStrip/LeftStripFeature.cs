using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Features.LeftStrip;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.LeftStrip;

// Owns: the experimental left strip: the slim vertical icon strip (Open, Save, Save as, Render, then the Composer toggle) at the window's left, the
//   same icons as a row beside the menus, the sidebar toggle icon at the left end of the menu bar (moved into the compact / Band mode bar while
//   that bar replaces the menus), the strip's right-click menu (Place beside menus, Keep vertical, Hide strip), the Composer panel's showing, and
//   the commands View.ToggleLeftStrip, View.ToggleComposer, View.ShowStripButton and View.ShowStripOnStart with their saved state.
// Does not own: the file commands the icons run (MainWindowCommands), the icon look (MenuRowToggles, QuickActionsGroup), the panel's content
//   (ComposerPanel), the saved values and start rules (Core LeftStripSettings), the menu rows (MainMenuTable).
// Tests: TabForge.Avalonia.Tests.LeftStripSettingsTests.
public sealed class LeftStripFeature
{
    public const string ToggleStripId = "View.ToggleLeftStrip", ToggleComposerId = "View.ToggleComposer";
    public const string ShowButtonId = "View.ShowStripButton", ShowOnStartId = "View.ShowStripOnStart";

    private const string StripIcon = "M3,4 H21 V20 H3 Z M9,4 V20";
    private const string OpenIcon = "M3,6 H9 L11,8 H21 V19 H3 Z";
    private const string SaveIcon = "M4,3 H17 L21,7 V21 H4 Z M8,3 V8 H15 V3 M8,21 V14 H17 V21";
    private const string SaveAsIcon = "M4,3 H15 L18,6 V11 M4,3 V21 H11 M8,3 V8 H13 V3 M13,21 L20,14 L22,16 L15,23 H13 Z";
    private const string RenderIcon = "M2,12 H5 L7,7 L10,18 L13,4 L16,16 L18,12 H22";
    private const string ComposerIcon = "M9,18 V5 L19,3 V16 M9,18 A3,3 0 1 1 3,18 A3,3 0 1 1 9,18 M19,16 A3,3 0 1 1 13,16 A3,3 0 1 1 19,16";

    private readonly MainWindowCommands _commands;
    private readonly MainWindowSettings? _settings;
    private readonly LeftStripSettings _state;
    private readonly List<ToggleButton> _composerToggles = new();
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
        commands.AddCheckState(ShowOnStartId, () => _state.ShowOnStart);
    }

    /// <summary>The window's left region (the vertical strip and the Composer panel); the window docks it left of the workspace.</summary>
    public DockPanel Left { get; } = new() { LastChildFill = false };

    /// <summary>Builds the strip's parts and places the toggle icon in the menu row, or in the mode bar while the menu row is hidden.</summary>
    public void Attach(Control menuRow, CompactTrackStrip modeBar)
    {
        _vertical = new Border { Width = ChromeMetrics.LeftStripWidth, BorderThickness = new Thickness(0, 0, 1, 0) };
        _vertical[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        _vertical[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        _vertical.Child = Icons(Orientation.Vertical);
        _vertical.ContextMenu = StripMenu();
        _composer = ComposerPanel.Build();
        DockPanel.SetDock(_vertical, Dock.Left);
        DockPanel.SetDock(_composer, Dock.Left);
        Left.Children.Add(_vertical);
        Left.Children.Add(_composer);
        _row = Icons(Orientation.Horizontal);
        _row.ContextMenu = StripMenu();
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
        Paint();
    }

    private StackPanel Icons(Orientation orientation)
    {
        var vertical = orientation == Orientation.Vertical;
        var panel = new StackPanel
        {
            Orientation = orientation, Spacing = 2, HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = vertical ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = vertical ? new Thickness(0, 6) : new Thickness(0),
        };
        panel.Children.Add(QuickActionsGroup.Button(() => _commands, "File.Open", "Open", OpenIcon));
        panel.Children.Add(QuickActionsGroup.Button(() => _commands, "File.Save", "Save", SaveIcon));
        panel.Children.Add(QuickActionsGroup.Button(() => _commands, "File.SaveAs", "Save as", SaveAsIcon));
        panel.Children.Add(QuickActionsGroup.Button(() => _commands, "File.Render", "Render", RenderIcon));
        if (vertical)
        {
            var line = new Border { Height = 1, Width = 18, Margin = new Thickness(0, 4), Opacity = 0.4, IsHitTestVisible = false };
            line[!Border.BackgroundProperty] = new DynamicResourceExtension("TextMuted");
            panel.Children.Add(line);
        }
        else panel.Children.Add(MenuRowToggles.Separator());
        var composer = MenuRowToggles.Button(new MenuRowToggles.Toggle(ToggleComposerId, "Composer", ComposerIcon), _commands);
        _composerToggles.Add(composer);
        panel.Children.Add(composer);
        return panel;
    }

    private ContextMenu StripMenu()
    {
        MenuItem Item(string header, System.Action run)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => run();
            return item;
        }
        return new ContextMenu
        {
            Items =
            {
                Item("Place beside menus", () => { _state.BesideMenus = true; Apply(); }),
                Item("Keep vertical", () => { _state.BesideMenus = false; Apply(); }),
                Item("Hide strip", () => SetVisible(false)),
            },
        };
    }

    private void SetVisible(bool visible)
    {
        _visible = visible;
        Apply();
    }

    private void ToggleComposer()
    {
        _composerOpen = !(_visible && _composerOpen);
        _visible |= _composerOpen;
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
        _vertical.IsVisible = _visible && !_state.BesideMenus;
        _row.IsVisible = _visible && _state.BesideMenus;
        _composer.IsVisible = _visible && _composerOpen;
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
