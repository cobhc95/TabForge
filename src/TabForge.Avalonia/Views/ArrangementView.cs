using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The arrangement: the transport bar in the top slot of the track header column, beside the ruler and section strip of the bar timeline. The track rows scroll down together inside it
/// (TimelineView.RowScrollOffset, driven by the TrackRowScrollBar); only the timeline scrolls sideways, so the headers stay in view. Rows align because the header reads its row positions from the timeline's Core row geometry.
/// Owns: the view's behaviour — the subscriptions on the controls, attach/detach, playback following, the wheel, the splitter drag and the inline track name editor. The control tree
/// and its layout are built by <see cref="ArrangementComposition"/>; the row stack (row source, armed rows, row scroll bar, natural max height, selected-row reload) is
/// <see cref="ArrangementTrackRows"/>. Does not own: drawing, gestures or edits (TimelineView, TimelineInputController and the command hosts), the toolbar and group headers of the
/// the arrangement panel, the track list's reload (ArrangementRowSource) and the top band's layout (ArrangementTopBand).
/// Tests: TabForge.Avalonia.Tests.ArrangementViewTests.
/// </summary>
public sealed partial class ArrangementView : UserControl
{
    /// <param name="zoomSettleTimer">Fires once when a wheel zoom has stopped (150 ms after the last tick); null makes the real one when first needed (tests pass a fake).</param>
    /// <param name="meterTick">The app's shared meter tick the armed rows' level poll rides on; null gives the view its own.</param>
    /// <param name="requestFrame">Runs the callback it is given on the next display frame; null uses the window's animation frame (tests pass a fake).</param>
    public ArrangementView(SongProject song, IEditableDocument document, ContextMenuHost? contextMenus = null, IUiTimer? zoomSettleTimer = null, Action<Action>? requestFrame = null, SharedMeterTick? meterTick = null)
    {
        _zoomSettle = zoomSettleTimer;
        _requestFrame = requestFrame ?? RequestDisplayFrame;
        var parts = ArrangementComposition.Build(this, song, document, contextMenus, meterTick);
        Timeline = parts.Timeline;
        Header = parts.Header;
        _trackRows = parts.TrackRows;
        Input = parts.Input;
        _names = parts.Names;
        _sideways = parts.Sideways;
        _topBand = parts.TopBand;
        _listSplitter = parts.ListSplitter;
        Content = parts.Content;
        _document = document;
        // A knob drag changes many values within one frame; the master row repaints once for the latest value, like the mixer's live pushes.
        _headerRepaint = new NextFrameCoalescer(AvaloniaUiThread.ForApplication(), Header.InvalidateVisual);

        // The behaviour wiring stays in the view: every subscription below runs a handler this class owns on the controls the composition built.
        _trackRows.RowsCountChanged += () => TrackRowsCountChanged?.Invoke();
        Input.TrackRowClicked += SelectTrackRow;
        Timeline.ClipDragPreviewChanged += ShowClipDragOnStrip;
        Timeline.NewTrackDropAvailable = AddTrackBar.IsVisible;
        // Arming from the track row's record cell changes the rows (the armed sub-row), and the timeline is not marked for that edit, so the rows reload here.
        Input.TrackArmToggled += row => { _trackRows.ReloadRows(); TrackArmChanged?.Invoke(row); };
        _trackRows.WatchTimeline();
        AddTrackBar.Clicked += OnAddTrackClicked;
        Header.DoubleTapped += (_, e) =>
        {
            var row = Header.NameRowAt(e.GetPosition(Header));
            if (row >= 0) BeginTrackRename(row);
        };
        FooterScrollBar.ValueChanged += (_, _) => { if (_syncingFooterBar) return; NoteHandScroll(); _sideways.Offset = new Vector(FooterScrollBar.Value, _sideways.Offset.Y); };
        _sideways.ScrollChanged += (_, _) => { FitTimelineToViewport(_sideways); SyncFooterScrollBar(); SidewaysScrolled?.Invoke(); };
        // A bar click seeks the playback, then pages the view if the bar is at an edge (the click still selects the bar in the input controller).
        // While this window is capturing, a ruler or lane click never moves the transport: the take stays anchored where recording began.
        void SeekToBar(int bar)
        {
            if (Transport.Recording) return;
            _playback?.Seek(bar, 0);
            TimelineFollowScroll.PageForEdgeBar(_sideways, Timeline, bar);
        }
        Input.BarSeekRequested += SeekToBar;
        if (contextMenus is not null) contextMenus.MovePlayhead = SeekToBar;   // the ruler menu's Move playhead here
        // A pass-aware click seeks the repeat pass; pass 1 keeps the old bar/cell seek, so that path is byte-identical.
        Input.PositionSeekRequested += position =>
        {
            if (Transport.Recording) return;
            if (position.Pass > 1) _playback?.Seek(position);
            else _playback?.Seek(position.WrittenBar, 0);
            if (_playback is not null) Timeline.SetPlayhead(position.WrittenBar, position.Fraction, position.Pass);
            TimelineFollowScroll.PageForEdgeBar(_sideways, Timeline, position.WrittenBar);
        };
        // Tunnelled from the arrangement itself, so a wheel over the lanes is seen whichever child the pointer hits (an enclosing viewer may take it first).
        AddHandler(PointerWheelChangedEvent, OnArrangementWheel, RoutingStrategies.Tunnel);
        // Touch pinch; a Windows precision touchpad's pinch arrives as Ctrl + wheel, which the wheel handler already zooms.
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(Gestures.PinchEvent, OnPinch);
        AddHandler(Gestures.PinchEndedEvent, (_, _) => _lastPinchScale = 1);
        _listSplitter.DragStarted += BeginTrackListDrag;
        _listSplitter.DragMoved += MoveTrackListDrag;
        _listSplitter.DragEnded += EndTrackListDrag;
        _trackRows.WatchLayout();
        Transport.BindCommands(contextMenus);
        // The master row's volume is the transport's master knob: the row reads it at each paint and repaints as it moves.
        Header.MasterVolumePercent = () => (int)Math.Round(Transport.MasterVolumeKnob.Value);
        Transport.MasterVolumeKnob.ValueChanged += (_, _) => _headerRepaint.Request();
        AttachedToVisualTree += (_, _) => { _attached = true; _trackRows.Watch(_document); Input.Watch(); SubscribePlayhead(true); FooterScrollBar.IsVisible = true; SyncFooterScrollBar(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; StopZoomSettle(); _trackRows.Unwatch(); Input.Unwatch(); SubscribePlayhead(false); FooterScrollBar.IsVisible = false; };
    }

    private IEditableDocument _document;

    /// <summary>The width of the divider between the track list and the timeline: one line.</summary>
    public const double DividerWidth = 1;

    // The track list width when the splitter drag began; the drag distance is added to it.
    private double _dragStartListWidth;

    /// <summary>
    /// The "Add track" strip: one band under the track list and the timeline, the row below the rows. Its IsVisible follows the View setting
    /// (<see cref="AddTrackLaneShown"/>) and compact mode; its height is the strip's, so the track rows end at its top.
    /// </summary>
    public AddTrackBar AddTrackBar { get; } = new();

    private bool _addTrackLaneShown = true;

    /// <summary>Whether the "Add track" strip shows under the track rows (the View setting); compact mode hides it.</summary>
    public bool AddTrackLaneShown
    {
        get => _addTrackLaneShown;
        set { _addTrackLaneShown = value; ApplyAddTrackBarVisibility(); }
    }

    private void ApplyAddTrackBarVisibility()
    {
        AddTrackBar.IsVisible = _addTrackLaneShown && !_topBand.Compact;
        Timeline.NewTrackDropAvailable = AddTrackBar.IsVisible;
        InvalidateMeasure();
    }

    /// <summary>The height the "Add track" strip takes under the track rows (0 when hidden).</summary>
    public double AddTrackBarHeight => _topBand.AddTrackBarHeight;

    /// <summary>Raised when a splitter drag ends, with the track list width it ended at (the window saves it).</summary>
    public event System.Action<double>? TrackListWidthCommitted;

    /// <summary>Sets the track list width, clamped to the minimum and to the pane's maximum share; the header and the timeline follow at once.</summary>
    public void SetTrackListWidth(double listWidth)
    {
        var clamped = TrackListWidthRules.ClampListWidth(listWidth, Bounds.Width);
        Timeline.TrackListNameWidth = TrackListWidthRules.NameWidthFor(clamped);
        Header.InvalidateMeasure();
        Header.InvalidateVisual();
        InvalidateMeasure();   // the transport's width and the columns' top band follow the list width
    }

    // The columns' top band (the transport above the header labels) is set before the children are measured, so the header and the timeline
    // measure the same rows.
    private readonly ArrangementTopBand _topBand;

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 0;
        _topBand.Layout(height);
        return base.MeasureOverride(availableSize);
    }

