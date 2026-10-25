using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Audio;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the timeline's hit-testing, geometry, clip selection and row height queries.
// Does not own: the rest of the class (see the main file).
// Tests: no dedicated test class.
public sealed partial class TimelineView
{
    public int BarAtX(double x) => TimelineGeometryRules.BarAt(_boundaries, x);

    /// <summary>Raised when <see cref="HoverRow"/> changes, so the track header column repaints its row too.</summary>
    public event Action? HoverRowChanged;

    /// <summary>Raised when the rows were rebuilt (a new stretch, height or track list): the header column measures and arranges again at once.</summary>
    public event Action? RowsChanged;

    private void SetHoverClip((int Row, int Index)? clip)
    {
        if (clip == HoverClip) return;
        HoverClip = clip;
        // A clip whose file could not be read says why on hover.
        var failed = clip is { } hit && hit.Row < _song.Tracks.Count && hit.Index < _song.Tracks[hit.Row].AudioClips.Count && _song.Tracks[hit.Row].AudioClips[hit.Index] is { IsMidi: false } audio
            && Waveforms.StatusOf(audio).State == WaveState.Failed;
        global::Avalonia.Controls.ToolTip.SetTip(this, failed ? TimelineWaveforms.FailedTip : null);
        InvalidateVisual();
    }

    private void SetHoverRow(int row)
    {
        if (row == HoverRow) return;
        HoverRow = row;
        InvalidateVisual();
        HoverRowChanged?.Invoke();
    }

    /// <summary>The track row under <paramref name="y"/> (scrolled), or -1 over the ruler, sections, master lane, Add track strip and below the rows.</summary>
    public int RowAtY(double y) => y < TrackRowsTop || y >= TrackRowsBottom ? -1 : ArrangementRowPresenter.RowIndexAt(_rows, y - RulerHeight - SectionHeight + _rowScrollOffset);

    /// <summary>The tab cell (track row, bar) at <paramref name="x"/>, <paramref name="y"/>, or null over anything else (the ruler, a section, a lane band, a track without notation, the empty grid).</summary>
    public (int Row, int Bar)? TabCellAt(double x, double y)
    {
        var row = RowAtY(y);
        var bar = row >= 0 && IsOverNotationBand(y, row) ? BarAtX(x) : -1;
        return bar >= 0 && bar < BarCount && row < _song.Tracks.Count && _song.Tracks[row].HasNotation ? (row, bar) : null;
    }

    /// <summary>Whether <paramref name="y"/> is over the tab (notation band) of <paramref name="row"/>; the audio lanes and the armed strip in them are not the tab.</summary>
    public bool IsOverNotationBand(double y, int row) => row >= 0 && row < _rows.Count && y >= RowTop(row) && y < RowTop(row) + NotationHeightOf(row);

    /// <summary>Shows the hover row under <paramref name="y"/> (-1 clears it); the header column calls it for its pointer.</summary>
    public void HoverRowAtY(double y) => SetHoverRow(RowAtY(y));

    /// <summary>Where a resize drag shows its section's edges before the release: the sorted section index and the bars it would span (no model change until the drop).</summary>
    internal (int Index, int Start, int End)? SectionResizePreview { get; set; }

    /// <summary>The first bar and the exclusive end bar of section <paramref name="i"/>, as drawn (a resize in progress shows its preview).</summary>
    private (int Start, int End) SectionBars(int i) =>
        SectionResizePreview is { } p && p.Index == i ? (p.Start, p.End) : (Math.Clamp(_sections[i].MeasureIndex, 0, BarCount), SectionLayout.End(_sections, i, BarCount));

    internal int SectionEndBar(int i) => SectionLayout.End(_sections, i, BarCount);

