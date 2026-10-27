using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// A button's label presenter paints no fill of its own: the pixel beside the label shows the button's fill, as the pixel on its border does.
/// Owns: the button fill regression. Does not own: the button templates (AvaloniaButtonTemplates).
/// Tests: this class.
/// </summary>
public class ButtonLabelFillTests
{
    // Offsets inside the button: 4 DIP is the border's own fill, 16 DIP is the label presenter's area left of the centred label.
    private const int BorderOffset = 4, LabelAreaOffset = 16;

    [AvaloniaFact]
    public void PreferencesActionButtonPaintsOneFillAroundItsLabel()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new TabForge.Services.AppSettings());
        window.Show();
        var button = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Cancel");
        var origin = button.TranslatePoint(new Point(0, 0), window)!.Value;
        var middle = button.Bounds.Height / 2;
        using var frame = window.CaptureRenderedFrame()!;
        var border = PixelAt(frame, origin.X + BorderOffset, origin.Y + middle);
        var labelArea = PixelAt(frame, origin.X + LabelAreaOffset, origin.Y + middle);
        Assert.Equal(border, labelArea);
    }

    // The 32-bit pixel at a point of a locked frame (BGRA, little-endian).
    internal static int PixelAt(WriteableBitmap frame, double x, double y)
    {
        using var locked = frame.Lock();
        return Marshal.ReadInt32(locked.Address, (int)y * locked.RowBytes + (int)x * 4);
    }
}
