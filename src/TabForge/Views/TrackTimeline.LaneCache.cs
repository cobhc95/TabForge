using System.Windows;
using System.Windows.Media;
using TabForge.Audio;
using TabForge.Models;

namespace TabForge.Views;

// Owns: the retained drawing of each track's audio and MIDI clips (boxes, waveform, labels, fades), reused while nothing it draws
//   changed, and the hit counter (ClipDrawingHits).
// Does not own: the clip drawing itself (TrackTimeline.Clips.cs) and the peak outline cache (TrackTimeline.Waveform.cs).
// Tests: TestClipWaveformSpan.

internal sealed partial class TrackTimeline
{
    private readonly Dictionary<TrackModel, (int Key, Drawing Drawing)> _clipDrawings = new();
    /// <summary>Clip drawings reused instead of redrawn (test probe).</summary>
    internal int ClipDrawingHits { get; private set; }

    private void DrawClipsCached(DrawingContext dc, TrackModel track, double rowTop, double width, Color trackColor, Color clipColour, double notation)
    {
        if (track.AudioClips.Count == 0 || track.AudioClips.Any(c => c.IsMidi))
        {
            _clipDrawings.Remove(track);   // MIDI note edits are in place, so those lanes always redraw
            DrawClips(dc, track, rowTop, width, clipColour, notation);
            return;
        }
        var h = new HashCode();
        h.Add(_theme); h.Add(rowTop); h.Add(width); h.Add(trackColor); h.Add(clipColour); h.Add(notation);
        h.Add(_viewFrom); h.Add(_viewWidth); h.Add(_waveZooming); h.Add(VisualTreeHelper.GetDpi(this).PixelsPerDip);
        h.Add(SelectedClip); h.Add(Media); h.Add(ArrangementPanel.AudioLaneHeight);
        foreach (var c in track.AudioClips)
        {
            h.Add(c); h.Add(c.StartSec); h.Add(c.EndSec); h.Add(c.Lane); h.Add(c.OffsetSec); h.Add(c.SourceLengthSec); h.Add(c.GainDb);
            h.Add(c.FadeInSec); h.Add(c.FadeOutSec); h.Add(c.Muted); h.Add(c.Name); h.Add(c.File); h.Add(c.IsMidi);
            h.Add(ClipLanes.Audible(track, c)); h.Add(ClipGestures.IsMovingOriginal(c));
            if (!c.IsMidi)
            {
                h.Add(WaveformCache.Get(c.File, Media)); var st = WaveformCache.StatusOf(c.File, Media); h.Add(st.State); h.Add(st.Message);
            }
           
        }
        var key = h.ToHashCode();
        if (_clipDrawings.TryGetValue(track, out var hit) && hit.Key == key) { ClipDrawingHits++; dc.DrawDrawing(hit.Drawing); return; }
        var group = new DrawingGroup();
        using (var gdc = group.Open()) DrawClips(gdc, track, rowTop, width, clipColour, notation);
        group.Freeze();
        _clipDrawings[track] = (key, group);
        dc.DrawDrawing(group);
    }
}
