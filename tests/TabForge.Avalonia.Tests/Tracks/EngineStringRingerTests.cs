using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the headless checks of the track-properties string sound: the adapter rings fire-and-forget through the
//   track's live device, channel and program, clamp, silence only the owned note and never panic.
// Does not own: the adapter (EngineStringRinger), the window or its vibrate and repeat timer.
// Tests: this class.
public class EngineStringRingerTests
{
    private const int Device = -1;
    private const int Channel = 0;
    private const int Program = 29;

    private sealed class FakePreviewEngine : IPlaybackEngine
    {
        public List<(int Device, int Channel, int Program, int Note, int Ms)> Previews { get; } = new();
        public List<(int Device, int Status, int Data1, int Data2)> Live { get; } = new();
        public int ProgramPreviews { get; private set; }
        public int ProgramEnds { get; private set; }
        public int Panics { get; private set; }

        // Never completes: Ring must not wait on it.
        private readonly TaskCompletionSource<object?> _hang = new();
        public bool IsPlaying => false;
        public bool IsPaused => false;
        public event Action<ScoreTimeline>? TimelineChanged;
        public event Action<ScoreTimeline>? TimelineRevised;
        public void RaiseTimelineChanged(ScoreTimeline timeline) => TimelineChanged?.Invoke(timeline);
        public void RaiseTimelineRevised(ScoreTimeline timeline) => TimelineRevised?.Invoke(timeline);

        public Task PreviewNoteAsync(int deviceId, int channel, int program, int note, int ms = 350)
        {
            Previews.Add((deviceId, channel, program, note, ms));
            return _hang.Task;
        }

        public void SendLive(int deviceId, int status, int data1, int data2) => Live.Add((deviceId, status, data1, data2));
        public void PanicAsync() => Panics++;
        public void BeginProgramPreview(SongProject project, int deviceId, int channel, int program) => ProgramPreviews++;
        public void EndProgramPreview(SongProject project, int deviceId, int channel, bool resend = true) => ProgramEnds++;

