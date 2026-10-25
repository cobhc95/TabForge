using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Features.Recording.Model;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the recording-over-a-repeat path at command level (MainWindowTransportRecord like RecordingRealPathTests): a recording through
//   score repeats lands as ONE ordinary clip (audio and MIDI; the repeat view only folds its drawing), the MIDI compile playing it straight
//   in performed time, the live second play stacking under the first, and the transport loop keeping its own T1 lane-per-lap behaviour.
// Does not own: the fold and the folded drawing (RepeatViewTests), the pure segment math (RepeatPassesTests) or the loop-lane stacks (LoopLanesStage*Tests).
// Tests: this class.
public sealed class RepeatTakeLandingTests
{
    private const int NoteOn = 0x90, NoteOff = 0x80;

    private static SongDocument RepeatSong(out TrackModel score)
    {
        // Bars 1-2 repeat x2 on the first track: performed 0,1,2,1,2,3.
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        song.MarkClean();
        score = song.Project.Tracks[0];
        score.Measures[1].RepeatStart = true;
        score.Measures[2].RepeatEnd = true;
        return song;
    }

    private static double At(SongProject project, int bar, double fraction) =>
        AudioClipTimelineRules.SecondsAtBar(project, bar, fraction);

    private static void AssertOrdinary(AudioClip clip, int lane)
    {
        Assert.Null(clip.TakeGroupId);
        Assert.NotEqual(TakeGroupKind.Repeat, clip.TakeGroupKind);
        Assert.Equal(0, clip.TakeGroupPass);
        Assert.Equal(lane, clip.Lane);
        Assert.False(clip.Muted);
    }

    [AvaloniaFact]
    public void A_ARecordingThroughTheRepeatLandsOneOrdinaryClip()
    {
        // (a) Replaces the T2 test (pre + 2 stamped parts + post, group, names, status): repeat view RV1 - a recording over a repeat is ONE
        // ordinary clip in performed time, no group and no parts; the repeat only shows in how it is drawn.
        var song = RepeatSong(out _);
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        var start = At(song.Project, 0, 0);
        var end = At(song.Project, 3, 1);

        var status = MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", start, end - start);

        var clip = Assert.Single(track.AudioClips);
        AssertOrdinary(clip, 0);
        Assert.Equal("take", clip.Name);
        Assert.Equal(start, clip.StartSec, 6);
        Assert.Equal(end, clip.EndSec, 6);
        Assert.Equal(0, clip.OffsetSec, 6);
        Assert.Equal($"Recorded {end - start:0.0} s on {track.Name}", status);
        Assert.True(ClipLanes.Audible(track, clip));
    }

    [AvaloniaFact]
    public void B_TransportLoopKeepsLandingSeparateOrdinaryTakesPerLap()
    {
        // (b) T1 is untouched by repeats: a looped recording still lands one ordinary take per lap, ungrouped.
        var song = RepeatSong(out _);
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        song.Project.Tracks.Add(track);
        var loopStart = At(song.Project, 0, 0);
        var loopEnd = At(song.Project, 2, 0);

        MainWindowTransportRecord.LandTake(new[] { song }, track, "take.wav", loopStart, (loopEnd - loopStart) + 1, loop: (loopStart, loopEnd));

        Assert.Equal(2, track.AudioClips.Count);
        Assert.All(track.AudioClips, clip => Assert.Null(clip.TakeGroupId));
    }

    [AvaloniaFact]
    public void C_MidiNotesLandInOneClipAndPlayStraightInPerformedTime()
    {
        var song = RepeatSong(out _);
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Keys);
        track.AudioInput = AudioInputs.Midi;
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        var performed = SongExtent.Timing(song.Project).Performed;
        var start = At(song.Project, 0, 0);
        var end = At(song.Project, 3, 1);
        var occurrence1 = RepeatPasses.PerformedStartForPass(performed, 1, 1);
        var occurrence2 = RepeatPasses.PerformedStartForPass(performed, 1, 2);

