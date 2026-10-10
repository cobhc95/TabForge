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

// Owns: the window's constructor and start-up wiring: the controllers, background services and engine client it creates, plus the fretboard style, GPU layer caches and memory trim.
// Does not own: the layout in MainWindow.xaml, and the controllers themselves.
// Tests: listed in docs/feature-map/windows-tabs-and-documents.md.
public partial class MainWindow : Window
{
    private readonly DocumentManager _documents;
    private readonly AppOptions _options;   // the application's shared settings-driven options; each song's engine and the views read them
    private readonly DocumentController _documentController = new();
    private readonly ArrangementController _arrangementController = new();
    private readonly TrackController _trackController = new();
    private readonly ISettingsWindowHost _settingsWindowHost;
    private readonly BackgroundServices _services;
    private readonly Audio.AudioEngineClient _engine;
    private readonly EngineSyncController _engineSync;
    private readonly TransportControlsController _transport;
    private readonly ClipEditController _clips;
    private readonly SectionEditFlow _sections;
    private PlaybackEngine _midi => Doc.Playback.Engine;
    private readonly TabTransferController _tabTransfer;
    private WpfResizeBorderFrame? _resizeBorderFrame;
    private WpfCaptionButtonFrame? _captionButtonFrame;
    private bool _restoring;
    // Loop state belongs to the song (DocumentSession); these forward to the active tab.
    private bool _loop { get => Doc.LoopEnabled; set => Doc.LoopEnabled = value; }
    private bool _mainWindowInitialized;
    private readonly ArrangementGestureState _gestures;
    private bool _previewNotes = true;
    // ---- keep-the-score-in-view ----
    private readonly ScoreFollowCoordinator _follow;
    private bool _suppressWorkspaceSave;

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

    // The engine reports ~60 times a second; the playback view applies the newest position once per rendered frame.
    private readonly PlaybackViewController _playbackView;
    private int _scoreWheelScrollPixels = 32;

    // Per-document state is kept in the active DocumentSession.
    private DocumentSession Doc => _documents.Active;

    /// <summary>Every song open in this window (used by crash recovery).</summary>
    internal IReadOnlyList<DocumentSession> OpenDocuments => _documents.Documents;
    private SongProject _project { get => Doc.Project; set => Doc.Project = value; }
    private UndoController _undo => Doc.Undo;
    private string? _currentPath { get => Doc.Path; set => Doc.Path = value; }

    public ObservableCollection<MidiOutputDeviceInfo> MidiDevices { get; } = new();

