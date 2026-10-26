using TabForge.Avalonia.Views;
﻿using System;
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

/// <summary>
/// The arrangement timeline at rest, read-only: the bar ruler, the section strip, one row per track with a block per occupied bar, and the
/// playhead line. Geometry comes from the Core rules (<see cref="TimelineGeometryRules"/>, <see cref="ArrangementRowPresenter"/>,
/// <see cref="SectionLayout"/>, <see cref="TimelineActivityRules"/>); colours are the Instrument palette keys the timeline reads.
/// Also draws the interaction state it is given (hover, selection band, section and row drag previews) and answers the hit tests the input
/// controller asks (bar, row, section slot) from the same geometry.
/// Owns: the Avalonia drawing of that layout. Does not own: the rules, the pointer handling (TimelineInputController), the waveform and audio lanes, the
/// master lane's compact mode, group headers, the loop band and the glow, which the `TrackTimeline` draws and this view leaves out.
/// Tests: TabForge.Avalonia.Tests.TimelineViewTests, TimelineInputTests.
/// </summary>
public sealed partial class TimelineView : Control
{
    public const double RulerHeight = 44;
    public const double SectionHeight = 24;
    /// <summary>The track row height the settings start at; <see cref="RowHeight"/> is the row height now.</summary>
    public const double DefaultRowHeight = 34;
    /// <summary>The tallest a track row stretches (the rule: one and a half rows).</summary>
    internal const double MaxRowHeight = DefaultRowHeight * 1.5 * 1.2;

    /// <summary>The shortest base row: the default scaled by the Compact density (0.85).</summary>
    internal const double MinRowHeight = DefaultRowHeight * 0.85;
    /// <summary>The fewest track rows the timeline pane keeps when its splitter is dragged.</summary>
    public const int MinimumVisibleRows = 3;
    /// <summary>The least height of the timeline pane: the ruler, the sections, <see cref="MinimumVisibleRows"/> default rows and the Add track strip.</summary>
    public static double MinimumPaneHeight => RulerHeight + SectionHeight + MinimumVisibleRows * DefaultRowHeight + AddTrackLane.Height;
    /// <summary>Track row tint strength in percent, from the Appearance settings (0 = no tint). Set by ThemeSettingsBinding.</summary>
    public int TrackTintPercent { get; set; } = new TabForge.Services.AppearanceSettings().TrackTintPercent;
    /// <summary>Muted track ink dimming in percent, from the Appearance settings (0 = not dimmed). Set by ThemeSettingsBinding.</summary>
    public int MutedTrackDimPercent { get; set; } = new TabForge.Services.AppearanceSettings().MutedTrackDimPercent;
    private const double BarUnitWidth = 30;

    private double _baseRowHeight = DefaultRowHeight;
    private double _paneHeight;

    /// <summary>The base track row height from the Timeline settings; clamped between the default and <see cref="MaxRowHeight"/>. The rows stretch from it when the pane is taller than the content.</summary>
    public double BaseRowHeight
    {
        get => _baseRowHeight;
        set
        {
            var clamped = Math.Round(Math.Clamp(double.IsFinite(value) ? value : DefaultRowHeight, MinRowHeight, MaxRowHeight), 1);
            if (clamped == _baseRowHeight) return;
            _baseRowHeight = clamped;
            Reload(_song);
        }
    }

    /// <summary>The height the arrangement gives the rows and the strip (the pane less its compact strip); 0 when unknown, which keeps the base height.</summary>
    public double PaneHeight
    {
        get => _paneHeight;
        set
        {
            if (value == _paneHeight) return;
            _paneHeight = value;
            RefreshRowScroll();
        }
    }

    /// <summary>The height of one track row: the base height (the rows never stretch; the arrangement pane is capped at its content instead).</summary>
    public double RowHeight => _baseRowHeight;

    // The height above the track rows: the ruler, the section strip and the master lane.
    private double FixedRowsHeight => RulerHeight + SectionHeight + MasterLaneHeight;

    private double _zoomFactor = TimelineZoomRules.DefaultFactor;

