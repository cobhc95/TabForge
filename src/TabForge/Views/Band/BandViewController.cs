using System.Diagnostics;
using System.Windows;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Visualization;

namespace TabForge.Views.Band;

/// <summary>What the Band view needs from its window, beyond the pane basics.</summary>
internal interface IBandViewHost : IPaneHost
{
    /// <summary>The song on show, for its playback position and timeline.</summary>
    DocumentSession ActiveDocument { get; }
    /// <summary>The options shared with the instrument pane: left-handed, note names, scale highlight, preview horizon.</summary>
    (bool LeftHanded, bool ShowNoteNames, string? Scale, int Horizon) InstrumentOptions { get; }
    VisualOptions? Visual { get; }
    /// <summary>A click in a lane: select the track and put the cursor (and playback, as the score does) on the bar and cell.</summary>
    void ShowCursor(int trackIndex, int bar, int cell);
    /// <summary>Moves a track in the song's own order, as dragging it on the timeline does (the sync setting uses it).</summary>
    void MoveSongTrack(int from, int to);
    /// <summary>Runs the Band menu's items the window owns: the score zoom and the Band settings door.</summary>
    void RunScoreMenu(Views.MenuSpec spec);
}

// Owns: the Band view's rows and its frame tick (only while the panel is on screen): a row for each shown track (pills, drag order and
//   row heights live in BandLayoutState), the shared playhead position handed to every lane, the instruments' notes (now and next while
//   playing, the cursor's notes when stopped), the lanes' sounding-note glow and the Band preferences (instrument size, lane content, follow
//   mode, rows per screen, order sync) applied to the rows.
// Does not own: the lane alignment (BandLaneAligner), playback timing (read from the document), the engraving (BandLane), the dock panel, the layout preset or the song's track order.
// Tests: TestBandViewRows, TestBandFrameClock, TestBandLaneCache, TestBandLaneClick, TestBandLayoutPreset, TestBandPillsAndRows, TestBandReorder, TestBandNoteGlow, TestBandSettings.
internal sealed class BandViewController : IDisposable
{
    private const double InstrumentIntervalMs = 33;
    private const double EngraveQuietMs = 100;
    private const double QuietCheckMs = 250;
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(60);

    private readonly IBandViewHost _host;
    private readonly BandLayoutState _state;
    private readonly FrameTicker _ticker = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private SongProject? _project;
    private int _revision = -1;
    private int _pillKey;
    private BandAppearance.Key _look;
    private int _lastBar = -2;
    private int _lastCell = -2;
    private double _lastFraction = -1;
    private bool _instrumentsDirty = true;
    private bool _glowShown;
    private double _lastInstrumentMs = double.NegativeInfinity;
    private double _changedAt = -1;
    private bool _disposed;
    private bool _playing;
    private double _checkedAt = double.NegativeInfinity;
    private int _checkedVersion = -1;
    private string _size = "", _content = "";
    private BandFollow _follow = BandFollow.Continuous;
    private readonly BandScroll _scroll = new();
    private string _layoutName = "";
    private readonly BandLaneAligner _aligner = new();
    private int _fittedRows;

    /// <summary>Export only: while playing, every tick checks the rows and refreshes the instruments, instead of waiting for the wall clock, so a frame's pixels do not depend on the machine's speed. The live view leaves it off.</summary>
    internal bool UseFrameClock { get; set; }

