using System.Windows;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// ArrangementPanel: audio and MIDI files dragged onto the timeline (from Windows or a plug-in editor, in this process or another).
// The scroll viewer around the timeline takes the drag, so the empty area below the last track takes drops too; the timeline
// plans where the files land and the panel's one ghost element shows it. Drags without media (song files, tabs) are left
// unhandled for the main window.
public sealed partial class ArrangementPanel
{
    private bool _simulatingGhost;

    private void HookMediaDrop()
    {
        _horizontal.AllowDrop = true;
        _horizontal.DragEnter += OnMediaDragOver;
        _horizontal.DragOver += OnMediaDragOver;
        _horizontal.DragLeave += (_, _) => _timeline.MediaDragLeave();
        _horizontal.Drop += OnMediaDrop;
        _timeline.DropPreviewChanged += preview => { if (!_simulatingGhost) _dropGhost.Show(preview); };
    }

    private static bool AltHeld(DragEventArgs e) => (e.KeyStates & DragDropKeyStates.AltKey) != 0;

    private void OnMediaDragOver(object sender, DragEventArgs e)
    {
        if (_project is null) return;
        if (_timeline.MediaDragOver(e.Data, e.GetPosition(_timeline), AltHeld(e)) is not { } effect) return;
        e.Effects = EffectFor(effect, e.AllowedEffects, _timeline.CurrentDropPreview);
        e.Handled = true;
    }

    private void OnMediaDrop(object sender, DragEventArgs e)
    {
        if (_project is null) return;
        var preview = _timeline.CurrentDropPreview;
        if (_timeline.DropMedia(e.Data, e.GetPosition(_timeline), AltHeld(e)) is not { } effect) return;
        e.Effects = EffectFor(effect, e.AllowedEffects, preview);
        e.Handled = true;
    }

    /// <summary>
    /// Copy when the source allows it (Explorer always does). A source that only offers Move gets Move for its own temp files
    /// (TabForge keeps a copy beside the song first), never for a file that stays where it is.
    /// </summary>
    internal static DragDropEffects EffectFor(DragDropEffects wanted, DragDropEffects allowed, DropPreview? preview)
    {
        if (wanted == DragDropEffects.None) return DragDropEffects.None;
        if ((allowed & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
        if ((allowed & DragDropEffects.Link) != 0) return DragDropEffects.Link;
        if ((allowed & DragDropEffects.Move) != 0 && preview is { Valid: true } p && p.Plan.Clips.All(c => c.Item.Transient)) return DragDropEffects.Move;
        return DragDropEffects.None;
    }

    // ---------- test and render hooks ----------
    internal TrackTimeline TimelineForTest => _timeline;
    internal Rect? DropGhostBlock => _dropGhost.BlockBounds;
    internal Rect? DropGhostSlot => _dropGhost.SlotBounds;
    internal bool DropGhostShown => _dropGhost.IsShown;

    /// <summary>Simulates a drag of <paramref name="session"/> at a point of the timeline (no fades): the off-screen render and tests.</summary>
    internal DropPreview SimulateMediaDrag(MediaDropSession session, Point timelinePoint)
    {
        _timeline.SetDropItemsForTest(session);
        var preview = _timeline.DropPreviewAt(session.Items, timelinePoint);
        _dropGhost.Show(preview, animate: false);
        return preview;
    }

    /// <summary>Simulates dragging a clip to a point of the timeline: the shared ghost shows where it would land (no fades).</summary>
    internal DropPreview? SimulateClipMove(AudioClip clip, int fromTrack, Point press, Point to, bool copy = false, bool alt = false)
    {
        _simulatingGhost = true;   // the timeline's own event would start the fade-in; the render wants the ghost at once
        try { _dropGhost.Show(_timeline.SimulateClipMove(clip, fromTrack, press, to, copy, alt), animate: false); }
        finally { _simulatingGhost = false; }
        var preview = _timeline.CurrentDropPreview;
        return preview;
    }

    internal void EndSimulatedClipMove() { _timeline.CancelClipDrag(); _dropGhost.Show(null, animate: false); }

    internal void EndSimulatedMediaDrag() => _dropGhost.Show(null, animate: false);
}
