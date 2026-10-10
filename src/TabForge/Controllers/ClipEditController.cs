using System.IO;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Controllers;

/// <summary>The last clicked spot on a clip lane (the edit cursor for clips): where Paste goes.</summary>
internal readonly record struct LaneCursor(TrackModel Track, int Lane, double Sec);

/// <summary>What the clip commands need from the window that shows the song they edit.</summary>
internal interface IClipHost
{
    AppSettings Settings { get; }
    /// <summary>True while <paramref name="document"/> is the song the window shows (false for a background or a closed song).</summary>
    bool IsShown(DocumentSession document);
    /// <summary>The clip selected on the timeline, or null.</summary>
    AudioClip? SelectedClip { get; set; }
    /// <summary>Cancels a clip drag in progress; false when there was none.</summary>
    bool CancelClipDrag();
    void SetStatus(string text);
    /// <summary>Takes the undo state of the shown song before an edit that does not go through <see cref="DocumentEdits"/>.</summary>
    void CheckpointUndo();
    void SyncAudioEngine();
    void RefreshTracks();
    void RefreshArrangement();
    /// <summary>The song grew by whole bars: the score and the timeline geometry follow.</summary>
    void RefreshAfterSongGrew();
    void InvalidateScoreLayout();
    void UpdateTitle();
    /// <summary>A track was added below the others: select it, show its instrument and fit the timeline to the tracks.</summary>
    void ShowNewTrack(int index);
    /// <summary>The clip properties window; true when the user accepted it.</summary>
    bool ShowClipProperties(AudioClip clip);
    DropItem MeasureDroppedFile(string file, DropItemKind kind, bool transient, MediaContext media);
    DropItem ReadDroppedMidi(string file, bool transient);
}

// Owns: the commands of the timeline's clip lanes: delete, nudge, lane moves, copy, cut, paste, duplicate, split, glue, fades, mute, properties,
//     moves between tracks, MIDI clip to notation, dropped files.
// Does not own: clip drawing and hit testing (arrangement views) and the clipboard format (ClipboardService).
// Tests: TestClipAndSectionEdits, TestDocumentOperations.
/// <summary>
/// The commands on the timeline's clip lanes: delete, nudge, lane moves, copy, cut, paste, duplicate, mute, properties, moving clips to other tracks, writing a
/// MIDI clip into notation, and audio or MIDI files dropped on a track. Every command is given the song it edits and does nothing when the window no longer
/// shows it; the edit itself is one <see cref="DocumentEdits"/> step. The clip clipboard is <see cref="ClipClipboard"/>, shared by every window.
/// </summary>
internal sealed class ClipEditController
{
    private readonly IClipHost _host;
    private readonly TrackController _tracks;
    private string _extentNote = "";
    private double _gestureEnd = double.NaN;
    private bool _midiClipsPlayed;

    public ClipEditController(IClipHost host, TrackController tracks)
    {
        _host = host;
        _tracks = tracks;
    }

    /// <summary>The last clicked lane position, or null.</summary>
    public LaneCursor? LaneCursor { get; set; }

    private static TrackModel? TrackOfClip(DocumentSession doc, AudioClip clip) => doc.Project.Tracks.FirstOrDefault(t => t.AudioClips.Contains(clip));

