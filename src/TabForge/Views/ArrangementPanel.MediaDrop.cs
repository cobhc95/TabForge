using System.Windows;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// ArrangementPanel: media drops, forwarded to MediaDropController.
public sealed partial class ArrangementPanel : IMediaDropHost
{
    private void AttachMediaDrop()
    {
        _mediaDrop = new MediaDropController(this, _horizontal, _timeline);
        _timelineHost.Children.Add(_mediaDrop.Ghost);
    }

    bool IMediaDropHost.HasProject => _project is not null;

    internal static bool AltHeld(DragEventArgs e) => MediaDropController.AltHeld(e);
    internal static DragDropEffects EffectFor(DragDropEffects wanted, DragDropEffects allowed, DropPreview? preview) => MediaDropController.EffectFor(wanted, allowed, preview);

    // ---------- test and render hooks ----------
    internal TrackTimeline TimelineForTest => _timeline;
    internal Rect? DropGhostBlock => _mediaDrop.Ghost.BlockBounds;
    internal Rect? DropGhostSlot => _mediaDrop.Ghost.SlotBounds;
    internal bool DropGhostShown => _mediaDrop.Ghost.IsShown;
    internal DropPreview SimulateMediaDrag(MediaDropSession session, Point timelinePoint) => _mediaDrop.SimulateMediaDrag(session, timelinePoint);
    internal DropPreview? SimulateClipMove(AudioClip clip, int fromTrack, Point press, Point to, bool copy = false, bool alt = false) => _mediaDrop.SimulateClipMove(clip, fromTrack, press, to, copy, alt);
    internal void EndSimulatedClipMove() => _mediaDrop.EndSimulatedClipMove();
    internal void EndSimulatedMediaDrag() => _mediaDrop.EndSimulatedMediaDrag();
}
