using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using NAudio.Wave;
using TabForge.Audio;
using TabForge.Avalonia.Capture;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the stress run of the vertical clip drag: an audio clip dragged up and down across lanes and tracks hundreds of times through the real pointer path
//   (window mouse events into the TimelineInputController), dropped or not, with Ctrl copy, Shift lock, drops that open a new lane, drops onto occupied spots that raise the
//   overlap prompt (answered at once, or left open while later drags go on), over a repeat and a loop, with the post-edit refresh and the engine clip sync of the real window.
//   It proves: no exception, no lane / undo / clip / peaks / engine-message growth beyond what the drops did, and no single step that blocks the UI thread; and the
//   focused regressions it found (a late overlap answer undoing a later edit, a gesture reading tracks that were removed under it).
// Does not own: the single gestures (ClipVerticalDragTests) or the engine process (EngineClipResyncStressTests).
// Tests: this class.
public sealed class ClipVerticalDragStressTests
{
    private sealed class FixedFileSystem : IMediaFileSystem
    {
        public DriveType DriveTypeOf(string root) => DriveType.Fixed;
        public string? FinalPathOf(string fullPath) => fullPath;
    }

    private sealed class Rig : IDisposable
    {
        public SongDocument Doc = null!;
        public ArrangementView Arrangement = null!;
        public TimelineView View = null!;
        public Window Window = null!;
        public AudioEngineClient Client = null!;
        public AvaloniaPlaybackSessions Sessions = null!;
        public readonly List<string> Files = new();
        public readonly List<Action> Pending = new();   // prompts left open: each completes its answer when called
        public IMediaFileSystem? SavedFileSystem;
        public string XId = "";
        public HashSet<string> Originals = new();
        public int Asked, ClipMessages, Refreshes;
        public SongProject Current => Doc.Project;
        public List<TrackModel> Audio => Current.Tracks.Where(t => t.IsAudio).ToList();

        public void Dispose()
        {
            Window.Close();
            Client.Dispose();
            MediaPathPolicy.FileSystem = SavedFileSystem!;
            foreach (var file in Files) try { File.Delete(file); } catch (IOException) { }
            Doc.MarkClean();
        }
    }

