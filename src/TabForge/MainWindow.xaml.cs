using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

public partial class MainWindow : Window
{
    private readonly DocumentManager _documents = new();
    private readonly DocumentController _documentController = new();
    private readonly ArrangementController _arrangementController = new();
    private readonly TrackController _trackController = new();
    private readonly ISettingsWindowHost _settingsWindowHost;
    private PlaybackEngine _midi => Doc.Playback.Engine;
    private DocumentSession? _observedPlaybackDocument;
    private MainWindow? _attachTarget;
    private int _attachTargetIndex = -1;
    private WpfResizeBorderFrame? _resizeBorderFrame;
    private WpfCaptionButtonFrame? _captionButtonFrame;
    private bool _restoring;
    // Loop state belongs to the song (DocumentSession); these forward to the active tab.
    private bool _loop { get => Doc.LoopEnabled; set => Doc.LoopEnabled = value; }
    private int _loopStartBar { get => Doc.LoopStartBar; set => Doc.LoopStartBar = value; }
    private int _loopEndBar { get => Doc.LoopEndBar; set => Doc.LoopEndBar = value; }
    private int _loopStartCell { get => Doc.LoopStartCell; set => Doc.LoopStartCell = value; }
    private int _loopEndCell { get => Doc.LoopEndCell; set => Doc.LoopEndCell = value; }
    private bool _metronome;
    private bool _syncingMetronomeSettings;
    private bool _mainWindowInitialized;
    private DispatcherTimer? _metronomeSettingsSaveTimer;
    private bool _countIn;
    private double _speed = 1.0;
    private bool _fullscreen;
    private bool _instrumentDragArmed;
    private bool _instrumentDragging;
    private bool _instrumentGestureMoved;
    private Point _instrumentDragStart;
    private UndoSnapshot? _sectionUndoSnapshot;
    private UndoSnapshot? _trackUndoSnapshot;
    private UndoController.UndoTransaction? _mixUndoTransaction;
    private bool _mixUndoChanged;
    private UndoController.UndoTransaction? _trackEditUndoTransaction;
    private bool _previewNotes = true;
    private double _zoomFactor;
    // ---- keep-the-score-in-view ----
    private readonly ScoreFollowCoordinator _follow;
    private bool _leftHanded;
    private bool _suppressWorkspaceSave;
    private bool _showNoteNames;
    private int _previewHorizon = 4;
    private string? _scaleHighlight;

    // These aliases keep the active editor concise while the backing state stays with its document.
    private DocumentPlaybackState Playback => Doc.Playback;
    private ScoreTimeline? _timeline { get => Playback.Timeline; set => Playback.Timeline = value; }
    private double _playheadMs { get => Playback.PlayheadMs; set => Playback.PlayheadMs = value; }
    private int[]? _playbackBarRemap { get => Playback.PlaybackBarRemap; set => Playback.PlaybackBarRemap = value; }
    private Dictionary<string, int[]> _playbackBarMappingsBySnapshot => Playback.PlaybackBarMappingsBySnapshot;
    private int _playheadBar { get => Playback.PlayheadBar; set => Playback.PlayheadBar = value; }
    private int _playheadCell { get => Playback.PlayheadCell; set => Playback.PlayheadCell = value; }
    private double _playheadFraction { get => Playback.PlayheadFraction; set => Playback.PlayheadFraction = value; }
    private bool _isPlayingVisual { get => Playback.IsPlayingVisual; set => Playback.IsPlayingVisual = value; }
    private MarkerModel? _playingSectionMarker;
    private bool _syncingPlayingSectionSelection;

    // Coalesced UI updates: the engine reports ~60 times a second, but the UI is refreshed on a
    // fixed cadence from the latest position. This keeps a slow render from queueing unbounded
    // dispatcher work (which previously made the app look frozen during playback).
    private readonly Shell.FrameTicker _playbackUiTick;
    private int _fretboardFrets = 24;
    private int _scoreWheelScrollPixels = 32;