    // The pane can be arranged shorter than it was measured (the dock splits its group after measuring), so the rows take the arranged height:
    // the pinned Add track strip then ends at the pane's bottom, above the status bar, at its full height.
    protected override Size ArrangeOverride(Size finalSize)
    {
        _topBand.Layout(finalSize.Height);
        return base.ArrangeOverride(finalSize);
    }

    /// <summary>The height the transport bar takes above the header's labels (see <see cref="ArrangementTopBand.TopBandHeight"/>).</summary>
    public double TopBandHeight => _topBand.TopBandHeight;

    /// <summary>The pane's height with every row at base height (see <see cref="ArrangementTopBand.NaturalHeight"/>); the dock caps the pane at it.</summary>
    public double NaturalHeight => _topBand.NaturalHeight;

    private readonly TabForge.Avalonia.Views.Docking.DockSplitter _listSplitter;

    /// <summary>The footer bar's height and the gap either side of it (the timeline scrollbar: 16 px, 6 px margin).</summary>
    public const double SidewaysBarHeight = 16;
    public const double SidewaysBarSideMargin = 6;

    /// <summary>The footer bar's large step as a share of the view's width, and its small step in DIPs (the usual values).</summary>
    private const double FooterLargeChangeShare = 0.8, FooterSmallChange = 24;