    /// <summary>The horizontal zoom (see <see cref="TimelineZoomRules"/>): scales every bar's width; the bars are laid out again, the header is untouched.</summary>
    public double ZoomFactor
    {
        get => _zoomFactor;
        set
        {
            var clamped = TimelineZoomRules.Clamp(value);
            if (clamped == _zoomFactor) return;
            _zoomFactor = clamped;
            Relayout();
        }
    }

    /// <summary>The x of the right edge of the last bar, i.e. the laid-out width of all bars.</summary>
    public double BarsWidth => _boundaries[^1];
    internal const string Font = "Segoe UI";

    /// <summary>The snap settings a dragged clip snaps with (see <see cref="TimelineClipSnap"/>); null or disabled: no snapping. The arrangement sets it from its transport bar.</summary>
    public SnapSettings? Snap { get; set; }
    /// <summary>The track and lane an audio clip drag is moving into, when that track is not the clip's own; null otherwise. The timeline draws that lane highlighted (see <see cref="TimelineAudioClips"/>).</summary>
    public (TrackModel Track, int Lane)? ClipDropLane { get; set; }
    /// <summary>The fold of the clip being moved, computed once at the drag start: the slices and the clip start they were folded at. While set, the clip's fold is these slices shifted by the move, so the preview never re-folds at a repeat edge; null Slices = the clip started unfolded and stays one plain box. Null when no move drag.</summary>
    internal (AudioClip Clip, double Start, IReadOnlyList<RepeatSlice>? Slices)? DragFold { get; set; }
    /// <summary>The frozen fold shifted by the current move: the slices it came from, the shift and the result, so draw and hit test of one frame share one array (see <see cref="TimelineAudioClips.FoldOf"/>).</summary>
    internal (IReadOnlyList<RepeatSlice>? Source, double By, IReadOnlyList<RepeatSlice>? Shifted) DragFoldShifted;
    /// <summary>An audio-file drag over the timeline or the Add track strip: the planned landing (the track or a new one, the lane and the span),
    /// drawn as a track-coloured block (see <see cref="TimelineAudioClips"/>); null when no file drag is over. Set by the audio drop handler.</summary>
    public AudioFileDropPreview? FileDropPreview { get; set; }
    private readonly AvaloniaDrawContext _draw = new();
    private SongProject _song;
    private double[] _boundaries = Array.Empty<double>();
    private List<MarkerModel> _sections = new();
    // Display colour per section, index-aligned with _sections (SectionDisplayColours).
    private Rgba[] _sectionColours = System.Array.Empty<Rgba>();
    private Rows _rows;
    private bool _masterLaneShown;
    private bool _compact;
    private bool _showIndividualNotes;
    private bool _showContinuousBlocks;

    /// <summary>View > Show the notes in each track lane: an occupied bar draws its note marks. Off by default; turns <see cref="ShowContinuousBlocks"/> off.</summary>
    public bool ShowIndividualNotes
    {
        get => _showIndividualNotes;
        set { if (_showIndividualNotes == value) return; _showIndividualNotes = value; if (value) _showContinuousBlocks = false; InvalidateVisual(); }
    }

    /// <summary>View > Show one continuous line per track lane: each run of occupied bars draws one line. Turns <see cref="ShowIndividualNotes"/> off.</summary>
    public bool ShowContinuousBlocks
    {
        get => _showContinuousBlocks;
        set { if (_showContinuousBlocks == value) return; _showContinuousBlocks = value; if (value) _showIndividualNotes = false; InvalidateVisual(); }
    }

    /// <summary>The window's menu host: the timeline's and track row's right-click menus read their commands and CanPaste from it.</summary>
    public ContextMenuHost ContextMenus { get; }

    /// <summary>The waveform outlines drawn inside the audio clips of this timeline.</summary>
    public TimelineWaveforms Waveforms { get; }