    // Per-document state is kept in the active DocumentSession.
    private DocumentSession Doc => _documents.Active;

    /// <summary>Every song open in this window (used by crash recovery).</summary>
    internal IReadOnlyList<DocumentSession> OpenDocuments => _documents.Documents;
    private SongProject _project { get => Doc.Project; set => Doc.Project = value; }
    private UndoController _undo => Doc.Undo;
    private string? _currentPath { get => Doc.Path; set => Doc.Path = value; }

    public ObservableCollection<MidiOutputDeviceInfo> MidiDevices { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        _follow = new ScoreFollowCoordinator(ScoreScroll, Editor, () => _isPlayingVisual, () => _midi.IsPaused,
            () => _playheadBar, () => _playheadFraction, MaxMeasures, () => _settings.Follow);
        WireScoreScrollGestures();
        _settingsWindowHost = new WpfSettingsWindowHost(this);
        _mainWindowInitialized = true;
        Loaded += (_, _) => ScheduleAutomaticUpdateCheck();
        _metronomeSettingsSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(280)
        };
        _metronomeSettingsSaveTimer.Tick += (_, _) =>
        {
            _metronomeSettingsSaveTimer.Stop();
            SaveSettings();
        };
        _documents.AddNew();
        StartAutosave();
        BuildToolsPalette();
        BuildPinnedToolStrip();
        Arrangement.SetPlaybar(PlaybarControls);
        // The right-click popups are declared next to the floating bar, which stays hidden once the
        // playbar moves into the arrangement header; move them along so they can actually open.
        if (PlaybarControls.Child is Panel playbarPanel)
        {
            foreach (var popup in new[] { MetronomeSettingsPopup, CountInSettingsPopup, LoopSettingsPopup })
            {
                if (popup.Parent is Panel owner) owner.Children.Remove(popup);
                playbarPanel.Children.Add(popup);
            }
        }
        MetronomeSettingsPopup.PlacementTarget = MetronomeButton;
        CountInSettingsPopup.PlacementTarget = CountInButton;
        LoopSettingsPopup.PlacementTarget = LoopButton;
        WireMixerHeader();
        Arrangement.AttachTimelineScrollBar(TimelineScrollHost);
        InitializeDockWorkspace();
        Arrangement.RefreshEmptyAreaMenus();   // now that the dock's own items exist
        SourceInitialized += (_, _) =>
        {
            _resizeBorderFrame = new WpfResizeBorderFrame(this);
            // Windows 11 Snap Layouts: the maximise button answers WM_NCHITTEST with HTMAXBUTTON.
            _captionButtonFrame = new WpfCaptionButtonFrame(this, MinButton, MaxButton, CloseButton);
        };
        DataContext = this;
        WireTabs();