    /// <summary>The smallest large step the footer bar takes: one DIP, so a narrow pane still pages.</summary>
    private const double FooterLargeChangeMin = 1;

    private bool _syncingFooterBar;

    /// <summary>
    /// The timeline's horizontal scroll bar, the footer bar: the status bar hosts it. It is two-way synced with the timeline's offset and shows only
    /// while this pane is attached (closing the pane hides it).
    /// </summary>
    public ScrollBar FooterScrollBar { get; } = new()
    {
        Name = "TimelineScrollBar",
        Orientation = Orientation.Horizontal,
        Height = SidewaysBarHeight,
        Margin = new Thickness(SidewaysBarSideMargin, 0, SidewaysBarSideMargin, 0),
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };

    // The timeline is never narrower than the viewer, so short songs draw their empty grid out to the pane's edge.
    private void FitTimelineToViewport(ScrollViewer sideways)
    {
        var width = Math.Max(0, sideways.Viewport.Width);
        if (Timeline.MinWidth != width) Timeline.MinWidth = width;
    }

    // The footer bar follows the viewer's extent, viewport and offset; its own value changes write back through the value handler.
    // Every assignment is equality-checked: a scroll tick that leaves a value unchanged does no dependency-property work.
    private void SyncFooterScrollBar()
    {
        var viewport = Math.Max(0, _sideways.Viewport.Width);
        var maximum = Math.Max(0, _sideways.Extent.Width - viewport);
        var value = Math.Clamp(_sideways.Offset.X, 0, maximum);
        var large = Math.Max(FooterLargeChangeMin, viewport * FooterLargeChangeShare);
        _syncingFooterBar = true;
        try
        {
            if (FooterScrollBar.Minimum != 0) FooterScrollBar.Minimum = 0;
            if (FooterScrollBar.Maximum != maximum) FooterScrollBar.Maximum = maximum;
            if (FooterScrollBar.ViewportSize != viewport) FooterScrollBar.ViewportSize = viewport;
            if (FooterScrollBar.LargeChange != large) FooterScrollBar.LargeChange = large;
            if (FooterScrollBar.SmallChange != FooterSmallChange) FooterScrollBar.SmallChange = FooterSmallChange;
            if (FooterScrollBar.Value != value) FooterScrollBar.Value = value;
        }
        finally { _syncingFooterBar = false; }
    }

