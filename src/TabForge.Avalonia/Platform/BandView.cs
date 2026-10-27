using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the Band view's content: one card per shown track (BandTrackPanel) in song order, stacked in one scroll viewer (the view's only
//   vertical scroll bar); which tracks show (multi-select: a track's card is added or removed); each card's fretboard (on for the first shown
//   card by default, off for any card added later); the selected card; the layout choices (fretboard above or beside, colour frame); the bars the
//   cards share; the playback binding and the follow of the playhead (every card's window of systems slides to the playing bar's system); and each open song's cards, kept while the song is open.
// Does not own: a card's contents (BandTrackPanel), one song's card state (BandSongCards), the followed bar (BandPlayheadFollow), the strip of track
//   capsules and its caption (CompactTrackStrip, MainWindowBandChrome), the layout and the dock (MainWindowBandView, DockWorkspaceController).
// Tests: TabForge.Avalonia.Tests.BandViewTests, TabForge.Avalonia.Tests.BandCardTests.
public sealed partial class BandView : ScrollViewer
{
    /// <summary>How many tracks show when a song opens or View > Reset view is chosen: the first ones that are not audio tracks.</summary>
    public const int DefaultShownCount = 3;

    /// <summary>The gap between two cards.</summary>
    public const double CardGap = 6;

    // The cards of each open song, keyed by the song object so a closed song drops out. One entry per open song, no ceiling. UI thread only.
    private readonly ConditionalWeakTable<SongDocument, BandSongCards> _cardsBySong = new();
    private readonly StackPanel _cardStack = new() { Spacing = CardGap };
    private int _followedBar;
    private SongDocument _document;
    private BandSongCards _current = new(Array.Empty<int>(), Array.Empty<TrackModel>());
    private AvaloniaPlaybackSession? _playback;
    private AvaloniaPlaybackSession? _followedSession;
    private SongDocument? _watchedSong;
    private bool _fretboardAbove = true;
    private bool _colourFrame = true;
    // Perf E2b (H1/H2): deferred share/fit. ShareBars/FitCards run only once laid out (Bounds.Height > 1);
    // earlier calls set _sharePending, retried on SizeChanged/LayoutUpdated.
    private bool _sharePending;

    /// <summary>A subclass takes the ScrollViewer's template, so the view scrolls (its own type has no theme of its own).</summary>
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    private readonly Action<ScorePreviewView, SongDocument>? _bindStaff;
    private readonly Action<ScorePreviewView>? _unbindStaff;
    private readonly BandSettings _sessionBand = new();   // the band settings of a view without a window: kept for this view's session