        var device = new FakeMidiDevice();
        using var recording = new TransportRecording(new AudioEngineClient(new TabForge.Plugins.PluginTrust()), new MidiInputHub(device).CreateClient());
        var stamp0 = Stopwatch.GetTimestamp();
        recording.Toggle(song.Project.Tracks, "unused", startSec: start,
            songSecAt: stamp => start + (stamp - stamp0) / (double)Stopwatch.Frequency);
        // One note per span: pre, first play, second play, post.
        PlayNote(device, stamp0, start, start + 0.1, 60);
        PlayNote(device, stamp0, start, occurrence1 + 0.1, 62);
        PlayNote(device, stamp0, start, occurrence2 + 0.1, 64);
        PlayNote(device, stamp0, start, end - 0.4, 65);
        recording.Toggle(song.Project.Tracks, "unused",
            songSecAt: stamp => start + (stamp - stamp0) / (double)Stopwatch.Frequency);
        MainWindowTransportRecord.LandMidiTakes(recording, song);

        // Repeat view: one ordinary MIDI clip holds every note at its performed time (no part dealing).
        var clip = Assert.Single(track.AudioClips);
        AssertOrdinary(clip, 0);
        Assert.True(clip.IsMidi);
        Assert.Equal(new[] { 60, 62, 64, 65 }, clip.Notes!.OrderBy(note => note.StartSec).Select(note => note.Pitch).ToArray());
        Assert.Equal(occurrence1 + 0.1, clip.StartSec + clip.Notes!.Single(note => note.Pitch == 62).StartSec, 3);
        Assert.Equal(occurrence2 + 0.1, clip.StartSec + clip.Notes!.Single(note => note.Pitch == 64).StartSec, 3);