    /// <summary>Starts a splitter drag: the list width at the press is the base the drag distance is added to.</summary>
    public void BeginTrackListDrag() => _dragStartListWidth = Header.ColumnWidth;

    /// <summary>Moves the list edge by the drag distance from the press.</summary>
    public void MoveTrackListDrag(double distance) => SetTrackListWidth(_dragStartListWidth + distance);

    /// <summary>Ends a splitter drag and reports the width it ended at.</summary>
    public void EndTrackListDrag() => TrackListWidthCommitted?.Invoke(Header.ColumnWidth);
    private AvaloniaPlaybackSession? _playback;
    private readonly TrackNameEditor _names;
    private readonly ArrangementTrackRows _trackRows;
    private readonly NextFrameCoalescer _headerRepaint;

    /// <summary>
    /// Raised when a track or a group row is added or removed, or a row's height changes (an armed track's sub-row), so the window re-fits the track
    /// list to its rows (it fits on track changes, as it does for the master lane).
    /// </summary>
    public event Action? TrackRowsCountChanged;

    /// <summary>Raised (with the track's row) after the track row's record cell armed or disarmed a track and the rows reloaded, so the window can grow the pane to show the armed sub-row.</summary>
    public event Action<int>? TrackArmChanged;

    /// <summary>The confirm flash on a section block after a jump to it (see <see cref="SectionButtonFeedback"/>).</summary>
    public void FlashSection(MarkerModel section) => Timeline.SectionButtons.Flash(section);

    /// <summary>Opens the inline name editor on the track at <paramref name="row"/> of the shown song (the track row menu's Rename row, or a double-click on the name).</summary>
    public void BeginTrackRename(int row)
    {
        Timeline.EnsureRowVisible(row);
        _names.Begin(_document, row);
    }

    /// <summary>A press on a track row without a drag selects that track: the document's TrackIndex, then the timeline and its header follow.</summary>
    private void SelectTrackRow(int row)
    {
        if (_document is not SongDocument song || row < 0 || row >= song.Project.Tracks.Count) return;
        song.TrackIndex = row;
        // The track row's keyboard commands (TrackRow.*) act on the selected row through the same host field the right-click menu uses.
        // A Ctrl+click that dropped the clicked track from the selection leaves the TrackRow.* keys on the tracks still selected, never on the one just deselected.
        var picked = song.Selection.Tracks.Rows(song.Project.Tracks);
        Timeline.ContextMenus.ClickedTrack = Timeline.TrackRowFocused && picked.Count > 0 && !picked.Contains(row) ? picked[^1] : row;
        _trackRows.SyncSelectedRow();
    }


    /// <summary>
    /// Scrolls the timeline so bars <paramref name="first"/> to <paramref name="last"/> start at its left edge and the section strip is in view;
    /// <paramref name="select"/> also selects those bars, else any bar range is cleared.
    /// </summary>
    public void RevealSection(int first, int last, bool select)
    {
        if (select) Input.Selection.SetRange(-1, first, last, SelectionOrigin.Timeline);
        else Input.Selection.Clear(SelectionOrigin.Timeline);
        _sideways.Offset = new Vector(Math.Max(0, Timeline.BarX(first) - SectionRevealLead), _sideways.Offset.Y);
    }

    /// <summary>Space kept left of a revealed section, in DIP.</summary>
    private const double SectionRevealLead = 8;