    /// <summary>The section edge knob under (<paramref name="x"/>, <paramref name="y"/>): the section's sorted index and which end, the nearest one when two knobs overlap; null elsewhere.</summary>
    internal (int Index, bool Right)? SectionKnobAt(double x, double y)
    {
        if (DragSection >= 0 || SectionResizePreview is not null || y >= RulerHeight + SectionKnobs.HitInto) return null;
        (int, bool)? best = null;
        var bestReach = double.MaxValue;
        for (var i = 0; i < _sections.Count; i++)
        {
            var start = Math.Clamp(_sections[i].MeasureIndex, 0, BarCount);
            var end = SectionLayout.End(_sections, i, BarCount);
            if (end <= start) continue;
            var left = BarX(start) + 1;
            var right = BarX(end) - 3;   // the chip's edges as DrawSections draws them
            if (SectionKnobs.Reach(left, x, y, RulerHeight) is { } l && l < bestReach) { bestReach = l; best = (i, false); }
            if (SectionKnobs.Reach(right, x, y, RulerHeight) is { } r && r < bestReach) { bestReach = r; best = (i, true); }
        }
        return best;
    }

    public SectionSlot[] SectionSlots()
    {
        var slots = new List<SectionSlot>();
        for (var i = 0; i < _sections.Count; i++)
        {
            var start = Math.Clamp(_sections[i].MeasureIndex, 0, BarCount);
            var end = SectionLayout.End(_sections, i, BarCount);
            if (end > start) slots.Add(new SectionSlot(i, start, BarX(start) + 1, RulerHeight + 1, Math.Max(2, BarX(end) - BarX(start) - 2), SectionHeight - 4));
        }
        return slots.ToArray();
    }

    private int _selectedRow = -1;

    /// <summary>The row of the selected track (the document's TrackIndex); its lane is tinted and the playhead arrow sits on it. Redraws only on change.</summary>
    public int SelectedRow
    {
        get => _selectedRow;
        set { if (_selectedRow == value) return; _selectedRow = value; InvalidateVisual(); PlayheadLayer.Redraw(); }
    }

    public int BarCount => _boundaries.Length - 1;
    public int RowCount => _rows.Count;
    public int SectionCount => _sections.Count;
    public double BarX(int bar) => TimelineGeometryRules.XOfBar(_boundaries, bar);

    /// <summary>The x of a song time in seconds: the bar that holds it, at the same fraction of the bar's width as the playhead uses.</summary>
    public double XOfSeconds(double seconds)
    {
        var (bar, fraction) = AudioClipTimelineRules.BarAtSeconds(_song, seconds);
        if (bar >= BarCount) return ContinuesPastEnd ? ExtraXOfSeconds(seconds) : _boundaries[^1];
        var left = BarX(bar);
        return left + (_boundaries[bar + 1] - left) * fraction;
    }

