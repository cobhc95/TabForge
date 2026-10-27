#pragma warning disable CS0618 // Avalonia 11.3 marks the IDataObject drag API obsolete; it still delivers the dropped file list.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the audio-file drag over an arrangement: the drop affordance (the Add track strip's shade and drop words, the target lane highlight), the
//   target/time mapping from the pointer, the asynchronous measurement with the existing decoder, and the one undo step that lands a plan.
// Does not own: the planning and model edit (AudioDropPlanner/AudioDropApplier in Core), opening song files (MainWindowFileDrop), the notice row
//   (the window), the status bar (MainWindowStatusBar) or the post-edit refresh (Timeline.ClipEdited, the batch-3 path).
// Tests: TabForge.Avalonia.Tests.AudioDropPlannerTests (planner and executor); the drag routing is UI-only.
internal sealed class MainWindowAudioDrop
{
    private readonly MainWindowDocumentPanes _panes;
    private readonly MainWindowTabs _songs;
    private readonly Action<string> _status;
    private readonly Action<ImportStatus>? _showImports;

    private MainWindowAudioDrop(MainWindowDocumentPanes panes, MainWindowTabs songs, Action<string> status, Action<ImportStatus>? showImports)
    {
        _panes = panes;
        _songs = songs;
        _status = status;
        _showImports = showImports;
    }

    /// <summary>Hooks the existing arrangement and every later one: audio drags are handled here, song drags stay with the window's file drop.</summary>
    public static MainWindowAudioDrop Register(MainWindowDocumentPanes panes, MainWindowTabs songs, Action<string> status, Action<ImportStatus>? showImports = null)
    {
        var drop = new MainWindowAudioDrop(panes, songs, status, showImports);
        if (panes.Arrangement is { } existing) drop.Attach(existing);
        panes.ArrangementCreated += drop.Attach;
        return drop;
    }

    /// <summary>Shown while an audio file hovers over the score: the drop lands as a new track at the playback position.</summary>
    internal const string WindowDropHint = "Drop audio here to add a new track at the playback position";

    /// <summary>True when at least one of <paramref name="files"/> would land as audio (the window file drop's accept test for the score).</summary>
    internal bool AcceptsWindowDrop(IEnumerable<string> files) => files.Any(file => AudioDropPlanner.Accepts(file, FilePathBoundary.RefusalReason));

    /// <summary>A score drop of <paramref name="files"/>: a new track at <paramref name="startSec"/> (the playback position), through the normal import.</summary>
    internal void DropOnWindow(IEnumerable<string> files, double startSec)
    {
        if (_panes.Arrangement is not { } arrangement)
        {
            _status.Warning(AudioDropPlanner.NoTargetText);
            return;
        }
        var usable = files.Where(file => AudioDropPlanner.Accepts(file, FilePathBoundary.RefusalReason)).ToList();
        if (usable.Count == 0)
        {
            var first = files.FirstOrDefault() ?? "";
            _status.Error($"Cannot add {Path.GetFileName(first)}: {AudioDropPlanner.RefusalOf(first, FilePathBoundary.RefusalReason)}.");
            return;
        }
        _ = ImportAsync(arrangement, usable, AudioDropTarget.NewTrack, startSec);
    }

    /// <summary>The take-lane menu's Import: audio <paramref name="files"/> land on <paramref name="track"/> from <paramref name="startSec"/> through the drop's own import (one undo step, one new lane).</summary>
    internal void ImportOnTrack(IEnumerable<string> files, TrackModel track, double startSec)
    {
        if (_panes.Arrangement is not { } arrangement)
        {
            _status.Warning(AudioDropPlanner.NoTargetText);
            return;
        }
        var all = files.ToList();
        var usable = all.Where(file => AudioDropPlanner.Accepts(file, FilePathBoundary.RefusalReason)).ToList();
        if (usable.Count == 0)
        {
            var first = all.FirstOrDefault() ?? "";
            _status.Error($"Cannot add {Path.GetFileName(first)}: {AudioDropPlanner.RefusalOf(first, FilePathBoundary.RefusalReason)}.");
            return;
        }
        _ = ImportAsync(arrangement, usable, AudioDropTarget.On(track), startSec);
    }