    /// <summary>Shows or hides the master lane above track 1, in the timeline and its header cell.</summary>
    public void ShowMasterLane(bool shown)
    {
        Timeline.MasterLaneShown = shown;
        Header.Reload(_document.Project);
    }

    /// <summary>
    /// The compact content on or off (compact mode and the Band view): the timeline hides its track rows and Add track strip (the master lane at
    /// double height), the track list column and its splitter hide, and the transport wraps in a narrow column left of the timeline.
    /// </summary>
    public void SetCompact(bool compact)
    {
        _topBand.Compact = compact;
        Timeline.Compact = compact;
        Header.Reload(_document.Project);
        Header.IsVisible = !compact;
        _listSplitter.IsVisible = !compact;
        ApplyAddTrackBarVisibility();
        if (!compact) Transport.Width = double.NaN;
        InvalidateMeasure();
    }

    /// <summary>Shows and edits <paramref name="document"/> (the shown tab's song) from now on.</summary>
    public void Show(IEditableDocument document)
    {
        _document = document;
        if (document is SongDocument shown) Timeline.Media = shown.Media;
        if (VisualRoot is not null) _trackRows.Watch(document);
        Input.Show(document);
        _trackRows.Show(document);
    }

    /// <summary>
    /// Follows <paramref name="session"/> (null: no playback): the transport bar's buttons and the timeline's playhead line. The line moves
    /// only when the played bar changes, so a frame that stays in its bar repaints nothing.
    /// </summary>
    public void BindPlayback(AvaloniaPlaybackSession? session)
    {
        SubscribePlayhead(false);
        _playback = session;
        SubscribePlayhead(true);
        Transport.Bind(session);
        ShowPlayhead();
    }

    // The playhead subscriptions live only while the arrangement is attached, so a closed or hidden tab does no per-frame work.
    private bool _attached;

    private void SubscribePlayhead(bool subscribed)
    {
        if (_playback is null || (subscribed && !_attached)) return;
        if (subscribed)
        {
            _playback.PlayheadBarChanged += ShowPlayhead;
            _playback.PlayheadFrame += ShowPlayhead;
        }
        else
        {
            _playback.PlayheadBarChanged -= ShowPlayhead;
            _playback.PlayheadFrame -= ShowPlayhead;
        }
    }

    // The playhead a frame last showed (MinValue/NaN: none yet): a bar-change frame delivers PlayheadBarChanged and then
    // PlayheadFrame with the same clock, so the second delivery repeats this frame and is skipped.
    private int _shownPlayheadBar = int.MinValue;
    private double _shownPlayheadFraction = double.NaN;
    private int _shownPlayheadPass = 1;

    // Every frame moves the line to the clock's interpolated position; only the playhead layer's transform changes (see TimelineView.SetPlayhead).
    // While playing, each N>=4 region shows the pass playing (the last played pass stays after leaving it); stopped, the picked passes show again.
    private void ShowPlayhead()
    {
        _trackRows.SyncSelectedRow();
        var position = _playback?.Playhead;
        var bar = position?.Bar ?? -1;
        var fraction = position?.BarFraction ?? 0;
        var pass = 1;
        if (_playback?.IsPlaying == true && _playback is not null)
        {
            var current = _playback.CurrentRepeatPosition;
            var pickerChanged = false;
            foreach (var region in Timeline.RepeatRegionsNow())
            {
                if (region.PassCount <= TimelineRepeatPicker.StackedMaxPasses) continue;
                if (current.WrittenBar < region.StartBar || current.WrittenBar > region.EndBar) continue;
                var before = Timeline.RepeatShown.ShownPass(region);
                Timeline.RepeatShown.FollowPlayback(region, current.Pass);
                if (Timeline.RepeatShown.ShownPass(region) != before) pickerChanged = true;
            }
            if (pickerChanged) Timeline.InvalidateVisual();
            pass = LabelPassFor(bar, current);
        }
        else if (Timeline.RepeatShown.ResumeUserChoices()) Timeline.InvalidateVisual();
        if (bar == _shownPlayheadBar && fraction == _shownPlayheadFraction && pass == _shownPlayheadPass) return;
        _shownPlayheadBar = bar;
        _shownPlayheadFraction = fraction;
        _shownPlayheadPass = pass;
        Timeline.SetPlayhead(bar, fraction, pass);
        FollowPlayheadIfAllowed();
    }

