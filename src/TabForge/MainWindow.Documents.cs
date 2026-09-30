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

// MainWindow, document activation: loading, switching and refreshing the active song.
public partial class MainWindow
{
    // ---------- document activation / refresh ----------

    /// <summary>Compatibility shim: the old measure overview is now the arrangement grid.</summary>

    /// <summary>Compatibility shim: the old fretboard tab is now the instrument panel above the score.</summary>

    /// <summary>Loads a project: reuses the tab if the file is already open, otherwise opens a new tab.</summary>
    private bool LoadProject(SongProject project, string? path, bool clearHistory, bool replaceCurrent = false, bool replaceAll = false)
    {
        if (path is not null)
        {
            var existing = _documents.FindByPath(path);
            if (existing is not null)
            {
                ActivateDocument(existing, focusTabSelection: true,
                    applyPlaybackSwitchPolicy: !ReferenceEquals(existing, _documents.Active));
                return true;
            }
        }

        // A currently sounding document must remain addressable by its own tab. An external open or
        // Ctrl+O therefore opens beside it rather than destroying its session in-place.
        // Only the tab being replaced matters: an idle tab (e.g. a fresh Untitled one) is reused even
        // while another tab keeps playing.
        // Opening "in the current tab" (Ctrl+O) replaces it even while it plays: stop that tab first.
        if (replaceCurrent && _documents.Documents.Count > 0 && _documents.Active.Playback.Engine.IsPlaying) StopPlayback();
        if (replaceAll && _documents.Documents.Any(d => d.Playback.Engine.IsPlaying))
        {
            replaceCurrent = false;
            replaceAll = false;
        }

        Plugins.AutoChains.Apply(_settings.Plugins, project);
        Plugins.StartupTracks.Apply(_settings.Plugins, project);   // not armed, not saved, and MarkClean below keeps the song clean
        var doc = DocumentSession.FromProject(project, path);
        doc.Notation = PreferredNotation;
        ApplyPreferredScoreView(doc);
        if (replaceAll) doc.ZoomFactor = 1.0;
        doc.MarkClean();
        if (clearHistory) doc.Undo.Clear();
        if (replaceAll)
        {
            var previous = _documents.Documents.ToArray();
            ApplyPlaybackSwitchPolicy(doc);
            _documents.ReplaceAll(doc);
            foreach (var closed in previous) closed.DisposePlayback();
            ActivateDocument(doc, revealScore: true, focusTabSelection: true);
            return true;
        }
        // While a save runs the current tab is not replaced: the song opens beside it.
        if (replaceCurrent && _documents.Documents.Count > 0 && !_documentController.IsSaving)
        {
            var active = _documents.Active;
            switch (AskDiscardDocument(active))
            {
                case DiscardAnswer.Keep:
                    return false;
                case DiscardAnswer.SaveFirst:
                    // No waiting here (no nested dispatcher frame): the new song opens beside the tab, the tab's save starts, and the
                    // tab closes once its save succeeded (it stays open, with its changes, when it did not).
                    if (_project.Lyrics != LyricsBox.Text) _project.Lyrics = LyricsBox.Text;   // the box shows the tab being saved
                    _documents.Insert(doc, _documents.IndexOf(active) + 1);
                    ActivateDocument(doc, revealScore: true, focusTabSelection: true, applyPlaybackSwitchPolicy: true);
                    SaveThenCloseDocument(active);
                    return true;
            }
            _documents.Replace(_documents.ActiveIndex, doc);
            active.DisposePlayback();
            ActivateDocument(doc, revealScore: true, focusTabSelection: true);
            return true;
        }
        _documents.Add(doc);
        ActivateDocument(doc, revealScore: true, focusTabSelection: true, applyPlaybackSwitchPolicy: true);
        return true;
    }

