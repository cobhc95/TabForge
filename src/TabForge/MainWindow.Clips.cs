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
        Arrangement.MediaDropped += ApplyMediaDrop;
        Arrangement.ClipMoveRequested += MoveClipTo;
        Arrangement.MidiClipToNotation += (clip, from, to) =>
        {
            if (from < 0 || to < 0 || from >= _project.Tracks.Count || to >= _project.Tracks.Count) return;
            var written = MidiClipToTab.Write(_project, _project.Tracks[to], clip, sec => SongClock.BarAt(_project, sec));
            if (written == 0) { StatusText.Text = "No notes of that MIDI clip fit this track's strings or bars"; RefreshArrangement(); return; }
            _project.Tracks[from].AudioClips.Remove(clip);
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
                if (Arrangement.CancelClipDrag()) { StatusText.Text = "Clip move cancelled"; return true; }
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
            case "Clip.Delete": EditClip(() => { track.AudioClips.Remove(clip); Arrangement.SelectedClip = null; }, $"Deleted {clip.Name}"); return true;
            case "Clip.NudgeLeft": EditClip(() => clip.StartSec = Math.Max(0, clip.StartSec - BeatSecAt(clip.StartSec))); return true;
            case "Clip.NudgeRight": EditClip(() => clip.StartSec += BeatSecAt(clip.StartSec)); return true;
            case "Clip.NudgeLeftFine": EditClip(() => clip.StartSec = Math.Max(0, clip.StartSec - 0.01)); return true;
            case "Clip.NudgeRightFine": EditClip(() => clip.StartSec += 0.01); return true;
            case "Clip.LaneUp": if (clip.Lane == 0) return true; EditClip(() => clip.Lane--); return true;
            case "Clip.LaneDown": EditClip(() => { clip.Lane++; ClipLanes.Ensure(track, clip.Lane + 1); }); return true;
            case "Clip.Copy": _clipClipboard = clip.Clone(); StatusText.Text = $"Copied {clip.Name}"; return true;
            case "Clip.Cut": _clipClipboard = clip.Clone(); EditClip(() => { track.AudioClips.Remove(clip); Arrangement.SelectedClip = null; }, $"Cut {clip.Name}"); return true;
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

    /// <summary>
    /// A clip dragged to a lane (this track or another, a new lane, or below the last track for a new track): moved, or copied with
    /// Ctrl, with the recording-take rules, as one undo step. Empty lanes close up afterwards when that setting is on.
    /// </summary>
    private void MoveClipTo(AudioClip clip, int fromIndex, MediaDropPlan plan, bool copy)
    {
        if (!plan.Valid || fromIndex < 0 || fromIndex >= _project.Tracks.Count) return;
        var source = _project.Tracks[fromIndex];
        if (!source.AudioClips.Contains(clip)) return;
        if (!plan.NewTrack && (plan.TrackIndex < 0 || plan.TrackIndex >= _project.Tracks.Count)) return;
        CaptureUndo();
        TrackModel target;
        if (plan.NewTrack)
        {
            target = _trackController.CreateTrack(_project, plan.NewTrackKind);
            if (clip.Name.Length > 0) target.Name = clip.Name;
            Plugins.AutoChains.Apply(_settings.Plugins, target);
            _project.Tracks.Add(target);
        }
        else target = _project.Tracks[plan.TrackIndex];
        var placed = MediaDrop.ApplyMove(source, target, clip, plan.Lane, plan.StartSec, copy, _settings.Timeline.AutoRemoveEmptyLanes);
        Arrangement.SelectedClip = placed;
        if (plan.NewTrack) _midi.Rebuild(_project);
        ClipsChanged(true);
        if (plan.NewTrack)
        {
            TrackMixerGrid.SelectedIndex = _project.Tracks.Count - 1;
            RefreshInstrument();
            ScheduleFitTimelineToTracks();
        }
        StatusText.Text = $"{(copy ? "Copied" : "Moved")} {clip.Name} to {(plan.NewTrack ? $"a new track ({target.Name})" : target.Name)}, lane {placed.Lane + 1}";
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
        if (_settings.Timeline.AutoRemoveEmptyLanes)
            foreach (var track in _project.Tracks) ClipLanes.Compact(track);   // empty lanes close up (armed tracks are skipped inside)
        WaveformCache.CancelUnused(_project.Tracks.SelectMany(t => t.AudioClips).Where(c => !c.IsMidi).Select(c => c.File));   // a removed clip stops being read
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
                    EditClip(() => { track.AudioClips.Remove(clip); });
                    break;
                case TimelineCommand.ClipPaste: PasteClip(track, lane, sec); break;
                case TimelineCommand.ClipDuplicate when clip is not null: DuplicateClip(track, clip); break;
                case TimelineCommand.ClipDelete when clip is not null:
                    EditClip(() => { track.AudioClips.Remove(clip); Arrangement.SelectedClip = null; });
                    break;
                case TimelineCommand.ClipMute when clip is not null: EditClip(() => clip.Muted = !clip.Muted); break;
                case TimelineCommand.ClipProperties when clip is not null: EditClipProperties(clip); break;
                case TimelineCommand.ClipWriteNotation when clip is not null:
                    CaptureUndo();
                    var written = MidiClipToTab.Write(_project, track, clip, s => SongClock.BarAt(_project, s));
                    if (written == 0) { StatusText.Text = "No notes of that MIDI clip fit this track's strings or bars"; return; }
                    track.AudioClips.Remove(clip);
                   
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

    /// <summary>Audio files chosen with "Add audio file…" on a lane: placed exactly like files dropped at that spot.</summary>
    private void AddAudioFiles(int trackIndex, double sec, string[] files)
    {
        if (trackIndex < 0 || trackIndex >= _project.Tracks.Count) return;
        var items = files.Select(f => MediaDropSession.Measure(f, DropItemKind.Audio, MediaDrop.IsTransient(f))).ToList();
        var lane = _laneCursor is { } cursor && ReferenceEquals(cursor.Track, _project.Tracks[trackIndex]) ? cursor.Lane : 0;
        ApplyMediaDrop(MediaDrop.Plan(_project, items, trackIndex, lane, sec, SongQuarterMap.For(_project)));
    }

    /// <summary>
    /// Audio and MIDI files dropped on the timeline (or chosen from the lane menu), as one undo step: temp and virtual files are
    /// copied into the song's media folder first (plug-ins delete their drag files), files on a network drive are measured now,
    /// a drop below the last track adds a track of a fitting kind, and the clips go on the planned lane with the take rules.
    /// </summary>
    private void ApplyMediaDrop(MediaDropPlan plan)
    {
        if (!plan.Valid) { StatusText.Text = plan.Problem ?? "Nothing to add there"; return; }
        if (!plan.NewTrack && (plan.TrackIndex < 0 || plan.TrackIndex >= _project.Tracks.Count)) return;
        var mediaFolder = MediaFolders.ForSong(_currentPath);
        var items = new List<DropItem>();
        var problems = new List<string>();
        foreach (var planned in plan.Clips)
        {
            var item = planned.Item;
            if (item.Deferred) { problems.Add($"{item.Name} could not be read from the drag"); continue; }
            if (item.IsMidi && item.Midi is null)
            {
                // A MIDI file on a network or removable drive is read only now that the user dropped it.
                item = MediaDropSession.ReadMidi(item.Path, item.Transient);
                if (item.Problem is { } why) { problems.Add(why); continue; }
            }
            try
            {
                if (!item.IsMidi && item.Transient)
                    item = new DropItem { Path = MediaDrop.KeepCopy(item.Path, mediaFolder), Name = item.Name, Kind = item.Kind, Seconds = item.Seconds };
                if (!item.IsMidi && item.Seconds <= 0)
                {
                    // The user chose this file: a network or removable folder is allowed (and remembered for this song below).
                    item.Seconds = WaveformCache.LengthOf(item.Path, userPicked: true);
                    if (item.Seconds <= 0) { problems.Add($"{item.Name} could not be read as audio"); continue; }
                }
                items.Add(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                problems.Add($"{item.Name} could not be kept: {ex.Message}");
            }
        }
        if (items.Count == 0) { StatusText.Text = problems.FirstOrDefault() ?? "Those files could not be added"; return; }

        var time = SongQuarterMap.For(_project);
        CaptureUndo();
        int trackIndex;
        if (plan.NewTrack)
        {
            var created = _trackController.CreateTrack(_project, plan.NewTrackKind);
            created.Name = items[0].Name.Length > 0 ? items[0].Name : created.Name;
            Plugins.AutoChains.Apply(_settings.Plugins, created);
            _project.Tracks.Add(created);
            trackIndex = _project.Tracks.Count - 1;
        }
        else trackIndex = plan.TrackIndex;
        var track = _project.Tracks[trackIndex];
        // Planned again with the final lengths: a file measured only now can change the span, and so the free lane.
        var final = MediaDrop.Plan(_project, items, trackIndex, plan.Lane, plan.StartSec, time);
        var drums = track.Kind == TrackKind.Drums || track.MidiChannel == 9;
        var clips = new List<AudioClip>();
        foreach (var planned in final.Clips)
        {
            var item = planned.Item;
            if (item.Midi is { } midi)
            {
                var (notes, length) = MidiFileImport.ToClipNotes(midi, planned.StartSec, time, drums);
                clips.Add(new AudioClip { Name = item.Name, StartSec = planned.StartSec, SourceLengthSec = length, FileLengthSec = length, Notes = notes });
            }
            else clips.Add(new AudioClip { File = item.Path, Name = item.Name, StartSec = planned.StartSec, SourceLengthSec = item.Seconds, FileLengthSec = item.Seconds });
        }
        foreach (var clip in clips.Where(c => !c.IsMidi))
            if (MediaPathPolicy.Classify(clip.File, MediaAccess.FolderOf(_currentPath)) is { Remote: true } picked) MediaAccess.Approve(picked, _currentPath);
        MediaDrop.AddClips(track, clips, final.Lane);
        Arrangement.SelectedClip = clips[^1];
        if (plan.NewTrack) _midi.Rebuild(_project);
        ClipsChanged(true);
        if (plan.NewTrack)
        {
            TrackMixerGrid.SelectedIndex = trackIndex;
            RefreshInstrument();
            ScheduleFitTimelineToTracks();
        }
        var what = clips.Count == 1 ? clips[0].Name : $"{clips.Count} files";
        var where = plan.NewTrack ? $"a new track ({track.Name})" : final.NewLane ? $"{track.Name}, new lane {final.Lane + 1}" : track.Name;
        StatusText.Text = $"Added {what} to {where}" + (problems.Count > 0 ? $" ({problems.Count} skipped: {problems[0]})" : "");
    }
}
