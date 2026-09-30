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

namespace TabForge.Views;

// TabEditorControl: playhead and the retained per-system drawings.
public sealed partial class TabEditorControl
{
    // ---------- playback ----------

    public void SetPlayhead(int measure, int cell)
    {
        PlaybackMeasure = measure;
        PlaybackCell = cell;
        // Playback-only repaint: keeps the cached engraving of systems whose playback state is unchanged.
        base.InvalidateVisual();
    }

    // ---- retained per-system engraving ----
    // Every playback beat used to re-engrave all visible systems. Each engraved system is now kept as a
    // frozen drawing and replayed while its playback state (sounding/struck notes) is unchanged; the
    // system holding the playhead is always re-engraved. Any other repaint request (edits, selection,
    // cursor, hover, colours, zoom, layout) clears the cache, so output is identical to before.
    private readonly Dictionary<int, (long Signature, Drawing Drawing)> _systemDrawings = new();
    private object? _systemDrawingsLayout;
    private int _systemDrawingsDpi = -1;

    /// <summary>Requests a full repaint (drops cached system drawings).</summary>
    public new void InvalidateVisual()
    {
        _systemDrawings.Clear();
        base.InvalidateVisual();
    }

    private long SystemPlaybackSignature(ScoreSystemPosition system)
    {
        unchecked
        {
            long hash = PlaybackActive ? 17 : 3;
            int first = system.FirstMeasure, last = system.LastMeasure;
            foreach (var (bar, cell, str) in _soundingNow)
                if (bar >= first && bar <= last) hash += (bar * 7919L + cell * 131L + str + 1) * 0x9E3779B1L;
            foreach (var (bar, cell, str) in _struckNow)
                if (bar >= first && bar <= last) hash += (bar * 6151L + cell * 257L + str + 1) * 0x85EBCA77L;
            return hash;
        }
    }

    public void ClearPlayhead()
    {
        PlaybackMeasure = -1;
        PlaybackCell = -1;
        InvalidateVisual();
    }
}
