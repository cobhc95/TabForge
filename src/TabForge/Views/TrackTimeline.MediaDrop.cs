using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// TrackTimeline: audio and MIDI files dragged from Windows or a plug-in. The drag is measured once when it enters (lengths, MIDI
// files read, virtual files written to a staging folder); each DragOver only snaps the pointer, plans the lane and moves the
// panel's one ghost element. The drop hands the plan to the host, which adds the clips as one undo step.
internal sealed partial class TrackTimeline : IDropPreviewGeometryHost
{
    /// <summary>Media dropped on the timeline (the plan says where; the host adds the clips).</summary>
    public event Action<MediaDropPlan>? MediaDropped;
    /// <summary>The drop preview changed (null = hide it).</summary>
    public event Action<DropPreview?>? DropPreviewChanged;

    private MediaDropSession? _dropSession;
    private DropPreview? _dropPreview;
    private (int Track, int Lane, int Zone, double Start, double Scroll, double Zoom, int Revision, IReadOnlyList<DropItem> Items)? _dropKey;
    private int _dropLeaveToken;
    private DropPreviewGeometryController? _dropPreviewGeometry;

    public DropPreview? CurrentDropPreview => _dropPreview;

    /// <summary>Song seconds and quarter notes (cached per song timeline).</summary>
    public SongQuarterMap QuarterMap() => (_songTimeMap ??= new TrackTimelineSongTimeMapController(this)).GetQuarterMap(Project!);

    /// <summary>DragEnter / DragOver: the effect to show, or null when the drag carries no media (left to the window).</summary>
    internal DragDropEffects? MediaDragOver(IDataObject data, Point p, bool altHeld)
    {
        _dropLeaveToken++;
        if (Project is null || SessionFor(data) is not { } session) { SetDropPreview(null); return null; }
        _lastDropPoint = p; _lastDropAlt = altHeld;
        var preview = DropPreviewAt(session.Items, p, altHeld);
        SetDropPreview(preview);
        SetAddLaneDrag(IsInAddLane(p) && preview.Valid);
        return preview.Valid ? DragDropEffects.Copy : DragDropEffects.None;
    }

    /// <summary>DragLeave: hidden unless the drag comes straight back in (moving between child elements raises leave then enter).</summary>
    internal void MediaDragLeave()
    {
        var token = ++_dropLeaveToken;
        // The drag's data object is let go (the source's COM object is not kept alive); the measured session stays for a re-entry.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (token == _dropLeaveToken) { SetDropPreview(null); ForgetDropData(); } });
    }

