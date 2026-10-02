using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// The drop preview of media dragged over the timeline: the block where the files will land (timeline coordinates), and the
/// new lane or new track slot the drop would create.
/// </summary>
internal sealed record DropPreview(MediaDropPlan Plan, Rect Block, Rect? Slot, Color Colour, string Label, string Detail, string? SlotLabel, double[] Splits)
{
    public bool Valid => Plan.Valid;
}

// TrackTimeline: audio and MIDI files dragged from Windows or a plug-in. The drag is measured once when it enters (lengths, MIDI
// files read, virtual files written to a staging folder); each DragOver only snaps the pointer, plans the lane and moves the
// panel's one ghost element. The drop hands the plan to the host, which adds the clips as one undo step.
internal sealed partial class TrackTimeline
{
    /// <summary>Media dropped on the timeline (the plan says where; the host adds the clips).</summary>
    public event Action<MediaDropPlan>? MediaDropped;
    /// <summary>The drop preview changed (null = hide it).</summary>
    public event Action<DropPreview?>? DropPreviewChanged;

    private MediaDropSession? _dropSession;
    private DropPreview? _dropPreview;
    private (int Track, int Lane, double Start, double Scroll, double Zoom, int Revision)? _dropKey;
    private IReadOnlyList<DropItem>? _dropKeyItems;
    private int _dropLeaveToken;
    private (SongProject Project, (int, int) Key, SongQuarterMap Map)? _quarterMap;

    internal DropPreview? CurrentDropPreview => _dropPreview;

    /// <summary>Song seconds and quarter notes (cached per song timeline).</summary>
    private SongQuarterMap QuarterMap()
    {
        var project = Project!;
        var key = SongClock.TimelineKey(project);
        if (_quarterMap is { } cached && ReferenceEquals(cached.Project, project) && cached.Key == key) return cached.Map;
        var map = SongQuarterMap.For(project);
        _quarterMap = (project, key, map);
        return map;
    }

    /// <summary>DragEnter / DragOver: the effect to show, or null when the drag carries no media (left to the window).</summary>
    internal DragDropEffects? MediaDragOver(IDataObject data, Point p, bool altHeld)
    {
        _dropLeaveToken++;
        if (Project is null || SessionFor(data) is not { } session) { SetDropPreview(null); return null; }
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
            var key = MediaDropSession.KeyOf(data);
            if (key is null) return null;
            if (_dropSession is { } current && current.Key == key) return current;   // the same drag re-entering the window
            _dropSession?.Dispose();
            _dropKey = null;
            _dropSession = MediaDropSession.From(data, media: Media);
            return _dropSession;
        }
    }

    private IDataObject? _dropSessionData;
    private MediaDropSession? _dropSessionForData;

    private void ForgetDropData() { _dropSessionData = null; _dropSessionForData = null; }

    /// <summary>Test / render hook: the drag is these items (as if they had just entered).</summary>
    internal void SetDropItemsForTest(MediaDropSession? session) { _dropSession?.Dispose(); _dropSession = session; _dropKey = null; ForgetDropData(); }

    private void SetDropPreview(DropPreview? preview)
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
        if (track >= 0 && track < project.Tracks.Count)
        {
            var offset = p.Y - LaneTop(track, 0);
            if (offset >= 0) lane = Math.Min((int)(offset / ArrangementPanel.AudioLaneHeight), Math.Max(0, ArrangementPanel.LaneCountOf(project.Tracks[track]) - 1));
        }
        var start = SnapSec(SecOfX(Math.Max(0, p.X)), null, out _, altHeld);
        var inLane = IsInAddLane(p);   // a file dropped on the Add-track lane always makes an AUDIO track (audio or MIDI)
        var key = (track, inLane ? -1 : lane, start, VerticalScrollOffset, MeasureWidth, project.TimelineRevision);
        // The pointer moved inside the same snapped spot: nothing to recompute (no allocation per DragOver).
        if (_dropKey == key && ReferenceEquals(_dropKeyItems, items) && _dropPreview is { Valid: true } same) return same;
        var plan = inLane ? MediaDrop.PlanAddTrackLane(project, items, QuarterMap(), start) : MediaDrop.Plan(project, items, track, lane, start, QuarterMap());
        _dropKey = key;
        _dropKeyItems = items;
        return DropGeometry(plan, p, items);
    }

    private DropPreview DropGeometry(MediaDropPlan plan, Point p, IReadOnlyList<DropItem> items)
    {
        var project = Project!;
        var laneHeight = ArrangementPanel.AudioLaneHeight;
        var width = Math.Max(TotalWidth, ActualWidth);
        var target = plan.TrackIndex >= 0 && plan.TrackIndex < project.Tracks.Count ? project.Tracks[plan.TrackIndex] : null;
        var colour = target is null ? _theme.Accent : Readable(Parse(target.ColorHex, _theme.Accent));
        if (!plan.Valid)
        {
            var barWidth = Math.Max(40, WidthOfBar(Math.Clamp(BarAt(Math.Max(0, p.X)), 0, Math.Max(0, BarCount - 1))));
            var block = new Rect(Math.Max(0, p.X), p.Y - (laneHeight - 6) / 2, barWidth, laneHeight - 6);
            var reason = items.Count == 0 ? "Not an audio or MIDI file" : plan.Problem ?? "Cannot drop here";
            return new DropPreview(plan, block, null, colour, reason, "", null, Array.Empty<double>());
        }
        double laneTop;
        Rect? slot = null;
        string? slotLabel = null;
        if (plan.NewTrack)
        {
            var rowTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + ArrangementPanel.RowsHeight(project) - VerticalScrollOffset;
            var notation = plan.NewTrackKind is TrackKind.Drums or TrackKind.Keys ? ArrangementPanel.RowHeightFor(Project) : 0;   // a new audio track is lanes only
            slot = new Rect(0, rowTop, width, notation + laneHeight);
            slotLabel = $"New {plan.NewTrackKind switch { TrackKind.Drums => "drum", TrackKind.Keys => "keys", _ => "audio" }} track";
            laneTop = rowTop + notation;
        }
        else
        {
            laneTop = LaneTop(plan.TrackIndex, plan.Lane);
            if (plan.NewLane) { slot = new Rect(0, laneTop, width, laneHeight); slotLabel = "New lane"; }
        }
        var x1 = XOfSec(plan.StartSec);
        var x2 = XOfSec(plan.EndSec);
        var rect = new Rect(x1, laneTop + 3, Math.Max(6, x2 - x1), laneHeight - 6);
        var splits = plan.Clips.Count <= 1 ? Array.Empty<double>() : plan.Clips.Skip(1).Select(c => XOfSec(c.StartSec) - x1).ToArray();
        var label = plan.Clips.Count == 1 ? plan.Clips[0].Item.Name : $"{plan.Clips.Count} files";
        var seconds = plan.EndSec - plan.StartSec;
        var detail = (plan.Estimated ? "length on drop" : LengthText(seconds))
            + (plan.Clips.Count == 1 && plan.Clips[0].Item.Midi is { Musical: true } midi ? $" · {midi.LengthQuarters:0.##} beats" : "")
            + (slotLabel is null ? "" : $" · {slotLabel.ToLowerInvariant()}");
        return new DropPreview(plan, rect, slot, colour, label, detail, slotLabel, splits);
    }

    internal static string LengthText(double seconds) =>
        seconds < 60 ? $"{seconds:0.0} s" : $"{(int)(seconds / 60)}:{seconds % 60:00.0}";
}