    /// <summary>Runs a clip key command; false when it does not apply (the key then goes on to note editing).</summary>
    public bool RunHotkey(DocumentSession doc, string id)
    {
        if (!_host.IsShown(doc)) return false;
        var clip = _host.SelectedClip;
        var track = clip is null ? null : TrackOfClip(doc, clip);
        if (clip is not null && track is null) { _host.SelectedClip = null; return false; }
        switch (id)
        {
            case "Clip.Deselect":
                if (_host.CancelClipDrag()) { _host.SetStatus("Clip move cancelled"); return true; }
                if (clip is null && LaneCursor is null) return false;
                _host.SelectedClip = null;
                LaneCursor = null;
                _host.SetStatus("Clip deselected");
                return true;
            case "Clip.Paste":
                if (!ClipClipboard.HasClip) return false;
                var target = LaneCursor ?? (track is not null ? new LaneCursor(track, clip!.Lane, clip.EndSec) : null);
                if (target is not { } at) return false;
                Paste(doc, at.Track, at.Lane, at.Sec);
                return true;
        }
        if (clip is null || track is null) return false;
        switch (id)
        {
            case "Clip.Delete": Remove(doc, track, clip, $"Deleted {clip.Name}", clearSelection: true); return true;
            case "Clip.NudgeLeft": Edit(doc, () => clip.StartSec = Math.Max(0, clip.StartSec - BeatSecAt(doc, clip.StartSec))); return true;
            case "Clip.NudgeRight": Edit(doc, () => clip.StartSec += BeatSecAt(doc, clip.StartSec)); return true;
            case "Clip.NudgeLeftFine": Edit(doc, () => clip.StartSec = Math.Max(0, clip.StartSec - 0.01)); return true;
            case "Clip.NudgeRightFine": Edit(doc, () => clip.StartSec += 0.01); return true;
            case "Clip.LaneUp": if (clip.Lane == 0) return true; Edit(doc, () => clip.Lane--); return true;
            case "Clip.LaneDown": Edit(doc, () => { clip.Lane++; ClipLanes.Ensure(track, clip.Lane + 1); }); return true;
            case "Clip.Copy": ClipClipboard.Copy(clip); _host.SetStatus($"Copied {clip.Name}"); return true;
            case "Clip.Cut": ClipClipboard.Copy(clip); Remove(doc, track, clip, $"Cut {clip.Name}", clearSelection: true); return true;
            case "Clip.Duplicate": Duplicate(doc, track, clip); return true;
            case "Clip.Mute": Edit(doc, () => clip.Muted = !clip.Muted, clip.Muted ? $"Unmuted {clip.Name}" : $"Muted {clip.Name}"); return true;
            case "Clip.Properties": EditProperties(doc, clip); return true;
            case "Clip.Split": SplitAtCursor(doc, track, clip); return true;
            case "Clip.Glue": Glue(doc, track, clip); return true;
            case "Clip.FadeReset": ResetFades(doc, clip); return true;
        }
        return false;
    }

    /// <summary>One beat at a song time (the bar's length / its time signature).</summary>
    private static double BeatSecAt(DocumentSession doc, double sec)
    {
        var (project, clock) = (doc.Project, doc.Playback.Clock);
        var bar = clock.BarAt(project, sec).Bar;
        var length = clock.BarEndSec(project, bar) - clock.BarStartSec(project, bar);
        var beats = MusicTime.BarOf(project, bar)?.TimeSigNum ?? project.TimeSignatureNumerator;
        return length > 0 ? length / Math.Max(1, beats) : 60.0 / Math.Max(20, project.Tempo);
    }

    /// <summary>
    /// A clip dragged to a lane (this track or another, a new lane, or below the last track for a new track): moved, or copied with
    /// Ctrl, with the recording-take rules, as one undo step. Empty lanes close up afterwards when that setting is on.
    /// </summary>
    public void MoveTo(DocumentSession doc, AudioClip clip, int fromIndex, MediaDropPlan plan, bool copy)
    {
        var project = doc.Project;
        if (!_host.IsShown(doc) || !plan.Valid || fromIndex < 0 || fromIndex >= project.Tracks.Count) return;
        var source = project.Tracks[fromIndex];
        if (!source.AudioClips.Contains(clip)) return;
        if (!plan.NewTrack && (plan.TrackIndex < 0 || plan.TrackIndex >= project.Tracks.Count)) return;
        var settings = _host.Settings;
        var endBefore = SongExtent.ClipsEnd(project);
        TrackModel target = null!;
        AudioClip placed = null!;
        DocumentEdits.Run(doc, p =>
        {
            if (plan.NewTrack)
            {
                target = _tracks.CreateTrack(p, plan.NewTrackKind);
                if (clip.Name.Length > 0) target.Name = clip.Name;
                AutoChains.Apply(settings.Plugins, target);
                p.Tracks.Add(target);
            }
            else target = p.Tracks[plan.TrackIndex];
            placed = MediaDrop.ApplyMove(source, target, clip, plan.Lane, plan.StartSec, copy, settings.Timeline.AutoRemoveEmptyLanes);
            return true;
        }, invalidatesTimeline: false);
        _host.SelectedClip = placed;
        if (plan.NewTrack && !CanAddAudioTrackWithoutMidiRebuild(project, target, new[] { placed })) doc.Playback.Engine.Rebuild(project);
        Changed(doc, true, SongExtent.ClipsEnd(project) < endBefore - 1e-6);
        if (plan.NewTrack) _host.ShowNewTrack(project.Tracks.Count - 1);
        _host.SetStatus($"{(copy ? "Copied" : "Moved")} {clip.Name} to {(plan.NewTrack ? $"a new track ({target.Name})" : target.Name)}, lane {placed.Lane + 1}" + TakeExtentNote());
    }