    public MainWindow(Audio.AudioEngineClient engine, AppOptions? options = null)
    {
        _gestures = new ArrangementGestureState(new ArrangementGestureHost(this));   // before InitializeComponent: the arrangement events read it
        _selLoop = new SelectionLoopController(this);   // before InitializeComponent: early handlers may read loop state
        _engine = engine;
        _options = options ?? new AppOptions();
        _documents = new DocumentManager(_options.Playback);
        _engineSync = new EngineSyncController(new EngineSyncHost(this), engine);
        _lifetime.Add(_engineSync.Dispose);
        _transport = new TransportControlsController(new TransportHost(this));
        _lifetime.Add(_transport.Dispose);
        _playbackView = new PlaybackViewController(new PlaybackViewHost(this));
        _tabTransfer = new TabTransferController(new TabTransferHost(this));
        InitializeComponent();
        BuildMainMenuFromTable();   // the plain commands of MainWindow.xaml's menu skeleton, before anything reads the menus
        Arrangement.ViewOptions = _options.Visual;
        Arrangement.QuarantinedPlugins = () => _settings.Plugins.Quarantined;   // faulted FX icon on tracks whose plug-in crashed
        _follow = new ScoreFollowCoordinator(ScoreScroll, Editor, () => _isPlayingVisual, () => _midi.IsPaused,
            () => _playheadBar, () => _playheadFraction, MaxMeasures, () => _settings.Follow);
        WireScoreScrollGestures();
        _settingsWindowHost = new WpfSettingsWindowHost(this, CreateSettingsActions());   // this window's own callbacks for its Settings dialogs
        _mainWindowInitialized = true;
        _documents.AddNew();
        _services = new BackgroundServices(this);
        _clips = new ClipEditController(new ClipHost(this), _trackController);
        _sections = new SectionEditFlow(new SectionHost(this), _arrangementController);
        Loaded += (_, _) => _services.Update.ScheduleAutomaticUpdateCheck();
        ToolPalette.BuildToolsPalette();
        ToolPalette.BuildPinnedToolStrip();
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

        Editor.EditHost = this;   // edits record undo whichever window shows the editor (a floating pane or a window not yet shown has no MainWindow ancestor)
        Editor.Edited += (_, _) =>
        {
            var changed = Editor.AffectedMeasureRange;
            Arrangement.InvalidateActivities(Editor.SelectedTrackIndex, changed.FirstMeasure, changed.LastMeasure);
            RefreshArrangementScore();
            // An edit in the score can remove bars under the range (the editor clamps it): the model follows.
            PushEditorSelectionToModel();
            RefreshInstrument();
            RefreshStatus();
            if (StatusText.Text == Views.Score.ScoreEditCommands.NoNoteMessage) StatusText.Text = "";   // a stale miss is gone once an edit lands
            RefreshTabs();
            UpdateTitle();
            RefreshToolsPalette();
        };
        Editor.SelectionChanged += (_, _) => OnEditorSelectionChanged();
        Editor.PlayRequested += (_, _) => TogglePlayback();
        Editor.AppendBarAtEnd = () => { if (_midi.IsPlaying) return; AppendBar(); Editor.MoveToBarStart(MaxMeasures() - 1); };
        Editor.NotePreview += OnNotePreview;
        Editor.StatusMessage += (_, msg) => StatusText.Text = msg;
        Editor.CursorMovedByKey += (_, _) => { if (Editor.HorizontalScroll || !_follow.BarInView(Editor.SelectedMeasure)) ScrollToCursor(); };
        Editor.ContextMenuRequested += (_, args) =>
        {
            if (args.OnNote || args.InsideSelection) ShowNoteContextMenu(args);
            else ShowScoreContextMenu(args.Position, args);
        };
        ScoreScroll.MouseRightButtonUp += (s, e) => { if (!Editor.IsMouseOver) { ShowScoreContextMenu(e.GetPosition(ScoreScroll)); e.Handled = true; } };
        ScoreScroll.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (Editor.IsMouseOver) return;
            Editor.ClearSelection();
            // A click on empty paper puts the keyboard on the editor (the ScrollViewer is not focusable, so it cannot take it).
            if (!Editor.IsKeyboardFocused) Editor.Focus();
        };
        // A plain click on the timeline (not a drag, not a clip/section/area-move gesture) clears the shared
        // selection in both views, exactly like a click on empty score paper. The timeline decides "plain" on
        // mouse-up, so a range drag no longer clears first and a clip click keeps selecting its clip.
        Arrangement.SelectionClearRequested += (_, _) => _selection.Clear(SelectionOrigin.Timeline);
        InitSelectionModel();