    private void ActivateDocument(DocumentSession session, bool firstLoad = false, bool revealScore = false,
        bool focusTabSelection = false, bool applyPlaybackSwitchPolicy = false)
    {
        if (applyPlaybackSwitchPolicy) ApplyPlaybackSwitchPolicy(session);

        _playbackUiTick.Stop();
        _follow.Halt();
        _restoring = true;
        try
        {
            _documents.Activate(session);
            ObservePlaybackDocument(session);
            if (session.Playback.IsPlayingVisual && !session.Playback.Engine.IsPlaying)
            {
                session.Playback.IsPlayingVisual = false;
                session.Playback.PlayheadBar = -1;
                session.Playback.PlaybackBarRemap = null;
                session.Playback.PlaybackBarMappingsBySnapshot.Clear();
                session.Playback.TakeFinished();
            }
            if (firstLoad) session.MarkClean();
            // Every opened / switched song fits the arrangement to its own track count (no gap, no scroll).
            ScheduleFitTimelineToTracks();
            if (firstLoad) ScheduleMemoryTrim();
            // A selected area belongs to the song it was made in: never carry it into another tab/song.
            // The score's range goes with it (silently: the editor still holds the previous song here).
            Editor.ClearSelection(notify: false);
            _selection.Clear(SelectionOrigin.Document);
            _loopHasArea = false;
            SyncAreaVisuals();
            UpdateTuningLabel();
            SetTransportActive(LoopButton, _loop);
            TempoBox.Text = _project.Tempo.ToString();
            LyricsBox.Text = _project.Lyrics ?? "";
            Editor.Project = _project;
            Editor.Notation = session.Notation;
            Editor.CenterSystems = session.ContinuousScoreView;
            Editor.HorizontalScroll = session.HorizontalScoreView;
            // Score paper is an app-wide appearance choice (it follows the Light/Dark theme), not per tab.
            session.DarkPaper = !string.Equals(_settings.Appearance.ScorePaper, "Light", StringComparison.OrdinalIgnoreCase);
            Editor.DarkPaper = session.DarkPaper;
            ApplyScorePageBackground();
            Editor.CurrentDurationDenominator = session.DurationDenominator;
            Editor.CurrentDots = session.DurationDots;
            Editor.CurrentTriplet = session.DurationTriplet;
            Editor.SetTupletEntryState(session.TupletNumerator, session.TupletDenominator);
            Editor.SelectedTrackIndex = Math.Max(0, session.TrackIndex);
            Editor.SetPosition(session.CursorBar, session.CursorCell, session.CursorString);
            Editor.SetActiveVoice(session.ActiveVoiceIndex);
            if (session.Playback.IsPlayingVisual && session.Playback.Engine.IsPlaying)
            {
                _timeline = session.Playback.Timeline ?? MidiTimelineBuilder.Build(_project, BuildOptions());
                Editor.Timeline = _timeline;
                Editor.PlaybackBarRemap = session.Playback.PlaybackBarRemap;
                Editor.PlaybackActive = true;
            }
            else
            {
                Editor.PlaybackActive = false;
                Editor.PlaybackBarRemap = null;
            }
            RefreshToolsPalette();
            _zoomFactor = session.ZoomFactor;
            UpdateZoomControl();
            RefreshTracks();
            RefreshPluginChain();
            RefreshMarkers();
            RefreshArrangement();
            if (!session.Playback.IsPlayingVisual) RebuildVisualTimeline();
            RefreshStatus();
            RefreshInstrument();
            RefreshScaleHighlightCombo();
            SyncSelectedOutput();
            RefreshTabs();
            UpdateTitle();
            if (!firstLoad) StatusText.Text = $"Switched to {session.DisplayName}";
        }
        finally { _restoring = false; }
        ApplyPageWidth();
        SyncPlaybackUiToActiveDocument();
        RefreshPlayingIndicators();
        // A freshly opened song should show the notation, not the title block at the top of the page.
        if (revealScore)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ScoreScroll.UpdateLayout();
                ScoreScroll.ScrollToHorizontalOffset(0);
                ScrollToCursor();
                ScoreScroll.ScrollToHorizontalOffset(0);
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!focusTabSelection || !Tabs.FocusActiveTab()) Editor.Focus();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ObservePlaybackDocument(DocumentSession session)
    {
        if (ReferenceEquals(_observedPlaybackDocument, session)) return;
        if (_observedPlaybackDocument is not null)
        {
            _observedPlaybackDocument.Playback.TimelineChanged -= OnPlaybackTimelineChanged;
            _observedPlaybackDocument.Playback.TimelineRevised -= OnPlaybackTimelineRevised;
        }
        _observedPlaybackDocument = session;
        session.Playback.TimelineChanged += OnPlaybackTimelineChanged;
        session.Playback.TimelineRevised += OnPlaybackTimelineRevised;
    }

    private void ApplyPlaybackSwitchPolicy(DocumentSession target)
        => TabPlaybackSwitchPolicy.Apply(_documents.Documents, target, _tabSettings.PlaybackOnTabSwitch);

    /// <summary>Stores the editor's per-document state so switching tabs is lossless.</summary>
    private void CaptureDocumentState()
    {
        var doc = Doc;
        doc.CursorBar = Editor.SelectedMeasure;
        doc.CursorCell = Editor.SelectedCell;
        doc.CursorString = Editor.SelectedString;
        doc.ActiveVoiceIndex = Editor.ActiveVoiceIndex;
        doc.TrackIndex = Math.Max(0, TrackMixerGrid.SelectedIndex);
        doc.Notation = Editor.Notation;
        doc.DarkPaper = Editor.DarkPaper;
        doc.ZoomFactor = _zoomFactor;
        doc.ContinuousScoreView = Editor.CenterSystems;
        doc.HorizontalScoreView = Editor.HorizontalScroll;
        doc.DurationDenominator = Editor.CurrentDurationDenominator;
        doc.DurationDots = Editor.CurrentDots;
        doc.DurationTriplet = Editor.CurrentTriplet;
        doc.TupletNumerator = Editor.CurrentTupletNumerator;
        doc.TupletDenominator = Editor.CurrentTupletDenominator;
    }

    private void ApplyPreferredScoreView(DocumentSession session)
    {
        session.ContinuousScoreView = _settings.PreferredContinuousScoreView;
        session.HorizontalScoreView = _settings.PreferredHorizontalScoreView;
    }

    private void RefreshTabs() => Tabs.Refresh();

    private int _fittedTrackCount = -1;

    private void RefreshTracks()
    {
        // Adding or removing a track resizes the arrangement to the new track count.
        if (_project.Tracks.Count != _fittedTrackCount)
        {
            _fittedTrackCount = _project.Tracks.Count;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FitTimelineToTracks);
        }
        var old = TrackMixerGrid.SelectedIndex;
        TrackMixerGrid.ItemsSource = null;
        TrackMixerGrid.ItemsSource = _project.Tracks;
        if (_project.Tracks.Count > 0)
            TrackMixerGrid.SelectedIndex = Math.Clamp(old < 0 ? Doc.TrackIndex : old, 0, _project.Tracks.Count - 1);
        Editor.SelectedTrackIndex = Math.Max(0, TrackMixerGrid.SelectedIndex);
        SyncSelectedOutput();
    }
}
