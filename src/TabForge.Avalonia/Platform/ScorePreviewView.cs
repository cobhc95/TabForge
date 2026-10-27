using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Shows the demo song's first bars through the shared Core score host and renderer, drawn by <see cref="AvaloniaDrawContext"/>, as an editor surface:
/// a click places the cursor, a drag or Shift+arrows select, hover outlines a beat, and the editor and catalogue keys edit (Core <c>ScorePreviewEditor</c>).
/// Owns: the preview surface, its size and the toolkit input events. Does not own: layout, drawing and hit maths (Core <c>ScoreGeometryProbe</c>),
/// the edit rules (Core <c>ScorePreviewEditor</c>), the key mapping (<see cref="AvaloniaEditorKeys"/>).
/// Tests: TabForge.Avalonia.Tests.ScoreGeometryProofTests, ScorePreviewEditorTests.
/// </summary>
public sealed partial class ScorePreviewView : Control
{
    private const int AllBars = int.MaxValue;   // the page draws every system of the song

    private readonly AvaloniaDrawContext _draw = new();
    private readonly ScoreGeometryProbe _probe;
    private readonly ScorePreviewEditor _editor;
    private readonly ScorePreviewInput _input;
    private readonly ScorePlayheadBinding _playbackBinding;
    private const double FitWidthFactor = 0;   // the song's ZoomFactor while Fit width is on (SongDocument.ZoomFactor: 0 = fit width)
    // Songs shown at least once, keyed by the song object; weak, so a closed song drops out. UI thread only, no ceiling (one entry per open song).
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TabForge.Documents.SongDocument, object> _songsShown = new();
    private TabForge.Documents.SongDocument? _shownSong;   // the song last passed to Show; its ZoomFactor is written back when another song is shown
    private bool _fitWidthOn;   // true while the zoom follows the viewport width; the song then keeps FitWidthFactor, not the fitted value
    private bool _zoomedByHand;   // true once the zoom was set by the user (a step, a zoom row, a stored zoom); page mode then keeps it instead of fitting
    private bool? _paperOverride;   // set by the shown song (SongDocument.DarkPaper); null keeps the theme's paper (a standalone view, the PDF, a proof)

    /// <summary>The window's menu host: the score's right-click menu reads its commands and CanPaste from it.</summary>
    public ContextMenuHost ContextMenus { get; }

    public ScorePreviewView(ContextMenuHost? contextMenus = null)
    {
        ContextMenus = contextMenus ?? new ContextMenuHost();
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
        // Perf (startup): the probe starts blank (a short empty song, no demo compose); the shown song arrives
        // through Show (the window calls it with the active tab right after this), so startup pays no demo build.
        var blank = TabForge.Documents.SongDocument.BlankProject();
        _probe = new ScoreGeometryProbe(blank, _draw);
        _probe.Appearance.DrawEmptyBarRests = true;
        _editor = new ScorePreviewEditor(_probe, blank, Views.PreferencesCatalog.DefaultHotkeys());
        _input = new ScorePreviewInput(this);
        _playbackBinding = new ScorePlayheadBinding(this);
        _editor.LoopTintChanged += (_, _) => InvalidateVisual();
        _editor.SelectionChanged += (_, _) => InvalidateVisual();   // a range made elsewhere (the timeline, a command) shows at once
        CursorChanged += KeepCursorInSight;   // a cursor move that leaves the cursor's system mostly out of view scrolls it back in
        ContextMenuBuilder.Attach(this, ContextMenus, ContextRowsAt);
        _overlay = new ScorePlayheadOverlay(_probe, () => Zoom);
        LogicalChildren.Add(_overlay);
        VisualChildren.Add(_overlay);
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => { InvalidateVisual(); _overlay.InvalidateVisual(); };
    }

    internal ScorePreviewEditor Editor => _editor;
    internal ScoreGeometryProbe Probe => _probe;

    /// <summary>Set by a host whose settings are not the view's own (a Band card): the menu state its rows show. Null reads the view.</summary>
    internal Func<ScoreEmptyState>? MenuStateSource { get; set; }