        public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false) { }
        public void Stop() { }
        public void Pause() { }
        public void Resume() { }
        public void Seek(SongProject project, int bar, int cell) { }
        public void SetLoop(bool loop) { }
        public PlaybackPosition Playhead() => new();
        public PlaybackPosition AudiblePlayhead(PlaybackPosition frame) => frame;
        public void Dispose() { }
    }

    private static (AvaloniaPlaybackSessions Sessions, FakePreviewEngine Engine, SongDocument Song, TrackModel Track) Rig(
        int channel = Channel, int program = Program, int device = Device)
    {
        var engine = new FakePreviewEngine();
        var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => engine);
        var song = new SongDocument(() => null);
        var track = new TrackModel
        {
            Name = "Strings", Kind = TrackKind.Guitar, InstrumentName = "Electric Guitar",
            MidiChannel = channel, MidiProgram = program, MidiOutputDeviceId = device,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
        };
        song.Project.Tracks.Add(track);
        return (sessions, engine, song, track);
    }

    [Fact]
    public void ForReturnsNullWithoutPlaybackOrSong()
    {
        var track = new TrackModel { Name = "T", Kind = TrackKind.Guitar, StringTunings = new List<int> { 64 } };
        Assert.Null(EngineStringRinger.For(null, new SongDocument(() => null), track));

        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new FakePreviewEngine());
        Assert.Null(EngineStringRinger.For(sessions, null, track));
    }

    [Fact]
    public void RingIsFireAndForgetThroughTheTracksLiveVoice()
    {
        using var rig = RigDisposable();
        var ringer = EngineStringRinger.For(rig.Sessions, rig.Song, rig.Track);
        Assert.NotNull(ringer);

        var elapsed = Stopwatch.StartNew();
        ringer!.Ring(64);
        elapsed.Stop();

        var preview = Assert.Single(rig.Engine.Previews);
        Assert.Equal((Device, Channel, Program, 64, (int)StringVibration.RingMs), preview);
        Assert.Empty(rig.Engine.Live);
        Assert.Equal(0, rig.Engine.Panics);
        Assert.True(elapsed.Elapsed < TimeSpan.FromMilliseconds(100), $"Ring took {elapsed.Elapsed.TotalMilliseconds:F1} ms with a hung engine.");
    }

    [Fact]
    public void RingClampsThePitch()
    {
        using var rig = RigDisposable();
        var ringer = EngineStringRinger.For(rig.Sessions, rig.Song, rig.Track)!;

        ringer!.Ring(-3);
        ringer.Ring(999);

        Assert.Equal(2, rig.Engine.Previews.Count);
        Assert.Equal(0, rig.Engine.Previews[0].Note);
        Assert.Equal(127, rig.Engine.Previews[1].Note);
    }

    [Fact]
    public void RingOfANewPitchSilencesTheOwnedOneFirstAndNeverPanics()
    {
        using var rig = RigDisposable();
        var ringer = EngineStringRinger.For(rig.Sessions, rig.Song, rig.Track)!;

        ringer!.Ring(62);
        ringer.Ring(57);

        Assert.Equal(2, rig.Engine.Previews.Count);
        Assert.Equal(57, rig.Engine.Previews[1].Note);
        var off = Assert.Single(rig.Engine.Live);
        Assert.Equal((Device, 0x80 | Channel, 62, 0), off);
        Assert.Equal(0, rig.Engine.Panics);
        Assert.Equal(0, rig.Engine.ProgramPreviews + rig.Engine.ProgramEnds);
    }

    [Fact]
    public void SamePitchReRingDoesNotCutTheNote()
    {
        using var rig = RigDisposable();
        var ringer = EngineStringRinger.For(rig.Sessions, rig.Song, rig.Track)!;

        ringer!.Ring(62);
        ringer.Ring(62);

        Assert.Equal(2, rig.Engine.Previews.Count);
        Assert.Empty(rig.Engine.Live);
    }

    [Fact]
    public void SilenceStopsOnlyTheOwnedNoteAndIsIdempotent()
    {
        using var rig = RigDisposable();
        var ringer = EngineStringRinger.For(rig.Sessions, rig.Song, rig.Track)!;

        ringer!.Silence();
        Assert.Empty(rig.Engine.Live);

        ringer.Ring(59);
        ringer.Silence();
        ringer.Silence();

        var off = Assert.Single(rig.Engine.Live);
        Assert.Equal((Device, 0x80 | Channel, 59, 0), off);
        Assert.Equal(0, rig.Engine.Panics);
    }

    [Fact]
    public void RingReadsLiveChannelAndProgramWhileSilenceUsesTheOwnedOnes()
    {
        using var rig = RigDisposable();
        var ringer = EngineStringRinger.For(rig.Sessions, rig.Song, rig.Track)!;

        ringer!.Ring(60);
        rig.Track.MidiChannel = 1;
        rig.Track.MidiProgram = 5;
        ringer.Ring(61);

        Assert.Equal(2, rig.Engine.Previews.Count);
        Assert.Equal((Device, 1, 5, 61, (int)StringVibration.RingMs), rig.Engine.Previews[1]);
        var off = Assert.Single(rig.Engine.Live);
        Assert.Equal((Device, 0x80 | Channel, 60, 0), off);
        Assert.Equal(0, rig.Engine.Panics);
    }

    private sealed class RigBundle : IDisposable
    {
        public AvaloniaPlaybackSessions Sessions { get; }
        public FakePreviewEngine Engine { get; }
        public SongDocument Song { get; }
        public TrackModel Track { get; }
        public RigBundle(AvaloniaPlaybackSessions sessions, FakePreviewEngine engine, SongDocument song, TrackModel track)
            => (Sessions, Engine, Song, Track) = (sessions, engine, song, track);
        public void Dispose() => Sessions.Dispose();
    }

    private static RigBundle RigDisposable(int channel = Channel, int program = Program, int device = Device)
    {
        var (sessions, engine, song, track) = Rig(channel, program, device);
        return new RigBundle(sessions, engine, song, track);
    }
}