    /// <summary>The song time in seconds under <paramref name="x"/>: the inverse of <see cref="XOfSeconds"/>, within the bars drawn.</summary>
    public double SecondsOfX(double x)
    {
        if (BarCount == 0) return 0;
        if (x > _boundaries[^1] && ContinuesPastEnd) return ExtraSecondsOfX(x);
        var bar = Math.Clamp(BarAtX(x), 0, BarCount - 1);
        var left = BarX(bar);
        var width = _boundaries[bar + 1] - left;
        return AudioClipTimelineRules.SecondsAtBar(_song, bar, width > 0 ? (x - left) / width : 0);
    }
    /// <summary>The audio clips the timeline has selected (the white outlines), in the order they were added; the selection model holds them.</summary>
    public IReadOnlyList<TimelineClipSelection> SelectedClips => FocusModel.Clips;
    /// <summary>True when <paramref name="clip"/> is one of the selected clips.</summary>
    public bool IsClipSelected(AudioClip clip)
    {
        var clips = FocusModel.Clips;
        for (var i = 0; i < clips.Count; i++)
            if (ReferenceEquals(clips[i].Clip, clip)) return true;
        return false;
    }
    /// <summary>Ctrl+click: adds the clip to the selected clips, or takes it out when it is already one of them.</summary>
    public void ToggleClip(TimelineClipSelection clip)
    {
        var next = FocusModel.Clips.Where(c => !ReferenceEquals(c.Clip, clip.Clip)).ToList();
        var added = next.Count == FocusModel.Clips.Count;
        if (added) next.Add(clip);
        FocusModel.SetClips(next);
        if (added) FocusModel.SetFocus(FocusTarget.Clips);
        InvalidateVisual();
    }
    /// <summary>Selects exactly these clips (a paste or duplicate leaves its copies selected); the edit keys go to the clips when there are any.</summary>
    public void SelectClips(IEnumerable<TimelineClipSelection> clips)
    {
        FocusModel.SetClips(clips);
        if (FocusModel.Clips.Count > 0) FocusModel.SetFocus(FocusTarget.Clips);
        InvalidateVisual();
    }
    /// <summary>The first selected clip (the one the clip keys act on) or null; setting it selects that clip alone (null: none).</summary>
    public TimelineClipSelection? SelectedClip
    {
        get => FocusModel.Clips.Count > 0 ? FocusModel.Clips[0] : null;
        set
        {
            var newlySelected = value is not null && SelectedClip?.Clip.Id != value.Clip.Id;
            FocusModel.SetClips(value is null ? Array.Empty<TimelineClipSelection>() : new[] { value });
            if (newlySelected) FocusModel.SetFocus(FocusTarget.Clips);   // selecting a clip puts the edit keys on clips (a restore re-selecting the same clip keeps the focus)
            InvalidateVisual();
        }
    }
    /// <summary>Which pass of each repeat region the timeline shows (the pill picker's choice per region, default pass 1).
    /// View state only: never saved, never undone. Reset when the song reloads.</summary>
    public RepeatViewState RepeatShown { get; } = new();
    /// <summary>The repeat map and clip folds of the performed order, kept between frames.</summary>
    internal TimelineFoldCache Folds { get; } = new();
    /// <summary>The repeat region whose pass popover floats above its lane (all its pills), or null when it is closed.
    /// S3 opens and closes it; S2 only draws it.</summary>
    public RepeatRegion? PickerPopoverRegion { get; set; }
    /// <summary>The repeat regions of the song now, in bar order (derived from the performed bar order).</summary>
    public IReadOnlyList<RepeatRegion> RepeatRegionsNow() => Folds.MapOf(SongExtent.Timing(Song).Performed).Regions;
    public double RowTop(int row) => RulerHeight + SectionHeight + ArrangementRowPresenter.RowTop(_rows, row) - _rowScrollOffset;

    /// <summary>
    /// Every track as one row: no collapsed groups, no master lane, no audio lanes. An armed track that has no lane bands yet takes the armed sub-row
    /// under its base band (<see cref="ArmedRowPainter.SubRowHeight"/>); once it has lanes the strip draws inside the last lane band and the row does
    /// not grow. Read from the song at each layout, so the rows below it move with it.
    /// </summary>
    private readonly struct Rows : IArrangementRowSource
    {
        public Rows(int count, double topOffset, double rowHeight, IReadOnlyList<TrackModel> tracks, TimelineView view)
        {
            Count = count;
            TopOffset = topOffset;
            _rowHeight = rowHeight;
            _tracks = tracks;
            _view = view;
        }
        private readonly double _rowHeight;
        private readonly IReadOnlyList<TrackModel> _tracks;
        private readonly TimelineView _view;
        public int Count { get; }
        public double TopOffset { get; }
        public double HeaderHeight => 0;
        public double HeightOf(int index)
        {
            if (index < 0 || index >= _tracks.Count) return _rowHeight;
            var track = _tracks[index];
            var lanes = _view.LaidOutLaneCountOf(track);
            var notation = track.IsAudio && lanes > 0 ? 0 : _rowHeight;
            var armed = track.RecordArm && lanes == 0 ? ArmedRowPainter.SubRowHeight : 0;
            return notation + lanes * ClipLanes.AudioLaneHeight + armed + _view.LiveStackHeightOf(index);
        }
        public bool StartsGroup(int index) => false;
    }