    /// <summary>The score's menu state as the view shows it now (notation, outline, page layout and scrolling).</summary>
    internal ScoreEmptyState ScoreState(bool overBeat = false) => MenuStateSource is { } source ? source() with { OverBeat = overBeat } : new(OverBeat: overBeat, CanPaste: false, ShowNotation: _probe.Notation != NotationMode.TabOnly,
        Continuous: _probe.Appearance.CenterSystems, Horizontal: _probe.HorizontalScroll, ShowOutline: _probe.Appearance.ShowNoteOutline);

    private const string AudioPlaceholderText = "Audio/MIDI track — no score. Select a tab or MIDI track to see its score.";
    private bool _audioPlaceholder;   // an audio track is shown: no score is laid out or drawn, only a centred note

    /// <summary>True while the view shows the audio-track note instead of a score.</summary>
    internal bool ShowsAudioPlaceholder => _audioPlaceholder;

    private void DrawAudioPlaceholder(DrawingContext context)
    {
        var d = _draw.Use(context);
        var light = ActualThemeVariant == ThemeVariant.Light;
        var text = d.Text(AudioPlaceholderText, InstrumentPanelView.Font, 13, AvaloniaPalette.Get("InstrumentMuted", light), d.PixelsPerDip);
        d.DrawText(text, Bounds.Width / 2 - text.Width / 2, Bounds.Height / 2 - text.Height / 2);
    }

    /// <summary>Draws track <paramref name="index"/> of the shown song (a Band panel's staff); the whole-song score keeps track 0.</summary>
    public void ShowTrack(int index)
    {
        _probe.ShowTrack(index);
        var audio = _probe.Track is { IsAudio: true };
        if (audio != _audioPlaceholder) { _audioPlaceholder = audio; InvalidateMeasure(); }
        InvalidateVisual();
        CursorChanged?.Invoke();
    }

    /// <summary>The tablature, standard notation or both this view draws (the notation button of a Band panel sets it).</summary>
    public TabForge.Models.NotationMode Notation
    {
        get => _probe.Notation;
        set { _probe.Notation = value; InvalidateVisual(); }
    }

    /// <summary>
    /// True when the page is drawn on the light paper. The shown song owns this (SongDocument.DarkPaper), so two tabs can differ; setting it
    /// repaints the page and the playhead layer without a layout pass.
    /// </summary>
    public bool LightPaper
    {
        get => _paperOverride ?? ActualThemeVariant == ThemeVariant.Light;
        set
        {
            _paperOverride = value;   // remembered even when it equals today's theme, so a later theme change cannot take the song's paper
            if (value == !_probe.Appearance.DarkPaper) return;
            _probe.Light = value;
            InvalidateVisual();
            _overlay.InvalidateVisual();
        }
    }

    /// <summary>Repaints after a score view setting changed (the notation setter already drops the layout cache).</summary>
    internal void Refresh() => Reflow();

    /// <summary>
    /// Re-derives the page from the live viewer and lays it out again: the one reflow path shared by attach, a viewport change, a UI scale
    /// change, a zoom, a mode or settings change, and an incremental relayout. Never called from inside a measure or arrange pass.
    /// </summary>
    private void Reflow()
    {
        UpdatePageWidth();
        CentreOneLineVertically();
        InvalidateMeasure();
        InvalidateVisual();
        if (this.FindAncestorOfType<ScrollViewer>() is { } viewer) EnsurePageOrigin(viewer, viewer.Offset);
    }

