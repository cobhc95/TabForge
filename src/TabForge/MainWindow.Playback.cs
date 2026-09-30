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
public partial class MainWindow
{
    // ---------- playback (standard: Space = play/pause, click to reposition) ----------

    private PlaybackOptions BuildOptions()
    {
        var (ls, le) = GetLoopRange();
        TabForge.Playback.PlaybackEngine.CountInEachSection = _countIn && _settings.Audio.CountInEachSection;
        _midi.SetSectionStarts(_project.Markers.Select(marker => marker.MeasureIndex).Where(bar => bar > 0));
        _midi.SetMasterVolume(_project, _settings.Audio.MasterVolume);
        return new PlaybackOptions
        {
            StartBar = Editor.SelectedMeasure,
            StartCell = Editor.SelectedCell,
            Speed = _speed,
            Loop = _loop,
            LoopStartBar = ls,
            LoopEndBar = le,
            LoopStartCell = _loopStartCell,
            LoopEndCell = _loopEndCell,
            Metronome = _metronome,
            // "Only from bar 1": the count-in plays only when starting at the top of the song.
            CountIn = _countIn && (!_settings.Audio.CountInOnlyAtSongStart || Editor.SelectedMeasure == 0),
            CountInBars = _settings.Audio.CountInBars,
            MetronomeAccentNote = _settings.Audio.MetronomeAccent,
            MetronomeClickNote = _settings.Audio.MetronomeClick,
            MetronomeVolume = _settings.Audio.MetronomeVolume,
            MetronomeAccentVolume = _settings.Audio.MetronomeAccentVolume,
            MetronomeClickVolume = _settings.Audio.MetronomeClickVolume,
            MetronomeSubdivision = _settings.Audio.MetronomeSubdivision,
            LetRingCapMs = _settings.Audio.LetRingCapMs
        };
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
        Editor.PlaybackActive = true;
        Arrangement.SetPlayhead(Editor.SelectedMeasure, 0, playbackActive: true);
        UpdatePlayingSectionMarker(Editor.SelectedMeasure);
        var options = BuildOptions();
        Editor.PlaybackTrackIndex = Math.Max(0, Editor.SelectedTrackIndex);
        Arrangement.SetLoopRange(GetLoopRange().start, GetLoopRange().end);
        _playbackUiTick.Start();
        _follow.ResetForPlayback();
        playback.PlaybackBarMappingsBySnapshot.Clear();
        playback.PlaybackBarRemap = Enumerable.Range(0, MaxMeasures()).ToArray();
        Editor.PlaybackBarRemap = playback.PlaybackBarRemap;
        RememberPlaybackBarMapping(_undo.Snapshot(_project));
        var songProject = session.Project;
        playback.Engine.Start(session.Project, options,
            // Engine thread: record the newest position, and keep audio clips in step with the song.
            position =>
            {
                playback.ReportPosition(position);
                SongClock.Report(songProject, position, !playback.Engine.IsPaused);
            },
            playback.MarkFinished);
        SetPlayIcon(true);
    }

    /// <summary>Receives the engine's compiled timeline (also after a seek or a speed change).</summary>
    private void OnPlaybackTimelineChanged(ScoreTimeline timeline)
    {
        if (!ReferenceEquals(_observedPlaybackDocument, Doc)) return;
        _timeline = timeline;
        Editor.Timeline = timeline;
        RebasePlaybackBarMappings();
        _playheadMs = timeline.PlayFromMs;
        _playheadBar = -1;                     // status shows the cursor until the first position tick
        _playheadCell = 0;
        Editor.PlaybackMs = timeline.PlayFromMs;
        _lastUiBar = -1;
        _lastUiCell = -1;
        _lastUiMs = timeline.PlayFromMs - 1;
    }