    private static int LabelPassFor(int bar, RepeatPosition current)
    {
        if (bar < 0) return 1;
        return current.WrittenBar == bar ? Math.Max(1, current.Pass) : 1;
    }

    /// <summary>
    /// The "Add track" strip asks the Add track question over the window. Audio adds an audio track at the end (one undo step);
    /// Instrument opens the Track properties window in add mode, which adds its track on OK.
    /// </summary>
    private async void OnAddTrackClicked()
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var countBefore = _document.Project.Tracks.Count;
            var outcome = await PromptWindow.AskAsync(owner, PromptTextsPresenter.AddTrack());
            switch (PromptTextsPresenter.AddTrackAnswer(outcome.Answer))
            {
                case AddTrackChoice.Audio:
                    DocumentEdits.Run(_document, project => { project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio)); return true; });
                    break;
                case AddTrackChoice.Instrument:
                    await new TrackPropertiesWindow(NewTrackFactory.CreateTrack(_document.Project, TrackKind.Guitar), _document, adding: true).ShowAndWaitAsync(owner);
                    break;
            }
            if (_document.Project.Tracks.Count > countBefore) SelectAndShowLastTrack();
        }
        catch (Exception ex)
        {
            // Reported, not swallowed (UiTasks.Forget pattern): no status line in this view, so the failure goes to the trace.
            Trace.Error(Trace.Ui, "add track: " + ex.Message);
        }
    }

    /// <summary>Selects the last track of the shown song and scrolls its header row into view (after the Add track strip added one).</summary>
    private void SelectAndShowLastTrack()
    {
        var last = _document.Project.Tracks.Count - 1;
        if (_document is SongDocument song) song.TrackIndex = last;
        _trackRows.SyncSelectedRow();
    }

    /// <summary>Re-reads <paramref name="project"/>'s tracks into the timeline and header through the row source, so a changed row count or height re-fits the track list at once (a clip edit that added a track).</summary>
    internal void ReloadRows(SongProject project) => _trackRows.Source.Reload(project);

    /// <summary>Selects <paramref name="track"/> and scrolls its row into view (a dropped file that made a new track).</summary>
    internal void ShowTrack(TrackModel track)
    {
        var row = _document.Project.Tracks.IndexOf(track);
        if (row < 0) return;
        if (_document is SongDocument song) song.TrackIndex = row;
        _trackRows.SyncSelectedRow();
    }

    public TransportBarView Transport { get; } = new();
    public TimelineView Timeline { get; }
    /// <summary>The document this arrangement shows (the tab's song).</summary>
    public IEditableDocument Document => _document;
    public TrackHeaderColumn Header { get; }
    /// <summary>The controls of the armed tracks' sub-rows (monitor, input) and their level poll.</summary>
    public ArmedRowLayer ArmedRows => _trackRows.Armed;
    public TimelineInputController Input { get; }

    /// <summary>Raised when the lanes scroll sideways (by hand, by zoom or following playback); the piano roll follows it.</summary>
    public event Action? SidewaysScrolled;

    /// <summary>The song second at the left edge of the visible lanes.</summary>
    public double LeftSeconds => Timeline.SecondsOfX(_sideways.Offset.X);

    /// <summary>Scrolls the lanes so <paramref name="seconds"/> is at their left edge (the piano roll was scrolled by hand).</summary>
    public void ScrollToSeconds(double seconds)
    {
        var x = Math.Max(0, Timeline.XOfSeconds(Math.Max(0, seconds)));
        if (Math.Abs(x - _sideways.Offset.X) < 0.5) return;
        _sideways.Offset = new Vector(x, _sideways.Offset.Y);
    }
}