    /// <summary>
    /// Sets the logical page width: the continuous view reflows to its viewer (Core rule: at least
    /// <see cref="ContinuousMinPageWidth"/>, the viewer width at the zoom less a border), and every system fills that width; the page view and
    /// the one-line view keep their own width.
    /// </summary>
    private void UpdatePageWidth()
    {
        var viewer = this.FindAncestorOfType<ScrollViewer>();
        var viewport = viewer?.Viewport.Width ?? 0;   // the width the content can use: the viewer's bars are already taken out
        var reflow = _probe.Appearance.CenterSystems && !_probe.HorizontalScroll && viewport > 1;
        _probe.PageWidthOverride = reflow ? ReflowPageWidth(viewport) : 0;
        _probe.Layout.JustifyAllSystems = reflow;
        var wantAlign = _probe.HorizontalScroll ? global::Avalonia.Layout.HorizontalAlignment.Left : global::Avalonia.Layout.HorizontalAlignment.Center;   // the page sits centred; the one-line view starts at the left
        if (HorizontalAlignment != wantAlign) HorizontalAlignment = wantAlign;
        if (viewer is not null && viewport > 1 && !_drawsInPanel)
        {
            var wantBar = HorizontalBarFor(reflow, viewport);
            if (viewer.HorizontalScrollBarVisibility != wantBar) viewer.HorizontalScrollBarVisibility = wantBar;
        }
    }

    /// <summary>A plain press placed the cursor: the bar and cell it sits on, or the start of a dragged range once released. Playback follows it on press.</summary>
    public event Action<int, int>? CursorPlaced;

    /// <summary>The cursor moved or an edit changed the score under it.</summary>
    public event Action? CursorChanged;

    /// <summary>The score took the focus (a press on it or keyboard focus): the edit keys act on its selection from now on.</summary>
    public event Action? Focused;

    /// <summary>A note selection was finished (the mouse button or Shift came up with notes selected): the loop link makes it the loop region.</summary>
    public event Action? SelectionReleased;

    /// <summary>A plain left click (no Shift, no drag) placed the cursor: the loop link clears the selection and a loop the score made.</summary>
    public event Action? PlainClicked;

    internal void RaisePlainClicked() => PlainClicked?.Invoke();

    internal void RaiseFocused() => Focused?.Invoke();

    internal void RaiseSelectionReleased() { if (Editor.HasSelection) SelectionReleased?.Invoke(); }

    /// <summary>The marquee of a drag-select in view coordinates while the button is held, else null (drawn over the page, see MarqueePainter).</summary>
    internal Rect? Marquee { get; set; }

    /// <summary>Selects every beat of the shown track (Edit.SelectAll).</summary>
    internal void SelectAll()
    {
        _editor.SelectAll();
        InvalidateVisual();
        CursorChanged?.Invoke();
    }

    /// <summary>Shows and edits <paramref name="song"/> (the shown tab's song) from now on.</summary>
    public void Show(TabForge.Documents.SongDocument song)
    {
        // The zoom belongs to the song: the outgoing song keeps no zoom of its own (0: Fit width, or 100% in the continuous view) unless one was
        // set by hand, and the incoming song opens at its own.
        if (_shownSong is not null) _shownSong.ZoomFactor = _fitWidthOn || !_zoomedByHand ? FitWidthFactor : Zoom;
        _shownSong = song;
        // A song shown for the first time takes the preferred notation (the mode on screen, which the View menu saves), as the song's own setting
        // PreferredNotation on every opened song; a song shown again keeps its own mode.
        if (_songsShown.TryGetValue(song, out _)) _probe.Notation = song.Notation;
        else { _songsShown.Add(song, song); song.Notation = _probe.Notation; }
        _editor.ShowDocument(song);
        // A song with no zoom of its own opens at 100% in the continuous view; only the page view fits its width.
        if (song.ZoomFactor > 0) SetZoom(song.ZoomFactor);
        else if (_probe.Appearance.CenterSystems) OpenAtNormalSize();
        else FitWidth();
        InvalidateVisual();
        if (this.FindAncestorOfType<ScrollViewer>() is { } viewer) RevealFirstSystemWhenLaidOut(viewer);
        CursorChanged?.Invoke();
    }

    /// <summary>Narrowest logical page the continuous view reflows to.</summary>
    private const double ContinuousMinPageWidth = 380;

    /// <summary>The zoom a Band card's staff draws at: the normal size, whatever the whole-song score's zoom.</summary>
    public const double PanelZoom = NormalZoom;