        Editor.EditStarting += (_, _) => CaptureUndo();
        Editor.Edited += (_, _) =>
        {
            _project.IsDirty = true;
            var changed = Editor.AffectedMeasureRange;
            Arrangement.InvalidateActivities(Editor.SelectedTrackIndex, changed.FirstMeasure, changed.LastMeasure);
            RefreshArrangementScore();
            // An edit in the score can remove bars under the range (the editor clamps it): the model follows.
            PushEditorSelectionToModel();
            RefreshInstrument();
            RefreshStatus();
            RefreshTabs();
            UpdateTitle();
            RefreshToolsPalette();
        };
        Editor.SelectionChanged += (_, _) => OnEditorSelectionChanged();
        Editor.PlayRequested += (_, _) => TogglePlayback();
        Editor.NotePreview += (_, e) => { if (_previewNotes) _midi.PreviewNote(e.DeviceId, e.Channel, e.Program, e.Midi, _settings.Audio.PreviewLengthMs); };
        Editor.StatusMessage += (_, msg) => StatusText.Text = msg;
        Editor.ContextMenuRequested += (_, args) =>
        {
            if (args.OnNote) ShowNoteContextMenu(args);
            else ShowScoreContextMenu(args.Position);
        };
        ScoreScroll.MouseRightButtonUp += (s, e) => { if (!Editor.IsMouseOver) { ShowScoreContextMenu(e.GetPosition(ScoreScroll)); e.Handled = true; } };
        ScoreScroll.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (!Editor.IsMouseOver) Editor.ClearSelection();
        };
        // A plain click on the timeline (not a drag, not a clip/section/area-move gesture) clears the shared
        // selection in both views, exactly like a click on empty score paper. The timeline decides "plain" on
        // mouse-up, so a range drag no longer clears first and a clip click keeps selecting its clip.
        Arrangement.SelectionClearRequested += (_, _) => _selection.Clear(SelectionOrigin.Timeline);
        InitSelectionModel();

        Instrument.PreviewMouseLeftButtonDown += Instrument_MouseLeftButtonDown;
        Instrument.PreviewMouseMove += Instrument_MouseMove;
        Instrument.PreviewMouseLeftButtonUp += Instrument_MouseLeftButtonUp;
        Instrument.LostMouseCapture += Instrument_LostMouseCapture;
        Instrument.MouseRightButtonUp += Instrument_MouseRightButtonUp;
        Instrument.LegendAnchorChanged += PlaceScaleFinderButton;
        InstrumentOverlay.SizeChanged += (_, _) => PlaceScaleFinderButton(_scaleButtonAnchor);
        Arrangement.BarSelected += (_, bar) =>
        {
            // Only moves the caret (the ruler seeks). Clearing the range is the timeline's plain-click event:
            // the old silent ClearSelection(notify: false) here left the timeline area selected.
            Editor.SetBar(bar);
            ScrollToCursor();
            RefreshArrangementSelection();
            Editor.Focus();
        };
        Arrangement.TrackSelected += (_, index) => { if (index >= 0 && index < _project.Tracks.Count) TrackMixerGrid.SelectedIndex = index; };
        // Dragging a range can report many bar changes per frame: apply only the latest, once per frame.
        (int start, int end)? pendingRange = null;
        Arrangement.RangeSelected += (_, range) =>
        {
            var queued = pendingRange is not null;
            pendingRange = range;
            if (queued) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                // The model applies it to the score (active track, which a lane press already switched to) and the area.
                if (pendingRange is { } latest)
                    _selection.SetRange(Editor.SelectedTrackIndex, latest.start, latest.end, SelectionOrigin.Timeline);
                pendingRange = null;
            }));
        };
        Arrangement.ProjectEdited += (_, _) =>
        {
            CompleteTrackEditUndo();
            OnArrangementEdited();
        };
        Arrangement.MuteSoloChanged += (_, _) =>
        {
            CompleteTrackEditUndo();
            CommitEdit(EditRefresh.Arrangement);
            _midi.SetMuteSolo(_project);
            SyncAudioEngine(); // audio clips / monitored input follow mute and solo
            _mixerWindow?.SyncValues();
        };
        Arrangement.TrackColorChanged += (_, _) =>
        {
            CompleteTrackEditUndo();
            OnArrangementTrackColorChanged();
        };
        Arrangement.TrackEditRequested += request =>
        {
            var transaction = _undo.BeginTransaction(_project);
            if (_trackController.ApplyEdit(_project, request))
                _trackEditUndoTransaction = transaction;
            else
                _undo.Cancel(transaction);
        };
        Arrangement.MixEditStarting += (_, _) =>
        {
            _mixUndoTransaction ??= _undo.BeginTransaction(_project);
        };
        Arrangement.MixChanged += (_, _) =>
        {
            if (_mixUndoTransaction is not null) _mixUndoChanged = true;
            OnArrangementMixChanged();
            SyncAudioEngine(); // cheap: only changed levels are sent
        };
        Arrangement.MixEditEnded += (_, _) =>
        {
            if (_mixUndoTransaction is not { } transaction) return;
            if (_mixUndoChanged)
            {
                var capture = _undo.Commit(transaction);
                if (capture.Stored) RememberPlaybackBarMapping(capture.Snapshot);
            }
            else _undo.Cancel(transaction);
            _mixUndoTransaction = null;
            _mixUndoChanged = false;
        };
        Arrangement.AddTrackRequested += (_, _) => AddTrackWithWindow();
        // Drag starts take the undo state before the drop edits the model (it only costs the bars changed since the last state).
        Arrangement.TrackDragStarted += (_, _) => _trackUndoSnapshot = _undo.Snapshot(_project);
        Arrangement.TrackReordered += (_, move) => MoveTrackTo(move.from, move.to);
        Arrangement.SectionDragStarted += (_, _) => _sectionUndoSnapshot = _undo.Snapshot(_project);
        Arrangement.SectionDragCancelled += (_, _) => _sectionUndoSnapshot = null;
        Arrangement.SectionReordered += (_, move) => MoveSection(move.from, move.insertBefore);
        Arrangement.SectionContextRequested += (_, markerIndex) => ShowSectionContextMenu(markerIndex);
        // A resize edits markers live during the drag: the state is taken before the first change.
        Arrangement.SectionResizeStarting += (_, _) => _sectionUndoSnapshot = _undo.Snapshot(_project);
        Arrangement.SectionMarkerMoved += (_, move) =>
        {
            var sections = SectionLayout.Sorted(_project);
            if (move.markerIndex < 0 || move.markerIndex >= sections.Count) return;
            var transaction = _undo.BeginTransaction(_project);
            if (!SectionLayout.MoveMarker(_project, sections[move.markerIndex], move.bar)) { _undo.Cancel(transaction); return; }
            _undo.Commit(transaction);
            CommitEdit(EditRefresh.Score | EditRefresh.Markers | EditRefresh.Arrangement);
            StatusText.Text = $"Section moved to bar {move.bar + 1} (bars unchanged; Ctrl+drag moves the bars too)";
        };
        Arrangement.SectionLaneContextRequested += (_, at) => ShowSectionLaneMenu(at.markerIndex, at.bar);
        Arrangement.SectionResized += (_, _) =>
        {
            var resizeStart = _sectionUndoSnapshot;
            _sectionUndoSnapshot = null;
            if (resizeStart is { } before)
            {
                var capture = _undo.Capture(before);
                if (capture.Stored) RememberPlaybackBarMapping(capture.Snapshot);
            }
            CommitEdit(EditRefresh.Score | EditRefresh.Arrangement);
        };
        Arrangement.MixerRequested += (_, _) => OpenMixer();
        Arrangement.FxChainRequested += (_, index) => { if (index >= 0 && index < _project.Tracks.Count) OpenFxChain(_project.Tracks[index]); };
        Arrangement.FxPowerRequested += (_, index) => { if (index >= 0 && index < _project.Tracks.Count) ToggleTrackChain(_project.Tracks[index]); };
        Arrangement.BusFxRequested += (_, group) => OpenBusFx(group);
        Arrangement.BusPowerRequested += (_, group) => ToggleBus(group);
        Arrangement.TrackOptionsRequested += (_, index) => { TrackMixerGrid.SelectedIndex = index; TrackProps_Click(this, new RoutedEventArgs()); };
        Arrangement.TimelineContextRequested += (_, context) => ShowArrangementContextMenu(context.bar, context.track);
        HookAudioLanes();
        Arrangement.AddTrackMenuRequested += ShowAddTrackMenu;
        Arrangement.GroupCollapseToggled += group =>
        {
            CaptureUndo();
            var collapsed = _project.Mixer.CollapsedGroups;
            if (!collapsed.Remove(group)) collapsed.Add(group);
            _project.IsDirty = true;
            RefreshTracks();
            RefreshArrangement();
            ScheduleFitTimelineToTracks();
            UpdateTitle();
        };
        Arrangement.GroupMoved += (start, count, before) =>
        {
            if (start < 0 || count <= 0 || start + count > _project.Tracks.Count) return;
            var orderBefore = CaptureOrderLayout();
            var capture = CaptureUndo();
            var moving = _project.Tracks.GetRange(start, count);
            if (!Models.TrackOrdering.MoveRun(_project, start, count, before))
            {
                if (capture is { } cancelled) _undo.Discard(cancelled);
                return;
            }
            _project.IsDirty = true;
            _project.MarkTimelineChanged();   // the first track (time signatures) may have changed
            SyncAudioEngine();
            if (_midi.IsPlaying) _midi.RefreshArrangement(_project, Enumerable.Range(0, MaxMeasures()).ToArray());
            RefreshTracks();
            RefreshArrangement();
            Editor.InvalidateScoreLayout();
            UpdateTitle();
            PlayOrderAnimation(orderBefore);   // the open mixer reorders with it, animating at the same time
            StatusText.Text = $"Moved the {Models.MixerGroups.GroupOf(_project, moving[0]).ToLowerInvariant()} group";
        };
        // Right-click on a group row opens the mixer at that group; the empty-area menus toggle "Show tracks in groups" (the mixer's
        // "Groups in track list" box is the same setting).
        Arrangement.GroupMixerRequested += group => { OpenMixer(); _mixerWindow?.RevealGroup(group); };
        Arrangement.GroupsShownState = () => _project.Mixer.ShowGroupsInTrackList;
        Arrangement.GroupsToggleRequested += on => ((IMixerHost)this).SetTrackListShows("groups", on);
        Arrangement.DockMenuItems = () => _dockWorkspace?.PanelMenuItems("timeline") ?? new List<System.Windows.Controls.Control>();
        _documents.Changed += (_, _) => RefreshTabs();
        PreviewMouseDown += (_, e) => Arrangement.DismissTrackNameEditOnClick(e.OriginalSource as DependencyObject);
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
        Loaded += (_, _) => TabWindowRegistry.Register(this);
        // Drawn text is shaped per control for that control's own display DPI (A-03: Draw.UseDpi in each OnRender), so two
        // windows on monitors with different scaling no longer share one value. Moving to another monitor re-renders.
        Loaded += (_, _) => ApplyGpuLayerCaches(VisualTreeHelper.GetDpi(this).PixelsPerDip);
        DpiChanged += (_, e) =>
        {
            ApplyGpuLayerCaches(e.NewDpi.PixelsPerDip);
            Editor.InvalidateScoreLayout();
            Arrangement.InvalidateTimeline();
            Instrument.InvalidateVisual();
        };
        Closed += (_, _) => { _resizeBorderFrame?.Dispose(); _captionButtonFrame?.Dispose(); ClearAttachTarget(); TabWindowRegistry.Unregister(this); };
        StateChanged += (_, _) => UpdateMaximiseGlyph();
        SizeChanged += (_, _) => ApplyLayout();

        // Fixed-cadence playback UI refresh; engine threads publish only into their document state.
        // Display-synchronised: one update per rendered frame at the monitor's refresh rate, only while playing.
        _playbackUiTick = new Shell.FrameTicker();
        _playbackUiTick.Tick += (_, _) => ApplyPendingPlayhead();

        // Score follow has its own render-priority timer so vertical movement glides without
        // increasing the cadence of playback-position, arrangement, or audio updates.

        ScoreScroll.ScrollChanged += ScoreScroll_ScrollChanged;

        LoadSettings();
        ApplyPreferredScoreView(_documents.Active);
        UpdateSpeedControls();
        UpdateZoomControl();
        RefreshTheoryCombos();
        RefreshScaleHighlightCombo();
        RefreshMidiDevices();
        // Optional capture of every dispatched MIDI message for offline jitter analysis.
        var midiLogPath = Environment.GetEnvironmentVariable("TABFORGE_MIDI_LOG");
        if (string.IsNullOrWhiteSpace(midiLogPath) && Diagnostics.Trace.IsOn(Diagnostics.Trace.Playback))
            midiLogPath = Diagnostics.Trace.PathFor("playback-midi");
        if (!string.IsNullOrWhiteSpace(midiLogPath))
        {
            try { _midi.StartDiagnostics(midiLogPath); }
            catch (Exception ex)
            {
                Debug.WriteLine($"TABFORGE_MIDI_LOG could not be enabled: {ex}");
                StatusText.Text = "MIDI diagnostics could not be enabled; check TABFORGE_MIDI_LOG.";
            }
        }
        _documents.Active.Notation = PreferredNotation;
        ActivateDocument(_documents.Active, firstLoad: true, focusTabSelection: true);
        Loaded += (_, _) =>
        {
            ApplyPageWidth();
            ApplyLayout();
            ReturnFocusToEditorAfterMouseClicks();
            UpdateMaximiseGlyph();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _dockWorkspace?.ShowFloatingWindows()));
            if (_settingsLoadFailed)
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    StatusText.Text = "Settings could not be loaded; defaults are active. Review Preferences before saving."));
        };
    }

    private int _globalTuneOffset => TuningIsUniform ? _globalStringOffsets[0] : 0;

    /// <summary>
    /// Keeps the heavy, mostly-static layers (score page, arrangement grid) as GPU textures. A moving
    /// playhead then only makes the GPU re-composite textures instead of the render thread re-drawing
    /// every glyph under it each frame. Cached at the real DPI with ClearType, so it looks identical.
    /// </summary>
    private void ApplyGpuLayerCaches(double pixelsPerDip)
    {
        BitmapCache Cache() => new() { RenderAtScale = Math.Max(1, pixelsPerDip), EnableClearType = true, SnapsToDevicePixels = true };
        Arrangement.SetTimelineCache(Cache());
    }

    /// <summary>
    /// Once a document has opened, return the import's temporary memory (Guitar Pro parsing, layout
    /// scratch) to Windows. Runs once at idle priority - never during playback or per frame.
    /// </summary>
    private void ScheduleMemoryTrim() =>
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (_documents.Documents.Any(d => d.Playback.Engine.IsPlaying)) return;
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        });


    private static void ApplyFretboardStyle(string? style)
    {
        TabForge.Visualization.InstrumentVisualizer.Gp5Mode = style switch
        {
            "GP5: Beat" => TabForge.Visualization.Gp5FretboardMode.Beat,
            "GP5: Beat + next beat" => TabForge.Visualization.Gp5FretboardMode.BeatAndNextBeat,
            "GP5: Beat + bar" => TabForge.Visualization.Gp5FretboardMode.BeatAndBar,
            "GP5: Bar" => TabForge.Visualization.Gp5FretboardMode.Bar,
            _ => null
        };
        TabForge.Visualization.FretboardRenderer.Gp5Style = TabForge.Visualization.InstrumentVisualizer.Gp5Mode is not null;
    }

    private bool _lastInputWasMouse;

    /// <summary>
    /// Toolbar buttons are keyboard-reachable (Tab, visible focus). After a mouse click the focus goes
    /// straight back to the score editor so Space (play/pause) and the numeric keypad keep working.
    /// </summary>
    private void ReturnFocusToEditorAfterMouseClicks()
    {
        PreviewMouseDown += (_, _) => _lastInputWasMouse = true;
        PreviewKeyDown += (_, _) => _lastInputWasMouse = false;
        AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (!_lastInputWasMouse || e.OriginalSource is not System.Windows.Controls.Primitives.ButtonBase button) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                // Only when nothing else (a dialog, a text box) took focus in the meantime.
                if (IsActive && ReferenceEquals(Keyboard.FocusedElement, button)) Editor.Focus();
            }));
        }), true);
    }

    private TrackModel? SelectedTrack => TrackMixerGrid.SelectedIndex >= 0 && TrackMixerGrid.SelectedIndex < _project.Tracks.Count
        ? _project.Tracks[TrackMixerGrid.SelectedIndex] : null;
}