    /// <summary>Changes the song's clips as one undo step, then does what every clip change needs (<see cref="Changed"/>). <paramref name="status"/> goes to the status line with the note about bars added.</summary>
    public void Edit(DocumentSession doc, Action change, string? status = null)
    {
        if (!_host.IsShown(doc)) return;
        var endBefore = SongExtent.ClipsEnd(doc.Project);
        DocumentEdits.Run(doc, _ => { change(); return true; }, invalidatesTimeline: false);
        Changed(doc, true, SongExtent.ClipsEnd(doc.Project) < endBefore - 1e-6);
        var note = TakeExtentNote();
        if (status is not null) _host.SetStatus(status + note);
        else if (note.Length > 0) _host.SetStatus(note.Trim(' ', '(', ')'));
    }

    /// <summary>Removes <paramref name="clip"/> from <paramref name="track"/> as one undo step.</summary>
    public void Remove(DocumentSession doc, TrackModel track, AudioClip clip, string? status, bool clearSelection) =>
        Edit(doc, () => { track.AudioClips.Remove(clip); if (clearSelection) _host.SelectedClip = null; }, status);

    /// <summary>A clip trim or fade drag begins: remembers where the clips end, so the release knows whether they shrank.</summary>
    public void BeginClipGesture(DocumentSession doc) => _gestureEnd = SongExtent.ClipsEnd(doc.Project);

    /// <summary>The clip trim or fade drag ended: <see cref="Changed"/>, trimming the empty end bars when the clips now end earlier.</summary>
    public void FinishClipGesture(DocumentSession doc)
    {
        var before = _gestureEnd;
        _gestureEnd = double.NaN;
        Changed(doc, true, SongExtent.ClipsEnd(doc.Project) < before - 1e-6);
    }