    /// <summary>
    /// Draws <paramref name="song"/> in a Band card: the song keeps its own notation and zoom (only the whole-song score writes them), the card
    /// draws at <see cref="PanelZoom"/> with its systems reflowed to the card's width (the continuous layout), and the header block stays off
    /// (<see cref="DrawsHeader"/>).
    /// </summary>
    public void ShowInPanel(TabForge.Documents.SongDocument song)
    {
        _drawsInPanel = true;
        _shownSong = null;
        _editor.ShowDocument(song);
        _probe.Appearance.CenterSystems = true;
        SetZoom(PanelZoom);
        InvalidateVisual();
        CursorChanged?.Invoke();
    }

    /// <summary>The width each bar of the shown track needs on its own, one per bar before any shared floor; null for an audio track.</summary>
    internal double[]? NaturalBarWidths()
    {
        if (_probe.Track is not { IsAudio: false } track) return null;
        _probe.Layout.GetLayout(track);
        return _probe.Layout.NaturalWidths;
    }

    /// <summary>Bars are at least these wide (null: each bar takes its own width). Cards that share the floor lay out the same systems, so bar N starts at one x in all of them.</summary>
    internal void ShareBarWidths(double[]? widths)
    {
        var current = _probe.Layout.MinBarWidths;
        if (ReferenceEquals(current, widths)) return;
        if (current is not null && widths is not null && current.Length == widths.Length)
        {
            var same = true;
            for (var i = 0; i < current.Length; i++) if (Math.Abs(current[i] - widths[i]) > 1e-9) { same = false; break; }
            if (same) return;   // P0: same floor must not re-measure, or SizeChanged/LayoutUpdated retries never converge
        }
        else if (current is null && widths is null) return;
        _probe.Layout.MinBarWidths = widths;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Whether the header block (title, authors, tuning) is drawn above the first system; a Band card turns it off.</summary>
    public bool DrawsHeader
    {
        get => _probe.DrawsHeader;
        set { _probe.DrawsHeader = value; Refresh(); }
    }

    /// <summary>Runs a menu command (Undo, Redo, a note, beat or bar command) on the shown song; false when it is not an editor command or changed nothing.</summary>
    /// <summary>Runs a clipboard edit (cut, paste) on the editor and repaints when it changed the song.</summary>
    internal bool Edit(Func<ScorePreviewEditor, bool> edit)
    {
        if (!edit(_editor)) return false;
        InvalidateVisual();
        CursorChanged?.Invoke();
        return true;
    }

    public bool Run(string commandId)
    {
        if (!_editor.RunCommand(commandId)) return false;
        InvalidateVisual();
        CursorChanged?.Invoke();
        return true;
    }

    /// <summary>The active state of a palette tool at the cursor (true, false, or null when absent or mixed).</summary>
    internal bool? ToolState(string toolId) => _editor.ToolState(toolId);

    /// <summary>Whether a palette tool can act at the cursor.</summary>
    internal bool ToolEnabled(string toolId) => _editor.ToolEnabled(toolId);

    /// <summary>Inserts an empty bar in front of <paramref name="bar"/> (a timeline bar menu row); false when nothing changed.</summary>
    public bool InsertBarAt(int bar)
    {
        if (!_editor.InsertBarAt(bar)) return false;
        InvalidateVisual();
        CursorChanged?.Invoke();
        return true;
    }

    /// <summary>Inserts <paramref name="count"/> empty bars in front of <paramref name="at"/> as one undo step; false when nothing changed.</summary>
    public bool InsertBarsAt(int at, int count)
    {
        if (!_editor.InsertBarsAt(at, count)) return false;
        InvalidateVisual();
        CursorChanged?.Invoke();
        return true;
    }

    /// <summary>Repeats the selected bars (else the cursor bar) <paramref name="times"/> times after themselves; false when nothing changed.</summary>
    public bool Repeat(int times)
    {
        if (!_editor.RepeatSelection(times)) return false;
        InvalidateVisual();
        CursorChanged?.Invoke();
        return true;
    }

    private readonly ScorePlayheadOverlay _overlay;

    /// <summary>The playing-marker layer (line and top dot), so a test can read where it sits.</summary>
    internal ScorePlayheadOverlay PlayheadOverlay => _overlay;

    protected override Size ArrangeOverride(Size finalSize)
    {
        _overlay.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <summary>Follows <paramref name="session"/> (null unbinds): the playing-bar band, lit notes and playhead line, repainted when the bar or beat changes or a note starts or ends, and the score scrolls to keep the playhead in view.</summary>
    public void BindPlayback(AvaloniaPlaybackSession? session) => _playbackBinding.Bind(session);

    /// <summary>Room kept above the first staff for the section and tempo labels, so the score opens on its first system, not the title block.</summary>
    private const double FirstSystemLabelRoom = 16;
    private bool _openedOnFirstSystem;
    private IDisposable? _revealSubscription;

    /// <summary>The top of the first staff in score coordinates at the current zoom (DIP), so a test or a caller can check that it is in view.</summary>
    internal double FirstStaffTop => _probe.StaffTop(0) * Zoom;

    /// <summary>The bottom line of the first TAB staff in score coordinates at the current zoom (DIP), so a test can check the whole first system is in view.</summary>
    internal double FirstTabBottom => (_probe.TabTop(0) + _probe.StringGap * Math.Max(0, (_probe.Track?.StringTunings.Count ?? 1) - 1)) * Zoom;

    /// <summary>
    /// Opens the score scrolled to its first system (staff and tab in view). The viewer clamps the offset to its
    /// extent, and the extent is zero until the page is laid out, so the offset is set on the first extent change that has a height.
    /// </summary>
    private void RevealFirstSystemWhenLaidOut(ScrollViewer viewer)
    {
        _revealSubscription = viewer.GetObservable(ScrollViewer.ExtentProperty).Subscribe(new AnonymousObserver<Size>(extent =>
        {
            if (extent.Height <= 0) return;
            _revealSubscription?.Dispose();
            _revealSubscription = null;
            EnsurePageOrigin(viewer, new Vector(viewer.Offset.X, Math.Max(0, FirstStaffTop - FirstSystemLabelRoom)));
        }));
    }

    /// <summary>The first attach reveals the first system; later re-attaches keep the reader's place.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _playbackBinding.SetAttached(true);
        ThemeSettingsBinding.UiScaleChanged += OnUiScaleChanged;
        if (this.FindAncestorOfType<ScrollViewer>() is not { } viewer) return;
        _scrollViewer = viewer;
        viewer.ScrollChanged += ViewerScrolled;
        viewer.AddHandler(InputElement.PointerPressedEvent, ViewerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerReleasedEvent, ViewerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _viewportSubscription = viewer.GetObservable(ScrollViewer.ViewportProperty).Subscribe(new AnonymousObserver<Size>(_ => ViewerResized()));
        QueueReflow(force: true);   // the new viewer may have another width, and a re-attach keeps the reader's offset (EnsurePageOrigin)
        if (_openedOnFirstSystem) return;
        _openedOnFirstSystem = true;
        RevealFirstSystemWhenLaidOut(viewer);
    }

    private IDisposable? _viewportSubscription;
    private ScrollViewer? _scrollViewer;   // the viewer whose scroll and size this view follows while attached

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _playbackBinding.SetAttached(false);
        ThemeSettingsBinding.UiScaleChanged -= OnUiScaleChanged;
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= ViewerScrolled;
            _scrollViewer.RemoveHandler(InputElement.PointerPressedEvent, ViewerPressed);
            _scrollViewer.RemoveHandler(InputElement.PointerReleasedEvent, ViewerReleased);
        }
        _scrollViewer = null;
        _viewportSubscription?.Dispose();
        _viewportSubscription = null;
        _revealSubscription?.Dispose();
        _revealSubscription = null;
    }

