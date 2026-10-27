using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Audio;
using TabForge.Audio.Contracts;
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

// Owns: the proof that splitting an audio clip and dragging the pieces apart reaches the engine as two separate regions with a silent gap between them
//   (the clip list the engine plays, as sent after the split and after the drag), in a plain song and in a song with repeats; and that muting the clip reaches it too.
// Does not own: the drag maths (ClipDragRepeatTests) or the engine's mixing (the engine tests).
// Tests: this class.
public sealed class SplitMovePlaybackSyncTests
{
    private sealed class FixedFileSystem : IMediaFileSystem
    {
        public DriveType DriveTypeOf(string root) => DriveType.Fixed;
        public string? FinalPathOf(string fullPath) => fullPath;
    }

    private static void Run(bool repeats)
    {
        var project = TemplateFactory.Blank();
        foreach (var t in project.Tracks) t.Measures = t.Measures.Take(20).ToList();
        if (repeats)
        {
            var m = project.Tracks[0].Measures;
            m[2].RepeatStart = true;
            m[3].RepeatEnd = true;
        }
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        project.Tracks.Add(track);
        var barSec = AudioClipTimelineRules.SecondsAtBar(project, 1, 0);
        var file = Path.Combine(Path.GetTempPath(), "tf-split-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(file, new byte[44]);
        var clip = new AudioClip { File = file, Name = "take", StartSec = 0, SourceLengthSec = 12 * barSec, FileLengthSec = 12 * barSec };
        track.AudioClips.Add(clip);
        var song = new SongDocument(() => null) { Project = project };
        song.MarkClean();
        var saved = MediaPathPolicy.FileSystem;
        MediaPathPolicy.FileSystem = new FixedFileSystem();
        MediaAccess.Evaluate(file, null);
        var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust())!;
        client.AttachFakeForTest();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), null) { Engine = client };
        var sent = new List<ClipSpec>();
        client.ClipsSentForTest = (slot, clips) => { if (slot != client.SlotOf(track)) return; sent.Clear(); sent.AddRange(clips); };
        var arrangement = new ArrangementView(project, song);
        var view = arrangement.Timeline;
        view.Snap = new SnapSettings { Enabled = false };
        // The same post-edit refresh the window attaches to every arrangement (MainWindowClipRefresh.Register): growth, lane compaction, engine sync, reload.
        view.ClipEdited = document => MainWindowClipRefresh.AfterEdit(arrangement, document, null, sessions);
        var window = new Window { Width = 1800, Height = 900, Content = arrangement };
        try
        {
            window.Show();
            window.UpdateLayout();
            view.UpdateLayout();
            sessions.SyncEngine(song, new PluginSettings());
            var whole = Assert.Single(sent);
            Assert.Equal(12 * barSec, whole.SourceLengthSec, 6);

            // Split at the end of performed time of bar 8 (after any repeat), through the clip-split command.
            var splitSec = SongExtent.Timing(project).Performed.Where(b => b.Bar == 8).First().StartMs / 1000.0;
            view.SelectedClip = new TimelineClipSelection(track, clip);
            view.LaneCursor = new TimelineLaneCursor(track, 0, splitSec, null);
            MainWindowClipProperties.Split(new MainWindowClipKeys.Target(song, view, new TimelineClipSelection(track, clip)), _ => { });
            Assert.Equal(2, track.AudioClips.Count);
            Assert.Equal(2, sent.Count);
            var right = track.AudioClips[1];

            // Drag the right piece three bars to the right with the mouse.
            var box = TimelineAudioClips.ClipBounds(view, track, right);
            var y = (box.Top + box.Bottom) / 2;
            var from = new Point((view.BarX(8) + view.BarX(9)) / 2, y);
            var to = new Point(from.X + (view.BarX(11) - view.BarX(8)), y);
            Point Win(Point p) => view.TranslatePoint(p, window)!.Value;
            window.MouseDown(Win(from), MouseButton.Left);
            for (var i = 1; i <= 10; i++) window.MouseMove(Win(from + (to - from) * (i / 10.0)), RawInputModifiers.LeftMouseButton);
            window.MouseUp(Win(to), MouseButton.Left);

            var left = track.AudioClips[0];
            Assert.True(right.StartSec - left.EndSec > barSec, "the pieces are not apart in the model");
            Assert.Equal(2, sent.Count);
            var a = sent.OrderBy(s => s.StartSec).ToArray();
            Assert.Equal(left.StartSec, a[0].StartSec, 6);
            Assert.Equal(left.LengthSec, a[0].SourceLengthSec, 6);
            Assert.Equal(0, a[0].OffsetSec, 6);
            Assert.Equal(right.StartSec, a[1].StartSec, 6);
            Assert.Equal(right.OffsetSec, a[1].OffsetSec, 6);
            Assert.Equal(right.SourceLengthSec, a[1].SourceLengthSec, 6);
            Assert.True(a[1].StartSec - (a[0].StartSec + a[0].SourceLengthSec) > barSec, "the engine regions leave no gap");
            Assert.Equal(left.EndSec, left.StartSec + a[0].SourceLengthSec, 6);

            // The track's M button reaches the engine at once as a level of 0 (the engine's mute gate), without a full sync.
            var commands = new List<EngineCommand>();
            client.SentForTest = command => commands.Add(command);
            track.Mute = true;
            AvaloniaEngineRouting.ApplyLevelsNow(project, client, 100);
            Assert.Contains(EngineCommand.SetTrackMix, commands);
            track.Mute = false;
            AvaloniaEngineRouting.ApplyLevelsNow(project, client, 100);

            // Muting every piece leaves the track nothing to play: the engine drops the track (and its clips) instead of keeping the old regions.
            var removed = false;
            client.SentForTest = command => removed |= command == EngineCommand.RemoveTrack;
            DocumentEdits.Run(song, _ => { foreach (var c in track.AudioClips) c.Muted = true; return true; });
            view.ClipEdited!(song);
            Assert.True(removed || client.SlotOf(track) < 0, "muted clips still play in the engine");
        }
        finally
        {
            window.Close();
            MediaPathPolicy.FileSystem = saved;
            client.Dispose();
            File.Delete(file);
            song.MarkClean();
        }
    }

    [AvaloniaFact]
    public void SplitAndDraggedApartPiecesReachTheEngineWithAGap() => Run(repeats: false);

    [AvaloniaFact]
    public void SplitAndDraggedApartPiecesReachTheEngineWithAGapOverRepeats() => Run(repeats: true);
}
