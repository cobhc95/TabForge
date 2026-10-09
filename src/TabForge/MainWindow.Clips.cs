using TabForge.Documents;
using System.Windows;
using System.Windows.Controls;
using TabForge.Audio;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow: the clip lanes on the timeline: arm / input choice, lane play buttons and the wiring of the lane events. The clip commands (delete, nudge, lane
// up/down, copy, cut, paste, duplicate, mute, properties, moves between tracks, MIDI clips into notation, dropped audio files) belong to ClipEditController;
// ClipHost is the window's side of it.
public partial class MainWindow
{
    private void HookAudioLanes()
    {
        Arrangement.SetSongTime(bar => SongClock.BarStartSec(_project, bar), sec => SongClock.BarAt(_project, sec));
        Arrangement.ArmRequested += (_, index) => { if (index >= 0 && index < _project.Tracks.Count) ToggleArm(_project.Tracks[index]); };
        Arrangement.AudioInputChosen += (index, input) =>
        {
            if (index < 0 || index >= _project.Tracks.Count) return;
            DocumentEdits.Run(Doc, p => { p.Tracks[index].AudioInput = input; return true; }, invalidatesTimeline: false);
            ArmChanged();
        };
        Arrangement.MonitorToggled += (index, on) =>
        {
            if (index < 0 || index >= _project.Tracks.Count) return;
            DocumentEdits.Run(Doc, p => { p.Tracks[index].MonitorInput = on; return true; }, invalidatesTimeline: false);
            ArmChanged();
            StatusText.Text = on ? $"{_project.Tracks[index].Name}: monitoring on (you hear the input live)" : $"{_project.Tracks[index].Name}: monitoring off (recorded and metered, not played)";
        };
        Arrangement.ClipLaneSelected += (index, lane, midi, add) =>
        {
            if (index < 0 || index >= _project.Tracks.Count) return;
            var track = _project.Tracks[index];
            if (!DocumentEdits.Run(Doc, _ => ClipLanes.SelectTake(track, lane, midi, add), invalidatesTimeline: false).Changed) return;   // already the playing take: nothing to do
            _clips.Changed(Doc, true);
        };
        Arrangement.SetSnap(_settings.Timeline.Snap, () => SongClock.CurrentSec);
        Arrangement.SnapChanged += (_, _) => { SaveSettings(); StatusText.Text = _settings.Timeline.Snap.Enabled ? $"Snapping on (grid {_settings.Timeline.Snap.Grid})" : "Snapping off"; };
        Arrangement.SnapSettingsRequested += (_, _) =>
            SnapSettingsDialog.Show(this, _settings.Timeline.Snap, () => { Arrangement.UpdateSnapButton(); SaveSettings(); });
        Arrangement.LaneClicked += (index, lane, sec) =>
        {
            if (index >= 0 && index < _project.Tracks.Count) _clips.LaneCursor = new LaneCursor(_project.Tracks[index], lane, sec);
        };
        Arrangement.ClipEditStarting += (_, _) => { CheckpointUndo(); _clips.BeginClipGesture(Doc); };
        Arrangement.ClipEdited += (_, _) => _clips.FinishClipGesture(Doc);
        Arrangement.ClipEdgeMoved += (_, _) => { if (SongExtent.EnsureCoversClips(_project).BarsAdded > 0) RefreshAfterEdit(EditRefresh.Score | EditRefresh.TimelineGeometry); };
        Arrangement.ClipPropertiesRequested += (_, clip) => _clips.EditProperties(Doc, clip);
        Arrangement.ClipContextRequested += ShowClipMenu;
        Arrangement.MediaDropped += plan => _clips.ApplyMediaDrop(Doc, plan);
        Arrangement.ClipMoveRequested += (clip, from, plan, copy) => _clips.MoveTo(Doc, clip, from, plan, copy);
        Arrangement.MidiClipToNotation += (clip, from, to) => _clips.MidiClipToNotation(Doc, clip, from, to);
        Subscribe(h => _engine.StatusChanged += h, h => _engine.StatusChanged -= h, (Action)(() => { if (!_isClosed) UpdateAudioDeviceStatus(); }));
        // Clicking the score leaves the clip context (keys go back to note editing).
        Editor.PreviewMouseLeftButtonDown += (_, _) => { if (Arrangement.SelectedClip is not null) Arrangement.SelectedClip = null; _clips.LaneCursor = null; };
    }

    /// <summary>Status bar: the running audio device, e.g. "ASIO: Audient USB (6 ms · 48 kHz · 128)".</summary>
    private void UpdateAudioDeviceStatus()
    {
        var engine = _engine;
        var output = engine.IsRunning ? engine.Output : null;
        var configured = _settings.Plugins;
        var asioChosen = configured.Driver == Audio.Contracts.AudioDriverNames.Asio;
        // The engine only runs while a track needs it; a chosen ASIO driver is still shown.
        var show = output is not null || asioChosen;
        AudioDeviceButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        OutputStatusText.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (!show) return;
        if (output is { } o)
        {
            AudioDeviceText.Text = $"{(o.Device.StartsWith(o.Driver, StringComparison.OrdinalIgnoreCase) ? o.Device : $"{o.Driver}: {o.Device}")} ({o.LatencyMs} ms · {o.Rate / 1000.0:0.#} kHz · {o.Buffer} smp)";
            AudioDeviceButton.ToolTip = "Audio device in use · click for Settings > Audio & Plug-ins (driver, device, sample rate, ASIO channels)";
        }
        else
        {
            var name = configured.Device.Length > 0 ? configured.Device : Audio.AudioDevices.Names(configured.Driver).FirstOrDefault() ?? "no driver found";
            AudioDeviceText.Text = $"ASIO: {name} (idle · {configured.SampleRate / 1000.0:0.#} kHz)";
            AudioDeviceButton.ToolTip = "ASIO is selected. It starts with the first track that uses plug-ins, audio clips or recording. Click for Settings > Audio & Plug-ins.";
        }
    }

    private void AudioDevice_Click(object sender, RoutedEventArgs e) => OpenSettingsCategory(SettingsCatalog.AudioVst);

    /// <summary>Clip keys are active while a clip is selected, or a lane was clicked and the timeline has focus.</summary>
    private bool ClipContextActive => Arrangement.SelectedClip is not null || (_clips.LaneCursor is not null && Arrangement.TimelineHasFocus);

    /// <summary>Runs a Range.* command on the bars selected in the shared selection; false when no bars are selected.</summary>
    // No bar range: the timeline's current bar is the range, so Delete on one bar asks the same question as on a selection.
    private bool RunRangeHotkey(string id) =>
        _selection.BarRange is { } range ? _sections.Range.RunCommand(Doc, id, range.Start, range.End, _selection.Scope == SelectionScope.AllTracks)
            : Editor.SelectedMeasure >= 0 && _sections.Range.RunCommand(Doc, id, Editor.SelectedMeasure, Editor.SelectedMeasure);

    private void ShowClipMenu(int trackIndex, AudioClip? clip, double sec)
    {
        if (trackIndex < 0 || trackIndex >= _project.Tracks.Count) return;
        var doc = Doc;
        var track = _project.Tracks[trackIndex];
        var lane = _clips.LaneCursor is { } cursor && ReferenceEquals(cursor.Track, track) ? cursor.Lane : clip?.Lane ?? 0;
        var state = new ClipMenuState(clip is not null, clip?.IsMidi ?? false, ClipClipboard.HasClip, clip?.Muted ?? false);
        var menu = NewTimelineMenu(clip is null ? "Empty lane options" : "Clip options", TimelineMenus.Clip(state, MenuKey), command =>
        {
            switch (command)
            {
                case TimelineCommand.ClipCopy when clip is not null: ClipClipboard.Copy(clip); break;
                case TimelineCommand.ClipCut when clip is not null:
                    ClipClipboard.Copy(clip);
                    _clips.Remove(doc, track, clip, null, clearSelection: false);
                    break;
                case TimelineCommand.ClipPaste: _clips.Paste(doc, track, lane, sec); break;
                case TimelineCommand.ClipDuplicate when clip is not null: _clips.Duplicate(doc, track, clip); break;
                case TimelineCommand.ClipSplit when clip is not null: _clips.SplitAt(doc, track, clip, sec); break;
                case TimelineCommand.ClipGlue when clip is not null: _clips.Glue(doc, track, clip); break;
                case TimelineCommand.ClipFadeReset when clip is not null: _clips.ResetFades(doc, clip); break;
                case TimelineCommand.ClipDelete when clip is not null: _clips.Remove(doc, track, clip, null, clearSelection: true); break;
                case TimelineCommand.ClipMute when clip is not null: _clips.Edit(doc, () => clip.Muted = !clip.Muted); break;
                case TimelineCommand.ClipProperties when clip is not null: _clips.EditProperties(doc, clip); break;
                case TimelineCommand.ClipWriteNotation when clip is not null: _clips.WriteNotation(doc, track, clip); break;
                case TimelineCommand.ClipAddAudioFile:
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "Audio files|" + string.Join(";", WaveformCache.Extensions.Select(x => "*" + x)) + "|All files|*.*",
                        Multiselect = true
                    };
                    if (dialog.ShowDialog(this) == true) _clips.AddAudioFiles(doc, trackIndex, sec, dialog.FileNames);
                    break;
            }
        });
        SpecMenus.Open(menu, Arrangement, null, fromKeyboard: false);
    }

    private sealed class ClipHost : IClipHost
    {
        private readonly MainWindow _window;

        public ClipHost(MainWindow window) => _window = window;

        public AppSettings Settings => _window._settings;
        public bool IsShown(DocumentSession document) => ReferenceEquals(document, _window.Doc);
        public AudioClip? SelectedClip { get => _window.Arrangement.SelectedClip; set => _window.Arrangement.SelectedClip = value; }
        public bool CancelClipDrag() => _window.Arrangement.CancelClipDrag();
        public void SetStatus(string text) => _window.StatusText.Text = text;
        public void CheckpointUndo() => _window.CheckpointUndo();
        public void SyncAudioEngine() => _window.SyncAudioEngine();
        public void RefreshTracks() => _window.RefreshTracks();
        public void RefreshArrangement() => _window.RefreshArrangement(keepRows: true);
        public void RefreshAfterSongGrew() => _window.RefreshAfterEdit(EditRefresh.Score | EditRefresh.TimelineGeometry);
        public void InvalidateScoreLayout() => _window.Editor.InvalidateScoreLayout();
        public void UpdateTitle() => _window.UpdateTitle();
        public void ShowNewTrack(int index)
        {
            _window.TrackMixerGrid.SelectedIndex = index;
            _window.RefreshInstrument();
            _window.ScheduleFitTimelineToTracks();
        }
        public bool ShowClipProperties(AudioClip clip) => ClipPropertiesDialog.Show(_window, clip);
        public DropItem MeasureDroppedFile(string file, DropItemKind kind, bool transient, MediaContext media) => MediaDropSession.Measure(file, kind, transient, media);
        public DropItem ReadDroppedMidi(string file, bool transient) => MediaDropSession.ReadMidi(file, transient);
    }
}
