using System.Windows.Media;
using TabForge.Views.Rendering;

namespace TabForge;

// Owns: the whole-pixel snap of scrolling layers (PixelSnap): offsets land on device pixels, both axes, and a bad DPI changes nothing.
// Does not own: the views that use it (TestKeyboardModeKeyView, the score and timeline tests).
// Tests: TestPixelSnap.
public static partial class SelfTest
{
    private static void TestPixelSnap()
    {
        Check("pixel snap: an offset lands on a whole device pixel at 125% and 150%", Math.Abs(PixelSnap.Snap(10.37, 1.25) * 1.25 - Math.Round(PixelSnap.Snap(10.37, 1.25) * 1.25)) < 1e-9 && Math.Abs(PixelSnap.Snap(-3.3, 1.5) * 1.5 - Math.Round(PixelSnap.Snap(-3.3, 1.5) * 1.5)) < 1e-9);
        var shift = new TranslateTransform();
        PixelSnap.SetOffset(shift, 5.21, -7.77, 2);
        Check("pixel snap: SetOffset snaps both axes (at 200% to half DIPs)", shift.X == 5.0 && shift.Y == -8.0, $"{shift.X} {shift.Y}");
        Check("pixel snap: a bad DPI leaves the value alone", PixelSnap.Snap(1.234, 0) == 1.234);
    }
}
