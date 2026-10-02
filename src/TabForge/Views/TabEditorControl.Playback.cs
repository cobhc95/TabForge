using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;

namespace TabForge.Views;

// TabEditorControl: playhead and the retained per-system drawings.
public sealed partial class TabEditorControl
{
    // ---------- playback (state and geometry live in PlaybackOverlay) ----------

    public void SetPlayhead(int measure, int cell) => _playback.SetPlayhead(measure, cell);

    public void ClearPlayhead() => _playback.Clear();

    public bool PlaybackNeedsRepaint(double fromMs, double toMs) => _playback.NeedsRepaint(fromMs, toMs);

    public (double X, double Top, double Bottom)? PlayheadGeometry() => _playback.PlayheadGeometry();

    public IReadOnlyList<(double X, double EndX, double Top, double Bottom)> PlaybackDurationGeometries() => _playback.DurationGeometries();

    public (int SystemIndex, double SystemLeft, double SystemRight, double PlayheadX, double BarWidth)?
        PlaybackHorizontalGeometry(int measure, double fraction) => _playback.HorizontalGeometry(measure, fraction);

    /// <summary>The playback surface (IScorePlayhead): state, geometry and the playing-bar band.</summary>
    internal PlaybackOverlay Playback => _playback;

    // ---- retained per-system engraving ----
    // Each engraved system is kept as a frozen drawing and replayed while its playback state (sounding/struck notes,
    // playing-bar band) is unchanged; the system holding the playhead is always re-engraved. Any other repaint request
    // (edits, selection, cursor, hover, colours, zoom, layout) clears the cache.
    private readonly SystemDrawingCache _systemCache = new();

    /// <summary>Requests a full repaint (drops cached system drawings).</summary>
    public new void InvalidateVisual()
    {
        _systemCache.Clear();
        base.InvalidateVisual();
    }

    void IScorePageHost.RepaintAll() => InvalidateVisual();
    void IPlaybackOverlayHost.RepaintPlaybackOnly() => base.InvalidateVisual();
}