    public BandViewController(IBandViewHost host)
    {
        _host = host;
        _state = new BandLayoutState(() => Band);
        View = new BandView(_state);
        View.PillClicked += track => { _state.Toggle(track); Sync(); };
        View.RowDropped += OnRowDropped;
        View.LaneZoomWheel += up => ChangeLaneZoom(up ? BandChoices.LaneZoomStep : 1 / BandChoices.LaneZoomStep);
        View.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(); };
        _ticker.Tick += (_, _) => Tick();
        View.IsVisibleChanged += OnVisibleChanged;
    }

    private void OnVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (View.IsVisible) Start(); else _ticker.Stop();
    }

    public BandView View { get; }

    private BandSettings Band => _host.Settings.Timeline.Band ??= new BandSettings();

    /// <summary>A dragged row was dropped: it takes its new place in the Band; with the sync setting on the song's track moves too.</summary>
    private void OnRowDropped(BandRow row, int index)
    {
        var from = _host.Project.Tracks.IndexOf(row.Track);
        _state.MoveShown(row.Track, index);
        if (Band.KeepOrderInSync && from >= 0) _host.MoveSongTrack(from, _state.Order.ToList().IndexOf(row.Track));
        Sync();
    }

    internal BandLayoutState State => _state;

    /// <summary>How many times the rows were rebuilt (a self-test counter).</summary>
    public int Rebuilds { get; private set; }

    /// <summary>How many times the instruments were handed new notes (a self-test counter).</summary>
    public int InstrumentRefreshes { get; private set; }

    /// <summary>A pretend playing position for off-screen captures (bar, fraction of it, song time, the song's timeline); null in normal use.</summary>
    internal (int Bar, double Fraction, double Ms, ScoreTimeline Timeline)? ProbePlay { get; set; }

    private ScoreTimeline? TimelineNow => ProbePlay?.Timeline ?? _host.ActiveDocument.Playback.Timeline;

    public bool IsTicking => _ticker.IsEnabled;

    /// <summary>Runs a Band view command by its hotkey id; false for any other id.</summary>
    public bool Run(string id)
    {
        if (_disposed) return false;
        switch (id)
        {
            case "Band.ToggleTrackRow": ToggleSelectedTrackRow(); return true;
            case "Band.RowsMore": ChangeRowsPerScreen(1); return true;
            case "Band.RowsFewer": ChangeRowsPerScreen(-1); return true;
            case "Band.CycleLaneContent": Band.LaneContent = BandChoices.Next(BandChoices.Contents, BandChoices.NormalizeContent(Band.LaneContent)); Remember("Band view lanes: " + Band.LaneContent); return true;
            case "Band.CycleLaneLayout": Band.LaneLayout = BandChoices.Next(BandChoices.Layouts, BandChoices.NormalizeLayout(Band.LaneLayout)); Remember("Band view lane layout: " + Band.LaneLayout); return true;
            case "Band.CycleInstrumentSize": Band.InstrumentSize = BandChoices.Next(BandChoices.Sizes, BandChoices.NormalizeSize(Band.InstrumentSize)); Remember("Band view instruments: " + Band.InstrumentSize); return true;
            case "Band.ToggleSmoothFollow": Band.FollowLikeScore = !Band.FollowsScore; Remember("Band view follow: " + (Band.FollowsScore ? "like the score" : Band.SmoothFollow ? "smooth" : "page by page")); return true;
            case "Band.ResetRowHeights": _state.ResetHeights(); View.FitRows(); _host.SetStatus("Band view: row heights reset"); return true;
        }
        return false;
    }

    /// <summary>The right-click menu: what the lanes show, instrument size, rows per screen, row heights, follow, zoom and Band settings.</summary>
    private void ShowMenu()
    {
        if (_disposed) return;
        var band = Band;
        var state = new Views.BandMenuState(BandChoices.NormalizeContent(band.LaneContent), BandChoices.NormalizeSize(band.InstrumentSize), _state.RowsPerScreen, band.FollowsScore, band.WidthPerRow, BandChoices.NormalizePlayhead(band.PlayheadLine), BandChoices.NormalizeLayout(band.LaneLayout));
        var menu = Views.SpecMenus.New("Band view options", Views.BandMenus.Build(state, id => HotkeyCatalog.DisplayAll(_host.Settings.Hotkeys, id)), RunMenu, View);
        LastMenu = menu;
        Views.SpecMenus.Open(menu, View, null, false);
    }

    /// <summary>The last right-click menu built (for tests and --capture).</summary>
    internal System.Windows.Controls.ContextMenu? LastMenu { get; private set; }

    internal void RunMenu(Views.MenuSpec spec)
    {
        switch (spec.Id)
        {
            case Views.BandMenus.ContentId: Band.LaneContent = spec.Arg ?? BandChoices.Tab; Remember("Band view lanes: " + Band.LaneContent); break;
            case Views.BandMenus.LayoutId: Band.LaneLayout = spec.Arg ?? BandChoices.Vertical; Remember("Band view lane layout: " + Band.LaneLayout); break;
            case Views.BandMenus.SizeId: Band.InstrumentSize = spec.Arg ?? BandChoices.FullNeck; Remember("Band view instruments: " + Band.InstrumentSize); break;
            case Views.BandMenus.RowsId: ChangeRowsPerScreen(int.Parse(spec.Arg ?? "3") - _state.RowsPerScreen); break;
            case Views.BandMenus.ResetHeightsId: Run("Band.ResetRowHeights"); break;
            case Views.BandMenus.WidthModeId: Band.WidthPerRow = spec.Arg == Views.BandMenus.ThisRow; Remember("Band view instrument width: " + (Band.WidthPerRow ? "this row only" : "all rows")); break;
            case Views.BandMenus.ResetWidthsId: Band.InstrumentWidth = 0; _state.ResetWidths(); Remember("Band view: instrument widths reset"); break;
            case Views.BandMenus.PlayheadId: Band.PlayheadLine = BandChoices.NormalizePlayhead(spec.Arg); Remember("Band view playhead line: " + Band.PlayheadLine); break;
            case Views.BandMenus.LaneZoomInId: ChangeLaneZoom(BandChoices.LaneZoomStep); break;
            case Views.BandMenus.LaneZoomOutId: ChangeLaneZoom(1 / BandChoices.LaneZoomStep); break;
            case Views.BandMenus.LaneZoomResetId: ChangeLaneZoom(0); break;
            case Views.BandMenus.ResetViewId: ResetView(); break;
            case Views.BandMenus.FollowId: Run("Band.ToggleSmoothFollow"); break;
            default: _host.RunScoreMenu(spec); break;
        }
    }

    /// <summary>Scales the Band lanes' zoom by <paramref name="factor"/> (0 = back to 1); every lane follows at once.</summary>
    internal void ChangeLaneZoom(double factor)
    {
        if (_disposed) return;
        Band.LaneZoom = factor == 0 ? 1 : BandChoices.ClampZoom(Band.LaneZoom * factor);
        Remember("Band view lane zoom: " + (int)Math.Round(Band.LaneZoom * 100) + "%");
    }

    /// <summary>Puts the view back to its defaults (heights, widths, rows per screen, instruments, zoom, layout, playhead line); the shown tracks and their order stay.</summary>
    internal void ResetView()
    {
        if (_disposed) return;
        _state.ResetView();
        var band = Band;
        band.InstrumentWidth = 0; band.LaneZoom = 1;
        band.LaneLayout = BandChoices.Vertical; band.PlayheadLine = BandChoices.TabOnly;
        View.FitRows();
        Remember("Band view reset to its defaults");
    }

    /// <summary>Capture/test door: shows or hides the instrument of the track at <paramref name="trackIndex"/>.</summary>
    internal void ToggleInstrumentOf(int trackIndex)
    {
        if (trackIndex < 0 || trackIndex >= _host.Project.Tracks.Count) return;
        _state.ToggleInstrument(_host.Project.Tracks[trackIndex]);
        _instrumentsDirty = true;
        Sync();
    }

    private void Remember(string status)
    {
        _host.SaveSettings();
        _host.SetStatus(status);
        Sync();
    }

    /// <summary>The command: show or hide the selected track's row.</summary>
    public void ToggleSelectedTrackRow()
    {
        if (_host.SelectedTrack is not { } track || _disposed) return;
        _state.Sync(_host.Project);
        _state.Toggle(track);
        Sync();
    }

    /// <summary>The command: one more (+1) or one fewer (-1) row on the screen at once, between 1 and 5.</summary>
    public void ChangeRowsPerScreen(int delta)
    {
        if (_disposed) return;
        _state.ChangeRowsPerScreen(delta);
        View.FitRows();
        _host.SetStatus("Band view: " + _state.RowsPerScreen + (_state.RowsPerScreen == 1 ? " row" : " rows") + " per screen");
    }

    private void Start()
    {
        if (_disposed) return;
        _instrumentsDirty = true;
        _lastBar = -2;
        Tick();
        _ticker.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _ticker.Stop();
        View.IsVisibleChanged -= OnVisibleChanged;
        View.Reorder.Cancel();
        View.SetRows(Array.Empty<BandRow>());
    }

    /// <summary>One frame: follows song, track and look changes, then moves the playhead and the instruments.</summary>
    internal void Tick()
    {
        if (_disposed) return;
        var doc = _host.ActiveDocument;
        var playback = doc.Playback;
        if (ProbePlay is { } probe)
        {
            _playing = true;
            Sync();
            if (!_ticker.IsEnabled || _ticker.Interval != TimeSpan.Zero) _ticker.Interval = TimeSpan.Zero;
            Apply(probe.Bar, probe.Fraction, probe.Ms, true, false);
            return;
        }
        var playing = playback.IsPlayingVisual && playback.Engine.IsPlaying && playback.PlayheadBar >= 0;
        _playing = playing;
        Sync();
        var interval = playing ? TimeSpan.Zero : IdleInterval;
        if (_ticker.Interval != interval) _ticker.Interval = interval;
        if (playing) Apply(playback.PlayheadBar, playback.PlayheadFraction, playback.PlayheadMs, true, playback.Engine.IsPaused);
        else
        {
            var editor = _host.Editor;
            var slots = Math.Max(1, MusicTime.BarSlots(_host.Project, editor.SelectedMeasure));
            Apply(editor.SelectedMeasure, editor.SelectedCell / (double)slots, 0, false, false, editor.SelectedCell);
        }
    }

    /// <summary>Puts every lane's playhead on a bar and a fraction of it, and refreshes the instruments when due. <paramref name="cell"/> is the cursor's cell while stopped.</summary>
    internal void Apply(int bar, double fraction, double ms, bool playing, bool paused, int cell = -1)
    {
        var moved = bar != _lastBar || Math.Abs(fraction - _lastFraction) > 1e-6;
        if (moved)
        {
            _scroll.Show(View.Rows, bar, fraction, playing, _follow);
        }
        if (playing || _glowShown)
        {
            var timeline = TimelineNow;
            var remap = _host.ActiveDocument.Playback.PlaybackBarRemap;
            var rows = View.Rows;
            for (var i = 0; i < rows.Count; i++)
            {
                if (playing) MarkNotes(rows[i], timeline, ms, remap); else rows[i].Lane.ShowSounding(Array.Empty<(int, int, int, string, bool)>());
            }
            _glowShown = playing;
        }
        var now = _clock.Elapsed.TotalMilliseconds;
        var due = _instrumentsDirty || bar != _lastBar || cell != _lastCell || (playing && (UseFrameClock || now - _lastInstrumentMs >= InstrumentIntervalMs));
        _lastBar = bar; _lastFraction = fraction; _lastCell = cell;
        if (!due) return;
        _instrumentsDirty = false;
        _lastInstrumentMs = now;
        RefreshInstruments(ms, playing, paused, bar, cell);
    }

    /// <summary>Rebuilds the rows when the song, its tracks or the shown set changed; engraves the lanes again when the content or the look changed.</summary>
    internal void Sync()
    {
        var project = _host.Project;
        var rows = View.Rows;
        // While playing, a frame with no edit and no change of rows skips the checks below; they run at least every QuietCheckMs.
        var nowMs = _clock.Elapsed.TotalMilliseconds;
        if (!UseFrameClock && _playing && ReferenceEquals(project, _project) && project.ContentRevision == _revision && _state.Version == _checkedVersion
            && _changedAt < 0 && nowMs - _checkedAt < QuietCheckMs) return;
        _checkedAt = nowMs;
        _state.Sync(project);
        _checkedVersion = _state.Version;
        if (_state.RowsPerScreen != _fittedRows) { _fittedRows = _state.RowsPerScreen; View.FitRows(); }
        if (!ReferenceEquals(project, _project) || !SameRows(rows, project))
        {
            Rebuild(project);
            return;
        }
        ApplyOptions(rows);
        ApplyLayout(rows);
        var look = BandAppearance.KeyOf(_host.Editor.Appearance);
        var lookChanged = look != _look;
        _look = look;
        var selected = _host.SelectedTrack;
        var changed = project.ContentRevision != _revision;
        _revision = project.ContentRevision;
        // Edits arrive in bursts: the lanes engrave again once the song has been quiet for a moment.
        var now = _clock.Elapsed.TotalMilliseconds;
        if (changed) _changedAt = now;
        var engrave = _changedAt >= 0 && (UseFrameClock || now - _changedAt >= EngraveQuietMs);
        if (engrave) _changedAt = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            row.SetSelected(ReferenceEquals(row.Track, selected));
            if (engrave) row.Lane.SongChanged();
            if (lookChanged) row.Lane.ApplyLook(_host.Editor.Appearance);
        }
        if (engrave || lookChanged) AlignLanes();
        if (changed || lookChanged || engrave) { _instrumentsDirty = true; _lastBar = -2; }
    }

    /// <summary>Hands a changed preference to the rows: lane content and follow mode to the lanes, instrument size to the next instrument refresh.</summary>
    private void ApplyOptions(IReadOnlyList<BandRow> rows)
    {
        var band = Band;
        var size = BandChoices.NormalizeSize(band.InstrumentSize);
        if (size != _size) { _size = size; _instrumentsDirty = true; }
        var content = BandChoices.NormalizeContent(band.LaneContent);
        var follow = BandFollow.Resolve(_host.Settings.Follow, band);
        var layout = BandChoices.NormalizeLayout(band.LaneLayout);
        if (layout != _layoutName)
        {
            _layoutName = layout;
            _scroll.Vertical = layout == BandChoices.Vertical;
            _scroll.Reset();
            for (var i = 0; i < rows.Count; i++) rows[i].Lane.Vertical = _scroll.Vertical;
            AlignLanes();
            _instrumentsDirty = true;
            _lastBar = -2;
        }
        if (content == _content && follow == _follow) return;
        var contentChanged = content != _content;
        _content = content; _follow = follow;
        for (var i = 0; i < rows.Count; i++) { rows[i].Lane.Follow = _follow; if (contentChanged) rows[i].Lane.SetContent(content); }
        if (contentChanged) AlignLanes();
        _instrumentsDirty = true;
        _lastBar = -2;
    }

    /// <summary>True when the rows are the shown tracks, in order, each as built, and the pills still match the song's tracks.</summary>
    private bool SameRows(IReadOnlyList<BandRow> rows, SongProject project)
    {
        var shown = _state.ShownTracks();
        if (rows.Count != shown.Count || PillKey(project) != _pillKey) return false;
        for (var i = 0; i < rows.Count; i++)
            if (!ReferenceEquals(rows[i].Track, shown[i]) || rows[i].Key != BandRow.KeyOf(shown[i]) || rows[i].TrackIndex != project.Tracks.IndexOf(shown[i])) return false;
        return true;
    }

    private int PillKey(SongProject project)
    {
        var hash = new HashCode();
        foreach (var track in project.Tracks) { hash.Add(track.Name); hash.Add(track.ColorHex); hash.Add(_state.IsShown(track)); }
        return hash.ToHashCode();
    }

    /// <summary>Builds the pills and a row for each shown track; a row that is still right is kept, so its lane is not engraved again.</summary>
    private void Rebuild(SongProject project)
    {
        Rebuilds++;
        _project = project;
        _revision = project.ContentRevision;
        var look = _host.Editor.Appearance;
        _look = BandAppearance.KeyOf(look);
        var old = View.Rows.ToDictionary(r => r.Track);
        var rows = new List<BandRow>();
        foreach (var track in _state.ShownTracks())
        {
            var index = project.Tracks.IndexOf(track);
            if (old.TryGetValue(track, out var kept) && kept.Key == BandRow.KeyOf(track) && kept.TrackIndex == index) { rows.Add(kept); continue; }
            var row = new BandRow(track, index, project, look, Band, BandFollow.Resolve(_host.Settings.Follow, Band));
            row.Lane.Vertical = _scroll.Vertical;
            row.Lane.WantedZoomChanged += AlignLanes;
            row.Lane.Clicked += (bar, cell) => _host.ShowCursor(_host.Project.Tracks.IndexOf(track), bar, cell);
            row.InstrumentToggleRequested += () => { _state.ToggleInstrument(track); _instrumentsDirty = true; Sync(); };
            row.WidthRequested += w => SetInstrumentWidth(track, w);
            row.WidthReset += () => { if (Band.WidthPerRow) _state.ResetWidth(track); else Band.InstrumentWidth = 0; _host.SaveSettings(); Sync(); };
            row.WidthCommitted += _host.SaveSettings;
            View.Adopt(row);
            rows.Add(row);
        }
        View.SetPills(project.Tracks);
        _pillKey = PillKey(project);
        View.SetRows(rows);
        AlignLanes();
        ApplyLayout(rows);
        _instrumentsDirty = true;
        _lastBar = -2;
    }

    /// <summary>Gives every lane the widest of each bar and the smallest wanted zoom, so one bar has the same place and size in all of them.</summary>
    internal void AlignLanes()
    {
        if (_disposed) return;
        if (_aligner.Align(View.Rows)) _lastBar = -2;
    }

    /// <summary>A grip drag: the shared width, or this row's own in "this row only" mode (widths change on drag only, never per frame).</summary>
    private void SetInstrumentWidth(TrackModel track, double width)
    {
        if (Band.WidthPerRow) _state.SetWidth(track, width); else Band.InstrumentWidth = BandChoices.ClampWidth(width);
        Sync();
    }

    /// <summary>Hands each row its hidden instrument, instrument width and playhead line length.</summary>
    private void ApplyLayout(IReadOnlyList<BandRow> rows)
    {
        var band = Band;
        var full = BandChoices.NormalizePlayhead(band.PlayheadLine) == BandChoices.FullRow;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var width = band.WidthPerRow ? _state.WidthOf(row.Track) ?? band.InstrumentWidth : band.InstrumentWidth;
            row.ApplyInstrument(_state.IsInstrumentHidden(row.Track), width);
            row.Lane.UserZoom = band.LaneZoom;
            row.Lane.PlayheadFullRow = full;
        }
    }

    private void RefreshInstruments(double ms, bool playing, bool paused, int bar, int cell)
    {
        InstrumentRefreshes++;
        var options = _host.InstrumentOptions;
        var timeline = TimelineNow;
        var rows = View.Rows;
        var size = BandChoices.NormalizeSize(Band.InstrumentSize);
        var frets = size == BandChoices.TwelveFrets ? 12 : 24;
        var small = size == BandChoices.SmallKeyboard;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Track.IsAudio || row.InstrumentHidden) continue;
            var track = row.Track;
            // While playing the instrument shows the notes sounding now and the next ones; stopped, the notes under the cursor.
            var state = InstrumentVisualizer.Build(_host.Project, track, timeline, ms, playing, paused, options.Horizon,
                options.LeftHanded, options.ShowNoteNames, options.Scale, frets, 0, _host.Visual);
            state.Kind = InstrumentVisualizer.NaturalKind(track);
            InstrumentPanelController.ApplyAppearance(state, _host.Settings);
            if (small) state.KeyboardKeys = BandChoices.SmallKeys;
            row.Instrument.SetState(state);
            if (!playing)
            {
                // Only a stopped song shows the cursor's notes; while playing the last ones are kept.
                var editor = _host.Editor;
                var cursor = InstrumentVisualizer.BuildEditingSelection(track, CellAt(track, editor.SelectedMeasure, editor.SelectedCell),
                    options.LeftHanded, options.ShowNoteNames, options.Scale, frets, _host.Visual);
                cursor.Kind = state.Kind;
                InstrumentPanelController.ApplyAppearance(cursor, _host.Settings);
                if (small) cursor.KeyboardKeys = BandChoices.SmallKeys;
                row.Instrument.SetEditingSelection(cursor);
            }
        }
    }

    private static TabCell? CellAt(TrackModel track, int bar, int cell) =>
        bar >= 0 && bar < track.Measures.Count && cell >= 0 && cell < track.Measures[bar].Cells.Count ? track.Measures[bar].Cells[cell] : null;

    /// <summary>The notes sounding at <paramref name="ms"/> (and those just struck) on the row's tab, as the score marks them.</summary>
    private static void MarkNotes(BandRow row, ScoreTimeline? timeline, double ms, int[]? remap)
    {
        var notes = timeline is null ? Array.Empty<NoteEvent>() : timeline.NotesFor(row.TrackIndex);
        if (notes.Length == 0 || ms < 0) { row.Lane.ShowSounding(Array.Empty<(int, int, int, string, bool)>()); return; }
        var struck = NoteTimeline.StruckWithin(notes, ms, 130);
        var list = new List<(int Bar, int Cell, int String, string Label, bool Struck)>();
        foreach (var n in NoteTimeline.SoundingAt(notes, ms))
        {
            var bar = remap is not null && n.Bar >= 0 && n.Bar < remap.Length ? remap[n.Bar] : n.Bar;
            list.Add((bar, n.Cell, n.StringIndex, BandNoteGlow.LabelOf(row.Track, n.Fret, n.Midi, n.Dead), struck.Contains(n)));
        }
        row.Lane.ShowSounding(list);
    }
}