        // Playback is plain: the clip plays straight through in performed time, so the first-play note sounds the first time through and the second-play note the second.
        var timeline = MidiTimelineBuilder.Build(song.Project, new PlaybackOptions { RespectMuteSolo = false });
        var trackIndex = song.Project.Tracks.IndexOf(track);
        var onsets = timeline.Events
            .Where(e => (e.Status & 0xF0) == NoteOn && e.Data2 > 0 && e.TrackIndex == trackIndex && (e.Data1 == 62 || e.Data1 == 64))
            .OrderBy(e => e.TimeMs).ToList();
        var first = Assert.Single(onsets, e => e.Data1 == 62);
        var second = Assert.Single(onsets, e => e.Data1 == 64);
        Assert.Equal((occurrence1 + 0.1) * 1000, first.TimeMs, 1);
        Assert.Equal((occurrence2 + 0.1) * 1000, second.TimeMs, 1);
        Assert.Equal((occurrence2 - occurrence1) * 1000, second.TimeMs - first.TimeMs, 1);
    }

    [Fact]
    public void F_LiveRepeatBandsStackBothPlaysInsideTheTakeLane()
    {
        // (f) While recording through the repeat, the second play draws as part 2 under part 1 inside the same lane (the repeat view fold, RV8).
        var performed = new[]
        {
            new PerformedBar(0, 0, 1000), new PerformedBar(1, 1000, 2000), new PerformedBar(2, 2000, 3000),
            new PerformedBar(1, 3000, 4000), new PerformedBar(2, 4000, 5000), new PerformedBar(3, 5000, 6000),
        };

        var bands = LiveTakeFold.RepeatBands(0, 4.5, performed).ToList();

        Assert.Equal(3, bands.Count);
        Assert.All(bands, band => Assert.Equal(0, band.Row));
        Assert.Equal(new[] { 0, 1, 2 }, bands.Select(band => band.Pass).ToArray());
        Assert.Equal(new[] { 1, 2, 2 }, bands.Select(band => band.PartCount).ToArray());
        Assert.Equal((0.0, 1.0), (bands[0].StartSec, bands[0].EndSec));
        Assert.Equal((1.0, 3.0), (bands[1].StartSec, bands[1].EndSec));
        Assert.Equal((3.0, 4.5), (bands[2].StartSec, bands[2].EndSec));
    }

    [AvaloniaFact]
    public void G_TheLiveOverlayKeepsOneLaneWhileTheSecondPlayGrowsUnderTheFirst()
    {
        var song = RepeatSong(out _);
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.RecordArm = true;
        song.Project.Tracks.Add(track);
        var performed = SongExtent.Timing(song.Project).Performed;
        var row = song.Project.Tracks.IndexOf(track);
        var view = new TimelineView(song.Project);
        var takes = new LiveTakes { RepeatPerformed = performed };
        view.UseLiveTakes(takes);
        takes.LaneCreated = _ => view.Reload(song.Project);
        var start = At(song.Project, 0, 0);
        var occurrence2 = RepeatPasses.PerformedStartForPass(performed, 1, 2);

        Assert.True(takes.Frame(track, start, 0.2f, midi: false));
        Assert.True(takes.Frame(track, occurrence2 + 0.5, 0.2f, midi: false));
        Assert.Equal(ClipLanes.AudioLaneHeight, view.RowHeightOf(row), 6);

        var drawn = new TextRecorder();
        view.LiveTakeLayer.Draw(drawn, light: false, view.BarsWidth + 100);
        // Both plays draw inside the one lane: same x span, stacked rows, numbered.
        var partBoxes = drawn.Rects.Where(rect => rect.Fill).ToList();
        Assert.True(partBoxes.Count >= 3, $"expected pre + 2 parts, saw {partBoxes.Count}");
        var parts = partBoxes.Where(rect => rect.Height < ClipLanes.AudioLaneHeight / 2).ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(parts[0].X, parts[1].X, 6);
        Assert.NotEqual(parts[0].Y, parts[1].Y);
        Assert.Contains("2", drawn.Texts);
    }

    private static void PlayNote(FakeMidiDevice device, long stamp0, double songStart, double songSec, int pitch)
    {
        device.Play(NoteOn, pitch, 100, StampFor(stamp0, songStart, songSec));
        device.Play(NoteOff, pitch, 0, StampFor(stamp0, songStart, songSec + 0.15));
    }

    private static long StampFor(long stamp0, double songStart, double songSec) =>
        stamp0 + (long)((songSec - songStart) * Stopwatch.Frequency);

    private sealed class FakeMidiDevice : IMidiInputSource
    {
        private bool _open;

        public event Action<int, int, int, long>? Message;
        public bool IsOpen => _open;

        public int Open()
        {
            _open = true;
            return 1;
        }

        public void Close() => _open = false;

        public void Play(int status, int pitch, int velocity, long stamp) => Message?.Invoke(status, pitch, velocity, stamp);

        public void Dispose() => Close();
    }

    /// <summary>Records drawn rectangles and laid-out texts; filled rectangles are boxes, stroked-only ones outlines.</summary>
    internal sealed class TextRecorder : IDrawContext
    {
        public List<(double X, double Y, double Width, double Height, bool Fill, bool Stroke)> Rects { get; } = new();
        public List<string> Texts { get; } = new();
        public double PixelsPerDip => 1;
        public IPathFactory Paths { get; } = new AvaloniaPathsAndText();

        public UiText Text(string text, string fontFamily, double size, UiColor colour, double pixelsPerDip, double maxWidth = 0,
            UiTextAlign align = UiTextAlign.Left, int weight = 400, bool italic = false, UiStroke? outline = null, bool idealLayout = false)
        {
            Texts.Add(text);
            return new UiText(text.Length * 6, 12, 10, null!);
        }

        public void DrawText(in UiText text, double x, double y) { }
        public void Rect(UiColor? fill, UiStroke? stroke, double x, double y, double width, double height, double radius = 0) =>
            Rects.Add((x, y, width, height, fill.HasValue, stroke.HasValue));
        public void Rect(IUiPaint? fill, IUiPen? pen, double x, double y, double width, double height, double radiusX = 0, double radiusY = 0) =>
            Rects.Add((x, y, width, height, fill is not null, pen is not null));
        public void Line(UiStroke stroke, double x1, double y1, double x2, double y2) { }
        public void Line(IUiPen pen, double x1, double y1, double x2, double y2) { }
        public void Ellipse(UiColor? fill, UiStroke? stroke, double cx, double cy, double rx, double ry) { }
        public void Ellipse(IUiPaint? fill, IUiPen? pen, double cx, double cy, double rx, double ry) { }
        public void Path(UiColor? fill, UiStroke? stroke, IUiPath path) { }
        public void Path(IUiPaint? fill, IUiPen? pen, IUiPath path) { }
        public void Image(IUiImage image, double x, double y, double width, double height) { }
        public void PushClip(double x, double y, double width, double height) { }
        public void PushClip(IUiPath clip) { }
        public void PushTranslate(double dx, double dy) { }
        public void PushScale(double sx, double sy, double centreX = 0, double centreY = 0) { }
        public void PushRotate(double degrees, double centreX, double centreY) { }
        public void PushTransform(IUiTransform transform) { }
        public void PushOpacity(double opacity) { }
        public void Drawing(IUiDrawing drawing) { }
        public void Pop() { }
    }
}