    /// <param name="bindStaff">Gives each card's staff the window's selection code path (null leaves a staff to its own editor).</param>
    /// <param name="unbindStaff">Ends that wiring when a card is dropped (null when nothing was bound).</param>
    public BandView(SongDocument document, Action<ScorePreviewView, SongDocument>? bindStaff = null, Action<ScorePreviewView>? unbindStaff = null)
    {
        _bindStaff = bindStaff;
        _unbindStaff = unbindStaff;
        _fretboardSize = new(() => _sessionBand, () => { });
        Content = _cardStack;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _document = document;
        _watch.Marked = OnProjectMarked;
        _watch.ProjectChanged = OnProjectReplaced;
        _watch.Restored = OnRestored;
        SizeChanged += (_, _) => ShareBars();
        // Perf E2b (H1/H2): the first layout may run before the cards' staffs are ready (StaffLineUnits 0);
        // a pending share retries on the next layout pass instead of leaving the cards unfitted.
        LayoutUpdated += (_, _) => { if (_sharePending) ShareBars(); };
        AddHandler(PointerWheelChangedEvent, ZoomWithWheel, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ShowSong(document);
    }

    /// <summary>Raised after the shown cards change (a track added or removed, the view shown or reset), so the strip's capsules follow.</summary>
    public event Action? ShownChanged;

    /// <summary>The settings the playhead follow reads (the score's Follow settings, read at each frame).</summary>
    public Func<FollowSettings> FollowSource { get; set; } = () => UnwiredFollow;

    // Until the host wires FollowSource the cards turn page by page (the follow as before the Smooth glide).
    private static readonly FollowSettings UnwiredFollow = new() { Mode = FollowModes.Jump };

    /// <summary>True while the window records (the arrangement's Record button state): a card click then never moves the transport.</summary>
    public Func<bool>? IsRecording { get; set; }

    /// <summary>The song the cards draw.</summary>
    public SongDocument Song => _document;

    /// <summary>The track indices whose cards show, in song order.</summary>
    public IReadOnlyList<int> ShownTrackIndices => _current.Shown;

    /// <summary>How many cards the view holds now (the shown ones).</summary>
    public int VisiblePanelCount => _cardStack.Children.Count;

    /// <summary>The selected card's track index, or -1 when no card shows.</summary>
    public int SelectedTrackIndex => _current.Selected;

    /// <summary>The card of <paramref name="trackIndex"/> once it has been shown, else null.</summary>
    public BandTrackPanel? PanelFor(int trackIndex) => _current.Panels.GetValueOrDefault(trackIndex);

    /// <summary>Whether the track's card shows.</summary>
    public bool IsShown(int trackIndex) => _current.Shown.Contains(trackIndex);

    /// <summary>Whether the shown track's card has its fretboard on.</summary>
    public bool FretboardOn(int trackIndex) => _current.FretboardsOn.Contains(trackIndex);

    /// <summary>
    /// Makes <paramref name="document"/> the song the cards draw: its own cards come back as they were left (the defaults when it has none yet).
    /// The cards of the song left behind keep their state and stop following the playback.
    /// </summary>
    public void ShowSong(SongDocument document)
    {
        foreach (var panel in _current.Panels.Values) panel.BindPlayback(null);
        _document = document;
        WatchSong(document);
        _current = _cardsBySong.GetValue(document, song => new BandSongCards(DefaultTracks(song), song.Project.Tracks));
        FollowTracks();
        _barSignature = BarSignature(document);
        _rowsSignature = RowsSignature(document);
        Refresh();
    }

    /// <summary>Adds the track's card when it is not shown, removes it when it is (a capsule click). A card added now has its fretboard off.</summary>
    public void ToggleShown(int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= _document.Project.Tracks.Count) return;
        if (!_current.Shown.Remove(trackIndex)) { _current.Shown.Add(trackIndex); _current.Shown.Sort(); }
        _current.FretboardsOn.Remove(trackIndex);
        if (!_current.Shown.Contains(_current.Selected)) _current.Selected = _current.Shown.Count > 0 ? _current.Shown[0] : -1;
        Refresh();
        if (_current.Shown.Contains(trackIndex)) ScrollRowIntoView(trackIndex);   // a pill that adds a row shows it
    }

    /// <summary>Scrolls the view so the shown track's card is on screen (no change when it already is); never runs during playback.</summary>
    public void ScrollRowIntoView(int trackIndex)
    {
        if (!_current.Panels.TryGetValue(trackIndex, out var panel) || !_current.Shown.Contains(trackIndex) || panel.Bounds.Height <= 0) return;
        var top = panel.Bounds.Top;
        var bottom = panel.Bounds.Bottom;
        var y = Offset.Y;
        var target = top < y ? top : bottom > y + Viewport.Height ? Math.Max(0, bottom - Viewport.Height) : y;
        if (bottom - top > Viewport.Height) target = top;   // a card taller than the view shows from its top
        if (target != y) Offset = new Vector(Offset.X, target);
    }

    private string _appliedRowMin = BandChoices.RowMedium;