    /// <summary>
    /// After any clip change: mark dirty, tell the engine (and the MIDI timeline), redraw. <paramref name="clipsShrank"/> (a clip was deleted,
    /// cropped or moved earlier) also removes the empty bars at the end of the song when that setting is on, in the same undo step.
    /// </summary>
    public void Changed(DocumentSession doc, bool refreshRows = false, bool clipsShrank = false)
    {
        var project = doc.Project;
        project.IsDirty = true;
        using var changedTrace = TabForge.Views.SlowTrace.Measure("clip change total", 4);
        ExtentResult extent; using (TabForge.Views.SlowTrace.Measure("clip extend", 2)) extent = ExtendSongToClips(project);
        var barsRemoved = clipsShrank && _host.Settings.Editing.TrimEmptyBarsAtEnd ? TrailingBars.Trim(project) : 0;
        if (_host.Settings.Timeline.AutoRemoveEmptyLanes)
            foreach (var track in project.Tracks) ClipLanes.Compact(track);   // empty lanes close up (armed tracks are skipped inside)
        using (TabForge.Views.SlowTrace.Measure("clip waveform cancel", 2)) WaveformCache.CancelUnused(project.Tracks.SelectMany(t => t.AudioClips).Where(c => !c.IsMidi).Select(c => c.File), doc.Media);   // a removed clip stops being read
        var hasMidiClips = project.Tracks.Any(t => t.AudioClips.Any(c => c.IsMidi));
        var growthRefreshStarted = false;
        if (extent.BarsAdded > 0 && doc.Playback.Engine.IsPlaying && !hasMidiClips && !_midiClipsPlayed)
        {
            var barCount = project.Tracks.Count == 0 ? 0 : project.Tracks.Max(t => t.Measures.Count);
            var map = doc.Playback.PlaybackBarRemap ?? Enumerable.Range(0, barCount).ToArray();
            growthRefreshStarted = doc.Playback.Engine.RefreshArrangementForAudioGrowth(project, map);
        }
        _host.SyncAudioEngine();
        if (!growthRefreshStarted && (extent.BarsAdded > 0 || barsRemoved > 0 || hasMidiClips || _midiClipsPlayed)) doc.Playback.Engine.Rebuild(project);
        _midiClipsPlayed = hasMidiClips;
        if (refreshRows) _host.RefreshTracks();
        if (extent.BarsAdded > 0 || barsRemoved > 0) _host.RefreshAfterSongGrew();
        _host.RefreshArrangement();
        using (TabForge.Views.SlowTrace.Measure("clip title", 2)) _host.UpdateTitle();
        _extentNote = SongExtent.Describe(extent) + (barsRemoved > 0 ? $" (removed {barsRemoved} empty bar{(barsRemoved == 1 ? "" : "s")} at the end)" : "");
        if (extent.Capped) _host.SetStatus("A clip ends after the last bar" + _extentNote);
    }

    /// <summary>The "(added N bars ...)" note of the last clip change, appended to its status line (cleared when read).</summary>
    private string TakeExtentNote() { var note = _extentNote; _extentNote = ""; return note; }

    /// <summary>
    /// The song always covers every clip: when a clip (dropped, moved, pasted, nudged, recorded) ends after the last bar, whole empty bars are
    /// appended to every track in the same undo step as the change (its undo state was taken before). The song never shrinks by itself.
    /// </summary>
    private static ExtentResult ExtendSongToClips(SongProject project) => SongExtent.EnsureCoversClips(project);

    private bool CanAddAudioTrackWithoutMidiRebuild(SongProject project, TrackModel target, IEnumerable<AudioClip> addedClips) =>
        target.IsAudio && addedClips.All(clip => !clip.IsMidi) && !_midiClipsPlayed &&
        !project.Tracks.Any(track => track.AudioClips.Any(existing => existing.IsMidi));

    public void EditProperties(DocumentSession doc, AudioClip clip)
    {
        if (!_host.IsShown(doc)) return;
        var before = clip.Clone();
        _host.CheckpointUndo();
        if (!_host.ShowClipProperties(clip)) return;
        if (clip.GainDb == before.GainDb && clip.Pitch == before.Pitch && clip.Speed == before.Speed && clip.Muted == before.Muted && clip.Name == before.Name) return;
        Changed(doc);
    }

    /// <summary>S: splits the clip at the edit cursor (the last clicked lane spot) when that is on the clip, else under the playhead.</summary>
    public void SplitAtCursor(DocumentSession doc, TrackModel track, AudioClip clip)
    {
        var at = LaneCursor is { } cursor && ReferenceEquals(cursor.Track, track) && cursor.Lane == clip.Lane && ClipSplitGlue.CanSplit(clip, cursor.Sec) ? cursor.Sec : doc.Playback.Clock.CurrentSec;
        SplitAt(doc, track, clip, at);
    }

    /// <summary>Splits the clip at a song time (both parts keep offset, gain, pitch and speed); one undo step. Says why when the time is not inside the clip.</summary>
    public void SplitAt(DocumentSession doc, TrackModel track, AudioClip clip, double sec)
    {
        if (!_host.IsShown(doc)) return;
        if (!ClipSplitGlue.CanSplit(clip, sec)) { _host.SetStatus("Put the edit cursor (click the clip) or the playhead inside the clip to split it"); return; }
        Edit(doc, () =>
        {
            var second = ClipSplitGlue.Split(track, clip, sec)!;
            _host.SelectedClip = second;
            LaneCursor = new LaneCursor(track, clip.Lane, sec);
        }, $"Split {clip.Name}");
    }