    /// <summary>Raised when a section name above a bar is clicked, with the section and whether Ctrl was held.</summary>
    public event Action<MarkerModel, bool>? SectionHeadingClicked;

    /// <summary>Scrolls the enclosing viewer so the system holding <paramref name="bar"/> sits near the top, leaving <paramref name="marginPercent"/> of the view above it.</summary>
    public void RevealBar(int bar, int marginPercent)
    {
        if (this.FindAncestorOfType<ScrollViewer>() is not { } viewer) return;
        var top = ScoreSectionLinks.BarTop(_probe, bar) * Zoom;
        EnsurePageOrigin(viewer, new Vector(viewer.Offset.X, Math.Max(0, top - viewer.Viewport.Height * marginPercent / 100.0)));
    }

    /// <summary>Selects bars <paramref name="first"/> to <paramref name="last"/> whole.</summary>
    public void SelectBars(int first, int last)
    {
        _editor.SelectRange(first, 0, last, Math.Max(0, _probe.SlotsFor(last) - 1));
        InvalidateVisual();
        CursorChanged?.Invoke();
    }

    /// <summary>The Follow settings (View scrolling while playing); the owner keeps them current.</summary>
    public FollowSettings Follow { get; set; } = new();

    /// <summary>Distance one mouse-wheel notch scrolls the score, in device pixels.</summary>
    public int WheelScrollPixels { get; set; } = new EditingSettings().ScoreWheelScrollPixels;

