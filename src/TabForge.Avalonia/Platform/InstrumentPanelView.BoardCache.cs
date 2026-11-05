using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the fretboard's still part (wood, binding, inlays, wires) kept as one bitmap at device pixels, so a ringing string redraws its strings and
//   notes over it instead of the whole neck on every screen refresh. Rebuilt only when the size, scale, look, frets or theme change.
// Does not own: the drawing itself (FretboardArtist), the live parts on top (InstrumentPanelView.Fretboard.cs).
//   Recording contexts (tests) and the video path (clips skipped) always draw live.
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
// Threads: UI thread only.
public sealed partial class InstrumentPanelView
{
    private readonly record struct BoardKey(Size Bounds, double Pixels, double Scale, FretboardGeometryRules.Layout Layout, FretboardStyles.Look Style, string MarkerKey, double MarkerBrightness, bool Bass, bool Light, int Palette);

    private BoardKey? _boardKey;
    private RenderTargetBitmap? _boardBitmap;
    private AvaloniaImage? _boardImage;

    /// <summary>Draws the still board from its bitmap, building it when its key changed; false when this surface draws it live.</summary>
    private bool DrawCachedBoard(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout)
    {
        if (d is not AvaloniaDrawContext || AvaloniaDrawContext.ClipsAreSkipped || TopLevel.GetTopLevel(this) is not { } top) return false;
        var pixels = top.RenderScaling;
        var scale = FretboardScale();
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || scale <= 0) return false;
        // The capo is drawn live and does not touch the still board, so dragging one never rebuilds it.
        var key = new BoardKey(Bounds.Size, pixels, scale, layout with { CapoByString = 0 }, _look.Style, _look.MarkerColourKey, _look.MarkerBrightness, FretInput.Bass,
            ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light, AvaloniaPalette.PublishVersion);
        if (_boardKey != key || _boardImage is null)
        {
            ReleaseBoardBitmap();
            try { BuildBoardBitmap(ink, layout, pixels, scale); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                TabForge.Services.Trace.Error(TabForge.Services.Trace.Ui, "fretboard bitmap: " + ex.Message);
                ReleaseBoardBitmap();
                return false;
            }
            _boardKey = key;
        }
        d.Image(_boardImage!, 0, 0, Bounds.Width / scale, Bounds.Height / scale);   // inside the panel's drawing scale: exactly the panel, pixel for pixel
        return true;
    }

    private void BuildBoardBitmap(Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, double pixels, double scale)
    {
        var size = new PixelSize(Math.Max(1, (int)Math.Round(Bounds.Width * pixels)), Math.Max(1, (int)Math.Round(Bounds.Height * pixels)));
        var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));   // 96 with the scale drawn in below: a bitmap at another dpi misplaces images and loses what follows a clip
        using (var context = bitmap.CreateDrawingContext())
        {
            var draw = new AvaloniaDrawContext().Use(context);
            draw.PushScale(scale * pixels, scale * pixels);
            var scene = new FretboardScene(draw, ink, layout, _look.Style, FretInput.Bass, _assets, _inlays, _look.MarkerColourKey, _look.MarkerBrightness);
            FretboardArtist.DrawBackdrop(scene);
            FretboardArtist.DrawInlays(scene);
            FretboardArtist.DrawWires(scene);
            draw.Pop();
        }
        _boardBitmap = bitmap;
        _boardImage = new AvaloniaImage(bitmap);
    }

    /// <summary>The still board's bitmap as the given display scale makes it (tests: headless windows are always at 1).</summary>
    internal RenderTargetBitmap BoardBitmapAt(double pixels, Func<string, UiColor> ink)
    {
        BuildBoardBitmap(ink, FretboardLayout(), pixels, FretboardScale());
        return _boardBitmap!;
    }

    private void ReleaseBoardBitmap()
    {
        _boardBitmap?.Dispose();
        _boardBitmap = null;
        _boardImage = null;
        _boardKey = null;
    }
}