    /// <summary>Glues the selected clip with the clips that continue it on its lane (non-destructive); says why when none does.</summary>
    public void Glue(DocumentSession doc, TrackModel track, AudioClip clip)
    {
        if (!_host.IsShown(doc)) return;
        if (ClipSplitGlue.Chain(track, clip).Count < 2)
        {
            _host.SetStatus("Nothing to glue: the next clip must touch this one on the lane, from the same file at the same speed, pitch and level (merging different recordings into a new file is not available)");
            return;
        }
        Edit(doc, () => _host.SelectedClip = ClipSplitGlue.Glue(track, clip), $"Glued {clip.Name}");
    }

    /// <summary>Removes both fades of the clip (one undo step); does nothing when it has none.</summary>
    public void ResetFades(DocumentSession doc, AudioClip clip)
    {
        if (clip.FadeInSec == 0 && clip.FadeOutSec == 0) { _host.SetStatus("This clip has no fades"); return; }
        Edit(doc, () => { clip.FadeInSec = 0; clip.FadeOutSec = 0; }, $"Fades of {clip.Name} reset");
    }

    public void Duplicate(DocumentSession doc, TrackModel track, AudioClip clip) => Edit(doc, () =>
    {
        var copy = clip.Clone();
        copy.StartSec = clip.EndSec;
        copy.Lane = clip.Lane;
        if (track.AudioClips.Any(c => c.Lane == copy.Lane && c.Overlaps(copy.StartSec, copy.EndSec))) copy.Lane = ClipLanes.FreeLane(track, copy.StartSec, copy.EndSec);
        ClipLanes.Ensure(track, copy.Lane + 1);
        track.AudioClips.Add(copy);
        _host.SelectedClip = copy;
    }, $"Duplicated {clip.Name}");

    /// <summary>Pastes the copied clip at a lane position (the first free lane when that one is busy); does nothing when no clip was copied.</summary>
    public void Paste(DocumentSession doc, TrackModel track, int lane, double sec)
    {
        if (ClipClipboard.Name is not { } name) return;
        Edit(doc, () =>
        {
            var pasted = ClipClipboard.Clone()!;
            pasted.StartSec = Math.Max(0, sec);
            pasted.Lane = track.AudioClips.Any(c => c.Lane == lane && c.Overlaps(pasted.StartSec, pasted.EndSec))
                ? ClipLanes.FreeLane(track, pasted.StartSec, pasted.EndSec) : lane;
            ClipLanes.Ensure(track, pasted.Lane + 1);
            track.AudioClips.Add(pasted);
            _host.SelectedClip = pasted;
            LaneCursor = new LaneCursor(track, pasted.Lane, pasted.EndSec);
        }, $"Pasted {name}");
    }

    /// <summary>Writes a MIDI clip's notes into <paramref name="to"/>'s notation (the clip is removed from <paramref name="from"/> as part of the same undo step).</summary>
    public void MidiClipToNotation(DocumentSession doc, AudioClip clip, int from, int to)
    {
        var project = doc.Project;
        if (!_host.IsShown(doc) || from < 0 || to < 0 || from >= project.Tracks.Count || to >= project.Tracks.Count || project.Tracks[to].IsAudio) return;
        var written = MidiClipToTab.Write(project, project.Tracks[to], clip, sec => doc.Playback.Clock.BarAt(project, sec));
        if (written == 0) { _host.SetStatus("No notes of that MIDI clip fit this track's strings or bars"); _host.RefreshArrangement(); return; }
        project.Tracks[from].AudioClips.Remove(clip);
        _host.SelectedClip = null;
        Changed(doc, true);
        _host.InvalidateScoreLayout();
        _host.SetStatus($"Wrote {written} notes from {clip.Name} into {project.Tracks[to].Name}; {TooltipShortcuts.Append("Undo", "Edit.Undo")} puts it back");
    }