    /// <summary>The media scope the shown song's audio is read in (its document's): the waveforms, their approvals and their retries all use it, as import and playback do.
    /// Until the window sets it the view reads in the anonymous scope, which cannot approve.</summary>
    public MediaContext Media { get; set; } = MediaContext.Anonymous;

    /// <summary>Raised when the user clicks a clip whose file waits for approval: the window opens its linked-audio review.</summary>
    public event Action? MediaApprovalRequested;

    /// <summary>A press on an audio clip whose waveform waits for approval asks for the approval (true: the press is used up).</summary>
    internal bool TryRequestApproval(Point at)
    {
        if (TimelineAudioClips.ClipAt(this, at) is not { Clip: { IsMidi: false } clip } || Waveforms.StatusOf(clip).State != WaveState.NeedsApproval) return false;
        if (MediaApprovalRequested is not { } requested) return false;
        requested();
        return true;
    }

    /// <param name="selection">The shown document's selection, which this timeline draws and routes edit keys by; the input controller rebinds it on a tab switch. Null (tests, a timeline with no document) gives it a selection of its own.</param>
    public TimelineView(SongProject song, ContextMenuHost? contextMenus = null, SelectionModel? selection = null, WaveformService? waveforms = null)
    {
        _song = song;
        FocusModel = selection ?? new SelectionModel();
        Focusable = true;   // the timeline owns Delete/arrows/Range keys while it has focus; the score keeps its own when it does
        ContextMenus = contextMenus ?? new ContextMenuHost();
        Waveforms = new TimelineWaveforms(AvaloniaUiThread.ForApplication(), () => InvalidateVisual(), () => Media, waveforms ?? WaveformCache.Default);
        LiveTakeLayer = new TimelineLiveTakeLayer(this) { Takes = LiveTakes };
        PlayheadLayer = new TimelinePlayheadLayer(this);
        SectionButtons = new SectionButtonFeedback(InvalidateVisual, AvaloniaUiThread.ForApplication());
        foreach (var layer in new Control[] { LiveTakeLayer, PlayheadLayer.NumberLayer, PlayheadLayer })
        {
            LogicalChildren.Add(layer);
            VisualChildren.Add(layer);
        }
        Reload(song);
        ContextMenuBuilder.Attach(this, ContextMenus, at => ContextMenuRows.TimelineAt(this, at));
        PointerMoved += (_, e) =>
        {
            SetHoverRow(RowAtY(e.GetPosition(this).Y));
            SetHoverClip(TimelineAudioClips.ClipIndexAt(this, e.GetPosition(this)));
            SetHoverMasterBar(MasterBarAt(e.GetPosition(this)));
        };
        PointerExited += (_, _) =>
        {
            SetHoverRow(-1);
            SetHoveredSection(null);
            SetHoverClip(null);
            SetHoverMasterBar(-1);
        };
    }

    /// <summary>The song the timeline draws.</summary>
    public SongProject Song => _song;

    /// <summary>One section block of the lane, in lane order, for the Core section-drag rules.</summary>
    public readonly record struct SectionSlot(int MarkerIndex, int FirstBar, double Left, double Top, double Width, double Height) : ISectionSlot;

    /// <summary>The bar under the pointer, or -1 (no hover).</summary>
    public int HoverBar { get; set; } = -1;

    /// <summary>The bar under the pointer on the master lane, or -1 when the pointer is not over it.</summary>
    public int HoverMasterBar { get; private set; } = -1;

    private int MasterBarAt(Point p)
    {
        var top = RulerHeight + SectionHeight;
        return MasterLaneHeight > 0 && p.Y >= top && p.Y < top + MasterLaneHeight ? BarAtX(p.X) : -1;
    }

    private void SetHoverMasterBar(int bar)
    {
        if (bar == HoverMasterBar) return;
        HoverMasterBar = bar;
        InvalidateVisual();
    }
    /// <summary>The row under the pointer, or -1.</summary>
    public int HoverRow { get; set; } = -1;

    /// <summary>The name column width of the track list beside this timeline; the header lays its columns out with it and the splitter sets it.</summary>
    public double TrackListNameWidth { get; set; } = TrackRowCells.NameWidth;

