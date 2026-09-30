using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow: the clip lanes on the timeline: arm / input choice, lane play buttons, clip moves and trims, the
// clip commands (keyboard and menu: delete, nudge, lane up/down, copy, cut, paste, duplicate, mute, properties),
// moving clips between tracks, MIDI clips into notation, and audio files dropped on a track.
public partial class MainWindow
{
    private static AudioClip? _clipClipboard;
    /// <summary>Last clicked lane position (the edit cursor for clips): where Paste goes.</summary>
    private (TrackModel Track, int Lane, double Sec)? _laneCursor;

    private void HookAudioLanes()
    {
        Arrangement.SetSongTime(bar => SongClock.BarStartSec(_project, bar), sec => SongClock.BarAt(_project, sec));
        Arrangement.ArmRequested += (_, index) => { if (index >= 0 && index < _project.Tracks.Count) ToggleArm(_project.Tracks[index]); };
        Arrangement.AudioInputChosen += (index, input) =>
        {
            if (index < 0 || index >= _project.Tracks.Count) return;
            CaptureUndo();
            _project.Tracks[index].AudioInput = input;
            _project.IsDirty = true;
            ArmChanged();
        };
        Arrangement.MonitorToggled += (index, on) =>
        {
            if (index < 0 || index >= _project.Tracks.Count) return;
            CaptureUndo();
            _project.Tracks[index].MonitorInput = on;
            _project.IsDirty = true;
            ArmChanged();
            StatusText.Text = on ? $"{_project.Tracks[index].Name}: monitoring on (you hear the input live)" : $"{_project.Tracks[index].Name}: monitoring off (recorded and metered, not played)";
        };
        Arrangement.ClipLaneSelected += (index, lane, midi, add) =>
        {
            if (index < 0 || index >= _project.Tracks.Count) return;
            var track = _project.Tracks[index];
            if (!ClipLanes.SelectTake(track, lane, midi, add, apply: false)) return;   // already the playing take: nothing to do
            CaptureUndo();
            ClipLanes.SelectTake(track, lane, midi, add);
            ClipsChanged(true);
        };
        Arrangement.SetSnap(_settings.Timeline.Snap, () => SongClock.CurrentSec);
        Arrangement.SnapChanged += (_, _) => { SaveSettings(); StatusText.Text = _settings.Timeline.Snap.Enabled ? $"Snapping on (grid {_settings.Timeline.Snap.Grid})" : "Snapping off"; };
        Arrangement.SnapSettingsRequested += (_, _) =>
            SnapSettingsDialog.Show(this, _settings.Timeline.Snap, () => { Arrangement.UpdateSnapButton(); SaveSettings(); });
        Arrangement.LaneClicked += (index, lane, sec) =>
        {
            if (index >= 0 && index < _project.Tracks.Count) _laneCursor = (_project.Tracks[index], lane, sec);
        };
        Arrangement.ClipEditStarting += (_, _) => CaptureUndo();
        Arrangement.ClipEdited += (_, _) => ClipsChanged(true);
        Arrangement.ClipPropertiesRequested += (_, clip) => EditClipProperties(clip);
        Arrangement.ClipContextRequested += ShowClipMenu;
        Arrangement.AudioFilesDropped += AddAudioFiles;
        Arrangement.ClipMovedToTrack += (clip, from, to, lane) =>
        {
            if (from < 0 || to < 0 || from >= _project.Tracks.Count || to >= _project.Tracks.Count) return;
            _project.Tracks[from].AudioClips.Remove(clip);
            clip.Lane = ClipLanes.FreeLane(_project.Tracks[to], clip.StartSec, clip.EndSec, startLane: lane) == lane ? lane : ClipLanes.FreeLane(_project.Tracks[to], clip.StartSec, clip.EndSec);
            _project.Tracks[to].AudioClips.Add(clip);
            ClipLanes.Ensure(_project.Tracks[to], clip.Lane + 1);
            ClipLanes.Trim(_project.Tracks[from]);
            ClipsChanged(true);
            StatusText.Text = $"Moved {clip.Name} to {_project.Tracks[to].Name}";
        };
        Arrangement.MidiClipToNotation += (clip, from, to) =>
        {
            if (from < 0 || to < 0 || from >= _project.Tracks.Count || to >= _project.Tracks.Count) return;
            var written = MidiClipToTab.Write(_project, _project.Tracks[to], clip, sec => SongClock.BarAt(_project, sec));
            if (written == 0) { StatusText.Text = "No notes of that MIDI clip fit this track's strings or bars"; RefreshArrangement(); return; }
            _project.Tracks[from].AudioClips.Remove(clip);
            ClipLanes.Trim(_project.Tracks[from]);
            Arrangement.SelectedClip = null;
            ClipsChanged(true);
            Editor.InvalidateScoreLayout();
            StatusText.Text = $"Wrote {written} notes from {clip.Name} into {_project.Tracks[to].Name} (undo with Ctrl+Z)";
        };
        Audio.AudioEngineClient.Instance.StatusChanged += UpdateAudioDeviceStatus;
        // Clicking the score leaves the clip context (keys go back to note editing).
        Editor.PreviewMouseLeftButtonDown += (_, _) => { if (Arrangement.SelectedClip is not null) Arrangement.SelectedClip = null; _laneCursor = null; };
    }

