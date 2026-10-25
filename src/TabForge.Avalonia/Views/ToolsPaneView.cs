using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the side panel's Tools tab: its groups in catalogue order, one icon button per tool, each button's selected, hover and disabled look,
//   and the click (or Enter and Space) that runs the tool's catalogue command. A button whose command has no Avalonia handler is disabled.
// Does not own: the tool rows (Core PaletteToolCatalog), the commands (MainWindowCommands), the editor state (ScorePreviewView.ToolState),
//   the dock tab that hosts the pane (DockPaneSetup).
// Tests: TabForge.Avalonia.Tests.ToolsPaneViewTests.
/// <summary>The Tools pane. The window creates it before the dock, then attaches its command table once the commands exist.</summary>
public sealed class ToolsPaneView : Border
{
    /// <summary>A tool button's square, as the WPF palette sizes it (DIPs).</summary>
    public const double ToolButtonSize = UiMetrics.ToolButtonSize;
    /// <summary>The icon inside a tool button (DIPs).</summary>
    public const double IconSize = 27;
    /// <summary>Group heading size.</summary>
    private const double HeadingFontSize = 11;
    /// <summary>Corner radius of a tool button's chrome (DIPs).</summary>
    private const double ButtonCornerRadius = 4;

    private readonly ScorePreviewView _score;
    private readonly PaletteTool[] _rows;
    private readonly List<ToolEntry> _entries = new();
    private MainWindowCommands? _commands;

    /// <summary>Builds the pane for <paramref name="rows"/>; the Tools tab's rows when none are given.</summary>
    public ToolsPaneView(ScorePreviewView score, PaletteTool[]? rows = null)
    {
        _score = score;
        _rows = rows ?? PaletteToolCatalog.ToolsTab;
        this[!MarginProperty] = new DynamicResourceExtension(UiDensityResources.PaneMargin);
        this[!PaddingProperty] = new DynamicResourceExtension(UiDensityResources.PanePad);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        this[!BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        this[!BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

        var groups = new StackPanel();
        foreach (var group in _rows.GroupBy(tool => tool.Group)) groups.Children.Add(GroupSection(group.Key, group));
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = groups,
        };
        // The groups take the scroll viewport's width, so each group's buttons wrap inside the pane whatever the host gives it.
        scroll.LayoutUpdated += (_, _) =>
        {
            var width = VisibleContentWidth(scroll);
            if (width > 0 && groups.Width != width) groups.Width = width;
        };
        Child = scroll;
        _score.CursorChanged += Refresh;
        this.FollowTheme(Refresh);
    }

    /// <summary>Connects the window's command table: a button runs its tool through it, and the buttons show which commands exist.</summary>
    public void Attach(MainWindowCommands commands)
    {
        _commands = commands;
        Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
    }

    /// <summary>The tool buttons in pane order (tests read them).</summary>
    internal IReadOnlyList<ToolEntry> Entries => _entries;

    /// <summary>Runs the tool with this id as its button does; nothing happens when its command is not handled.</summary>
    internal void RunTool(string toolId)
    {
        var entry = _entries.FirstOrDefault(candidate => candidate.Tool.Id == toolId);
        if (entry is { Enabled: true } && CommandOf(entry.Tool) is { } command) _commands!.Run(command);
    }

    // The width the content can use: the scroll viewer's width less its vertical bar while that bar shows (the viewport reports the content size, not this).
    private static double VisibleContentWidth(ScrollViewer scroll)
    {
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(candidate => candidate.Orientation == Orientation.Vertical && candidate.IsVisible);
        return scroll.Bounds.Width - (bar?.Bounds.Width ?? 0);
    }

    private Control GroupSection(string heading, IEnumerable<PaletteTool> tools)
    {
        var label = new TextBlock { Text = heading, FontSize = HeadingFontSize, FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, 2, 2, 3) };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SecondaryTextBrush");
        var buttons = new WrapPanel();
        foreach (var tool in tools)
        {
            var entry = ToolButton(tool);
            _entries.Add(entry);
            buttons.Children.Add(entry.Chrome);
        }
        var section = new StackPanel { Children = { label, buttons } };
        section[!Layoutable.MarginProperty] = new DynamicResourceExtension(UiDensityResources.GroupGap);
        return section;
    }

