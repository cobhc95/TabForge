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

// MainWindow, tracks and mixer: mixer header menu, global tuning, mixer drag-reorder, add/delete/move tracks.
public partial class MainWindow
{
    private MenuItem MixerMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header, Style = (Style)FindResource(typeof(MenuItem)) };
        item.Click += (_, _) => action();
        return item;
    }

    private ContextMenu MixerMenu() => new()
    {
        Style = (Style)FindResource(typeof(ContextMenu)),
        Background = (Brush)FindResource("Panel2Brush"),
        Foreground = (Brush)FindResource("TextBrush")
    };

    /// <summary>Pan style, master volume knob and global tuning button in the arrangement header.</summary>
    private void WireMixerHeader()
    {
        Views.DiscardPrompt.IsEnabled = () => _settings.General.ConfirmDiscardSettingsChanges;
        Views.DiscardPrompt.DisableAndSave = () =>
        {
            _settings.General.ConfirmDiscardSettingsChanges = false;
            SaveSettings();
        };
        if (!_settings.Audio.PanSliderAdopted)
        {
            _settings.Audio.PanSliderAdopted = true;
            _settings.Audio.PanKnobs = false;
        }
        Arrangement.PanKnobs = _settings.Audio.PanKnobs;
        Arrangement.VolumeKnobs = _settings.Audio.VolumeKnobs;
        TabForge.Playback.PlaybackEngine.MetronomeBoost = _settings.Audio.MetronomeBoost;
        MetronomeBoostCheck.IsChecked = _settings.Audio.MetronomeBoost;
        TabForge.Playback.PlaybackEngine.CountInVolume = _settings.Audio.CountInVolume;
        ApplyCountInSound();
        CountInVolumeSlider.Value = _settings.Audio.CountInVolume;
        ApplyLoopBehaviour();
        TabForge.Playback.PlaybackEngine.LoopCompleted += (engine, done) => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(engine, _midi)) UpdateLoopCountBadge(done);
        });
        var timeline = _settings.Timeline;
        if (timeline.TrackColumnOrder is not null || timeline.TrackColumnWidths is not null || timeline.TrackControlsWidth > 0)
            if (timeline.TrackColumnOrder is { Count: > 1 } saved && saved[0] == "colour" && saved[1] == "settings")
                (saved[0], saved[1]) = ("settings", "colour"); // new default: settings icon before the colour
            Arrangement.ColumnState = (timeline.TrackColumnOrder ?? Views.ArrangementPanel.DefaultColumnOrder.ToList(),
                timeline.TrackColumnWidths ?? new(), timeline.TrackControlsWidth);
        Arrangement.HiddenColumns = timeline.HiddenTrackColumns;
        Arrangement.AutoFitState = () => _settings.Timeline.AutoFitTrackList;
        Arrangement.AutoFitToggleRequested += ToggleAutoFitTrackList;
        Arrangement.ColumnLayoutChanged += (_, _) =>
        {
            var (order, widths, area) = Arrangement.ColumnState;
            timeline.TrackColumnOrder = order;
            timeline.TrackColumnWidths = widths;
            timeline.TrackControlsWidth = area;
            SaveSettings();
        };
        Arrangement.PanStyleChanged += (_, knobs) =>
        {
            _settings.Audio.PanKnobs = knobs;
            Arrangement.PanKnobs = knobs;
            RefreshArrangement();
            SaveSettings();
        };

        var master = Arrangement.MasterVolumeKnob;
        master.Value = _settings.Audio.MasterVolume;
        master.ValueChanged += (_, e) =>
        {
            _settings.Audio.MasterVolume = (int)e.NewValue;
            _midi.SetMasterVolume(_project, (int)e.NewValue);
            if (_mainWindowInitialized) SyncAudioEngine(); // plug-in tracks follow the master too
            _mixerWindow?.SyncValues();                    // the mixer's Master row follows the knob
            StatusText.Text = $"Master volume {(int)e.NewValue}%";
            QueueMetronomeSettingsSave();
        };
        var masterMenu = MixerMenu();
        masterMenu.Items.Add(MixerMenuItem("Set exact volume…", () =>
        {
            var text = GpDialogs.Prompt("Master volume", "Master volume for all tracks (0–100 %):", ((int)master.Value).ToString());
            if (int.TryParse(text?.Trim().TrimEnd('%'), out var value)) master.Value = Math.Clamp(value, 0, 100);
        }));
        masterMenu.Items.Add(MenuSeparator());
        foreach (var preset in new[] { 100, 75, 50, 25 })
            masterMenu.Items.Add(MixerMenuItem(preset == 100 ? "Reset to 100%" : $"{preset}%", () => master.Value = preset));
        master.ContextMenu = masterMenu;

        Arrangement.TuningMenuRequested += (_, _) =>
        {
            var menu = MixerMenu();
            menu.Items.Add(MixerMenuItem("Tune up a semitone (+1)", () => RetuneAllTracks(1)));
            menu.Items.Add(MixerMenuItem("Tune down a semitone (−1)", () => RetuneAllTracks(-1)));
            menu.Items.Add(MixerMenuItem("Global tuning window…", ShowGlobalTuningWindow));
            menu.Items.Add(MenuSeparator());
            var reset = MixerMenuItem("Back to original tuning", () => RetuneStrings(_globalStringOffsets.Select(o => -o).ToArray()));
            reset.IsEnabled = _globalStringOffsets.Any(o => o != 0);
            menu.Items.Add(reset);
            menu.PlacementTarget = Arrangement.TuningButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        Arrangement.TuningNumberClicked += (_, _) => ShowGlobalTuningWindow();
        Arrangement.TuningIconClicked += (_, _) => ShowGlobalTuningWindow();
        Arrangement.TuningShiftEdited += (_, shift) => SetUniformTuningShift(shift);
        Arrangement.AreaMoveDropped += (_, target) => MoveAreaTo(target);
    }

    // Per-string shift from the song's original tuning, six-string reference (high to low).
    private int[] _globalStringOffsets => Doc.TuningShift;

    private bool TuningIsUniform => _globalStringOffsets.All(o => o == _globalStringOffsets[0]);

    private void SetUniformTuningShift(int shift) =>
        RetuneStrings(_globalStringOffsets.Select(o => shift - o).ToArray());

    private void ShowGlobalTuningWindow()
    {
        var current = Views.GlobalTuningWindow.StandardE.Select((p, i) => p + _globalStringOffsets[i]).ToArray();
        var chosen = Views.GlobalTuningWindow.Show(this, current);
        if (chosen is null) return;
        RetuneStrings(chosen.Select((p, i) => p - current[i]).ToArray());
    }

    private void RetuneAllTracks(int semitones) => RetuneStrings(Enumerable.Repeat(semitones, 6).ToArray());

    /// <summary>
    /// Retunes every pitched track by a per-string change (six-string reference, high to low): string
    /// tunings and sounding pitches move together and fret numbers stay the same, like physically
    /// retuning the instrument. Strings map from the lowest string up, so a bass follows the four
    /// lowest strings and extra low strings on 7/8-strings follow the lowest.
    /// </summary>
    private void RetuneStrings(int[] delta)
    {
        if (delta.All(d => d == 0)) return;
        CaptureUndo();
        foreach (var track in _project.Tracks.Where(t => t.MidiChannel != 9))
        {
            var count = track.StringTunings.Count;
            int DeltaOf(int stringIndex) => delta[Math.Clamp(count <= 6 ? stringIndex + (6 - count) : Math.Min(stringIndex, 5), 0, 5)];
            track.StringTunings = track.StringTunings.Select((p, s) => Math.Clamp(p + DeltaOf(s), 0, 127)).ToList();
            foreach (var measure in track.Measures)
                foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                    foreach (var note in cell.Notes)
                    {
                        var d = DeltaOf(note.StringIndex);
                        note.MidiValue = Math.Clamp(note.MidiValue + d, 0, 127);
                        if (note.SlideTargetMidi > 0) note.SlideTargetMidi = Math.Clamp(note.SlideTargetMidi + d, 0, 127);
                        if (note.TrillTargetMidi > 0) note.TrillTargetMidi = Math.Clamp(note.TrillTargetMidi + d, 0, 127);
                    }
        }
        for (var i = 0; i < 6; i++) _globalStringOffsets[i] += delta[i];
        UpdateTuningLabel();
        CommitEdit(EditRefresh.Score | EditRefresh.Arrangement | EditRefresh.Instrument);
        _midi.Rebuild(_project);
        StatusText.Text = _globalStringOffsets.All(o => o == 0) ? "Original tuning"
            : TuningIsUniform ? $"All tracks retuned {(_globalTuneOffset > 0 ? "+" : "")}{_globalTuneOffset} semitones"
            : "All tracks retuned (custom per-string tuning)";
    }

    private void UpdateTuningLabel()
    {
        if (TuningIsUniform) { Arrangement.SetTuningLabel(_globalStringOffsets[0]); return; }
        var tuning = Views.GlobalTuningWindow.StandardE.Select((p, i) => p + _globalStringOffsets[i]).ToArray();
        var preset = Views.TrackPropertiesWindow.SixStringPresets.FirstOrDefault(p => p.HighToLow.SequenceEqual(tuning));
        Arrangement.SetTuningLabel(preset.Name?.Split(" (")[0] ?? "Custom");
    }
    /// <summary>Sizes the arrangement dock so every track row fits with no empty space below.</summary>
    private void ToggleAutoFitTrackList()
    {
        _settings.Timeline.AutoFitTrackList = !_settings.Timeline.AutoFitTrackList;
        SaveSettings();
        if (_settings.Timeline.AutoFitTrackList) FitTimelineToTracks();
        StatusText.Text = _settings.Timeline.AutoFitTrackList ? "Track list auto-resizes to fit" : "Track list keeps its size";
    }

    private void FitTimelineToTracks()
    {
        if (!_settings.Timeline.AutoFitTrackList) return;
        var rows = Math.Max(1, _project.Tracks.Count);
        _dockWorkspace?.FitPanelHeight("timeline", Arrangement.PreferredHeight(rows));
    }

    private void ScheduleFitTimelineToTracks() =>
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, FitTimelineToTracks);


    // --- drag a mixer row to reorder tracks ---
    private Point _mixerDragStart;
    private int _mixerDragFrom = -1;
    private int _mixerDragTarget = -1;
    private bool _mixerDragArmed;

    private void TrackMixerGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mixerDragFrom = RowIndexAt(e.OriginalSource as DependencyObject);
        _mixerDragTarget = _mixerDragFrom;
        _mixerDragArmed = false;
        _mixerDragStart = e.GetPosition(TrackMixerGrid);
    }

    private void TrackMixerGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_mixerDragFrom < 0 || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(TrackMixerGrid);
        if (!_mixerDragArmed && Math.Abs(p.Y - _mixerDragStart.Y) < 6) return;
        _mixerDragArmed = true;
        var target = RowIndexAt(TrackMixerGrid.InputHitTest(p) as DependencyObject);
        if (target >= 0 && target != _mixerDragTarget)
        {
            _mixerDragTarget = target;
            TrackMixerGrid.SelectedIndex = target;   // live feedback: selection follows the drag
        }
        e.Handled = true;
    }

    private void TrackMixerGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var from = _mixerDragFrom;
        var to = _mixerDragTarget;
        var armed = _mixerDragArmed;
        _mixerDragFrom = -1;
        _mixerDragTarget = -1;
        _mixerDragArmed = false;
        if (armed && from >= 0 && to >= 0 && to != from)
        {
            MoveTrackTo(from, to);
            e.Handled = true;
        }
    }

    private static int RowIndexAt(DependencyObject? source)
    {
        while (source is not null and not DataGridRow)
            source = VisualTreeHelper.GetParent(source);
        return source is DataGridRow row ? row.GetIndex() : -1;
    }

    private void TrackMixerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)    {
        if (TrackMixerGrid.SelectedIndex < 0) return;
        Editor.SelectedTrackIndex = TrackMixerGrid.SelectedIndex;
        Doc.TrackIndex = TrackMixerGrid.SelectedIndex;
        // The selected bars stay selected on the new track: the model re-applies them to the score.
        _selection.SetTrack(TrackMixerGrid.SelectedIndex);
        RefreshPluginChain();
        RefreshInstrument();
        RefreshArrangementSelection();
        SyncSelectedOutput();
        RefreshStatus();
        RefreshToolsPalette();
    }

    private void TrackMixerGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_restoring) return;
        _project.IsDirty = true;
        // Live mixer edit while playing; a rename does not change the sound, so it must not restart playback.
        var renamed = e.Column is DataGridBoundColumn { Binding: System.Windows.Data.Binding { Path.Path: nameof(TrackModel.Name) } };
        if (!renamed) _midi.Rebuild(_project);
        Dispatcher.BeginInvoke(new Action(() => { RefreshStatus(); RefreshInstrument(); RefreshArrangement(); RefreshTabs(); UpdateTitle(); }));
    }

    private void SyncSelectedOutput()
    {
        if (SelectedTrack is null) return;
        SelectedOutputCombo.SelectedValue = SelectedTrack.MidiOutputDeviceId;
        OutputStatusText.Text = MidiDevices.FirstOrDefault(d => d.DeviceId == SelectedTrack.MidiOutputDeviceId)?.Name ?? "MIDI output";
    }

    private void RefreshStatus()
    {
        var track = SelectedTrack;
        // While the transport runs the status shows the *playing* position (like the standard LCD),
        // not the editing cursor.
        var playing = _isPlayingVisual && _playheadBar >= 0;
        var shownBar = playing ? Math.Clamp(_playheadBar, 0, Math.Max(0, MaxMeasures() - 1)) : Editor.SelectedMeasure;
        var shownCell = playing ? Math.Max(0, _playheadCell) : Editor.SelectedCell;
        PositionText.Text = track is null
            ? "No track"
            : $"Measure {shownBar + 1}  ·  {track.Name}  ·  cell {shownCell + 1}";

        // The status bar shows the bar's actual:theoretical duration in the status bar.
        var bar = MusicTime.BarOf(_project, shownBar);
        var num = bar?.TimeSigNum ?? _project.TimeSignatureNumerator;
        var den = bar?.TimeSigDenom ?? _project.TimeSignatureDenominator;
        var state = MusicTime.AnalyzeBar(_project, shownBar);
        BarStateText.Text = $"{num}/{den}  {state.Used:0.##}:{state.Slots}";
        BarStateText.Foreground = state.Error
            ? new SolidColorBrush(Color.FromRgb(229, 72, 77))
            : (Brush)FindResource("MutedBrush");
        BarStateText.ToolTip = state.Error
            ? "This bar is incomplete or too long for its time signature"
            : "Bar duration: written : expected";

        // The toolbar shows the signatures in force at the bar you are looking at (a change carries forward to the bars after it).
        var shownKey = bar?.KeySignature ?? _project.KeySignature;
        TimeSigLabel.Text = $"{num}/{den}";
        KeyLabelText.Text = KeyLabel(shownKey);
        MasterInfoText.Text = $"♩={_project.Tempo}  ·  {KeyLabel(shownKey)}  ·  {MusicTime.DurationName(Editor.CurrentDurationDenominator)}{(Editor.CurrentDots == 1 ? " dotted" : Editor.CurrentDots >= 2 ? " double-dotted" : "")}{(Editor.CurrentTriplet ? " triplet" : "")}";
    }

    private static string KeyLabel(int k) => k == 0 ? "C major" : k > 0 ? $"{k}♯" : $"{-k}♭";


    // ---------- track ----------

    private void AddGuitar_Click(object sender, RoutedEventArgs e) => AddTrack(TrackKind.Guitar);
    private void AddBass_Click(object sender, RoutedEventArgs e) => AddTrack(TrackKind.Bass);
    private void AddDrums_Click(object sender, RoutedEventArgs e) => AddTrack(TrackKind.Drums);
    private void AddKeys_Click(object sender, RoutedEventArgs e) => AddTrack(TrackKind.Keys);

    private void AddTrack(TrackKind kind)
    {
        CaptureUndo();
        var track = _trackController.CreateTrack(_project, kind);
        Plugins.AutoChains.Apply(_settings.Plugins, track);
        _project.Tracks.Add(track); _project.IsDirty = true; RefreshTracks(); TrackMixerGrid.SelectedIndex = _project.Tracks.Count - 1; RefreshArrangement(); ScheduleFitTimelineToTracks(); UpdateTitle();
    }

    /// <summary>
    /// The arrangement's + Track button: the Add track window (the Track properties window for a new track),
    /// with the instrument catalogue, tuning, mixer and the position in the track list.
    /// </summary>
    private void AddTrackWithWindow()
    {
        var track = _trackController.CreateTrack(_project, TrackKind.Guitar);
        var placement = new Views.TrackPropertiesWindow.AddTrackPlacement(_project.Tracks.Count, TrackMixerGrid.SelectedIndex);
        if (!Views.TrackPropertiesWindow.ShowAdd(this, track, placement)) return;
        if (track.Kind == TrackKind.Drums)
        {
            var drums = _trackController.CreateTrack(_project, TrackKind.Drums);
            track.MidiChannel = 9;
            if (track.StringTunings.SequenceEqual(new[] { 64, 59, 55, 50, 45, 40 })) track.StringTunings = drums.StringTunings.ToList();
        }
        Plugins.AutoChains.Apply(_settings.Plugins, track);
        CaptureUndo();
        var at = Math.Clamp(placement.InsertIndex, 0, _project.Tracks.Count);
        _project.Tracks.Insert(at, track);
        _project.IsDirty = true;
        _midi.Rebuild(_project);
        RefreshTracks();
        TrackMixerGrid.SelectedIndex = at;
        RefreshArrangement();
        RefreshInstrument();
        ScheduleFitTimelineToTracks();
        UpdateTitle();
        StatusText.Text = $"Added {track.Name} as track {at + 1}";
    }

    private void DeleteTrack_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTrack is null || _project.Tracks.Count <= 1) { StatusText.Text = "Cannot delete the last track"; return; }
        CaptureUndo();
        _trackController.DeleteTrack(_project, TrackMixerGrid.SelectedIndex);
        _project.IsDirty = true; RefreshTracks(); RefreshArrangement(); ScheduleFitTimelineToTracks(); UpdateTitle();
    }

    private void MoveTrackUp_Click(object sender, RoutedEventArgs e) => MoveTrack(-1);
    private void MoveTrackDown_Click(object sender, RoutedEventArgs e) => MoveTrack(1);
    private void MoveTrack(int dir) => MoveTrackTo(TrackMixerGrid.SelectedIndex, TrackMixerGrid.SelectedIndex + dir);

    /// <summary>
    /// Moves a track to a new position. Called by the Track menu and by drag-reordering in the
    /// arrangement track list and the detailed mixer.
    /// </summary>
    private void MoveTrackTo(int from, int to)
    {
        if (from < 0 || from >= _project.Tracks.Count || from == Math.Clamp(to, 0, _project.Tracks.Count - 1)) return;
        var name = _project.Tracks[from].Name;
        var orderBefore = CaptureOrderLayout();
        // The undo state was taken when the drag started (the model is unchanged until now).
        var dragSnapshot = _trackUndoSnapshot;
        _trackUndoSnapshot = null;
        UndoCapture? capture;
        if (dragSnapshot is { } dragStart && !_restoring)
        {
            capture = _undo.Capture(dragStart);
            if (capture is { Stored: true } stored) RememberPlaybackBarMapping(stored.Snapshot);
        }
        else capture = CaptureUndo();
        if (!_trackController.MoveTrack(_project, from, to))
        {
            if (capture is { } cancelled) _undo.Discard(cancelled);
            return;
        }
        to = Math.Clamp(to, 0, _project.Tracks.Count - 1);
        _project.IsDirty = true;
        RefreshTracks();
        TrackMixerGrid.SelectedIndex = to;
        RefreshArrangement();
        UpdateTitle();
        PlayOrderAnimation(orderBefore);   // the open mixer reorders with it, animating at the same time
        // Note events carry the track index: hand the new order to the engine, which swaps it in at the
        // next bar boundary without stopping playback (no restart, no audible gap).
        if (_midi.IsPlaying)
            _midi.RefreshArrangement(_project, Enumerable.Range(0, MaxMeasures()).ToArray());
        StatusText.Text = $"Moved '{name}' to position {to + 1}";
    }

    private void TrackProps_Click(object sender, RoutedEventArgs e)
    {
        var t = SelectedTrack; if (t is null) return;
        var capture = CaptureUndo();
        if (Views.TrackPropertiesWindow.Show(this, t)) { _project.IsDirty = true; Editor.InvalidateScoreLayout(); _midi.Rebuild(_project); RefreshTracks(); RefreshArrangement(); RefreshInstrument(); RefreshStatus(); UpdateTitle(); }
        else if (capture is { } cancelled) _undo.Discard(cancelled);
    }
}