    /// <summary>Installs the live-reordered future timeline without moving the current playhead.</summary>
    private void OnPlaybackTimelineRevised(ScoreTimeline timeline)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnPlaybackTimelineRevised(timeline));
            return;
        }
        if (!ReferenceEquals(_observedPlaybackDocument, Doc)) return;
        _timeline = timeline;
        Editor.Timeline = timeline;
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

    /// <summary>Applies the newest playback position, doing only the work that actually changed.</summary>
    private void ApplyPendingPlayhead()
    {
        var playback = Playback;
        if (playback.TakeFinished())
        {
            CompletePlaybackForActiveDocument();
            return;
        }
        if (!playback.TryTakePendingPosition(out var position) || position is null) return;
        // Draw what is heard now: the live clock minus the output latency, not the scheduler's last report.
        if (playback.Engine.IsPlaying && !playback.Engine.IsPaused) position = playback.Engine.AudiblePlayhead();

        var remap = playback.PlaybackBarRemap;
        var bar = remap is not null && position.Bar >= 0 && position.Bar < remap.Length
            ? remap[position.Bar]
            : position.Bar;
        _playheadMs = position.ElapsedMs;
        _playheadBar = bar;
        _playheadCell = position.Cell;
        _playheadFraction = position.BarFraction;
        // Keep score following independent of score-cache repaint cadence. In particular, layout/zoom
        // and loop-wrap updates can change the follow target without changing a rendered note boundary.
        _follow.OnPlayheadBar(bar);

        var barChanged = bar != _lastUiBar || position.Cell != _lastUiCell;
        var now = DateTime.UtcNow;
        if (barChanged) UpdatePlayingSectionMarker(bar);

        // The arrangement playhead is a cheap overlay: update every tick.
        Arrangement.SetPlayhead(bar, position.BarFraction, playbackActive: true, playbackPaused: _midi.IsPaused);

        // The score page is the expensive one: repaint only when the beat changes, when a note
        // actually starts/ends (so the highlight is exactly as long as the note), or as a fallback
        // cadence. The fretboard follows at a lower cadence.
        var noteBoundary = Editor.PlaybackNeedsRepaint(_lastUiMs, position.ElapsedMs);
        if (barChanged || noteBoundary || (now - _lastEditorUpdate).TotalMilliseconds >= 250)
        {
            _lastEditorUpdate = now;
            Editor.PlaybackMs = position.ElapsedMs;
            Editor.PlaybackFraction = position.BarFraction;
            Editor.PlaybackTrackIndex = Math.Max(0, Editor.SelectedTrackIndex);
            Editor.SetPlayhead(bar, position.Cell);
        }
        else
        {
            Editor.PlaybackMs = position.ElapsedMs;
            Editor.PlaybackFraction = position.BarFraction;
        }

        // A drum hit starts at a note boundary; per-frame redraws run only while its glow is fading.
        var drumGlowing = Instrument.IsAnimating && SelectedTrack is { } shownTrack &&
            (shownTrack.Kind == TrackKind.Drums || shownTrack.MidiChannel == 9);
        if (drumGlowing || barChanged || noteBoundary || (now - _lastInstrumentUpdate).TotalMilliseconds >= 200)
        {
            _lastInstrumentUpdate = now;
            if (_follow.FollowFretboard) RefreshInstrument();
        }
        if (barChanged) RefreshStatus();
        Playhead.SetGeometry(Editor.PlayheadGeometry());
        // The duration glow is off by default; don't compute its geometry every frame when it is hidden.
        Playhead.SetDurationGeometries(_settings.Follow.DurationTintEnabled
            ? Editor.PlaybackDurationGeometries()
            : Array.Empty<(double, double, double, double)>());
        _lastUiBar = bar;
        _lastUiCell = position.Cell;
        _lastUiMs = position.ElapsedMs;
    }

    private void SyncPlaybackUiToActiveDocument()
    {
        var playback = Playback;
        var engine = playback.Engine;
        if (!engine.IsPlaying || !playback.IsPlayingVisual)
        {
            _playbackUiTick.Stop();
            _follow.Halt();
            Editor.PlaybackActive = false;
            Editor.PlaybackBarRemap = null;
            Editor.ClearPlayhead();
            Playhead.SetGeometry(null);
            Playhead.SetDurationGeometry(null);
            SyncArrangementPlayhead();
            SetPlayIcon(false);
            MidiLed.Fill = Brushes.Gray;
            return;
        }

        Editor.Timeline = playback.Timeline;
        Editor.PlaybackBarRemap = playback.PlaybackBarRemap;
        Editor.PlaybackActive = true;
        Editor.PlaybackTrackIndex = Math.Max(0, Editor.SelectedTrackIndex);
        _follow.ResetRow();
        _lastUiBar = -1;
        _lastUiCell = -1;
        _lastUiMs = double.NegativeInfinity;
        playback.ReportPosition(engine.Playhead());
        _playbackUiTick.Start();
        _follow.Halt();
        ApplyPendingPlayhead();
        Arrangement.SetPlayhead(Math.Max(0, _playheadBar), _playheadFraction,
            playbackActive: true, playbackPaused: engine.IsPaused);
        SetPlayIcon(!engine.IsPaused);
        MidiLed.Fill = engine.IsPaused ? Brushes.Gray : Brushes.LimeGreen;
        StatusText.Text = engine.IsPaused ? "Paused" : "Playing";
    }

    private void CompletePlaybackForActiveDocument()
    {
        var playback = Playback;
        playback.IsPlayingVisual = false;
        playback.PlayheadBar = -1;
        playback.PlaybackBarRemap = null;
        playback.PlaybackBarMappingsBySnapshot.Clear();
        _follow.ResetRow();
        Editor.PlaybackBarRemap = null;
        UpdatePlayingSectionMarker(-1);
        Editor.PlaybackActive = false;
        Playhead.SetGeometry(null);
        Playhead.SetDurationGeometry(null);
        _playbackUiTick.Stop();
        _follow.Halt();
        Editor.ClearPlayhead();
        SyncArrangementPlayhead();
        SetPlayIcon(false);
        StatusText.Text = "Playback finished";
        MidiLed.Fill = Brushes.Gray;
        RefreshInstrument();
    }

    private int _lastUiBar = -1;
    private int _lastUiCell = -1;
    private double _lastUiMs;
    private DateTime _lastEditorUpdate = DateTime.MinValue;
    private DateTime _lastInstrumentUpdate = DateTime.MinValue;

    /// <summary>Rebuilds the visual timeline (used when the score changes while stopped).</summary>
    private void RebuildVisualTimeline()
    {
        _timeline = MidiTimelineBuilder.Build(_project, BuildOptions());
        Editor.Timeline = _timeline;
        _playheadMs = 0;
    }

    private (int start, int end) GetLoopRange()
    {
        var max = Math.Max(0, MaxMeasures() - 1);
        var s = Math.Clamp(_loopStartBar, 0, max);
        var e2 = Math.Clamp(_loopEndBar, 0, max);
        if (s > e2) (s, e2) = (e2, s);
        return (s, e2);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();
    private void StopPlayback()
    {
        if (IsRecording) ToggleRecording();
        _midi.Stop();
        SongClock.Stopped();
        Playback.ClearPlaybackPosition();
        _follow.ResetRow();
        Editor.PlaybackBarRemap = null;
        Editor.PlaybackActive = false;
        UpdatePlayingSectionMarker(-1);
        Playhead.SetGeometry(null);
        Playhead.SetDurationGeometry(null);
        _playheadBar = -1;
        _playbackUiTick.Stop();
        _follow.Halt();
        Editor.ClearPlayhead();
        Editor.PlaybackMs = 0;
        SyncArrangementPlayhead();
        SetPlayIcon(false);
        StatusText.Text = "Stopped";
        MidiLed.Fill = Brushes.Gray;
        RefreshInstrument();
        RefreshStatus();
    }

    private bool _loopHasArea;

    private void Loop_Click(object sender, RoutedEventArgs e) => SetLoopActive(!_loop);

    private void SetLoopActive(bool loop)
    {
        // The score selection is authoritative when the loop button is enabled. The button can
        // receive focus (and the editor selection can be cleared by a click handler), so reapply
        // the selected musical range before falling back to the whole song/default section.
        if (loop && Editor.HasSelection)
        {
            var range = Editor.SelectionCellRange;
            ApplyLoopRange(range.StartMeasure, range.EndMeasure, range.StartCell, range.EndCell);
        }
        else if (loop && !_loopHasArea)
        {
            // No selected area: loop the whole song, or (opt-in) the section being played.
            var lastBar = Math.Max(0, MaxMeasures() - 1);
            var (start, end) = _settings.Audio.LoopButtonLoopsSection ? DefaultLoopArea() : (0, lastBar);
            _loopStartBar = start;
            _loopEndBar = end;
            _loopStartCell = 0;
            _loopEndCell = -1;
            Arrangement.SetLoopRange(start, end);
            _midi.SetLoopRange(start, end, _loopStartCell, _loopEndCell);
        }
        // Keep the selected loop range when toggling looping off. Turning the transport mode off
        // should not discard the user's selection; pressing the button again reuses that range.
        _loop = loop;
        SetTransportActive(LoopButton, _loop);
        Arrangement.SetLoopEnabled(ShowLoopOnTimeline);
        _midi.SetLoop(_loop);
        SyncAreaVisuals();
        StatusText.Text = _loop ? $"Looping bars {_loopStartBar + 1}-{_loopEndBar + 1}" : "Loop off";
    }

    /// <summary>The bars the loop button covers when no area was picked, per the loop settings.</summary>
    private (int start, int end) DefaultLoopArea()
    {
        var lastBar = Math.Max(0, (_project.Tracks.Count == 0 ? 1 : _project.Tracks.Max(t => t.Measures.Count)) - 1);
        var bar = Math.Clamp(_isPlayingVisual && _playheadBar >= 0 ? _playheadBar : Editor.SelectedMeasure, 0, lastBar);
        switch (_settings.Audio.LoopDefaultScope)
        {
            case "Bar":
                return (bar, bar);
            case "Song":
                return (0, lastBar);
            default:
                var markers = SectionLayout.Sorted(_project);
                var index = markers.FindLastIndex(m => m.MeasureIndex <= bar);
                if (index < 0) return markers.Count > 0 ? (0, Math.Max(0, markers[0].MeasureIndex - 1)) : (0, lastBar);
                var sectionEnd = SectionLayout.End(markers, index, lastBar + 1);
                // A bar in the gap after a resized section loops the gap itself.
                if (bar >= sectionEnd)
                    return (sectionEnd, Math.Clamp((index + 1 < markers.Count ? markers[index + 1].MeasureIndex : lastBar + 1) - 1, sectionEnd, lastBar));
                return (markers[index].MeasureIndex, Math.Clamp(sectionEnd - 1, markers[index].MeasureIndex, lastBar));
        }
    }

    private void LoopButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _syncingLoopSettings = true;
        LoopRangeText.Text = _loopHasArea || _loop
            ? $"Loop area: bars {_loopStartBar + 1}-{_loopEndBar + 1}{(_loop ? "" : " (loop off)")}"
            : "No loop area selected";
        LoopScopeCombo.SelectedItem = LoopScopeCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == _settings.Audio.LoopDefaultScope) ?? LoopScopeCombo.Items[0];
        LoopClearOnDisableCheck.IsChecked = _settings.Audio.LoopClearAreaOnDisable;
        LoopButtonSectionCheck.IsChecked = _settings.Audio.LoopButtonLoopsSection;
        SyncLoopBehaviourControls();
        _syncingLoopSettings = false;
        LoopSettingsPopup.IsOpen = true;
        e.Handled = true;
    }

    private bool _syncingLoopSettings;

    private void CloseLoopSettings_Click(object sender, RoutedEventArgs e) => LoopSettingsPopup.IsOpen = false;

    private void LoopScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingLoopSettings || LoopScopeCombo.SelectedItem is not ComboBoxItem { Tag: string scope }) return;
        _settings.Audio.LoopDefaultScope = scope;
        SaveSettings();
    }

    private void LoopClearOnDisable_Click(object sender, RoutedEventArgs e)
    {
        _settings.Audio.LoopClearAreaOnDisable = LoopClearOnDisableCheck.IsChecked == true;
        SaveSettings();
    }

    private void ClearLoopArea_Click(object sender, RoutedEventArgs e)
    {
        if (_loop) SetLoopActive(false);
        _selection.Clear(SelectionOrigin.Command);   // the area is the shared selection: the score drops it too
        LoopSettingsPopup.IsOpen = false;
        StatusText.Text = "Loop area cleared";
    }

    // One source of truth: _speed. The only speed control is SpeedCombo (Zoom & speed pane, "100%");
    // it, restored settings and any command write through ApplySpeed and re-draw from _speed.
    private bool _speedSync;

    private void SpeedCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_speedSync || !IsLoaded || _restoring || SpeedCombo.SelectedItem is not ComboBoxItem item) return;
        ApplySpeedText(item.Content?.ToString() ?? "100%");
    }

    private void SpeedCombo_ReSync(object sender, RoutedEventArgs e) => UpdateSpeedControls();

    private void SpeedCombo_VisibleReSync(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) UpdateSpeedControls();
    }

    private void SpeedCombo_LostFocus(object sender, RoutedEventArgs e) => CommitCustomSpeed();

    private void SpeedCombo_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitCustomSpeed();
        e.Handled = true;
    }

    private void CommitCustomSpeed()
    {
        if (!IsLoaded || _restoring || SpeedCombo.SelectedItem is ComboBoxItem) return;
        ApplySpeedText(SpeedCombo.Text);
    }

    /// <summary>Parses typed speed: "90%" or "90" = 0.9; a bare value of 4 or less is a factor ("0.9", "1.25").</summary>
    internal static double? ParseSpeedText(string? text)
    {
        var t = (text ?? "").Trim().Replace(',', '.');
        var pct = t.EndsWith('%');
        t = t.TrimEnd('%', '×', 'x', 'X').Trim();
        if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
            return null;
        return pct || v > 4 ? v / 100.0 : v;
    }

    private static readonly double[] SpeedPresets = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

    /// <summary>The next preset above (direction +1) or below (-1) the current speed; a custom speed steps to its neighbouring preset.</summary>
    internal static double NextSpeedPreset(double current, int direction)
    {
        current = ClampSpeed(current);
        if (direction > 0) foreach (var p in SpeedPresets) { if (p > current + 1e-6) return p; }
        else for (var i = SpeedPresets.Length - 1; i >= 0; i--) { if (SpeedPresets[i] < current - 1e-6) return SpeedPresets[i]; }
        return current;
    }

    internal static double ClampSpeed(double v) => double.IsFinite(v) ? Math.Clamp(v, 0.25, 2.0) : 1.0;

    private void ApplySpeedText(string text)
    {
        if (ParseSpeedText(text) is not { } v) { UpdateSpeedControls(); return; }
        ApplySpeed(v);
    }

    /// <summary>Sets the playback speed everywhere (engine and the speed box) and saves it.</summary>
    internal void ApplySpeed(double value)
    {
        _speed = ClampSpeed(value);
        if (_midi.IsPlaying) _midi.SetSpeed(_project, _speed);
        UpdateSpeedControls();
        StatusText.Text = $"Speed {_speed:0.00}×";
        if (IsLoaded && !_restoring) SaveSettings();
    }

    internal double CurrentSpeed => _speed;

    /// <summary>Re-draws the speed box from _speed (also used after settings restore).</summary>
    private void UpdateSpeedControls()
    {
        if (SpeedCombo is null) return;
        var was = _speedSync;
        _speedSync = true;
        try { ShowSpeedOn(SpeedCombo, _speed); }
        finally { _speedSync = was; }
    }

    /// <summary>Makes an editable speed combo display the given speed as a percentage; returns the text shown.</summary>
    internal static string ShowSpeedOn(ComboBox combo, double speed)
    {
        combo.ApplyTemplate();
        var label = $"{ClampSpeed(speed) * 100:0}%";
        var preset = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Content?.ToString(), label, StringComparison.Ordinal));
        combo.SelectedItem = preset;
        if (preset is null) combo.SelectedIndex = -1;
        combo.Text = label;
        return label;
    }

    private void TempoBox_LostFocus(object sender, RoutedEventArgs e) => ApplyTempo();
    private void ApplyTempo()
    {
        if (!int.TryParse(TempoBox.Text, out var bpm)) bpm = _project.Tempo;
        bpm = Math.Clamp(bpm, 20, 400);
        if (bpm != _project.Tempo) { CaptureUndo(); _project.Tempo = bpm; CommitEdit(EditRefresh.Status); }
        TempoBox.Text = bpm.ToString();
    }

    private void Beginning_Click(object sender, RoutedEventArgs e) { Editor.MoveToFirstBar(); ScrollToCursor(); }
    private void Previous_Click(object sender, RoutedEventArgs e) { Editor.MoveBar(-1); ScrollToCursor(); }
    private void Next_Click(object sender, RoutedEventArgs e) { Editor.MoveBar(1); ScrollToCursor(); }

    /// <summary>Keeps the playhead / cursor visible by scrolling the score pane.</summary>
    private void ScrollToCursor()
    {
        var track = SelectedTrack;
        if (track is null || track.Measures.Count == 0) return;
        _follow.JumpTo(Editor.ScrollOffsetForMeasure(Editor.SelectedMeasure));
        // One-line mode: bring the cursor bar into view unless it already is.
        if (Editor.HorizontalScroll)
        {
            var x = Editor.HorizontalOffsetForMeasure(Editor.SelectedMeasure);
            if (x < ScoreScroll.HorizontalOffset || x > ScoreScroll.HorizontalOffset + ScoreScroll.ViewportWidth - 120)
                ScoreScroll.ScrollToHorizontalOffset(x);
        }
    }

    private void ScoreScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => _follow.OnScrollChanged(e);

    /// <summary>Use a tunable, smaller pixel step instead of WPF's coarse default score-wheel jump.</summary>
    private void ScoreScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Delta != 0) ZoomBy(Math.Sign(e.Delta), e.GetPosition(ScoreScroll));
            e.Handled = true;
            return;
        }
        _follow.NoteUserScrollGesture();
        var distance = _scoreWheelScrollPixels * (e.Delta / 120.0);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            ScoreScroll.ScrollToHorizontalOffset(ScoreScroll.HorizontalOffset - distance);
        else
            ScoreScroll.ScrollToVerticalOffset(ScoreScroll.VerticalOffset - distance);
        e.Handled = true;
    }

    /// <summary>Scroll gestures that are not the wheel: scrollbar press/drag, scroll keys and touch pan.</summary>
    private void WireScoreScrollGestures()
    {
        ScoreScroll.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(d) is not null)
                _follow.SetScrollBarDrag(true);
        }), true);
        ScoreScroll.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => _follow.SetScrollBarDrag(false)), true);
        ScoreScroll.AddHandler(LostMouseCaptureEvent, new MouseEventHandler((_, _) =>
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed) _follow.SetScrollBarDrag(false);
        }), true);
        ScoreScroll.AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Up or Key.Down or Key.Left or Key.Right)
                _follow.NoteUserScrollGesture();
        }), true);
        ScoreScroll.AddHandler(PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>((_, _) => _follow.NoteUserScrollGesture()), true);
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }
}
