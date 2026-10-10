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

// MainWindow, transport: play/pause/stop, playhead sync, score following, loop and speed.
// Owns: transport: play, pause, stop, playhead sync, score following, loop and speed.
// Does not own: playback itself (PlaybackEngine).
// Tests: listed in docs/feature-map/playback.md.
public partial class MainWindow
{
    // ---------- playback (standard: Space = play/pause, click to reposition) ----------

    private PlaybackOptions BuildOptions()
    {
        var (ls, le) = GetLoopRange();
        _midi.SetSectionStarts(_project.Markers.Select(marker => marker.MeasureIndex).Where(bar => bar > 0));
        _midi.SetMasterVolume(_project, _settings.Audio.MasterVolume);
        return _transport.BuildOptions(Editor.SelectedMeasure, Editor.SelectedCell, _loop, ls, le, _selLoop.StartCell, _selLoop.EndCell);
    }

    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlayback();
    private void PlayFromStart_Click(object sender, RoutedEventArgs e) => PlayFromStart();
    private void RewindToBeginning_Click(object sender, RoutedEventArgs e)
    {
        JumpKeepingPlayback(() => Editor.SetPosition(0, 0, Editor.SelectedString));
    }

    /// <summary>Moves the cursor; if playback was running it restarts from the new position, otherwise it just moves.</summary>
    private void JumpKeepingPlayback(Action move)
    {
        bool wasPlaying = _midi.IsPlaying && !_midi.IsPaused;
        if (wasPlaying) _midi.Stop();
        else StopPlayback();
        move();
        ScrollToCursor();
        if (wasPlaying) StartPlayback();
    }

    private void PlayFromStart()
    {
        Editor.SetPosition(0, 0, Editor.SelectedString);
        if (_midi.IsPlaying) _midi.Stop();
        StartPlayback();
    }

    private void FirstBar_Click(object sender, RoutedEventArgs e) => JumpKeepingPlayback(Editor.MoveToFirstBar);
    private void LastBar_Click(object sender, RoutedEventArgs e) => JumpKeepingPlayback(Editor.MoveToLastBar);

    /// <summary>Space toggles play / pause, resuming exactly where it stopped.</summary>
    private void TogglePlayback()
    {
        if (_midi.IsPlaying && !_midi.IsPaused)
        {
            _midi.Pause();
            SongClock.Paused();
            _follow.Halt();
            Arrangement.SetPlayhead(Math.Max(0, _playheadBar), _playheadFraction, playbackActive: true, playbackPaused: true);
            SetPlayIcon(false);
            StatusText.Text = "Paused";
            MidiLed.Fill = Brushes.Gray;
            return;
        }
        if (_midi.IsPlaying && _midi.IsPaused)
        {
            _midi.Resume();
            _follow.Resume();
            Arrangement.SetPlayhead(Math.Max(0, _playheadBar), _playheadFraction, playbackActive: true);
            SetPlayIcon(true);
            StatusText.Text = "Playing";
            MidiLed.Fill = Brushes.LimeGreen;
            return;
        }
        StartPlayback();
    }

    private void StartPlayback()
    {
        var session = Doc;
        var playback = session.Playback;
        ApplyTempo();
        StatusText.Text = "Playing";
        MidiLed.Fill = Brushes.LimeGreen;
        playback.IsPlayingVisual = true;
        playback.ClearPending();
        Editor.Playback.Active = true;
        Arrangement.SetPlayhead(Editor.SelectedMeasure, 0, playbackActive: true);
        UpdatePlayingSectionMarker(Editor.SelectedMeasure);
        var options = BuildOptions();
        Editor.Playback.TrackIndex = Math.Max(0, Editor.SelectedTrackIndex);
        Arrangement.SetLoopRange(GetLoopRange().start, GetLoopRange().end);
        _playbackView.StartTick();
        _follow.ResetForPlayback();
        playback.PlaybackBarMappingsBySnapshot.Clear();
        playback.PlaybackBarRemap = Enumerable.Range(0, MaxMeasures()).ToArray();
        Editor.Playback.BarRemap = playback.PlaybackBarRemap;
        Playback.RememberBarMapping(_undo.Snapshot(_project));
        var songProject = session.Project;
        var clock = playback.Clock;   // the song's own clock, not this window's: the engine keeps this callback when the tab moves to another window
        clock.PrepareForPlayback(songProject);   // the song map is built while the engine starts, not after the first playhead asks for it
        playback.Engine.Start(session.Project, options,
            // Engine thread: record the newest position, and keep audio clips in step with the song.
            position =>
            {
                playback.ReportPosition(position);
                clock.Report(songProject, position, !playback.Engine.IsPaused);
            },
            playback.MarkFinished);
        SetPlayIcon(true);
    }