    /// <summary>Fits the cards again after Preferences changed the "Band row minimum height" row (does nothing when it is the same).</summary>
    public void ApplyRowMinHeight()
    {
        var choice = BandChoices.NormalizeRowMin(_scoreMenu?.RowMinHeightChoice);
        if (choice == _appliedRowMin) return;
        _appliedRowMin = choice;
        ShareBars();
    }

    // The timeline (or any pane) selected another track: its row scrolls into view.
    private void OnTrackIndexChanged() => ScrollRowIntoView(_document.TrackIndex);

    /// <summary>Selects a shown track's card (the outline marks it). The cards stay where they are: a press on a card must not rebuild the stack under the pointer.</summary>
    public void SelectTrack(int trackIndex)
    {
        SelectCard(trackIndex);
        ScrollRowIntoView(trackIndex);
    }

    // A press on the card itself selects it without moving the view under the pointer.
    private void SelectCard(int trackIndex)
    {
        if (!_current.Shown.Contains(trackIndex) || _current.Selected == trackIndex) return;
        _current.Selected = trackIndex;
        // Perf E2b (H1/H2): panels are lazy (may not exist while detached); the selection is applied to built cards now and to the rest in Refresh.
        foreach (var index in _current.Shown) if (_current.Panels.TryGetValue(index, out var panel)) panel.SetSelected(index == trackIndex);
    }

    /// <summary>Turns a shown track's fretboard on or off in its card (the card's toggle runs this); the cards fit the view again.</summary>
    public void SetFretboardOn(int trackIndex, bool on)
    {
        if (!_current.Shown.Contains(trackIndex)) return;
        if (on) _current.FretboardsOn.Add(trackIndex);
        else _current.FretboardsOn.Remove(trackIndex);
        // Perf E2b (H1/H2): lazy panels may not exist yet; the flag is applied on creation in Refresh/EnsurePanel.
        if (_current.Panels.TryGetValue(trackIndex, out var panel)) panel.SetFretboardShown(on);
        FitCards();
    }

    /// <summary>The fretboard above each card's staff (true) or beside it (false).</summary>
    public void SetFretboardAbove(bool above)
    {
        _fretboardAbove = above;
        // Perf E2b (H1/H2): lazy panels may not exist yet (ApplyChoices before first attach); Refresh applies on creation.
        foreach (var index in _current.Shown) if (_current.Panels.TryGetValue(index, out var panel)) panel.SetFretboardAbove(above);
    }

    /// <summary>The track colour frame of each card (true) or the plain outline (false).</summary>
    public void SetColourFrame(bool on)
    {
        _colourFrame = on;
        foreach (var index in _current.Shown) if (_current.Panels.TryGetValue(index, out var panel)) panel.SetColourFrame(on);
    }