    /// <summary>Insert > Audio file / MIDI file: <paramref name="files"/> land from <paramref name="startSec"/> on <paramref name="track"/> (one new lane, the drop's own import) or, with a null track, on a new audio / MIDI track. Audio and MIDI files both land; the task ends when the clips are in.</summary>
    internal Task InsertAtAsync(IEnumerable<string> files, TrackModel? track, double startSec)
    {
        if (_panes.Arrangement is not { } arrangement)
        {
            _status.Warning(AudioDropPlanner.NoTargetText);
            return Task.CompletedTask;
        }
        var all = files.ToList();
        var usable = all.Where(Lands).ToList();
        if (usable.Count == 0)
        {
            var first = all.FirstOrDefault() ?? "";
            _status.Error($"Cannot add {Path.GetFileName(first)}: {AudioDropPlanner.RefusalOf(first, FilePathBoundary.RefusalReason)}.");
            return Task.CompletedTask;
        }
        return ImportAsync(arrangement, usable, track is null ? AudioDropTarget.NewTrack : AudioDropTarget.On(track), startSec);
    }

    /// <summary>The score side of an audio drag: accept with the new-track hint, and drop as a new track at <paramref name="playheadSec"/>.</summary>
    internal MainWindowFileDrop.WindowAudioDrop ForWindow(Func<double> playheadSec) =>
        new(AcceptsWindowDrop, WindowDropHint, files => DropOnWindow(files, playheadSec()));

    private void Attach(ArrangementView arrangement)
    {
        DragDrop.SetAllowDrop(arrangement, true);
        arrangement.AddHandler(DragDrop.DragEnterEvent, new EventHandler<DragEventArgs>((_, e) => OnDragOver(arrangement, e)));
        arrangement.AddHandler(DragDrop.DragOverEvent, new EventHandler<DragEventArgs>((_, e) => OnDragOver(arrangement, e)));
        arrangement.AddHandler(DragDrop.DragLeaveEvent, new EventHandler<DragEventArgs>((_, e) => ClearPreview(arrangement, e)));
        arrangement.AddHandler(DragDrop.DropEvent, new EventHandler<DragEventArgs>((_, e) => OnDrop(arrangement, e)));
    }

