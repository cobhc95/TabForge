using System.Windows;
using System.Windows.Controls;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the media-drop controller needs from its panel.</summary>
internal interface IMediaDropHost
{
    bool HasProject { get; }
    System.Windows.Threading.Dispatcher Dispatcher { get; }
}

// Owns: audio and MIDI files dragged onto the timeline (from Windows or a plug-in editor, in this process or another): the drag
//   wiring on the scroll viewer, the one ghost element, and the simulated-drag seams for tests and the off-screen render.
// Does not own: planning where the files land (TrackTimeline) or importing them (the window).
// Tests: TestAddTrackLane and the timeline render captures.
/// <summary>
/// The scroll viewer around the timeline takes the drag, so the empty area below the last track takes drops too; the timeline
/// plans where the files land and the ghost shows it. Drags without media (song files, tabs) are left unhandled for the main window.
/// </summary>
internal sealed class MediaDropController
{
    private readonly IMediaDropHost _host;
    private readonly TrackTimeline _timeline;
    private bool _simulatingGhost;

    public MediaDropController(IMediaDropHost host, ScrollViewer scroll, TrackTimeline timeline)
    {
        _host = host;
        _timeline = timeline;
        scroll.AllowDrop = true;
        scroll.DragEnter += OnMediaDragOver;
        scroll.DragOver += OnMediaDragOver;
        scroll.DragLeave += (_, _) => _timeline.MediaDragLeave();
        scroll.Drop += OnMediaDrop;
        _timeline.DropPreviewChanged += preview => { if (!_simulatingGhost) Ghost.Show(preview); };
        _timeline.Loaded += (_, _) =>
        {
            // Weak: an idle operation still queued when the window closes must not keep the timeline alive.
            var weak = new WeakReference<TrackTimeline>(_timeline);
            _host.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { if (weak.TryGetTarget(out var t)) t.PrewarmDrop(); }));
        };
    }

    /// <summary>The element that shows where dropped files would land.</summary>
    public MediaDropGhost Ghost { get; } = new();

    public static bool AltHeld(DragEventArgs e) => (e.KeyStates & DragDropKeyStates.AltKey) != 0;

    private void OnMediaDragOver(object sender, DragEventArgs e)
    {
        if (!_host.HasProject) return;
        if (_timeline.MediaDragOver(e.Data, e.GetPosition(_timeline), AltHeld(e)) is not { } effect) return;
        e.Effects = EffectFor(effect, e.AllowedEffects, _timeline.CurrentDropPreview);
        e.Handled = true;
    }

    private void OnMediaDrop(object sender, DragEventArgs e)
    {
        if (!_host.HasProject) return;
        var preview = _timeline.CurrentDropPreview;
        if (_timeline.DropMedia(e.Data, e.GetPosition(_timeline), AltHeld(e)) is not { } effect) return;
        e.Effects = EffectFor(effect, e.AllowedEffects, preview);
        e.Handled = true;
    }

    /// <summary>
    /// Copy when the source allows it (Explorer always does). A source that only offers Move gets Move for its own temp files
    /// (TabForge keeps a copy beside the song first), never for a file that stays where it is.
    /// </summary>
    public static DragDropEffects EffectFor(DragDropEffects wanted, DragDropEffects allowed, DropPreview? preview)
    {
        if (wanted == DragDropEffects.None) return DragDropEffects.None;
        if ((allowed & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
        if ((allowed & DragDropEffects.Link) != 0) return DragDropEffects.Link;
        if ((allowed & DragDropEffects.Move) != 0 && preview is { Valid: true } p && p.Plan.Clips.All(c => c.Item.Transient)) return DragDropEffects.Move;
        return DragDropEffects.None;
    }

    // ---------- test and render hooks ----------

    /// <summary>Simulates a drag of <paramref name="session"/> at a point of the timeline (no fades): the off-screen render and tests.</summary>
    public DropPreview SimulateMediaDrag(MediaDropSession session, Point timelinePoint)
    {
        _timeline.SetDropItemsForTest(session);
        var preview = _timeline.DropPreviewAt(session.Items, timelinePoint);
        Ghost.Show(preview, animate: false);
        return preview;
    }

    /// <summary>Simulates dragging a clip to a point of the timeline: the shared ghost shows where it would land (no fades).</summary>
    public DropPreview? SimulateClipMove(AudioClip clip, int fromTrack, Point press, Point to, bool copy = false, bool alt = false)
    {
        _simulatingGhost = true;   // the timeline's own event would start the fade-in; the render wants the ghost at once
        try { Ghost.Show(_timeline.ClipGestures.SimulateMove(clip, fromTrack, press, to, copy, alt), animate: false); }
        finally { _simulatingGhost = false; }
        var preview = _timeline.CurrentDropPreview;
        return preview;
    }

    public void EndSimulatedClipMove() { _timeline.ClipGestures.Cancel(); Ghost.Show(null, animate: false); }

    public void EndSimulatedMediaDrag() => Ghost.Show(null, animate: false);
}