    // Scrolls the enclosing viewer so the playhead line is inside it (page coordinates scaled by the zoom). the switch off does not move;
    // Jump sets the offset at once; Smooth glides toward it, one step per frame allowed by Follow.MaxFps.
    internal void FollowPlayhead()
    {
        if (_scrollViewer is not { } viewer) return;   // null while detached: no work
        EnsurePageOrigin(viewer, viewer.Offset);   // the line, its margin and the offset all come from the one live origin this frame
        if (!Follow.FollowPlayback || !FollowHold.Allows(Follow)) return;
        // The follow keeps the whole playhead line (staff and tab) in view: parking the tab alone cut the staff off when the margin is shorter than the staff.
        if (_probe.Play.PlayheadLine(_probe) is not var (x, top, bottom)) return;
        _follower.Follow(Follow, viewer, PageLeftMargin(viewer), Zoom, (x, top, bottom), _probe.Play.CurrentBarWidth(_probe) ?? 0);
    }

    private readonly ScorePlayheadFollower _follower = new();

    /// <summary>
    /// The page's left edge inside the viewer: a centred page sits half the spare width in from the left. Worked out from the viewer's size and
    /// the live page width by <see cref="PageExtentWidth"/> (never the last measured page, which lags a layout pass), so it agrees with the draw and the follow.
    /// </summary>
    private double PageLeftMargin(ScrollViewer viewer) =>
        HorizontalAlignment == global::Avalonia.Layout.HorizontalAlignment.Center ? Math.Max(0, (viewer.Viewport.Width - PageExtentWidth(viewer)) / 2) : 0;

    public override void Render(DrawingContext context)
    {
        if (_audioPlaceholder) { DrawAudioPlaceholder(context); return; }
        // The paper belongs to the shown song (LightPaper), not to the theme: the document panes set it on every tab switch.
        // A view with no song (a proof, a standalone preview) has no override and follows the theme it is rendered in.
        if (_paperOverride is null) _probe.Light = ActualThemeVariant == ThemeVariant.Light;
        // An edit or a width change changed the page size; invalidating inside the render pass throws, so the reflow is queued after it.
        if (_probe.ContentSize() != _measuredPage) QueueReflow(force: true);
        using (context.PushTransform(Matrix.CreateScale(Zoom, Zoom)))
            _probe.DrawPage(_draw.Use(context), AllBars, withPlayhead: false, visibleRows: DrawnRows());
        if (Marquee is { } box) MarqueePainter.Draw(_draw.Use(context), key => AvaloniaPalette.Get(key, ActualThemeVariant == ThemeVariant.Light), box.X, box.Y, box.Width, box.Height);
    }

    internal void RaiseCursorChanged() => CursorChanged?.Invoke();

    internal void RaiseCursorPlaced(int bar, int cell) => CursorPlaced?.Invoke(bar, cell);

    internal void RaiseSectionHeadingClicked(MarkerModel section, bool ctrl) => SectionHeadingClicked?.Invoke(section, ctrl);
}