    /// <summary>The audio clip under the pointer as (track row, index in the track's clips); null when none. It is drawn lighter. UI thread only.</summary>
    public (int Row, int Index)? HoverClip { get; internal set; }
    /// <summary>The drag-select marquee on the lanes (from the press to the pointer) while a box drag runs, else null; drawn over everything.</summary>
    public global::Avalonia.Rect? Marquee { get; set; }
    /// <summary>The loop as the loop link last pushed it from SelectionModel (bars, and whether the Loop button repeats it, which draws it stronger); null: none.</summary>
    public (int Start, int End, bool Repeat)? LoopMarks { get; set; }
    /// <summary>The bars a ruler drag shows while it runs (the model takes them once, on release); null outside a drag.</summary>
    public (int Start, int End)? LoopPreview { get; set; }
    /// <summary>The drawn loop: the drag preview, else the pushed marks; -1 for none.</summary>
    public int LoopStart => LoopPreview?.Start ?? LoopMarks?.Start ?? -1;
    public int LoopEnd => LoopPreview?.End ?? LoopMarks?.End ?? -1;
    public bool LoopOn => LoopMarks?.Repeat == true;
    /// <summary>While a section drags: its lane position, its left x, and the packed x of every section (null otherwise).</summary>
    /// <summary>The hover, press and confirm-flash state of the section blocks (see <see cref="SectionButtonFeedback"/>).</summary>
    public SectionButtonFeedback SectionButtons { get; }

    /// <summary>The section under the pointer gets the hover shade; null clears it.</summary>
    public void SetHoveredSection(MarkerModel? section)
    {
        if (ReferenceEquals(section, SectionButtons.Hovered)) return;
        SectionButtons.SetHovered(section);
    }

    /// <summary>The section at <paramref name="markerIndex"/> of the timeline's bar order (the index a section slot carries), or null.</summary>
    public MarkerModel? SectionMarkerAt(int markerIndex) => markerIndex >= 0 && markerIndex < _sections.Count ? _sections[markerIndex] : null;

    public int DragSection { get; set; } = -1;
    public double DragSectionX { get; set; }
    public double[]? SectionPreviewX { get; set; }
    /// <summary>While a section drags: the x each section slot rests at once it lands, and the x of the drop line (null / ignored otherwise).</summary>
    public double[]? SectionRestingX { get; set; }
    public double SectionDropX { get; set; }
    /// <summary>While a row drags: its index and every row's y offset (null otherwise).</summary>
    public int DragRow { get; set; } = -1;
    public double[]? RowOffsets { get; set; }

    /// <summary>Rebuilds the layout from <paramref name="song"/> after an edit changed it.</summary>
    public void Reload(SongProject song)
    {
        var songChanged = !ReferenceEquals(_song, song);
        _song = song;
        if (songChanged) RepeatShown.Reset();
        else RepeatShown.Prune(RepeatRegionsNow());
        // The cursor is kept by track id: a replaced song (undo) holds new TrackModel objects, so it is looked up again.
        if (LaneCursor is { } kept)
            LaneCursor = new TimelineLaneCursor(kept.Track, kept.Lane, kept.Sec, kept.Position is { } at && at.WrittenBar < song.Tracks.Select(t => t.Measures.Count).DefaultIfEmpty(0).Max() ? at : null).ResolveIn(song);
        if (LaneCursor is { } resolved && !resolved.InsideClip()) LaneCursor = null;   // the clip it sat in was deleted or moved away
        PickerPopoverRegion = null;
        _activityCache = song.Tracks.Select(track => new TimelineActivityRules.CacheEntry[track.Measures.Count]).ToArray();
        _masterLaneActivity = MasterLaneStrip.Activity(song);
        _activityGeneration++;
        _trackColours.Clear();
        ReloadCount++;
        var bars = LayoutBars(song);
        _sections = SectionLayout.Sorted(song);
        _sectionColours = SectionDisplayColours.Resolve(_sections, _matchSimilarSectionColours);
        _rows = new Rows(_compact ? 0 : song.Tracks.Count, MasterLaneHeight, RowHeight, song.Tracks, this);
        Width = _boundaries[bars] + GhostWidth;
        RefreshRowScroll();
        RowsChanged?.Invoke();
        InvalidateVisual();
        PlayheadLayer.Redraw();
        LiveTakeLayer.InvalidateVisual();
    }