    private void OnDragOver(ArrangementView arrangement, DragEventArgs e)
    {
        var files = MainWindowFileDrop.LocalFiles(e.Data).ToList();
        if (files.Count == 0) return;
        if (HasSongs(files)) return;   // a song drag is the window file drop's

        var hit = HitTest(arrangement, e);
        var usable = files.Where(Lands).ToArray();
        if (YieldsToWindow(usable, hit))   // MIDI files off the rows and the strip open as a song in the window's drop
        {
            ClearPreview(arrangement, e);
            e.Handled = false;
            return;
        }
        EnsureMeasured(arrangement, usable);
        Preview(arrangement, hit, usable);
        e.DragEffects = usable.Length > 0 && hit.Kind != AudioDropHitKind.None ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(ArrangementView arrangement, DragEventArgs e)
    {
        var files = MainWindowFileDrop.LocalFiles(e.Data).ToList();
        if (files.Count == 0) return;
        if (HasSongs(files)) return;
        var hit = HitTest(arrangement, e);
        if (YieldsToWindow(files.Where(Lands).ToList(), hit))
        {
            ClearPreview(arrangement, e);
            e.Handled = false;
            return;
        }
        ClearPreview(arrangement, e);
        e.Handled = true;

        if (hit.Kind == AudioDropHitKind.None || hit.Kind == AudioDropHitKind.Empty)
        {
            e.DragEffects = DragDropEffects.None;
            _status.Warning(AudioDropPlanner.NoTargetText);
            return;
        }

        var usable = files.Where(Lands).ToList();
        if (usable.Count == 0)
        {
            e.DragEffects = DragDropEffects.None;
            var first = files[0];
            _status.Error($"Cannot add {Path.GetFileName(first)}: {AudioDropPlanner.RefusalOf(first, FilePathBoundary.RefusalReason)}.");
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        var target = hit.Kind == AudioDropHitKind.Strip ? AudioDropTarget.NewTrack : AudioDropTarget.On(hit.Track!);
        _ = ImportAsync(arrangement, usable, target, hit.Seconds);
    }

    /// <summary>True when the drag carries a song file other than a MIDI file: that is the window's open-as-song drop (a MIDI file is a song only off the timeline).</summary>
    internal static bool HasSongs(IEnumerable<string> files) => MainWindowFileDrop.SongPaths(files).Any(file => !FileTypes.IsMidi(Path.GetExtension(file)));

    /// <summary>True when the file lands as a clip: audio the decoder takes, or a MIDI file (both past the path guard).</summary>
    internal static bool Lands(string file) =>
        AudioDropPlanner.Accepts(file, FilePathBoundary.RefusalReason)
        || (FileTypes.IsMidi(Path.GetExtension(file)) && !(FilePathBoundary.RefusalReason(file) is { Length: > 0 }));

    /// <summary>True when only MIDI files would land and the pointer is off the rows and the strip: the window opens them as a song.</summary>
    private static bool YieldsToWindow(IReadOnlyList<string> usable, AudioDropHit hit) => YieldsToWindow(usable, hit.Kind is AudioDropHitKind.Track or AudioDropHitKind.Strip);

    internal static bool YieldsToWindow(IReadOnlyList<string> usable, bool onRowsOrStrip) =>
        !onRowsOrStrip && usable.Count > 0 && usable.All(file => FileTypes.IsMidi(Path.GetExtension(file)));

    private static AudioClip? ReadMidi(string path)
    {
        if (new FileInfo(path).Length > 16L * 1024 * 1024) throw new InvalidDataException("The MIDI file is too large.");
        return MidiClipImport.Read(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Shows the affordance: the Add track strip's shade/words for a strip drop, the target lane's track-colour shade and
    /// the landing block for a track drop (a new track or lane gets its slot too).</summary>
    private void Preview(ArrangementView arrangement, AudioDropHit hit, IReadOnlyList<string> usable)
    {
        arrangement.AddTrackBar.DropActive = usable.Count > 0 && hit.Kind == AudioDropHitKind.Strip;
        arrangement.Timeline.ClipDropLane = usable.Count > 0 && hit.Kind == AudioDropHitKind.Track
            ? (hit.Track!, PreviewLane(hit.Track!))
            : null;
        AudioFileDropPreview? preview = null;
        if (usable.Count > 0 && (hit.Kind == AudioDropHitKind.Track || hit.Kind == AudioDropHitKind.Strip))
        {
            var song = arrangement.Timeline.Song;
            var lengths = new List<(string File, double LengthSec)>(usable.Count);
            foreach (var file in usable) lengths.Add((file, _lengths.TryGetValue(file, out var length) ? length : 0));
            var target = hit.Kind == AudioDropHitKind.Strip ? AudioDropTarget.NewTrack : AudioDropTarget.On(hit.Track!);
            preview = AudioDropPreviewPlanner.Plan(song, target, hit.Seconds, lengths);
        }
        arrangement.Timeline.FileDropPreview = preview;
        arrangement.AddTrackBar.DropGhost = preview is { Track: null } strip ? StripGhost(arrangement, strip) : null;
        arrangement.Timeline.InvalidateVisual();
        _previewed = (arrangement, hit, usable);
    }

    /// <summary>The strip's ghost for a new-track drop: the first clip's span in the strip's own x (the strip is outside the timeline's scroll), null when the strip is not laid out.</summary>
    internal static (double X1, double X2, string Label, string Readout)? StripGhost(ArrangementView arrangement, AudioFileDropPreview preview)
    {
        var timeline = arrangement.Timeline;
        var first = preview.Parts is { Count: > 0 } parts ? parts[0] : null;
        var (x1, x2) = (timeline.XOfSeconds(preview.StartSec), timeline.XOfSeconds(first?.EndSec ?? preview.EndSec));
        if (timeline.TranslatePoint(new Point(x1, 0), arrangement.AddTrackBar) is not { } left) return null;
        return (left.X, left.X + Math.Max(3, x2 - x1), first?.Name ?? preview.Label, preview.Readout);
    }

    private void ClearPreview(ArrangementView arrangement, DragEventArgs e)
    {
        arrangement.AddTrackBar.DropActive = false;
        arrangement.Timeline.ClipDropLane = null;
        arrangement.Timeline.FileDropPreview = null;
        arrangement.AddTrackBar.DropGhost = null;
        arrangement.Timeline.InvalidateVisual();
        _previewed = null;
        e.Handled = true;
    }

    private readonly Dictionary<string, double> _lengths = new(StringComparer.OrdinalIgnoreCase);
    private string _lengthsKey = "";
    private (ArrangementView Arrangement, AudioDropHit Hit, IReadOnlyList<string> Usable)? _previewed;

    // The first hover of a drag measures the files' lengths off the UI thread (a header read each):
    // the preview refines from the one-bar estimate to the real span when each arrives, and the drop re-measures with the song's context.
    private void EnsureMeasured(ArrangementView arrangement, IReadOnlyList<string> usable)
    {
        var key = string.Join("\n", usable);
        if (key == _lengthsKey) return;
        _lengthsKey = key;
        _lengths.Clear();
        if (_songs.Active is not { } song || !ReferenceEquals(arrangement.Timeline.Song, song.Project)) return;
        var media = song.Media;
        var timelineSong = song.Project;
        foreach (var file in usable)
        {
            var measured = file;
            _ = Task.Run(() =>
            {
                var length = AudioDropLanding.LengthOf(measured, path => _songs.Waveforms.LengthOf(path, media));
                AvaloniaUiThread.ForApplication().Post(() =>
                {
                    if (_lengthsKey != key || !ReferenceEquals(arrangement.Timeline.Song, timelineSong)) return;
                    _lengths[measured] = length;
                    if (_previewed is { } previewed) Preview(previewed.Arrangement, previewed.Hit, previewed.Usable);
                });
            });
        }
    }

    private async Task ImportAsync(ArrangementView arrangement, IReadOnlyList<string> files, AudioDropTarget target, double startSec)
    {
        var song = _songs.Active;
        if (!ReferenceEquals(arrangement.Timeline.Song, song.Project)) return;
        var names = files.Select(Path.GetFileName).ToArray();
        // The progress lives on the top bar (sticky with Cancel); the transient status is only for the outcome, so the sticky stays behind it.
        _showImports?.Invoke(ImportStatusPresenter.ForPending(names!));
        try
        {
            // The existing decoder path (WaveformCache/NAudio), off the UI thread; no second loader.
            // a dropped file is the user's own pick: its header is measured (userPicked) and its verdict kept, so the folders of the files that land are approved below
            var isMidi = (string file) => FileTypes.IsMidi(Path.GetExtension(file));
            var measured = await Task.Run(() => files.Where(file => !isMidi(file)).ToDictionary(file => file, file => (Length: _songs.Waveforms.LengthOf(file, song.Media, userPicked: true), Verdict: MediaPathPolicy.Classify(file, song.Media.BaseDirectory))));
            var midis = await Task.Run(() => files.Where(isMidi).ToDictionary(file => file, file => { try { return ReadMidi(file); } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return null; } }));
            var lengths = measured.ToDictionary(pair => pair.Key, pair => pair.Value.Length);
            var verdicts = measured.ToDictionary(pair => pair.Key, pair => pair.Value.Verdict);
            if (!ReferenceEquals(_songs.Active, song) || !ReferenceEquals(arrangement.Timeline.Song, song.Project)) return;
            var plan = AudioDropPlanner.Plan(files, target, startSec, file => lengths.GetValueOrDefault(file), FilePathBoundary.RefusalReason, file => midis.GetValueOrDefault(file));
            var result = plan.Accepted ? AudioDropApplier.Apply(song, plan) : null;
            if (result is null)
            {
                _status.Warning(AudioDropPlanner.SkippedNotice(plan) ?? plan.Refusal ?? "Nothing was added");
                return;
            }
            ApproveLanded(plan, verdicts, song.Media);   // before the clip edit: the new clips' waveform requests are then allowed
            arrangement.Timeline.SelectedClip = new TimelineClipSelection(result.Track, result.LastClip);
            arrangement.Timeline.ClipEditCommitted(song, result.Track.AudioClips.TakeLast(result.Added).ToList());   // the one post-edit path (lane compaction, engine sync, rows reload), after the overlap check
            if (plan.NewTrack) arrangement.ShowTrack(result.Track);   // the new track is the selected one and its row is scrolled into view
            arrangement.Timeline.InvalidateVisual();
            _status(result.Status);
        }
        finally
        {
            _showImports?.Invoke(ImportStatusPresenter.ForPending(Array.Empty<string>()));
        }
    }

    /// <summary>A dropped file that landed is the user's explicit pick: its folder is approved for this song when the drop is applied (never on hover). Only folders that still need approval are touched.</summary>
    internal static void ApproveLanded(AudioDropPlan plan, IReadOnlyDictionary<string, MediaVerdict> verdicts, MediaContext media)
    {
        foreach (var item in plan.Items)
        {
            if (verdicts.TryGetValue(item.File, out var verdict) && MediaAccess.Decide(verdict, media).State == MediaAccessState.NeedsApproval)
                MediaAccess.Approve(verdict, media);
        }
    }

    private static int PreviewLane(TrackModel track) =>track.AudioClips.Count == 0 ? 0 : ClipLanes.Count(track);

    /// <summary>The drop position mapped to the strip, a track (with its row and song second), empty timeline space, or nothing of ours.</summary>
    private static AudioDropHit HitTest(ArrangementView arrangement, DragEventArgs e)
    {
        var bar = arrangement.AddTrackBar;
        if (bar.IsVisible)
        {
            var inBar = e.GetPosition(bar);
            if (inBar.X >= 0 && inBar.Y >= 0 && inBar.X <= bar.Bounds.Width && inBar.Y <= bar.Bounds.Height)
                return new AudioDropHit(AudioDropHitKind.Strip, null, SecondsAt(arrangement, e));
        }
        var timeline = arrangement.Timeline;
        var point = e.GetPosition(timeline);
        if (point.X < 0 || point.Y < 0 || point.X > timeline.Bounds.Width || point.Y > timeline.Bounds.Height)
            return new AudioDropHit(AudioDropHitKind.None, null, 0);
        var row = timeline.RowAtY(point.Y);
        if (row >= 0 && row < timeline.Song.Tracks.Count)
            return new AudioDropHit(AudioDropHitKind.Track, timeline.Song.Tracks[row], SecondsAt(arrangement, e));
        return new AudioDropHit(AudioDropHitKind.Empty, null, SecondsAt(arrangement, e));
    }

    private static double SecondsAt(ArrangementView arrangement, DragEventArgs e)
    {
        // The one landing second: the drag preview and the drop both read it here (snapped like a clip edge, along the repeat pass nearest the playhead; Alt: no snap).
        return AudioDropLanding.StartSec(arrangement.Timeline, e.GetPosition(arrangement.Timeline).X, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
    }

    private enum AudioDropHitKind { None, Empty, Strip, Track }

    private readonly record struct AudioDropHit(AudioDropHitKind Kind, TrackModel? Track, double Seconds);
}
#pragma warning restore CS0618