    // The button is a focusable Border: the theme's Button template paints its own disabled and idle boxes, which the palette does not show.
    private ToolEntry ToolButton(PaletteTool tool)
    {
        var icon = new ToolIconView(tool.Icon) { Width = IconSize, Height = IconSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var chrome = new Border
        {
            CornerRadius = new CornerRadius(ButtonCornerRadius),
            Child = icon,
            Focusable = true,
        };
        chrome[!Layoutable.WidthProperty] = new DynamicResourceExtension(UiDensityResources.PaletteButton);
        chrome[!Layoutable.HeightProperty] = new DynamicResourceExtension(UiDensityResources.PaletteButton);
        chrome[!Layoutable.MinWidthProperty] = new DynamicResourceExtension(UiDensityResources.PaletteButton);
        chrome[!Layoutable.MarginProperty] = new DynamicResourceExtension(UiDensityResources.PaletteGap);
        var entry = new ToolEntry(tool, chrome, icon);
        TabForge.Avalonia.Platform.Controls.HotkeyTip.Set(chrome, PaletteToolCatalog.HotkeyIdFor(tool.Id), tool.Label);
        AutomationProperties.SetName(chrome, tool.Label);
        chrome.PointerEntered += (_, _) => { entry.Hovered = true; Paint(entry, IsLight(chrome)); };
        chrome.PointerExited += (_, _) => { entry.Hovered = false; entry.Pressed = false; Paint(entry, IsLight(chrome)); };
        chrome.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(chrome).Properties.IsLeftButtonPressed) return; entry.Pressed = true; Paint(entry, IsLight(chrome)); RunTool(tool.Id); };
        chrome.PointerReleased += (_, _) => { entry.Pressed = false; Paint(entry, IsLight(chrome)); };
        chrome.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;   // Space is Play/Pause in the window (MainWindowKeyboard.RouteBeforeControls), never a click here
            RunTool(tool.Id);
            e.Handled = true;
        };
        return entry;
    }

    private string? CommandOf(PaletteTool tool) =>
        _commands is not null && PaletteToolCatalog.CommandIdFor(tool.Id) is { } id && _commands.Handles(id) ? id : null;

    /// <summary>Repaints every button from the editor state at the cursor and from the command table.</summary>
    private void Refresh()
    {
        var light = IsLight(this);
        foreach (var entry in _entries)
        {
            entry.Active = _score.ToolState(entry.Tool.Id) == true;
            entry.Enabled = CommandOf(entry.Tool) is not null && _score.ToolEnabled(entry.Tool.Id);
            Paint(entry, light);
        }
    }

    private static bool IsLight(StyledElement element) => element.ActualThemeVariant == ThemeVariant.Light;

    // The look: an active tool has the accent soft fill, the accent border and a thick bottom edge (also marked in its name). Its icon takes the
    // accent colour; an enabled tool takes the text colour, a disabled one the muted colour. A hovered enabled tool takes the hover fill.
    private static void Paint(ToolEntry entry, bool light)
    {
        var chrome = entry.Chrome;
        chrome.Background = entry.Active ? Ink(entry.Enabled ? "SelectedTintBrush" : "HoverBrush", light) : entry.Pressed && entry.Enabled ? Ink("PressBrush", light) : entry.Hovered && entry.Enabled ? Ink("HoverBrush", light) : Brushes.Transparent;
        chrome.BorderBrush = entry.Active ? Ink(entry.Enabled ? "SelectedOutlineBrush" : "BorderBrush", light) : Brushes.Transparent;
        chrome.BorderThickness = entry.Active ? new Thickness(1, 1, 1, 3) : new Thickness(1);
        var colour = entry.Active && !light ? "IconActiveBrush" : entry.Enabled ? "IconDefaultBrush" : "IconDisabledBrush";
        entry.Icon.Colour = AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames[colour], light);
        AutomationProperties.SetName(chrome, entry.Active ? entry.Tool.Label + ", active" : entry.Tool.Label);
    }

    private static IBrush Ink(string brushName, bool light) =>
        new SolidColorBrush(AvaloniaPalette.ColorOf(AvaloniaPalette.Get(AvaloniaPalette.BrushPaletteNames[brushName], light)));

    /// <summary>One tool button and the state it is painted in.</summary>
    internal sealed class ToolEntry
    {
        public ToolEntry(PaletteTool tool, Border chrome, ToolIconView icon)
        {
            Tool = tool;
            Chrome = chrome;
            Icon = icon;
        }

        public PaletteTool Tool { get; }
        public Border Chrome { get; }
        public ToolIconView Icon { get; }
        public bool Active { get; set; }
        public bool Enabled { get; set; }
        public bool Hovered { get; set; }
        public bool Pressed { get; set; }
    }
}