    /// <summary>Status bar: the running audio device, e.g. "ASIO: Audient USB (6 ms · 48 kHz · 128)".</summary>
    private void UpdateAudioDeviceStatus()
    {
        var engine = Audio.AudioEngineClient.Instance;
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
            AudioDeviceButton.ToolTip = "Audio device in use · click for Settings > Audio & VST (driver, device, sample rate, ASIO channels)";
        }
        else
        {
            var name = configured.Device.Length > 0 ? configured.Device : Audio.AudioDevices.Names(configured.Driver).FirstOrDefault() ?? "no driver found";
            AudioDeviceText.Text = $"ASIO: {name} (idle · {configured.SampleRate / 1000.0:0.#} kHz)";
            AudioDeviceButton.ToolTip = "ASIO is selected. It starts with the first track that uses plug-ins, audio clips or recording. Click for Settings > Audio & VST.";
        }
    }

    private void AudioDevice_Click(object sender, RoutedEventArgs e) => OpenSettingsCategory(SettingsCatalog.AudioVst);

    /// <summary>Clip keys are active while a clip is selected, or a lane was clicked and the timeline has focus.</summary>
    private bool ClipContextActive => Arrangement.SelectedClip is not null || (_laneCursor is not null && Arrangement.TimelineHasFocus);

    private TrackModel? TrackOfClip(AudioClip clip) => _project.Tracks.FirstOrDefault(t => t.AudioClips.Contains(clip));

    private bool RunClipHotkey(string id)
    {
        var clip = Arrangement.SelectedClip;
        var track = clip is null ? null : TrackOfClip(clip);
        if (clip is not null && track is null) { Arrangement.SelectedClip = null; return false; }
        switch (id)
        {
            case "Clip.Deselect":
                if (clip is null && _laneCursor is null) return false;
                Arrangement.SelectedClip = null;
                _laneCursor = null;
                StatusText.Text = "Clip deselected";
                return true;
            case "Clip.Paste":
                if (_clipClipboard is null) return false;
                var target = _laneCursor ?? (track is not null ? (track, clip!.Lane, clip.EndSec) : null);
                if (target is not { } at) return false;
                PasteClip(at.Track, at.Lane, at.Sec);
                return true;
        }
        if (clip is null || track is null) return false;
        switch (id)
        {
            case "Clip.Delete": EditClip(() => { track.AudioClips.Remove(clip); ClipLanes.Trim(track); Arrangement.SelectedClip = null; }, $"Deleted {clip.Name}"); return true;
            case "Clip.NudgeLeft": EditClip(() => clip.StartSec = Math.Max(0, clip.StartSec - BeatSecAt(clip.StartSec))); return true;
            case "Clip.NudgeRight": EditClip(() => clip.StartSec += BeatSecAt(clip.StartSec)); return true;
            case "Clip.NudgeLeftFine": EditClip(() => clip.StartSec = Math.Max(0, clip.StartSec - 0.01)); return true;
            case "Clip.NudgeRightFine": EditClip(() => clip.StartSec += 0.01); return true;
            case "Clip.LaneUp": if (clip.Lane == 0) return true; EditClip(() => { clip.Lane--; ClipLanes.Trim(track); }); return true;
            case "Clip.LaneDown": EditClip(() => { clip.Lane++; ClipLanes.Ensure(track, clip.Lane + 1); }); return true;
            case "Clip.Copy": _clipClipboard = clip.Clone(); StatusText.Text = $"Copied {clip.Name}"; return true;
            case "Clip.Cut": _clipClipboard = clip.Clone(); EditClip(() => { track.AudioClips.Remove(clip); ClipLanes.Trim(track); Arrangement.SelectedClip = null; }, $"Cut {clip.Name}"); return true;
            case "Clip.Duplicate": DuplicateClip(track, clip); return true;
            case "Clip.Mute": EditClip(() => clip.Muted = !clip.Muted, clip.Muted ? $"Unmuted {clip.Name}" : $"Muted {clip.Name}"); return true;
            case "Clip.Properties": EditClipProperties(clip); return true;
        }
        return false;
    }

    /// <summary>One beat at a song time (the bar's length / its time signature).</summary>
    private double BeatSecAt(double sec)
    {
        var bar = SongClock.BarAt(_project, sec).Bar;
        var length = SongClock.BarEndSec(_project, bar) - SongClock.BarStartSec(_project, bar);
        var beats = MusicTime.BarOf(_project, bar)?.TimeSigNum ?? _project.TimeSignatureNumerator;
        return length > 0 ? length / Math.Max(1, beats) : 60.0 / Math.Max(20, _project.Tempo);
    }

    private void EditClip(Action change, string? status = null)
    {
        CaptureUndo();
        change();
        ClipsChanged(true);
        if (status is not null) StatusText.Text = status;
    }

    /// <summary>After any clip change: mark dirty, tell the engine (and the MIDI timeline), redraw.</summary>
    private void ClipsChanged(bool refreshRows = false)
    {
        _project.IsDirty = true;
        SyncAudioEngine();
        if (_project.Tracks.Any(t => t.AudioClips.Any(c => c.IsMidi)) || _midiClipsPlayed) _midi.Rebuild(_project);
        _midiClipsPlayed = _project.Tracks.Any(t => t.AudioClips.Any(c => c.IsMidi));
        if (refreshRows) RefreshTracks();
        RefreshArrangement();
        UpdateTitle();
    }

    private bool _midiClipsPlayed;

    private void EditClipProperties(AudioClip clip)
    {
        var before = clip.Clone();
        CaptureUndo();
        if (!ClipPropertiesDialog.Show(this, clip)) return;
        if (clip.GainDb == before.GainDb && clip.Pitch == before.Pitch && clip.Speed == before.Speed && clip.Muted == before.Muted && clip.Name == before.Name) return;
        ClipsChanged();
    }

    private void DuplicateClip(TrackModel track, AudioClip clip) => EditClip(() =>
    {
        var copy = clip.Clone();
        copy.StartSec = clip.EndSec;
        copy.Lane = clip.Lane;
        if (track.AudioClips.Any(c => c.Lane == copy.Lane && c.Overlaps(copy.StartSec, copy.EndSec))) copy.Lane = ClipLanes.FreeLane(track, copy.StartSec, copy.EndSec);
        ClipLanes.Ensure(track, copy.Lane + 1);
        track.AudioClips.Add(copy);
        Arrangement.SelectedClip = copy;
    }, $"Duplicated {clip.Name}");

    private void PasteClip(TrackModel track, int lane, double sec) => EditClip(() =>
    {
        var pasted = _clipClipboard!.Clone();
        pasted.StartSec = Math.Max(0, sec);
        pasted.Lane = track.AudioClips.Any(c => c.Lane == lane && c.Overlaps(pasted.StartSec, pasted.EndSec))
            ? ClipLanes.FreeLane(track, pasted.StartSec, pasted.EndSec) : lane;
        ClipLanes.Ensure(track, pasted.Lane + 1);
        track.AudioClips.Add(pasted);
        Arrangement.SelectedClip = pasted;
        _laneCursor = (track, pasted.Lane, pasted.EndSec);
    }, $"Pasted {_clipClipboard!.Name}");

    private void ShowClipMenu(int trackIndex, AudioClip? clip, double sec)
    {
        if (trackIndex < 0 || trackIndex >= _project.Tracks.Count) return;
        var track = _project.Tracks[trackIndex];
        var lane = _laneCursor is { } cursor && ReferenceEquals(cursor.Track, track) ? cursor.Lane : clip?.Lane ?? 0;
        var state = new ClipMenuState(clip is not null, clip?.IsMidi ?? false, _clipClipboard is not null, clip?.Muted ?? false);
        var menu = NewTimelineMenu(clip is null ? "Empty lane options" : "Clip options", TimelineMenus.Clip(state, MenuKey), command =>
        {
            switch (command)
            {
                case TimelineCommand.ClipCopy when clip is not null: _clipClipboard = clip.Clone(); break;
                case TimelineCommand.ClipCut when clip is not null:
                    _clipClipboard = clip.Clone();
                    EditClip(() => { track.AudioClips.Remove(clip); ClipLanes.Trim(track); });
                    break;
                case TimelineCommand.ClipPaste: PasteClip(track, lane, sec); break;
                case TimelineCommand.ClipDuplicate when clip is not null: DuplicateClip(track, clip); break;
                case TimelineCommand.ClipDelete when clip is not null:
                    EditClip(() => { track.AudioClips.Remove(clip); ClipLanes.Trim(track); Arrangement.SelectedClip = null; });
                    break;
                case TimelineCommand.ClipMute when clip is not null: EditClip(() => clip.Muted = !clip.Muted); break;
                case TimelineCommand.ClipProperties when clip is not null: EditClipProperties(clip); break;
                case TimelineCommand.ClipWriteNotation when clip is not null:
                    CaptureUndo();
                    var written = MidiClipToTab.Write(_project, track, clip, s => SongClock.BarAt(_project, s));
                    if (written == 0) { StatusText.Text = "No notes of that MIDI clip fit this track's strings or bars"; return; }
                    track.AudioClips.Remove(clip);
                    ClipLanes.Trim(track);
                    ClipsChanged(true);
                    Editor.InvalidateScoreLayout();
                    StatusText.Text = $"Wrote {written} notes into {track.Name}";
                    break;
                case TimelineCommand.ClipAddAudioFile:
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Filter = "Audio files|" + string.Join(";", WaveformCache.Extensions.Select(x => "*" + x)) + "|All files|*.*",
                        Multiselect = true
                    };
                    if (dialog.ShowDialog(this) == true) AddAudioFiles(trackIndex, sec, dialog.FileNames);
                    break;
            }
        });
        menu.IsOpen = true;
    }

    /// <summary>Audio files dropped (or chosen) on a track: one clip each, placed one after another on a free lane.</summary>
    private void AddAudioFiles(int trackIndex, double sec, string[] files)
    {
        if (trackIndex < 0 || trackIndex >= _project.Tracks.Count) return;
        var track = _project.Tracks[trackIndex];
        var clips = new List<AudioClip>();
        var at = Math.Max(0, sec);
        foreach (var file in files)
        {
            var length = WaveformCache.LengthOf(file);
            if (length <= 0) continue;
            clips.Add(new AudioClip
            {
                File = file, Name = Path.GetFileNameWithoutExtension(file), StartSec = at,
                SourceLengthSec = length, FileLengthSec = length,
            });
            at += length;
        }
        if (clips.Count == 0)
        {
            StatusText.Text = "Those files could not be read as audio";
            return;
        }
        CaptureUndo();
        foreach (var clip in clips)
        {
            clip.Lane = ClipLanes.FreeLane(track, clip.StartSec, clip.EndSec);
            ClipLanes.Ensure(track, clip.Lane + 1);
            track.AudioClips.Add(clip);
        }
        Arrangement.SelectedClip = clips[^1];
        ClipsChanged(true);
        StatusText.Text = clips.Count == 1 ? $"Added {clips[0].Name} to {track.Name}" : $"Added {clips.Count} audio files to {track.Name}";
    }
}