    /// <summary>
    /// The lane bands a track lays out, including the one a live recording take needs before it lands. Recording never writes
    /// <see cref="TrackModel.Lanes"/>: only this layout and the overlay see the extra lane until the landing edit stores it.
    /// </summary>
    internal int LaidOutLaneCountOf(TrackModel track)
    {
        var laidOut = ClipLanes.LaidOutCount(track);
        // A live recording take grows its lane before it lands, and keeps it after a mid-take disarm until the global stop.
        return Math.Max(laidOut, LiveTakes.LaneCountOf(track));
    }

    /// <summary>Live laps are full lane bands counted in <see cref="LaidOutLaneCountOf"/>; nothing stacks below the lanes (always 0).</summary>
    internal double LiveStackHeightOf(int row) =>
        0;

    /// <summary>
    /// The height an armed track adds under its base row band (the armed sub-row); 0 for an unarmed track, a track that already has lane bands
    /// (its strip draws inside the last lane band), or an index outside the tracks.
    /// </summary>
    private double ExtraHeightOf(int index)
    {
        if (index < 0 || index >= _song.Tracks.Count) return 0;
        var track = _song.Tracks[index];
        var armed = track.RecordArm && LaidOutLaneCountOf(track) == 0 ? ArmedRowPainter.SubRowHeight : 0;
        return armed + LiveStackHeightOf(index);
    }

    /// <summary>Whether the track at <paramref name="row"/> is armed, so its row draws the armed strip (inside its last lane band, or in the sub-row when it has no lanes).</summary>
    public bool IsArmed(int row) => row >= 0 && row < _song.Tracks.Count && _song.Tracks[row].RecordArm;

    /// <summary>The base band of a track's row: its notation band (see <see cref="ClipLanes.NotationBandHeight"/>) plus its lane bands; an armed track's strip sits inside the last lane band, or below this band when it has no lanes.</summary>
    public double BaseBandHeightOf(int row)
    {
        if (row < 0 || row >= _song.Tracks.Count) return RowHeight;
        var track = _song.Tracks[row];
        var lanes = LaidOutLaneCountOf(track);
        return (track.IsAudio && lanes > 0 ? 0 : RowHeight) + lanes * ClipLanes.AudioLaneHeight;
    }

    /// <summary>Whether the track at <paramref name="row"/> is one of the header-selected rows (the selection model's Rows).</summary>
    internal bool IsRowPicked(int row) => row >= 0 && row < _song.Tracks.Count && FocusModel.Rows.Contains(_song.Tracks[row].Id);

    /// <summary>The top of a track's lane bands in layout coordinates: below its notation band (none for an audio track with lanes).</summary>
    public double LaneBandTopOf(int row) => RowTop(row) + NotationHeightOf(row);

    /// <summary>The top of one lane band: the lane bands' top plus the lane index.</summary>
    public double LaneBandTopOf(int row, int lane) =>
        LaneBandTopOf(row) + (row >= 0 && row < _song.Tracks.Count ? Math.Max(0, lane) * ClipLanes.AudioLaneHeight : lane * ClipLanes.AudioLaneHeight);

    /// <summary>The whole height of a track's row: the base band, plus the armed sub-row when the track is armed and has no lane bands.</summary>
    public double RowHeightOf(int row) => BaseBandHeightOf(row) + ExtraHeightOf(row);

    /// <summary>The notation band of the track at <paramref name="row"/>; <see cref="RowHeight"/> outside the tracks.</summary>
    internal double NotationHeightOf(int row)
    {
        if (row < 0 || row >= _song.Tracks.Count) return RowHeight;
        var track = _song.Tracks[row];
        return track.IsAudio && LaidOutLaneCountOf(track) > 0 ? 0 : RowHeight;
    }
}
