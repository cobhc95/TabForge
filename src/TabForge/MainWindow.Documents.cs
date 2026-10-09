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
    private DocumentViewBinder? _viewBinder;
    private DocumentViewBinder ViewBinder => _viewBinder ??= new DocumentViewBinder(new DocumentViewSurface(Editor, TrackMixerGrid, TempoBox, LyricsBox));

    // ---------- document activation / refresh ----------

    /// <summary>Compatibility shim: the old measure overview is now the arrangement grid.</summary>

    /// <summary>Compatibility shim: the old fretboard tab is now the instrument panel above the score.</summary>

    /// <summary>Loads a project: reuses the tab if the file is already open, otherwise opens a new tab.</summary>
    /// <param name="replaceTarget">The tab "open in the current tab" replaces: chosen when the open started, so a background import that completes after another
    /// tab was selected still replaces the tab it was started for (and opens beside the others when that tab has left this window). Null: a new tab.</param>
    private bool LoadProject(SongProject project, string? path, bool clearHistory, DocumentSession? replaceTarget = null, bool replaceAll = false, string? sourcePath = null)
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
        if (replaceTarget is not null && ReferenceEquals(replaceTarget, _documents.Active) && replaceTarget.Playback.Engine.IsPlaying) StopPlayback();

        AttachAppRules(project);
        Plugins.AutoChains.Apply(_settings.Plugins, project);
        Plugins.StartupTracks.Apply(_settings.Plugins, project);   // not armed, not saved, and MarkClean below keeps the song clean
        var doc = DocumentSession.FromProject(project, path);
        if (sourcePath is not null) doc.Media.SetSourceDirectory(System.IO.Path.GetDirectoryName(sourcePath));   // an imported song has no native path yet: its relative media resolves here
        doc.Notation = PreferredNotation;
        ApplyPreferredScoreView(doc);
        if (replaceAll) doc.ZoomFactor = 0;
        doc.MarkClean();
        if (clearHistory) doc.Undo.Clear();
        // Where the song goes (replace the target, open beside, replace everything) is DocumentPlacement's decision; the window only reacts to it.
        // While a save runs the target tab is not replaced: the song opens beside it.
        var displayed = _documents.Documents.Count > 0 ? _documents.Active : null;
        var placed =DocumentPlacement.Place(_documents, doc, replaceTarget, replaceAll, _documentController.IsSaving, AskDiscardDocument,
            beforeReplaceAll: ApplyPlaybackSwitchPolicy,
            beforeSaveFirst: target => { if (ReferenceEquals(target, _documents.Active) && target.Project.Lyrics != LyricsBox.Text) target.Project.Lyrics = LyricsBox.Text; });   // the box shows the tab being saved
        switch (placed.Kind)
        {
            case PlacementKind.Kept:
                return false;
            case PlacementKind.ReplacedAll:
                ActivateDocument(doc, revealScore: true, focusTabSelection: true);
                return true;
            case PlacementKind.Replaced:
                // The displayed tab was replaced in place (no switch). A background import whose target is another tab switches away from the
                // displayed one, so the playback-on-tab-switch policy applies as for any other switch.
                ActivateDocument(doc, revealScore: true, focusTabSelection: true, applyPlaybackSwitchPolicy: !ReferenceEquals(placed.Target, displayed));
                return true;
            case PlacementKind.OpenedBesideToSave:
                ActivateDocument(doc, revealScore: true, focusTabSelection: true, applyPlaybackSwitchPolicy: true);
                SaveThenCloseDocument(placed.Target!);
                return true;
            default:
                ActivateDocument(doc, revealScore: true, focusTabSelection: true, applyPlaybackSwitchPolicy: true);
                return true;
        }
    }

    private void ActivateDocument(DocumentSession session, bool firstLoad = false, bool revealScore = false,
        bool focusTabSelection = false, bool applyPlaybackSwitchPolicy = false)
    {
        // The one switch entry point: the document on show is captured here (callers need not), and what was typed for it in the boxes is settled on it.
        if (ViewBinder.Shown is { } outgoing) CaptureDocumentState(leaving: !ReferenceEquals(outgoing, session));
        AttachAppRules(session.Project);
        if (applyPlaybackSwitchPolicy) ApplyPlaybackSwitchPolicy(session);

        _playbackView.StopTick();
        _follow.Halt();
        _restoring = true;
        try
        {
            _documents.Activate(session);
            _playbackView.Observe(session);
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
            if (firstLoad) { ScheduleMemoryTrim(); SchedulePrewarmMenus(); }
            // A selected area belongs to the song it was made in: never carry it into another tab/song.
            // The score's range goes with it (silently: the editor still holds the previous song here).
            Editor.ClearSelection(notify: false);
            _selection.Clear(SelectionOrigin.Document);
            _selLoop.Drop();
            SyncAreaVisuals();
            UpdateTuningLabel();
            SetTransportActive(LoopButton, _loop);
            // Score paper follows the Light/Dark theme, not the tab.
            ViewBinder.Show(session, !string.Equals(_settings.Appearance.ScorePaper, "Light", StringComparison.OrdinalIgnoreCase));
            ApplyScorePageBackground();
            if (session.Playback.IsPlayingVisual && session.Playback.Engine.IsPlaying)
            {
                _timeline = session.Playback.Timeline ?? MidiTimelineBuilder.Build(_project, BuildOptions());
                Editor.Playback.Timeline = _timeline;
                Editor.Playback.BarRemap = session.Playback.PlaybackBarRemap;
                Editor.Playback.Active = true;
            }
            else
            {
                Editor.Playback.Active = false;
                Editor.Playback.BarRemap = null;
            }
            RefreshToolsPalette();
            ScoreZoom.Factor = session.ZoomFactor;
            ScoreZoom.UpdateZoomControl();
            RefreshTracks(fromDocument: true);
            RefreshPluginChain();
            RefreshMarkers();
            RefreshArrangement();
            if (!session.Playback.IsPlayingVisual) RebuildVisualTimeline();
            RefreshStatus();
            RefreshInstrument();
            SyncSelectedOutput();
            RefreshTabs();
            UpdateTitle();
            if (!firstLoad) StatusText.Text = $"Switched to {session.DisplayName}";
        }
        finally { _restoring = false; }
        ScoreZoom.ApplyPageWidth();
        _playbackView.ShowActiveDocument();
        RefreshPlayingIndicators();
        // A freshly opened song should show the notation, not the title block at the top of the page.
        if (revealScore)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ScoreScroll.UpdateLayout();
                ScoreScroll.ScrollToHorizontalOffset(0);
                ScrollToCursor();
                // a pane shorter than title + one system shows the cursor's system, not the title block
                if (!_follow.BarInView(Editor.SelectedMeasure) && ScoreScroll.ViewportHeight > 1) _follow.JumpTo(_follow.FocusTop(Editor.SystemTopForMeasure(Editor.SelectedMeasure)));
                ScoreScroll.ScrollToHorizontalOffset(0);
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!focusTabSelection || !Tabs.FocusActiveTab()) Editor.Focus();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyPlaybackSwitchPolicy(DocumentSession target)
        => TabPlaybackSwitchPolicy.Apply(_documents.Documents, target, _tabSettings.PlaybackOnTabSwitch);

    /// <summary>Stores the view state of the document on show so switching tabs is lossless; <paramref name="leaving"/> also settles the typed boxes on it.</summary>
    private void CaptureDocumentState(bool leaving = false)
    {
        if (ViewBinder.Shown is { } shown) shown.ZoomFactor = ScoreZoom.Factor;
        ViewBinder.Capture(leaving);
    }

    private void ApplyPreferredScoreView(DocumentSession session)
    {
        session.ContinuousScoreView = _settings.PreferredContinuousScoreView;
        session.HorizontalScoreView = _settings.PreferredHorizontalScoreView;
    }

    private void RefreshTabs() => Tabs.Refresh();

    private int _fittedTrackCount = -1;

    /// <param name="fromDocument">The list is rebuilt for another document: its own selected track is shown, not the one the previous document had selected.</param>
    private void RefreshTracks(bool fromDocument = false)
    {
        // Adding or removing a track resizes the arrangement to the new track count.
        if (_project.Tracks.Count != _fittedTrackCount)
        {
            _fittedTrackCount = _project.Tracks.Count;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FitTimelineToTracks);
        }
        var old = fromDocument ? -1 : TrackMixerGrid.SelectedIndex;
        TrackMixerGrid.ItemsSource = null;
        TrackMixerGrid.ItemsSource = _project.Tracks;
        if (_project.Tracks.Count > 0)
            TrackMixerGrid.SelectedIndex = Math.Clamp(old < 0 ? Doc.TrackIndex : old, 0, _project.Tracks.Count - 1);
        Editor.SelectedTrackIndex = Math.Max(0, TrackMixerGrid.SelectedIndex);
        SyncSelectedOutput();
    }
}