    /// <summary>The clip menu's "Write notation": the MIDI clip's notes go into the notation of its own track.</summary>
    public void WriteNotation(DocumentSession doc, TrackModel track, AudioClip clip)
    {
        if (!_host.IsShown(doc)) return;
        var written = 0;
        DocumentEdits.Run(doc, p =>   // writes notes into the score: the timeline is invalidated
        {
            written = MidiClipToTab.Write(p, track, clip, s => doc.Playback.Clock.BarAt(p, s));
            if (written == 0) return false;
            track.AudioClips.Remove(clip);
            return true;
        });
        if (written == 0) { _host.SetStatus("No notes of that MIDI clip fit this track's strings or bars"); return; }

        Changed(doc, true);
        _host.InvalidateScoreLayout();
        _host.SetStatus($"Wrote {written} notes into {track.Name}");
    }

    /// <summary>Audio files chosen with "Add audio file…" on a lane: placed exactly like files dropped at that spot.</summary>
    public void AddAudioFiles(DocumentSession doc, int trackIndex, double sec, string[] files)
    {
        var project = doc.Project;
        if (!_host.IsShown(doc) || trackIndex < 0 || trackIndex >= project.Tracks.Count) return;
        var items = files.Select(f => _host.MeasureDroppedFile(f, DropItemKind.Audio, MediaDrop.IsTransient(f), doc.Media)).ToList();
        var lane = LaneCursor is { } cursor && ReferenceEquals(cursor.Track, project.Tracks[trackIndex]) ? cursor.Lane : 0;
        ApplyMediaDrop(doc, MediaDrop.Plan(project, items, trackIndex, lane, sec, SongQuarterMap.For(project)));
    }

    /// <summary>
    /// Files dropped on the "Add track" lane below the last track (part D1 wires the drag): audio or MIDI files become a new AUDIO track with
    /// the clips from the song start, one undo step. Files that are neither audio nor MIDI are left out; nothing happens when none is left.
    /// </summary>
    public void AddTrackLaneDrop(DocumentSession doc, IReadOnlyList<string> files)
    {
        if (!_host.IsShown(doc)) return;
        var items = new List<DropItem>();
        foreach (var file in files)
            switch (MediaDrop.RoleOf(file))
            {
                case MediaDrop.FileRole.Audio: items.Add(_host.MeasureDroppedFile(file, DropItemKind.Audio, MediaDrop.IsTransient(file), doc.Media)); break;
                case MediaDrop.FileRole.Midi: items.Add(_host.MeasureDroppedFile(file, DropItemKind.Midi, MediaDrop.IsTransient(file), doc.Media)); break;
            }
        if (items.Count == 0) { _host.SetStatus("Drop audio or MIDI files to add an audio track"); return; }
        ApplyMediaDrop(doc, MediaDrop.PlanAddTrackLane(doc.Project, items, SongQuarterMap.For(doc.Project)));
    }