    /// <summary>How many times <see cref="Reload"/> has run; a zoom only re-lays the bars out (<see cref="Relayout"/>), so a test can tell the two apart.</summary>
    internal int ReloadCount { get; private set; }

    /// <summary>How many times a zoom has re-laid the bars out without a reload.</summary>
    internal int RelayoutCount { get; private set; }

    // Computes the bar x positions at the current zoom; returns the bar count.
    private int LayoutBars(SongProject song)
    {
        var bars = 0;
        foreach (var track in song.Tracks) bars = Math.Max(bars, track.Measures.Count);
        _boundaries = new double[bars + 1];
        for (var bar = 0; bar < bars; bar++)
            _boundaries[bar + 1] = _boundaries[bar] + TimelineGeometryRules.BarWidth(BarUnitWidth * _zoomFactor, MusicTime.BarSlots(song, bar));
        return bars;
    }

    // The zoom changed and nothing else: only the x positions, the width and the layers that draw at them are redone (no clip, waveform, fold or activity rebuild).
    private void Relayout()
    {
        RelayoutCount++;
        var bars = LayoutBars(_song);
        Width = _boundaries[bars] + GhostWidth;
        InvalidateVisual();
        PlayheadLayer.Redraw();
        LiveTakeLayer.InvalidateVisual();
    }

    /// <summary>Compact mode: the track rows and the "Add track" strip are hidden and the master lane shows at double height.</summary>
    public bool Compact
    {
        get => _compact;
        set { if (_compact == value) return; _compact = value; Reload(_song); }
    }

    /// <summary>The overview lane above track 1 (one thin line per track, filled where the track plays); off by default, as in the timeline.</summary>
    public bool MasterLaneShown
    {
        get => _masterLaneShown;
        set { if (_masterLaneShown == value) return; _masterLaneShown = value; Reload(_song); }
    }

    /// <summary>Height the master lane takes between the section strip and track 1 (0 when hidden).</summary>
    public double MasterLaneHeight => MasterLaneRules.Height(MasterLaneRules.Rows((_masterLaneShown || _compact) && _song.Tracks.Count > 0, _compact), _baseRowHeight);

    /// <summary>
    /// The ink for a section label. The window draws these labels white, so on the dark theme the light ink is kept unless it would be illegible
    /// on a genuinely light chip (a near-white colour, or one the person picked exactly, which skips the taming), where the dark ink is the only
    /// readable choice. The light theme keeps the WCAG pick, whose dark ink is the readable one on its chips: no fill there is dark enough for
    /// the light ink's bias to matter.
    /// </summary>
    public static UiColor LabelInk(UiColor section, UiColor background)
    {
        return SectionLabelWhite;   // always white in both themes and on every fill; the hairline outline (SectionLabelOutline) keeps it readable on pale chips
    }

    /// <summary>The section label ink (white) and its hairline dark-grey glyph outline.</summary>
    public static readonly UiColor SectionLabelWhite = new(0xFF, 0xFF, 0xFF, 0xFF);
    public static readonly UiStroke SectionLabelOutline = new(ThemeColours.Blend(ThemeColours.InkDark, ThemeColours.InkLight, 0.1), 0.75);

    /// <summary>The contrast the light ink must reach on the fill to be kept there (WCAG's graphics / large-text minimum); below it the dark ink is taken.</summary>
    private const double MinLightInkContrast = 3;

    private const double SectionFillOpacity = 0.68;

    /// <summary>The opacity of a section chip's fill: the section's own colour whole on the light theme, 0.68 over the dark one.</summary>
    private static double SectionFillOf(bool light) => light ? 1 : SectionFillOpacity;

}
