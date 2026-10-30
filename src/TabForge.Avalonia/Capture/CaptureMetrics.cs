using Avalonia;
using Avalonia.Controls;

namespace TabForge.Avalonia.Capture;

// Owns: the window placement of capture mode: where windows sit (far off every screen) and how they are kept from focus and the taskbar.
// Does not own: the window sizes (the script, ToolWindowCatalog) or the run (CaptureSession).
// Tests: TabForge.Avalonia.Tests.CaptureSessionTests.
internal static class CaptureMetrics
{
    private const int OffScreenCoordinate = -20000;

    /// <summary>The default size (DIPs across, down) of each tool window the catalogue opens; a script's "size" overrides it.</summary>
    public static (int Across, int Down) ToolSize(string name) => name switch
    {
            "Mixer" => (1180, 560),
            "GroupRules" => (640, 660),
            "Render" => (760, 860),
            "VideoExport" => (640, 760),
            "AddTrack" => (800, 781),
            "TrackProperties" => (800, 811),
            "Tuner" => (420, 330),
            "GlobalTuning" => (640, 437),
            "InstrumentPicker" => (760, 620),
            "ScoreTextStyle" => (520, 281),
            "MixTable" => (470, 415),
            "ChordFinder" => (380, 330),
            "SongStats" => (400, 190),
            "PasteOptions" => (470, 345),
            "Preferences" => (1294, 857),
            "CommandPalette" => (520, 420),
            _ => (800, 600),
    };

    /// <summary>Puts a window far off every screen and keeps it from taking focus or a taskbar button, so a run never shows on a monitor.</summary>
    public static void PlaceOffScreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(OffScreenCoordinate, OffScreenCoordinate);
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }
}