    private static string Wav(Rig rig, double seconds)
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-stress-" + Guid.NewGuid().ToString("N") + ".wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(8000, 16, 1)))
            for (var i = 0; i < (int)(seconds * 8000); i++) writer.WriteSample((float)(0.4 * Math.Sin(i * 0.05)));
        rig.Files.Add(path);
        return path;
    }

    private static AudioClip Clip(string name, string file, double start, double length, int lane = 0) =>
        new() { Name = name, File = file, StartSec = start, SourceLengthSec = length, FileLengthSec = length, Lane = lane };

    /// <summary>A song with four audio tracks (T0 holds the dragged clip X), the real post-edit refresh and a pipe-less engine client counting the clip lists it is sent.</summary>
    private static Rig Open(bool repeat, bool prompt, bool loop = false)
    {
        var rig = new Rig();
        var project = TemplateFactory.Blank();
        foreach (var t in project.Tracks) t.Measures = t.Measures.Take(24).ToList();
        if (repeat)
        {
            var m = project.Tracks[0].Measures;
            m[3].RepeatStart = true;
            m[4].RepeatEnd = true;
        }
        for (var i = 0; i < 4; i++) project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        var tracks = project.Tracks.Where(t => t.IsAudio).ToList();
        var barSec = AudioClipTimelineRules.SecondsAtBar(project, 1, 0);
        var a = Wav(rig, 8);
        var b = Wav(rig, 6);
        var x = Clip("X", a, 0, 2 * barSec);
        rig.XId = x.Id;
        tracks[0].AudioClips.AddRange(new[] { x, Clip("P", b, 9 * barSec, barSec), Clip("Q", a, 3 * barSec, barSec, 1) });
        tracks[1].AudioClips.AddRange(new[] { Clip("K", b, 0, 3 * barSec), Clip("L", a, 5 * barSec, barSec, 1) });
        tracks[3].AudioClips.Add(Clip("M", a, 2 * barSec, 2 * barSec));
        foreach (var t in tracks) ClipLanes.Ensure(t, t.AudioClips.Count == 0 ? 0 : t.AudioClips.Max(c => c.Lane) + 1);
        rig.Originals = tracks.SelectMany(t => t.AudioClips).Select(c => c.Id).ToHashSet();
        rig.Doc = new SongDocument(() => null) { Project = project };
        rig.Doc.MarkClean();
        rig.SavedFileSystem = MediaPathPolicy.FileSystem;
        MediaPathPolicy.FileSystem = new FixedFileSystem();
        foreach (var file in rig.Files) MediaAccess.Evaluate(file, null);
        rig.Client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust())!;
        rig.Client.AttachFakeForTest();
        rig.Sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), null) { Engine = rig.Client };
        rig.Client.ClipsSentForTest = (_, _) => rig.ClipMessages++;
        rig.Arrangement = new ArrangementView(project, rig.Doc);
        rig.View = rig.Arrangement.Timeline;
        rig.View.Snap = new SnapSettings { Enabled = false };
        rig.View.ClipEdited = document => { rig.Refreshes++; MainWindowClipRefresh.AfterEdit(rig.Arrangement, document, null, rig.Sessions); };
        if (prompt) rig.View.ClipOverlap = OverlapPolicy(rig);
        rig.Window = new Window { Width = 1800, Height = 900, Content = rig.Arrangement };
        rig.Window.Show();
        rig.Window.UpdateLayout();
        rig.View.UpdateLayout();
        if (loop) rig.Doc.Selection.SetLoop(2, 6);
        rig.Sessions.SyncEngine(rig.Doc, new PluginSettings());
        return rig;
    }

    /// <summary>The overlap prompt seam: every third question stays open until the test answers it later (the drag goes on under it); the others answer at once: Move, Keep, Move, and so on.</summary>
    private static ClipOverlapPolicy OverlapPolicy(Rig rig) => new()
    {
        Setting = () => ClipOverlapChoice.Ask,
        Remember = _ => { },
        Prompt = question =>
        {
            rig.Asked++;
            if (rig.Asked % 3 != 0) return Task.FromResult<ClipOverlapAnswer?>(new ClipOverlapAnswer(rig.Asked % 2 == 1, false));
            var open = new TaskCompletionSource<ClipOverlapAnswer?>();
            var ask = rig.Asked;
            rig.Pending.Add(() => open.SetResult(ask % 2 == 0 ? new ClipOverlapAnswer(true, false) : null));
            return open.Task;
        },
    };

    private static (TrackModel Track, AudioClip Clip) Where(Rig rig, string name)
    {
        var track = rig.Current.Tracks.First(t => t.AudioClips.Any(c => c.Name == name && (name != "X" || c.Id == rig.XId)));
        return (track, track.AudioClips.First(c => c.Name == name && (name != "X" || c.Id == rig.XId)));
    }

    private static Point Win(Rig rig, Point p) => rig.View.TranslatePoint(p, rig.Window)!.Value;

    /// <summary>The y that puts the dragged clip middle on a lane of a track (a lane at or past the last one: the new-lane slot).</summary>
    private static double YOf(Rig rig, TrackModel track, int lane)
    {
        var row = rig.Current.Tracks.IndexOf(track);
        return rig.View.LaidOutLaneCountOf(track) == 0 ? rig.View.RowTop(row) + rig.View.RowHeight / 2 : rig.View.LaneBandTopOf(row, lane) + ClipLanes.AudioLaneHeight / 2;
    }

    private sealed class Timings
    {
        public long Worst, Total, Steps;
        public void Time(Action step)
        {
            var clock = Stopwatch.StartNew();
            step();
            Worst = Math.Max(Worst, clock.ElapsedMilliseconds);
            Total += clock.ElapsedMilliseconds;
            Steps++;
        }
    }

    private static void CheckModel(Rig rig, int expectedClips)
    {
        var all = rig.Current.Tracks.SelectMany(t => t.AudioClips).ToList();
        Assert.Equal(expectedClips, all.Count);
        Assert.Equal(all.Count, all.Select(c => c.Id).Distinct().Count());
        foreach (var track in rig.Current.Tracks)
        {
            Assert.All(track.AudioClips, c => Assert.True(c.Lane >= 0));
            if (track.AudioClips.Count == 0) continue;
            Assert.True(track.Lanes.Count >= track.AudioClips.Max(c => c.Lane) + 1, "a clip is on a lane the track does not have");
            Assert.True(track.Lanes.Count <= track.AudioClips.Count, $"empty lanes leaked on {track.Name}: {track.Lanes.Count} lanes for {track.AudioClips.Count} clips");
        }
        Assert.Null(rig.View.ClipDragPreview);
        Assert.Equal(0, rig.View.GhostBars);
    }

    private static void Run(Rig rig, int passes, int seed, Timings timings)
    {
        var random = new Random(seed);
        var barSec = AudioClipTimelineRules.SecondsAtBar(rig.Current, 1, 0);
        var baseClips = rig.Current.Tracks.SelectMany(t => t.AudioClips).Count();
        for (var pass = 0; pass < passes; pass++)
        {
            // Answers of prompts left open come in late, with other drags in between.
            if (pass % 7 == 6) AnswerPending(rig);
            var (source, x) = Where(rig, "X");
            var box = TimelineAudioClips.ClipBounds(rig.View, source, x);
            var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
            var mods = random.Next(10) switch { 0 => RawInputModifiers.Control, 1 => RawInputModifiers.Shift, 2 => RawInputModifiers.Alt, _ => RawInputModifiers.None };
            var held = RawInputModifiers.LeftMouseButton | mods;
            var cancel = random.Next(20) == 0;
            var drop = !cancel && random.Next(4) != 0;
            timings.Time(() => rig.Window.MouseDown(Win(rig, press), MouseButton.Left, mods));
            var audio = rig.Audio;
            var last = press;
            // Wiggle: up and down across lanes and tracks, ending on a landing; the lane past the last one and the area above the rows are targets too.
            var moves = random.Next(2, 6);
            for (var i = 0; i < moves; i++)
            {
                var track = audio[random.Next(audio.Count)];
                var lane = random.Next(0, rig.View.LaidOutLaneCountOf(track) + 2);
                var y = i == moves - 1 && !drop ? press.Y : random.Next(12) == 0 ? rig.View.RowTop(0) - 30 : YOf(rig, track, lane) + (random.Next(5) == 0 ? ClipLanes.AudioLaneHeight * 0.4 : 0);
                var sideways = random.Next(3) == 0 ? (random.NextDouble() - 0.5) * 4 * barSec : 0;
                last = new Point(rig.View.XOfSeconds(Math.Max(0, rig.View.SecondsOfX(press.X) + sideways)), y);
                var step = last;
                timings.Time(() => rig.Window.MouseMove(Win(rig, step), held));
            }
            if (pass % 10 == 0) CaptureImage.Render(rig.View, 1);   // the preview, the fold and the waveforms draw for real mid-drag
            if (cancel) timings.Time(() => rig.Arrangement.Input.Show(rig.Doc));
            var end = last;
            timings.Time(() => rig.Window.MouseUp(Win(rig, end), MouseButton.Left, mods));
            RemoveCopies(rig, baseClips);
            rig.Window.UpdateLayout();
            if (pass % 5 == 0) rig.View.InvalidateVisual();
            if (rig.Pending.Count == 0) CheckModel(rig, baseClips);
            else Assert.Null(rig.View.ClipDragPreview);
        }
        AnswerPending(rig);
        rig.Window.UpdateLayout();
        CheckModel(rig, baseClips);
    }

    private static void AnswerPending(Rig rig)
    {
        foreach (var answer in rig.Pending.ToList()) answer();
        rig.Pending.Clear();
    }

    /// <summary>A copy landed (Ctrl): takes it away again so the song stays the size the run started with.</summary>
    private static void RemoveCopies(Rig rig, int baseClips)
    {
        while (rig.Current.Tracks.SelectMany(t => t.AudioClips).Count() > baseClips)
        {
            var copy = rig.Current.Tracks.SelectMany(t => t.AudioClips).First(c => !rig.Originals.Contains(c.Id));
            var home = rig.Current.Tracks.First(t => t.AudioClips.Contains(copy));
            DocumentEdits.Run(rig.Doc, _ => home.AudioClips.Remove(copy));
            ClipLanes.Compact(home);
        }
    }

    private static void Stress(bool repeat, bool prompt, bool loop, int passes, int seed)
    {
        using var rig = Open(repeat, prompt, loop);
        var peaksBefore = WaveformCache.Default.CachedCount;
        var timings = new Timings();
        var whole = Stopwatch.StartNew();
        Run(rig, passes, seed, timings);
        // Growth: the peaks cache holds the files of the song (an entry per file, not per drag), the history is the drops, the engine hears the changed lists only.
        Assert.True(WaveformCache.Default.CachedCount <= peaksBefore + 3, $"peaks cache grew: {peaksBefore} -> {WaveformCache.Default.CachedCount}");
        Assert.True(rig.Doc.Undo.UndoCount <= passes * 3 + 1, "the undo history grew past the drops");
        Assert.True(rig.ClipMessages <= (rig.Refreshes + 2) * 5, $"engine clip lists: {rig.ClipMessages} for {rig.Refreshes} refreshes");
        Assert.True(timings.Worst < 3000, $"a pointer step blocked the UI thread for {timings.Worst} ms");
    }

    [AvaloniaFact]
    public void HundredsOfWigglesAcrossLanesAndTracksKeepTheSongConsistent() => Stress(repeat: false, prompt: false, loop: false, passes: 70, seed: 1);

    [AvaloniaFact]
    public void WigglesWithTheOverlapPromptOpenAndAnsweredLateKeepTheSongConsistent() => Stress(repeat: false, prompt: true, loop: false, passes: 70, seed: 2);

    [AvaloniaFact]
    public void WigglesOverARepeatAndALoopKeepTheSongConsistent() => Stress(repeat: true, prompt: true, loop: true, passes: 50, seed: 3);

    // ---- focused regressions ----

    private static void Drag(Rig rig, string name, TrackModel to, int lane, double dxSec = 0)
    {
        var (source, clip) = Where(rig, name);
        var box = TimelineAudioClips.ClipBounds(rig.View, source, clip);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        rig.Window.MouseDown(Win(rig, press), MouseButton.Left);
        var target = new Point(rig.View.XOfSeconds(rig.View.SecondsOfX(press.X) + dxSec), YOf(rig, to, lane));
        for (var i = 1; i <= 6; i++) rig.Window.MouseMove(Win(rig, press + (target - press) * (i / 6.0)), RawInputModifiers.LeftMouseButton);
        rig.Window.MouseUp(Win(rig, target), MouseButton.Left);
    }

    [AvaloniaFact]
    public void AnOverlapPromptCancelledAfterAnotherEditDoesNotUndoThatLaterEdit()
    {
        using var rig = Open(repeat: false, prompt: true);
        var barSec = AudioClipTimelineRules.SecondsAtBar(rig.Current, 1, 0);
        rig.Asked = 2;   // the next question (the third) stays open and answers Cancel
        var tracks = rig.Audio;
        var ids = tracks.Select(t => t.Id).ToList();
        Drag(rig, "X", tracks[1], 0);   // lands over K: asks, and the question stays open
        Assert.Single(rig.Pending);
        var undoAfterFirst = rig.Doc.Undo.UndoCount;
        // While the question is open the user drags another clip somewhere free.
        Drag(rig, "M", rig.Audio[2], 0, dxSec: 8 * barSec);
        Assert.Equal(undoAfterFirst + 1, rig.Doc.Undo.UndoCount);
        var mStart = Where(rig, "M").Clip.StartSec;
        rig.Pending[0]();   // the late answer is Cancel
        Assert.Equal(undoAfterFirst + 1, rig.Doc.Undo.UndoCount);
        var (after, moved) = Where(rig, "M");
        Assert.Equal(ids[2], after.Id);
        Assert.Equal(mStart, moved.StartSec, 6);
        Assert.Equal(ids[1], Where(rig, "X").Track.Id);   // the first drop stays: its question is out of date
    }

    [AvaloniaFact]
    public void RepeatedNewLaneHoversAndDropsLeaveNoEmptyLane()
    {
        using var rig = Open(repeat: false, prompt: false);
        var tracks = rig.Audio;
        for (var i = 0; i < 40; i++)
        {
            Drag(rig, "X", tracks[0], rig.View.LaidOutLaneCountOf(tracks[0]));   // the new-lane slot of its own track
            Drag(rig, "X", tracks[2], 0);
            Drag(rig, "X", tracks[0], 0, dxSec: (i % 5) * 0.2);
        }
        CheckModel(rig, 6);
    }

    [AvaloniaFact]
    public void ATrackRemovedUnderTheDragEndsTheDragWithoutThrowing()
    {
        using var rig = Open(repeat: false, prompt: false);
        var tracks = rig.Audio;
        var (source, x) = Where(rig, "X");
        var box = TimelineAudioClips.ClipBounds(rig.View, source, x);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        var held = RawInputModifiers.LeftMouseButton;
        rig.Window.MouseDown(Win(rig, press), MouseButton.Left);
        rig.Window.MouseMove(Win(rig, new Point(press.X, YOf(rig, tracks[3], 0))), held);
        // A track edit under the drag shortens the track list while the gesture still reads the old rows.
        DocumentEdits.Run(rig.Doc, project => { project.Tracks.Remove(tracks[3]); project.Tracks.Remove(tracks[2]); return true; });
        rig.Window.MouseMove(Win(rig, new Point(press.X + 20, YOf(rig, tracks[1], 0))), held);
        rig.Window.MouseUp(Win(rig, new Point(press.X + 20, press.Y)), MouseButton.Left);
        Assert.Null(rig.View.ClipDragPreview);
    }

    [AvaloniaFact]
    public void ThousandsOfPointerStepsCostLittleAndAllocateNoPreviewsWhenNothingChanges()
    {
        using var rig = Open(repeat: true, prompt: false);
        var tracks = rig.Audio;
        var (source, x) = Where(rig, "X");
        var box = TimelineAudioClips.ClipBounds(rig.View, source, x);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        var gestures = new TimelineClipGestureController(rig.View);
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 4000; i++)
        {
            var track = tracks[i % tracks.Count];
            gestures.Update(new Point(press.X + (i % 40), YOf(rig, track, 0) + (i % 3) * 5), altHeld: false);
        }
        var elapsed = clock.ElapsedMilliseconds;
        gestures.Cancel();
        // A pointer step is a few comparisons and one small preview; 4000 of them (hover across tracks, over a repeat) take well under a second.
        Assert.True(elapsed < 3000, $"4000 steps took {elapsed} ms");
        Assert.Null(rig.View.ClipDragPreview);
        CheckModel(rig, 6);
    }

    [AvaloniaFact]
    public void AClipBoxNarrowerThanAPixelStillHasAFirstWaveformColumn()
    {
        var span = ClipGesturePresenter.WaveSpanOf(100.3, 100.8, 5000, 0, 0);
        Assert.True(span.To > span.From && span.To - span.From < 1);
        Assert.Equal(1, ClipGesturePresenter.WaveColumns(span));
        Assert.Equal(40, ClipGesturePresenter.WaveColumns(ClipGesturePresenter.WaveSpanOf(10, 50, 5000, 0, 0)));
    }

    [AvaloniaFact]
    public void APressDuringAPreviewingDragPutsThatClipBackFirst()
    {
        using var rig = Open(repeat: false, prompt: false);
        var tracks = rig.Audio;
        var (source, x) = Where(rig, "X");
        var start = x.StartSec;
        var box = TimelineAudioClips.ClipBounds(rig.View, source, x);
        var press = new Point((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2);
        var gestures = new TimelineClipGestureController(rig.View);
        gestures.Press(true, press);
        Assert.True(gestures.Begin());
        gestures.Update(new Point(press.X + 90, YOf(rig, tracks[2], 0)), altHeld: false);
        Assert.NotEqual(start, x.StartSec);
        gestures.Press(true, new Point(2, 2));   // a second press arrives before any release
        Assert.Equal(start, x.StartSec, 6);
        Assert.Null(rig.View.ClipDragPreview);
        Assert.False(gestures.Commit(rig.Doc));
    }
}