    /// <summary>
    /// Audio and MIDI files dropped on the timeline (or chosen from the lane menu), as one undo step: temp and virtual files are
    /// copied into the song's media folder first (plug-ins delete their drag files), files on a network drive are measured now,
    /// a drop below the last track adds an audio track, and the clips go on the planned lane with the take rules.
    /// A drop for a song the window no longer shows is ignored.
    /// </summary>
    public void ApplyMediaDrop(DocumentSession doc, MediaDropPlan plan)
    {
        if (!_host.IsShown(doc)) return;
        var project = doc.Project;
        if (!plan.Valid) { _host.SetStatus(plan.Problem ?? "Nothing to add there"); return; }
        if (!plan.NewTrack && (plan.TrackIndex < 0 || plan.TrackIndex >= project.Tracks.Count)) return;
        var media = doc.Media;
        var mediaFolder = MediaFolders.ForSong(doc.Path);
        var items = new List<DropItem>();
        var problems = new List<string>();
        foreach (var planned in plan.Clips)
        {
            var item = planned.Item;
            if (item.Deferred) { problems.Add($"{item.Name} could not be read from the drag"); continue; }
            if (item.IsMidi && item.Midi is null)
            {
                // A MIDI file on a network or removable drive is read only now that the user dropped it.
                item = _host.ReadDroppedMidi(item.Path, item.Transient);
                if (item.Problem is { } why) { problems.Add(why); continue; }
            }
            try
            {
                if (!item.IsMidi && item.Transient)
                    item = new DropItem { Path = MediaDrop.KeepCopy(item.Path, mediaFolder), Name = item.Name, Kind = item.Kind, Seconds = item.Seconds };
                if (!item.IsMidi && item.Seconds <= 0)
                {
                    // The user chose this file: a network or removable folder is allowed (and remembered for this song below).
                    item.Seconds = WaveformCache.LengthOf(item.Path, media, userPicked: true);
                    if (item.Seconds <= 0) { problems.Add($"{item.Name} could not be read as audio"); continue; }
                }
                items.Add(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                Services.Trace.Error(Services.Trace.Ui, "clip edit: keep item: " + ex.Message);
                problems.Add($"{item.Name} could not be kept: {ex.Message}");
            }
        }
        if (items.Count == 0) { _host.SetStatus(problems.FirstOrDefault() ?? "Those files could not be added"); return; }

        var time = SongQuarterMap.For(project);
        _host.CheckpointUndo();   // not a DocumentEdits.Run: the plan is recomputed from the new track's final state and folder approvals (events) interleave with the mutation
        int trackIndex;
        if (plan.NewTrack)
        {
            var created = _tracks.CreateTrack(project, plan.NewTrackKind);
            created.Name = items[0].Name.Length > 0 ? items[0].Name : created.Name;
            AutoChains.Apply(_host.Settings.Plugins, created);
            project.Tracks.Add(created);
            trackIndex = project.Tracks.Count - 1;
        }
        else trackIndex = plan.TrackIndex;
        var track = project.Tracks[trackIndex];
        // Planned again with the final lengths: a file measured only now can change the span, and so the free lane.
        var final = MediaDrop.Plan(project, items, trackIndex, plan.Lane, plan.StartSec, time);
        var trackDrums = track.Kind == TrackKind.Drums || track.MidiChannel == 9;
        var clips = new List<AudioClip>();
        foreach (var planned in final.Clips)
        {
            var item = planned.Item;
            if (item.Midi is { } midi)
            {
                var drums = trackDrums || (track.IsAudio && MediaDrop.LooksLikeDrums(item));   // an audio track keeps a drum groove's notes too
                var (notes, length) = MidiFileImport.ToClipNotes(midi, planned.StartSec, time, drums);
                clips.Add(new AudioClip { Name = item.Name, StartSec = planned.StartSec, SourceLengthSec = length, FileLengthSec = length, Notes = notes });
            }
            else clips.Add(new AudioClip { File = item.Path, Name = item.Name, StartSec = planned.StartSec, SourceLengthSec = item.Seconds, FileLengthSec = item.Seconds });
        }
        foreach (var clip in clips.Where(c => !c.IsMidi))
            if (MediaPathPolicy.Classify(clip.File, media.BaseDirectory) is { Remote: true } picked) MediaAccess.Approve(picked, media);
        MediaDrop.AddClips(track, clips, final.Lane);
        _host.SelectedClip = clips[^1];
        if (plan.NewTrack && !CanAddAudioTrackWithoutMidiRebuild(project, track, clips)) doc.Playback.Engine.Rebuild(project);
        Changed(doc, true);
        if (plan.NewTrack) _host.ShowNewTrack(trackIndex);
        var what = clips.Count == 1 ? clips[0].Name : $"{clips.Count} files";
        var where = plan.NewTrack ? $"a new track ({track.Name})" : final.NewLane ? $"{track.Name}, new lane {final.Lane + 1}" : track.Name;
        _host.SetStatus($"Added {what} to {where}" + (problems.Count > 0 ? $" ({problems.Count} skipped: {problems[0]})" : "") + TakeExtentNote());
    }
}