        Instrument.PreviewMouseLeftButtonDown += (_, e) => InstrumentPane.OnMouseLeftButtonDown(e);
        Instrument.PreviewMouseMove += (_, e) => InstrumentPane.OnMouseMove(e);
        Instrument.PreviewMouseLeftButtonUp += (_, e) => InstrumentPane.OnMouseLeftButtonUp(e);
        Instrument.LostMouseCapture += (_, _) => InstrumentPane.OnLostMouseCapture();
        Instrument.MouseRightButtonUp += Instrument_MouseRightButtonUp;
        Instrument.ContextMenuKeyPressed += (_, _) => ShowInstrumentContextMenu(fromKeyboard: true);
        Arrangement.TimelineKeyboardContextRequested += (_, _) => ShowTimelineContextMenuFromKeyboard();
        Instrument.LegendAnchorChanged += InstrumentPane.PlaceScaleFinderButton;
        InstrumentOverlay.SizeChanged += (_, _) => InstrumentPane.ReplaceScaleFinderButton();
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
            _clips.LaneCursor = null; Arrangement.FocusTimeline();   // a dragged bar range takes the keys (the press gave them to the score): Delete acts on the bars
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
            CompleteTrackEditUndo();   // the undo step; the sound follows in ApplyMuteSolo
            MixerHost.ApplyMuteSolo();
        };
        Arrangement.TrackColorChanged += (_, _) =>
        {
            CompleteTrackEditUndo();
            OnArrangementTrackColorChanged();
        };
        Arrangement.TrackEditRequested += request =>
        {
            _gestures.BeginTrackEdit(() => _trackController.ApplyEdit(_project, request));
        };
        Arrangement.InstrumentPreview += (track, name) => TabForge.Controllers.InstrumentLivePreview.Apply(_midi, track, name);
        Arrangement.MixEditStarting += (_, _) => _gestures.MixEditStarting();
        Arrangement.MixChanged += (_, _) =>
        {
            _gestures.MixChanged();
            if (Arrangement.InMuteSoloGesture) return;   // a group mute/solo: ApplyMuteSolo follows with the sound
            OnArrangementMixChanged();
            SyncAudioEngine(); // cheap: only changed levels are sent
        };
        Arrangement.MixEditEnded += (_, _) => _gestures.MixEditEnded();
        Arrangement.AddTrackRequested += (_, _) => AddTrackWithWindow();
        // Drag starts take the undo state before the drop edits the model (it only costs the bars changed since the last state).
        Arrangement.TrackDragStarted += (_, _) => _gestures.TrackDragStarted();
        Arrangement.TrackReordered += (_, move) => MoveTrackTo(move.from, move.to);
        Arrangement.SectionDragStarted += (_, _) => _gestures.SectionDragStarted();
        Arrangement.SectionDragCancelled += (_, _) => _gestures.SectionDragCancelled();
        Arrangement.SectionReordered += (_, move) => MoveSection(move.from, move.insertBefore);
        Arrangement.SectionContextRequested += (_, markerIndex) => ShowSectionContextMenu(markerIndex);
        // A resize edits markers live during the drag: the state is taken before the first change.
        Arrangement.SectionResizeStarting += (_, _) => _gestures.SectionDragStarted();
        Arrangement.SectionMarkerMoved += (_, move) =>
        {
            var sections = SectionLayout.Sorted(_project);
            if (move.markerIndex < 0 || move.markerIndex >= sections.Count) return;
            if (!DocumentEdits.Run(Doc, p => SectionLayout.MoveMarker(p, sections[move.markerIndex], move.bar)).Changed) return;
            RefreshAfterEdit(EditRefresh.Score | EditRefresh.Markers | EditRefresh.Arrangement);
            StatusText.Text = $"Section moved to bar {move.bar + 1} (bars unchanged; Ctrl+drag moves the bars too)";
        };
        Arrangement.SectionLaneContextRequested += (_, at) => ShowSectionLaneMenu(at.markerIndex, at.bar);
        Arrangement.SectionResized += (_, _) =>
        {
            var resizeStart = _gestures.TakeSectionSnapshot();
            // The drag changed the markers live; the state taken at its start is the undo step.
            if (resizeStart is { } before) DocumentEdits.Run(Doc, _ => true, before);
            RefreshAfterEdit(EditRefresh.Score | EditRefresh.Arrangement);
        };
        Arrangement.MixerRequested += (_, _) => OpenMixer();
        Arrangement.FxChainRequested += (_, index) => { if (index >= 0 && index < _project.Tracks.Count) OpenFxChain(_project.Tracks[index]); };
        Arrangement.FxPowerRequested += (_, index) => { if (index >= 0 && index < _project.Tracks.Count) MixerHost.ToggleTrackChain(_project.Tracks[index]); };
        Arrangement.BusFxRequested += (_, group) => OpenBusFx(group);
        Arrangement.BusPowerRequested += (_, group) => MixerHost.ToggleBus(group);
        Arrangement.TrackOptionsRequested += (_, index) => { TrackMixerGrid.SelectedIndex = index; TrackProps_Click(this, new RoutedEventArgs()); };
        Arrangement.TimelineContextRequested += (_, context) => ShowArrangementContextMenu(context.bar, context.track);
        HookAudioLanes();
        HookAddTrackLane();
        Arrangement.AddTrackMenuRequested += ShowAddTrackMenu;
        Arrangement.GroupCollapseToggled += group => MixerHost.SetGroupsCollapsed(new[] { group }, null);
        Arrangement.GroupMoved += (start, count, before) =>
        {
            if (start < 0 || count <= 0 || start + count > _project.Tracks.Count) return;
            var orderBefore = MixerHost.CaptureOrderLayout();
            var moving = _project.Tracks.GetRange(start, count);
            if (!DocumentEdits.Run(Doc, p => Models.TrackOrdering.MoveRun(p, start, count, before)).Changed) return;   // the first track (time signatures) may have changed: the timeline is invalidated
            SyncAudioEngine();
            if (_midi.IsPlaying) _midi.RefreshArrangement(_project, Enumerable.Range(0, MaxMeasures()).ToArray());
            RefreshTracks();
            RefreshArrangement();
            Editor.InvalidateScoreLayout();
            UpdateTitle();
            MixerHost.PlayOrderAnimation(orderBefore);   // the open mixer reorders with it, animating at the same time
            StatusText.Text = $"Moved the {Models.MixerGroups.GroupOf(_project, moving[0]).ToLowerInvariant()} group";
        };
        // Right-click on a group row opens the mixer at that group; the empty-area menus toggle "Show tracks in groups" (the mixer's
        // "Groups in track list" box is the same setting).
        Arrangement.GroupMixerRequested += group => { OpenMixer(); MixerHost.Windows.Mixer?.RevealGroup(group); };
        Arrangement.GroupsShownState = () => _project.Mixer.ShowGroupsInTrackList;
        Arrangement.GroupsToggleRequested += on => MixerHost.SetTrackListShows("groups", on);
        Arrangement.ColourByGroupRequested += MixerHost.ColourTracksByGroup;
        Arrangement.ColourTracksRequested += MixerHost.ColourTracksWithDialog;
        Arrangement.TrackListSettingsRequested += () => OpenSettings(SettingsCatalog.Timeline, "timeline.trackgroups");
        Arrangement.DockMenuItems = () => _dockWorkspace?.PanelMenuItems("timeline") ?? new List<System.Windows.Controls.Control>();
        _documents.Changed += (_, _) => RefreshTabs();
        PreviewMouseDown += (_, e) => Arrangement.DismissTrackNameEditOnClick(e.OriginalSource as DependencyObject);
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
        Loaded += (_, _) => _tabTransfer.Register();
        // Drawn text is shaped per control for that control's own display DPI (Draw.UseDpi in each OnRender), so two
        // windows on monitors with different scaling no longer share one value. Moving to another monitor re-renders.
        Loaded += (_, _) => ApplyGpuLayerCaches(VisualTreeHelper.GetDpi(this).PixelsPerDip);
        DpiChanged += (_, e) =>
        {
            ApplyGpuLayerCaches(e.NewDpi.PixelsPerDip);
            Editor.InvalidateScoreLayout();
            Arrangement.InvalidateTimeline();
            Instrument.InvalidateVisual();
        };
        Closed += (_, _) => ReleaseWindowResources();   // every attachment to a longer-lived object ends here, once (MainWindow.Lifetime.cs)
        StateChanged += (_, _) => UpdateMaximiseGlyph();
        SizeChanged += (_, _) => ApplyLayout();

        // Score follow has its own render-priority timer so vertical movement glides without
        // increasing the cadence of playback-position, arrangement, or audio updates.

        ScoreScroll.ScrollChanged += ScoreScroll_ScrollChanged;

        LoadSettings();
        ApplyPreferredScoreView(_documents.Active);
        UpdateSpeedControls();
        ScoreZoom.UpdateZoomControl();
        RefreshMidiDevices();
        // Optional capture of every dispatched MIDI message for offline jitter analysis.
        var midiLogPath = Environment.GetEnvironmentVariable("TABFORGE_MIDI_LOG");
        if (string.IsNullOrWhiteSpace(midiLogPath) && Services.Trace.IsOn(Services.Trace.Playback))
            midiLogPath = Services.Trace.PathFor("playback-midi");
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
            ScoreZoom.ApplyPageWidth();
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


    private void ApplyFretboardStyle(string? style)
    {
        _options.Visual.Gp5Mode = style switch
        {
            "GP5: Beat" => TabForge.Visualization.Gp5FretboardMode.Beat,
            "GP5: Beat + next beat" => TabForge.Visualization.Gp5FretboardMode.BeatAndNextBeat,
            "GP5: Beat + bar" => TabForge.Visualization.Gp5FretboardMode.BeatAndBar,
            "GP5: Bar" => TabForge.Visualization.Gp5FretboardMode.Bar,
            _ => null
        };
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