    /// <summary>
    /// The reference behaviour: moving the cursor while the transport is running repositions playback.
    /// The editor keeps its own playback caret, so this cannot feed back into the engine.
    /// </summary>
    private bool _selectionRefreshQueued;

    private void OnEditorSelectionChanged()
    {
        // While dragging a range, run the side-panel refresh at most once per rendered frame.
        if (Editor.IsDragSelecting)
        {
            if (_selectionRefreshQueued) return;
            _selectionRefreshQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _selectionRefreshQueued = false;
                RefreshStatus();
                // The score range is the shared selection (timeline highlight + selected loop area).
                PushEditorSelectionToModel();
                RefreshArrangementSelection();
            }));
            return;
        }
        RefreshStatus();
        // Selected or cleared, the model mirrors it to the timeline (a cleared score clears the area too).
        PushEditorSelectionToModel();
        RefreshArrangementSelection();
        RefreshInstrument();
        RefreshToolsPalette();
        if (!_restoring && _midi.IsPlaying && Editor.SelectionShouldSeekPlayback)
            _midi.Seek(_project, Editor.SelectedMeasure, Editor.SelectedCell);
    }

    /// <summary>Applies the newest playback position (the controller's tick body).</summary>
    private void ApplyPendingPlayhead() => _playbackView.Apply();

    /// <summary>Rebuilds the visual timeline (used when the score changes while stopped).</summary>
    private void RebuildVisualTimeline()
    {
        _timeline = MidiTimelineBuilder.Build(_project, BuildOptions());
        Editor.Playback.Timeline = _timeline;
        _playheadMs = 0;
    }

    private (int start, int end) GetLoopRange()
    {
        var max = Math.Max(0, MaxMeasures() - 1);
        var s = Math.Clamp(_selLoop.StartBar, 0, max);
        var e2 = Math.Clamp(_selLoop.EndBar, 0, max);
        if (s > e2) (s, e2) = (e2, s);
        return (s, e2);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();
    private void StopPlayback()
    {
        VideoRecordTransportStopped();
        if (IsRecording) ToggleRecording();
        _midi.Stop();
        SongClock.Stopped();
        Playback.ClearPlaybackPosition();
        _follow.ResetRow();
        Editor.Playback.BarRemap = null;
        Editor.Playback.Active = false;
        UpdatePlayingSectionMarker(-1);
        Playhead.SetGeometry(null);
        Playhead.SetDurationGeometry(null);
        _playheadBar = -1;
        _playbackView.StopTick();
        _follow.Halt();
        Editor.Playback.Clear();
        Editor.Playback.Ms = 0;
        SyncArrangementPlayhead();
        SetPlayIcon(false);
        StatusText.Text = "Stopped";
        MidiLed.Fill = Brushes.Gray;
        RefreshInstrument();
        RefreshStatus();
    }

    private void Loop_Click(object sender, RoutedEventArgs e) => SetLoopActive(!_loop);

    private void SetLoopActive(bool loop)
    {
        // The score selection is authoritative when the loop button is enabled. The button can
        // receive focus (and the editor selection can be cleared by a click handler), so reapply
        // the selected musical range before falling back to the whole song/default section.
        if (loop && Editor.HasSelection)
        {
            var range = Editor.SelectionCellRange;
            // The loop never changes the scope: the model's own scope while it holds a range, else the score's (one track).
            // The loop never changes the scope: the model's own scope while it holds a range, else the score's (one track).
            ApplyLoopRange(range.StartMeasure, range.EndMeasure, range.StartCell, range.EndCell, _selection.HasRange ? _selection.Scope : SelectionScope.ThisTrack);
        }
        else if (loop && !_selLoop.HasArea)
        {
            // No selected area: loop the whole song, or (opt-in) the section being played.
            var lastBar = Math.Max(0, MaxMeasures() - 1);
            var (start, end) = _settings.Audio.LoopButtonLoopsSection
                ? TransportControlsController.DefaultLoopArea(_project, _settings.Audio.LoopDefaultScope, _isPlayingVisual && _playheadBar >= 0 ? _playheadBar : Editor.SelectedMeasure)
                : (0, lastBar);
            _selLoop.SetRange(start, end, 0, -1);
            Arrangement.SetLoopRange(start, end);
            _midi.SetLoopRange(start, end, 0, -1);
        }
        // Keep the selected loop range when toggling looping off. Turning the transport mode off
        // should not discard the user's selection; pressing the button again reuses that range.
        _loop = loop;
        SetTransportActive(LoopButton, _loop);
        Arrangement.SetLoopEnabled(ShowLoopOnTimeline);
        _midi.SetLoop(_loop);
        SyncAreaVisuals();
        StatusText.Text = _loop ? $"Looping bars {_selLoop.StartBar + 1}-{_selLoop.EndBar + 1}" : "Loop off";
    }

    private void LoopButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        TransportSettings.OpenLoopSettings(_selLoop.HasArea || _loop
            ? $"Loop area: bars {_selLoop.StartBar + 1}-{_selLoop.EndBar + 1}{(_loop ? "" : " (loop off)")}"
            : "No loop area selected");
        e.Handled = true;
    }

    private void CloseLoopSettings_Click(object sender, RoutedEventArgs e) => LoopSettingsPopup.IsOpen = false;
    private void LoopScope_SelectionChanged(object sender, SelectionChangedEventArgs e) => TransportSettings.OnLoopScope(LoopScopeCombo);
    private void LoopClearOnDisable_Click(object sender, RoutedEventArgs e) => TransportSettings.OnLoopClearOnDisable(LoopClearOnDisableCheck);

    private void ClearLoopArea_Click(object sender, RoutedEventArgs e)
    {
        if (_loop) SetLoopActive(false);
        _selection.Clear(SelectionOrigin.Command);   // the area is the shared selection: the score drops it too
        LoopSettingsPopup.IsOpen = false;
        StatusText.Text = "Loop area cleared";
    }

    // The one speed control is SpeedCombo (toolbar, "100%"); the transport controller holds the speed and draws it.
    private void SpeedCombo_Changed(object sender, SelectionChangedEventArgs e) => _transport.OnSpeedComboChanged();
    private void SpeedCombo_ReSync(object sender, RoutedEventArgs e) => _transport.ShowSpeed();

    private void SpeedCombo_VisibleReSync(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) _transport.ShowSpeed();
    }

    private void SpeedCombo_LostFocus(object sender, RoutedEventArgs e) => _transport.CommitCustomSpeed();

    private void SpeedCombo_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _transport.CommitCustomSpeed();
        e.Handled = true;
    }

    internal static double NextSpeedPreset(double current, int direction) => TransportControlsController.NextSpeedPreset(current, direction);
    internal void ApplySpeed(double value) => _transport.ApplySpeed(value);
    internal double CurrentSpeed => _transport.Speed;
    private void UpdateSpeedControls() => _transport.ShowSpeed();

    private void TempoBox_LostFocus(object sender, RoutedEventArgs e) => ApplyTempo();

    /// <summary>Enter applies the typed tempo and keeps focus in the box (so more can be typed); it is never a transport or editor key.</summary>
    private void TempoBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        ApplyTempo();
        TempoBox.SelectAll();
        e.Handled = true;
    }

    /// <summary>The tempo a typed text means: a whole number clamped to 20-400, or <paramref name="current"/> when it is not a number.</summary>
    internal static int ResolveTempoText(string? text, int current) => DocumentViewBinder.ResolveTempoText(text, current);

    /// <summary>A typed note sounds once for the set length at the song tempo (the Preferences length is only the fallback).</summary>
    private void OnNotePreview(object? sender, NotePreviewEventArgs e)
    {
        if (_previewNotes) _midi.PreviewNote(e.DeviceId, e.Channel, e.Program, e.Midi, e.LengthMs > 0 ? e.LengthMs : _settings.Audio.PreviewLengthMs);
    }

    private void ApplyTempo()
    {
        if (DocumentViewBinder.CommitTempo(Doc, TempoBox.Text)) RefreshAfterEdit(EditRefresh.Status);
        TempoBox.Text = _project.Tempo.ToString();
    }

    // Score pane scrolling (ScoreScrollController): the wheel step, the gestures that make the follow stand down, the cursor into view.
    private ScoreScrollController? _scoreScroll;
    private ScoreScrollController ScoreScrolling => _scoreScroll ??= new ScoreScrollController(this);

    private void ScrollToCursor() => ScoreScrolling.ScrollToCursor();
    private void ScoreScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => _follow.OnScrollChanged(e);
    private void ScoreScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { ScoreScrolling.OnMouseWheel(e, _scoreWheelScrollPixels); return; }
        if (e.Delta != 0) ScoreZoom.ZoomBy(Math.Sign(e.Delta), e.GetPosition(ScoreScroll));
        e.Handled = true;
    }
    private void WireScoreScrollGestures() => ScoreScrolling.WireGestures();

    /// <summary>The window as the host of its <see cref="PlaybackViewController"/>.</summary>
    private sealed class PlaybackViewHost : IPlaybackViewHost
    {
        private readonly MainWindow _window;
        public PlaybackViewHost(MainWindow window) => _window = window;

        public bool IsClosed => _window._isClosed;
        public DocumentSession ActiveDocument => _window.Doc;
        public IScorePlayhead Score => _window.Editor.Playback;
        public int ScoreTrackIndex => Math.Max(0, _window.Editor.SelectedTrackIndex);
        public void SelectScoreTrack() => _window.Editor.Playback.TrackIndex = ScoreTrackIndex;
        public bool OnUiThread => _window.Dispatcher.CheckAccess();
        public void Post(Action work) => _window.PostIfOpen(work, DispatcherPriority.Normal);

        public void FollowPlayheadBar(int bar) => _window._follow.OnPlayheadBar(bar);
        public void ShowPlayingSection(int bar) => _window.UpdatePlayingSectionMarker(bar);
        public void ShowArrangementPlayhead(int bar, double fraction, bool paused) => _window.Arrangement.SetPlayhead(bar, fraction, playbackActive: true, playbackPaused: paused);
        public bool InstrumentAnimating => _window.Instrument.IsAnimating && _window.SelectedTrack is { } shown && (shown.Kind == TrackKind.Drums || shown.MidiChannel == 9);
        public bool FollowsFretboard => _window._follow.FollowFretboard;
        public void RefreshInstrument() => _window.RefreshInstrument();
        public void RefreshStatus() => _window.RefreshStatus();

        public void ShowPlayheadGeometry()
        {
            var window = _window;
            window.Playhead.SetGeometry(window.Editor.Playback.PlayheadGeometry());
            // The duration glow is off by default; don't compute its geometry every frame when it is hidden.
            window.Playhead.SetDurationGeometries(window._settings.Follow.DurationTintEnabled
                ? window.Editor.Playback.DurationGeometries()
                : Array.Empty<(double, double, double, double)>());
        }

        public void ShowTimeline(ScoreTimeline timeline, bool rebaseBarMappings)
        {
            _window._timeline = timeline;
            _window.Editor.Playback.Timeline = timeline;
            if (rebaseBarMappings) _window.RebasePlaybackBarMappings();
        }

        public void ShowStopped()
        {
            var window = _window;
            window.Editor.Playback.Active = false;
            window.Editor.Playback.BarRemap = null;
            window.Editor.Playback.Clear();
            window.Playhead.SetGeometry(null);
            window.Playhead.SetDurationGeometry(null);
            window.SyncArrangementPlayhead();
            window.SetPlayIcon(false);
            window.MidiLed.Fill = Brushes.Gray;
        }

        public void ShowTransportRunning(bool paused)
        {
            _window.SetPlayIcon(!paused);
            _window.MidiLed.Fill = paused ? Brushes.Gray : Brushes.LimeGreen;
            _window.StatusText.Text = paused ? "Paused" : "Playing";
        }

        public void ShowPlaybackFinished()
        {
            var window = _window;
            window._follow.ResetRow();
            window.Editor.Playback.BarRemap = null;
            window.UpdatePlayingSectionMarker(-1);
            window.Editor.Playback.Active = false;
            window.Playhead.SetGeometry(null);
            window.Playhead.SetDurationGeometry(null);
            window._follow.Halt();
            window.Editor.Playback.Clear();
            window.SyncArrangementPlayhead();
            window.SetPlayIcon(false);
            window.VideoRecordTransportStopped();
            window.StatusText.Text = "Playback finished";
            window.MidiLed.Fill = Brushes.Gray;
            window.RefreshInstrument();
        }

        public void HaltFollow() => _window._follow.Halt();
        public void ReattachFollow() => _window._follow.ResetForPlayback();
        public void ResetFollowRow() => _window._follow.ResetRow();
    }
}