    // Ctrl+wheel over the cards changes the Band lane zoom (a step per notch) instead of scrolling the view.
    private void ZoomWithWheel(object? sender, global::Avalonia.Input.PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasPrimary() || e.Delta.Y == 0 || _scoreMenu is null) return;
        _scoreMenu.WheelZoom(e.Delta.Y > 0);
        e.Handled = true;
    }

    /// <summary>Draws every card's score as the band settings say, after a score menu row changed them.</summary>
    public void ApplyScoreSettings()
    {
        foreach (var panel in _current.Panels.Values) _scoreMenu?.Apply(panel);
        ShareBars();
    }

    /// <summary>Applies the band settings' "Playhead line" row to every card after Preferences changed it.</summary>
    public void ApplyPlayheadLine()
    {
        foreach (var panel in _current.Panels.Values) _scoreMenu?.ApplyPlayheadLine(panel);
    }

    /// <summary>Applies the layout choices the Band view's menu holds.</summary>
    public void ApplyChoices(BandLayoutChoices choices)
    {
        SetFretboardAbove(choices.FretboardAbove);
        SetColourFrame(choices.ColourFrame);
    }

    /// <summary>Shows the default cards again and selects the first (the menu's Reset view); every card's fretboard goes back to the default.</summary>
    public void ResetView()
    {
        _current.ResetTo(DefaultTracks(_document));
        ClearRows();
        _fretboardSize.Reset();
        ApplyFretboardSize();
        Refresh();
    }

    /// <summary>Follows <paramref name="session"/> (null unbinds) in every card's staff, and scrolls the view to the playing system.</summary>
    public void BindPlayback(AvaloniaPlaybackSession? session)
    {
        _playback = session;
        foreach (var panel in _current.Panels.Values) panel.BindPlayback(session);
        UnfollowSession();
        _followedSession = session;
        if (_attached) FollowSession();
    }

    /// <summary>Forgets the row counts the user chose (one undo step), so the cards fit their defaults again (the Band view's reset row heights).</summary>
    public void ResetRowHeights()
    {
        ClearRows();
        Refresh();
    }

    // The playhead follow runs only while the view is attached, so a detached band view does no per-frame work.
    private bool _attached;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (_fretboardMenu is not null) _fretboardMenu.TrackViewChanged += ApplyInstrumentViews;
        FollowSession();
        WatchSong(_document);
        _barSignature = null;   // a detached view missed the edits: read the current project
        SongChanged();
        // Perf E2b (H1/H2): the constructor's ShareBars deferred for want of layout; run it now that the view is attached
        // (SizeChanged/LayoutUpdated re-runs it once the view is laid out).
        if (_sharePending) ShareBars();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        if (_fretboardMenu is not null) _fretboardMenu.TrackViewChanged -= ApplyInstrumentViews;
        UnfollowSession();
        UnwatchSong();
    }

    private void FollowSession()
    {
        if (_followedSession is null) return;
        _followedSession.PlayheadFrame += FollowPlayhead;
        _followedSession.PlayheadBarChanged += FollowPlayhead;
        _followedSession.StateChanged += FollowPlayhead;
    }

    private void UnfollowSession()
    {
        if (_followedSession is null) return;
        _followedSession.PlayheadFrame -= FollowPlayhead;
        _followedSession.PlayheadBarChanged -= FollowPlayhead;
        _followedSession.StateChanged -= FollowPlayhead;
    }

    private static IEnumerable<int> DefaultTracks(SongDocument song) => song.Project.Tracks
        .Select((track, index) => (track, index))
        .Where(x => !x.track.IsAudio)
        .Take(DefaultShownCount)
        .Select(x => x.index);

    /// <summary>Follows a song's edits while the view is attached; a detached view (a rebuilt dock, a closed pane) lets the song go.</summary>
    private void WatchSong(SongDocument song)
    {
        UnwatchSong();
        _watchedSong = song;
        if (!_attached) return;
        song.DisplayStateChanged += OnSongChanged;
        song.ScorePaperChanged += ApplyPaper;
        song.TrackIndexChanged += OnTrackIndexChanged;
        _watch.Bind(song);
    }

    private void UnwatchSong()
    {
        if (_watchedSong is not null)
        {
            _watchedSong.DisplayStateChanged -= OnSongChanged;
            _watchedSong.ScorePaperChanged -= ApplyPaper;
            _watchedSong.TrackIndexChanged -= OnTrackIndexChanged;
        }
        _watch.Dispose();
        _watchedSong = null;
    }

    // The song's paper changed (the View menu on the whole-song score): every card draws the new paper too.
    private void ApplyPaper()
    {
        foreach (var panel in _current.Panels.Values) panel.Staff.LightPaper = !_document.DarkPaper;
    }

    // The attached view's listener on the DOCUMENT (marks and replaced songs), so an undo that swaps the project keeps it drawing the current one.
    private readonly DocumentWatch _watch = new();

    /// <summary>
    /// An edit (every edit marks the song's timeline). A card keeps its bars where they are while a note is edited in place, so the shared bar
    /// widths and the card fit are measured again only when the song's bars changed (a bar or a track came or went); otherwise the cards repaint
    /// the same layout, which shows the new notes without moving the tab around them.
    /// </summary>
    private void OnProjectMarked(SongProject project) => SongChanged();

    // The document's song was replaced (undo, redo): the cards' probes take the current song even when its bars match the old one (a rename-only undo).
    private void OnProjectReplaced(SongProject old, SongProject current) => TakeCurrentSong();

    // An undo or redo finished (also when the song object stayed): the probes take the current song again.
    private void OnRestored() => TakeCurrentSong();

    private void TakeCurrentSong()
    {
        _barSignature = null;
        SongChanged();
    }

    private void OnSongChanged(object? sender, EventArgs e)
    {
        SongChanged();
    }

    // A track was deleted, added or moved: every kept card follows its track by id, a card of a deleted track goes (an index kept across the edit would point at another track or past the end).
    private void FollowTracks()
    {
        var dropped = _current.Reconcile(_document.Project.Tracks);
        if (dropped.Count == 0) return;
        foreach (var panel in dropped)
        {
            panel.BindPlayback(null);
            _unbindStaff?.Invoke(panel.Staff);
        }
        if (_current.Shown.Count == 0 && _document.Project.Tracks.Count > 0) _current.ResetTo(DefaultTracks(_document));
        Refresh();
    }

    private void SongChanged()
    {
        FollowTracks();
        FollowRows();
        var bars = BarSignature(_document);
        if (bars != _barSignature)
        {
            _barSignature = bars;
            foreach (var panel in _current.Panels.Values)
                panel.Staff.Probe.ReplaceSong(_document.Project);
            ShareBars();
            RefreshInstrumentCursors();
            return;
        }
        foreach (var panel in _current.Panels.Values)
        {
            panel.Staff.RepaintSongChanged();
            panel.RefreshInstrumentCursor();
        }
    }

    // An edit may have added or removed the notes under a cursor: every card's fretboard re-lights.
    private void RefreshInstrumentCursors()
    {
        foreach (var panel in _current.Panels.Values) panel.RefreshInstrumentCursor();
    }

    // The song's bars as the cards lay them out (one count per track): a bar added, deleted or a track added changes it.
    private int? _barSignature;

    private static int BarSignature(SongDocument song)
    {
        var hash = new HashCode();
        foreach (var track in song.Project.Tracks) hash.Add(track.Measures.Count);
        return hash.ToHashCode();
    }

    private BandTrackPanel EnsurePanel(int trackIndex)
    {
        if (_current.Panels.TryGetValue(trackIndex, out var existing)) return existing;
        var panel = new BandTrackPanel(_document, trackIndex);
        panel.Selected += SelectCard;
        _bindStaff?.Invoke(panel.Staff, _document);
        // A click on a card's staff seeks playback to the pressed cell, as a click on the main score does; never while recording.
        panel.Staff.CursorPlaced += (bar, cell) => { if (IsRecording?.Invoke() != true) _playback?.Seek(bar, cell); };
        panel.FretboardToggled += index => SetFretboardOn(index, !_current.FretboardsOn.Contains(index));
        panel.InstrumentResized += height => ResizeFretboards(panel, height);
        panel.InstrumentResizeEnded += () => _fretboardSize.Commit();
        panel.CardResized += height => PreviewRows(panel, height);
        panel.CardResizeEnded += () => EndRowsDrag(panel);
        panel.RowsPicked += rows => SetRows(panel, rows);
        panel.SetInstrumentSize(_fretboardSize.UserHeight(panel.InstrumentShape), panel.InstrumentAutoMaxHeight);
        panel.BindPlayback(_playback);
        ScoreMenu?.Bind(panel);
        BindInstrumentMenu(panel);
        ApplyInstrumentView(panel);
        _current.Panels[trackIndex] = panel;
        return panel;
    }

    private void Refresh()
    {
        // Perf E2b (H1/H2): the panels are built eagerly (as before) so ApplyChoices/SetSelected always find them;
        // the expensive ShareBars/FitCards underneath defers to the first layout (see ShareBars/FitCards).
        // The cards already on screen in this order are left in place: a press on a card (which selects it) must not detach the control under the
        // pointer, or its button never sees the release and the click is lost. A changed shown set (a capsule, a reset, another song) rebuilds.
        if (!CardsMatch())
        {
            _cardStack.Children.Clear();
            foreach (var index in _current.Shown) _cardStack.Children.Add(EnsurePanel(index));
        }
        foreach (var index in _current.Shown)
        {
            var panel = EnsurePanel(index);
            panel.SetSelected(index == _current.Selected);
            panel.SetFretboardAbove(_fretboardAbove);
            panel.SetColourFrame(_colourFrame);
            panel.SetFretboardShown(_current.FretboardsOn.Contains(index));
            panel.RefreshInstrumentCursor();   // also settles the card's visibility after a delete or undo
        }
        ShareBars();
        ShownChanged?.Invoke();
    }

    // True while the cards on screen are exactly the shown ones, in song order.
    private bool CardsMatch()
    {
        if (_cardStack.Children.Count != _current.Shown.Count) return false;
        for (var i = 0; i < _current.Shown.Count; i++)
            if (!_current.Panels.TryGetValue(_current.Shown[i], out var panel) || !ReferenceEquals(_cardStack.Children[i], panel)) return false;
        return true;
    }

    private void FitCards()
    {
        // Perf E2b (H1/H2): the fit needs a laid-out view; before the first layout it only marks a pending share.
        // The cards' staffs also need their first layout (StaffLineUnits 0); a pending share retries on LayoutUpdated.
        if (!_attached || Bounds.Height <= 1) { _sharePending = true; return; }
        if (_current.Shown.Count == 0) return;
        var cards = _current.Shown.Where(index => _current.Panels.TryGetValue(index, out _)).Select(index => _current.Panels[index]).ToList();
        if (cards.Count != _current.Shown.Count) { _sharePending = true; return; }
        if (cards.Max(card => card.StaffLineUnits()) <= 0) { _sharePending = true; return; }
        BandCardFit.Apply(cards, Bounds.Height, CardGap, _scoreMenu?.LaneZoom ?? 1, _scoreMenu?.RowMinScale ?? 1);
        ApplySavedRows();
    }

    /// <summary>
    /// Gives every shown card the widest need of each bar, so the cards lay out the same bars at the same widths: bar N starts at one x in
    /// every card and a playhead at that bar lines up across them. Runs when the cards change, the view resizes and the song's bars change.
    /// </summary>
    private void ShareBars()
    {
        // Perf E2b (H1/H2): deferred to the first layout. NaturalBarWidths/FitCards force layout; running them
        // with no height (constructor, detached rebuild, pre-layout SizeChanged) only wastes the entry cost.
        if (!_attached || Bounds.Height <= 1) { _sharePending = true; return; }
        _sharePending = false;
        FitCards();
        // The fit may have deferred for want of the staffs' first layout; the bars wait for the same retry.
        if (_sharePending) return;
        double[]? widest = null;
        foreach (var index in _current.Shown)
        {
            if (!_current.Panels.TryGetValue(index, out var panel)) { _sharePending = true; return; }
            if (panel.Staff.NaturalBarWidths() is not { } natural) continue;
            if (widest is null) { widest = (double[])natural.Clone(); continue; }
            if (natural.Length > widest.Length) Array.Resize(ref widest, natural.Length);
            for (var bar = 0; bar < natural.Length; bar++) widest[bar] = Math.Max(widest[bar], natural[bar]);
        }
        foreach (var index in _current.Shown) if (_current.Panels.TryGetValue(index, out var panel)) panel.Staff.ShareBarWidths(widest);
        SlideCards();
    }
}
