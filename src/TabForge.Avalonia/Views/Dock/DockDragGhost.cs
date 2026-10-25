using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;

namespace TabForge.Avalonia.Views.Docking;

// Owns: the drag ghost: a small borderless, topmost, never-activated window with the dragged tab's title that follows the pointer
//   while a tab is dragged outside its window.
// Does not own: the pointer capture (the tab that was pressed keeps it) or what the drop does (DockPaneDragFlow, DocumentTabDragFlow).
// Tests: TabForge.Avalonia.Tests.TabTearOffTests.
/// <summary>The tab title under the pointer while a dock or document tab is dragged outside a window.</summary>
public sealed class DockDragGhost : Window
{
    /// <summary>Where the ghost sits from the pointer, in device pixels, so the pointer stays on what lies under the drop.</summary>
    private static readonly PixelVector PointerOffset = new(14, 10);

    private readonly TextBlock _title = new() { Name = "DragGhostTitle", Margin = new Thickness(10, 4) };

    public DockDragGhost()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        var frame = new Border { BorderThickness = new Thickness(1), Child = _title };
        frame[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        frame[!Border.BorderBrushProperty] = new DynamicResourceExtension("Accent");
        Content = frame;
    }

    public string Text => _title.Text ?? "";

    /// <summary>Shows the ghost (first call) with this title next to the pointer's screen position.</summary>
    public void Follow(string title, PixelPoint screen, ThemeVariant theme)
    {
        _title.Text = title;
        RequestedThemeVariant = theme;
        Position = screen + PointerOffset;
        if (!IsVisible) Show();
    }
}