    /// <summary>
    /// Drop: the effect (Copy when something is added, None when it cannot go here), or null when the drag carried no media.
    /// Runs inside the source's drag loop on purpose: a plug-in deletes its temp file when the drag returns, so the host copies it now.
    /// </summary>
    internal DragDropEffects? DropMedia(IDataObject data, Point p, bool altHeld)
    {
        _dropLeaveToken++;
        if (Project is null || SessionFor(data) is not { } session) { SetDropPreview(null); return null; }
        if (session.HasDeferred && MediaDropSession.From(data, onDrop: true, media: Media) is { } reread)
        {
            // Virtual files that could not be read during the drag: read now, while the source still serves them.
            session.Dispose();
            _dropSession = session = reread;
            _dropKey = null;
        }
        if (session.HasPending) { session.MeasurePending(Media); _dropKey = null; }   // the drop needs the real lengths (waits for a running measurement)
        var preview = DropPreviewAt(session.Items, p, altHeld);
        SetDropPreview(null);
        try { if (preview.Valid) MediaDropped?.Invoke(preview.Plan); }
        finally
        {
            // The host kept copies of what it needed: the staging folder goes.
            session.Dispose();
            _dropSession = null;
            ForgetDropData();
        }
        return preview.Valid ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private MediaDropSession? SessionFor(IDataObject data)
    {
        // The same data object for every DragOver of one entry into the window: no data read per DragOver.
        if (ReferenceEquals(data, _dropSessionData)) return _dropSessionData is null ? null : _dropSessionForData;
        _dropSessionData = data;
        _dropSessionForData = Resolve();
        return _dropSessionForData;

        MediaDropSession? Resolve()
        {
            var key = MediaDropSession.KeyOf(data, out var files);
            if (key is null) return null;
            if (_dropSession is { } current && current.Key == key) return current;   // the same drag re-entering the window
            _dropSession?.Dispose();
            _dropKey = null;
            // Local files are placeholders (estimated length) so the ghost shows at once; their headers are read off the UI thread.
            _dropSession = MediaDropSession.From(data, media: Media, measureLater: true, knownKey: key, knownFiles: files);
            if (_dropSession is { HasPending: true } fresh) MeasureInBackground(fresh);
            return _dropSession;
        }
    }

    private Point _lastDropPoint;
    private bool _lastDropAlt;

    private void MeasureInBackground(MediaDropSession session)
    {
        var media = Media;
        Task.Run(() => session.MeasurePending(media)).ContinueWith(_ =>
        {
            // Still the same drag and the ghost showing: it takes the real lengths.
            if (!ReferenceEquals(_dropSession, session) || _dropPreview is null) return;
            _dropKey = null;
            SetDropPreview(DropPreviewAt(session.Items, _lastDropPoint, _lastDropAlt));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Warms the drop planner and the ghost geometry once the timeline is up, so the first drag does not pay for cold code.</summary>
    internal void PrewarmDrop()
    {
        if (Project is not { Tracks.Count: > 0 }) return;
        var items = new[] { new DropItem { Path = "", Name = "", Seconds = 1 } };
        DropGeometry(MediaDrop.Plan(Project, items, 0, 0, 0, QuarterMap()), new Point(0, 0), items);
    }

    private IDataObject? _dropSessionData;
    private MediaDropSession? _dropSessionForData;

    private void ForgetDropData() { _dropSessionData = null; _dropSessionForData = null; }

    /// <summary>Test / render hook: the drag is these items (as if they had just entered).</summary>
    internal void SetDropItemsForTest(MediaDropSession? session) { _dropSession?.Dispose(); _dropSession = session; _dropKey = null; ForgetDropData(); }

    public void SetDropPreview(DropPreview? preview)
    {
        if (preview is null) { _dropKey = null; SetAddLaneDrag(false); }
        if (ReferenceEquals(preview, _dropPreview)) return;
        _dropPreview = preview;
        DropPreviewChanged?.Invoke(preview);
    }

    /// <summary>Where the items would land for a pointer at <paramref name="p"/> (timeline coordinates), and the ghost's geometry.</summary>
    internal DropPreview DropPreviewAt(IReadOnlyList<DropItem> items, Point p, bool altHeld = false)
    {
        var project = Project!;
        var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        var track = TrackAt(p.Y);
        if (track < 0 && p.Y >= gridTop && p.Y + VerticalScrollOffset - gridTop >= ArrangementPanel.RowsHeight(project)) track = project.Tracks.Count;   // below the last track
        var lane = 0;
        var zone = 0;
        if (track >= 0 && track < project.Tracks.Count)
        {
            var offset = p.Y - LaneTop(track, 0);
            if (offset >= 0) lane = Math.Min((int)(offset / ArrangementPanel.AudioLaneHeight), Math.Max(0, ArrangementPanel.LaneCountOf(project.Tracks[track]) - 1));
            else zone = -1;   // notation row and lane 0 can have the same preferred lane but different preview bounds
        }
        var start = SnapSec(SecOfX(Math.Max(0, p.X)), null, out _, altHeld);
        var inLane = IsInAddLane(p);   // a file dropped on the Add-track lane always makes an AUDIO track (audio or MIDI)
        var key = (track, inLane ? -1 : lane, inLane ? 0 : zone, start, VerticalScrollOffset, MeasureWidth, project.TimelineRevision, items);
        // The pointer moved inside the same snapped spot: nothing to recompute (no allocation per DragOver).
        if (_dropKey is { } previous && ReferenceEquals(previous.Items, items) && previous == key && _dropPreview is { Valid: true } same) return same;
        var plan = inLane ? MediaDrop.PlanAddTrackLane(project, items, QuarterMap(), start) : MediaDrop.Plan(project, items, track, lane, start, QuarterMap());
        _dropKey = key;
        return DropGeometry(plan, p, items);
    }

    public DropPreview DropGeometry(MediaDropPlan plan, Point p, IReadOnlyList<DropItem> items)
        => (_dropPreviewGeometry ??= new DropPreviewGeometryController(this)).Build(plan, p, items);

    public DropPreview NotationDropGeometry(MediaDropPlan plan, int trackIndex, double startSec, double endSec)
        => (_dropPreviewGeometry ??= new DropPreviewGeometryController(this)).Notation(plan, trackIndex, startSec, endSec);

    double IDropPreviewGeometryHost.VerticalScrollOffset => VerticalScrollOffset;
    Color IDropPreviewGeometryHost.ColourFor(TrackModel? track) => track is null ? _theme.Accent : Readable(Parse(track.ColorHex, _theme.Accent));
    internal static string LengthText(double seconds) =>
        seconds < 60 ? $"{seconds:0.0} s" : $"{(int)(seconds / 60)}:{seconds % 60:00.0}";
}
