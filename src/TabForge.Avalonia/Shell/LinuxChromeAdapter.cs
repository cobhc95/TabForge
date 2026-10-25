using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Platform;

namespace TabForge.Avalonia.Shell;

// Owns: the Linux client-side decorations: the eight invisible resize grips round the window edge, the 1 px palette border, and hiding both
//       while the window is maximised or full screen. Window moves and resizes are Avalonia's drags (the base class).
// Does not own: the caption buttons or the drag-to-move (TitleBarView) or the border width rule (Core ChromeHitRules.ClassifyResizeEdge).
// Tests: TabForge.Avalonia.Tests.ChromeModeTests. Real Linux behaviour is unproven until phase 7.
/// <summary>The window chrome on Linux.</summary>
public sealed class LinuxChromeAdapter : AvaloniaWindowChrome
{
    /// <summary>The resize border thickness in DIPs, the same as the Windows hit test.</summary>
    public const double GripSize = TabForge.Avalonia.Shell.Win32.NcHitTestHook.ResizeBorderDips;

    public LinuxChromeAdapter(Window window) : base(window) { }

    /// <summary>Builds the frame round <paramref name="content"/> for any chrome: the grips and the border over it; they hide unless the window is normal.</summary>
    public static Grid Frame(Control content, IWindowChrome chrome)
    {
        var grips = Grips(chrome);
        var border = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
        border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("ChromeBorder"));
        chrome.StateChanged += (_, _) => grips.IsVisible = border.IsVisible = chrome.State == WindowChromeState.Normal;
        return new Grid { Children = { content, grips, border } };
    }

    private static Grid Grips(IWindowChrome chrome)
    {
        var grid = new Grid { Name = "ResizeGrips" };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GripSize, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GripSize, GridUnitType.Pixel));
        grid.RowDefinitions.Add(new RowDefinition(GripSize, GridUnitType.Pixel));
        grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        grid.RowDefinitions.Add(new RowDefinition(GripSize, GridUnitType.Pixel));
        var cells = new (ResizeEdge Edge, int Column, int Row, StandardCursorType Cursor)[]
        {
            (ResizeEdge.TopLeft, 0, 0, StandardCursorType.TopLeftCorner), (ResizeEdge.Top, 1, 0, StandardCursorType.TopSide),
            (ResizeEdge.TopRight, 2, 0, StandardCursorType.TopRightCorner), (ResizeEdge.Left, 0, 1, StandardCursorType.LeftSide),
            (ResizeEdge.Right, 2, 1, StandardCursorType.RightSide), (ResizeEdge.BottomLeft, 0, 2, StandardCursorType.BottomLeftCorner),
            (ResizeEdge.Bottom, 1, 2, StandardCursorType.BottomSide), (ResizeEdge.BottomRight, 2, 2, StandardCursorType.BottomRightCorner),
        };
        foreach (var (edge, column, row, cursor) in cells)
        {
            var grip = new Border { Name = "Grip" + edge, Tag = edge, Background = AvaloniaPalette.TransparentBrush, Cursor = new Cursor(cursor) };
            Grid.SetColumn(grip, column);
            Grid.SetRow(grip, row);
            grip.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
                (chrome as AvaloniaWindowChrome)?.NotePress(e);
                chrome.BeginResize(edge);
            };
            grid.Children.Add(grip);
        }
        return grid;
    }
}
