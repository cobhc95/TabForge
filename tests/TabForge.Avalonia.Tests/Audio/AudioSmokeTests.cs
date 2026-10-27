using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeltySynth;
using TabForge.Avalonia.Transport;
using TabForge.AudioEngine.Synth;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a no-device audio smoke test. The demo song is driven through <see cref="AvaloniaPlaybackSession"/> (play, metronome, count-in, seek, loop)
///     over an engine that compiles the timeline exactly as the real one does (<see cref="MidiTimelineBuilder"/>), and the compiled MIDI is rendered offline
///     by the engine's own General MIDI bank (MeltySynth over gm.dls) into float samples that are checked for level, silence, NaN, stereo and the transport features.
/// Does not own: the real-time scheduler (except the master-volume, session transport, loop-wrap and live-mute checks, which run the real PlaybackEngine into a capture sink), mixer chains, plug-ins or any device.
/// Tests: this class.
/// </summary>
public class AudioSmokeTests
{
    private const int Rate = 44100;
    private const double RenderSeconds = 12;
    private const double SilenceFloorRms = 0.005;
    private const double PeakCeiling = 0.98;
    private const double ZeroRunSlackSeconds = 0.3;
    private const double SeekToleranceMs = 5;

    private static bool Available => OperatingSystem.IsWindows() && System.IO.File.Exists(DlsToSoundFont.WindowsBankPath);

    // ---------- rendering ----------

    private sealed record Audio(float[] Left, float[] Right)
    {
        public int Frames => Left.Length;
        public double Rms(int from = 0, int to = int.MaxValue)
        {
            to = Math.Min(to, Frames); from = Math.Clamp(from, 0, to);
            if (to == from) return 0;
            double sum = 0;
            for (var i = from; i < to; i++) sum += (double)Left[i] * Left[i] + (double)Right[i] * Right[i];
            return Math.Sqrt(sum / (2.0 * (to - from)));
        }
        public double Peak() => Left.Concat(Right).Max(v => (double)Math.Abs(v));
        public static double ChannelRms(float[] c) { double s = 0; foreach (var v in c) s += (double)v * v; return Math.Sqrt(s / Math.Max(1, c.Length)); }
    }

    private static int Frame(double ms) => (int)Math.Round(ms * Rate / 1000.0);

    /// <summary>Renders the timeline's events from time 0 for <paramref name="seconds"/>; <paramref name="map"/> may change or drop an event (null drops it).</summary>
    private static Audio Render(ScoreTimeline timeline, double seconds, Func<ScoreEvent, (int Status, int D1, int D2)?>? map = null)
    {
        var settings = new SynthesizerSettings(Rate) { BlockSize = 64, MaximumPolyphony = 64, EnableReverbAndChorus = false };
        var synth = new Synthesizer(DlsToSoundFont.LoadWindowsBank(), settings) { MasterVolume = GmSynthTuning.EffectiveMasterVolume };
        var total = (int)(seconds * Rate);
        var left = new float[total]; var right = new float[total];
        var done = 0;
        foreach (var e in timeline.Events.Where(e => e.TimeMs >= 0).OrderBy(e => e.TimeMs))
        {
            var at = Math.Min(total, Frame(e.TimeMs));
            if (at > done) { synth.Render(left.AsSpan(done, at - done), right.AsSpan(done, at - done)); done = at; }
            if (at >= total) break;
            var m = map is null ? (e.Status, e.Data1, e.Data2) : map(e);
            if (m is not { } msg) continue;
            var command = msg.Status & 0xF0;
            if (command is >= 0x80 and <= 0xE0) synth.ProcessMidiMessage(msg.Status & 0x0F, command, msg.D1, msg.D2);
        }
        if (done < total) synth.Render(left.AsSpan(done), right.AsSpan(done));
        return new Audio(left, right);
    }

    private static (SongProject Project, AvaloniaPlaybackSession Session, TimelineEngine Engine) Open()
    {
        var project = DemoSongFactory.Create();
        var engine = new TimelineEngine();
        return (project, new AvaloniaPlaybackSession(engine, new InlineUiThread(), () => project), engine);
    }

    private static ScoreTimeline Compile(SongProject project, Action<PlaybackOptions>? tweak = null)
    {
        var o = new PlaybackOptions { LoopEndBar = project.Tracks.Max(t => t.Measures.Count) - 1 };
        tweak?.Invoke(o);
        return MidiTimelineBuilder.Build(project, o);
    }

    /// <summary>The longest stretch inside the window where no note of any track sounds (the song's own rests, e.g. a silent bar).</summary>
    private static double LongestNoteFreeGapMs(ScoreTimeline t, double windowMs)
    {
        var covered = 0.0; var longest = 0.0;
        foreach (var n in t.Notes.Where(n => n.OnsetMs < windowMs).OrderBy(n => n.OnsetMs))
        {
            if (covered > 0 && n.OnsetMs > covered) longest = Math.Max(longest, n.OnsetMs - covered);
            covered = Math.Max(covered, n.EndMs);
        }
        return longest;
    }

    private static double EightBarsMs(ScoreTimeline t) => t.Bars[Math.Min(7, t.Bars.Count - 1)].EndMs;

    // ---------- tests ----------

    [Fact]
    public void MixIsSaneNotSilentNoNaNStereoAndNoLongHoles()
    {
        if (!Available) return;
        var (_, session, engine) = Open();
        using var _s = session;
        session.PlayOrPause();
        Assert.Contains(engine.Timeline!.Events, e => e.IsSetup);
        var audio = Render(engine.Timeline!, RenderSeconds);

        Assert.True(audio.Rms() > SilenceFloorRms, $"rms {audio.Rms():0.0000} is above the floor");
        Assert.True(audio.Peak() < PeakCeiling, $"peak {audio.Peak():0.000} leaves headroom below 0 dBFS");
        Assert.All(audio.Left.Concat(audio.Right), v => Assert.True(float.IsFinite(v)));
        Assert.True(Audio.ChannelRms(audio.Left) > SilenceFloorRms / 2, "left is active");
        Assert.True(Audio.ChannelRms(audio.Right) > SilenceFloorRms / 2, "right is active");

        var first = Array.FindIndex(audio.Left, v => v != 0);
        Assert.True(first >= 0);
        var longest = 0; var run = 0;
        for (var i = first; i < audio.Frames; i++) { if (audio.Left[i] == 0 && audio.Right[i] == 0) { run++; longest = Math.Max(longest, run); } else run = 0; }
        var plannedRest = LongestNoteFreeGapMs(engine.Timeline!, RenderSeconds * 1000) / 1000.0;
        Assert.True(longest <= (plannedRest + ZeroRunSlackSeconds) * Rate, $"longest exact-zero run {longest / (double)Rate:0.00}s exceeds the score's own longest rest {plannedRest:0.00}s");
    }

    [Fact]
    public void EachTrackSoloedIsAudibleAndMutingATrackLowersTheLevel()
    {
        if (!Available) return;
        var trackCount = DemoSongFactory.Create().Tracks.Count;
        var full = Compile(DemoSongFactory.Create());
        var window = EightBarsMs(full) / 1000.0;
        var fullRms = Render(full, window).Rms();
        for (var i = 0; i < trackCount; i++)
        {
            var project = DemoSongFactory.Create();
            project.Tracks[i].Solo = true;
            var soloRms = Render(Compile(project), window).Rms();
            Assert.True(soloRms > SilenceFloorRms / 2, $"track {i} ({project.Tracks[i].Name}) soloed is audible (rms {soloRms:0.0000})");
        }
        var muted = DemoSongFactory.Create();
        muted.Tracks[0].Mute = true;
        var mutedRms = Render(Compile(muted), window).Rms();
        Assert.True(mutedRms < fullRms, $"muting a track lowers the level ({mutedRms:0.0000} < {fullRms:0.0000})");
    }

    [Fact]
    public void MetronomeAddsEnergyOnTheBeatsAndCountInAddsLeadingTime()
    {
        if (!Available) return;
        var (project, session, engine) = Open();
        using var _s = session;
        session.PlayOrPause();
        var off = engine.Timeline!;
        session.SetMetronome(true);
        var on = engine.Timeline!;
        Assert.Contains(on.Events, e => e.IsMetronome && !e.IsCountInClick && e.IsNoteOn);
        Assert.DoesNotContain(off.Events, e => e.IsMetronome && !e.IsCountInClick);

        var window = EightBarsMs(on) / 1000.0;
        var quiet = Render(off, window);
        var loud = Render(on, window);
        var diff = new Audio(loud.Left.Zip(quiet.Left, (a, b) => a - b).ToArray(), loud.Right.Zip(quiet.Right, (a, b) => a - b).ToArray());
        var beats = on.Events.Where(e => e.IsMetronome && e.IsNoteOn && e.TimeMs < window * 1000 - 100).Select(e => Frame(e.TimeMs)).ToArray();
        var clickLength = Rate / 10;
        double atBeats = 0;
        foreach (var b in beats) atBeats += diff.Rms(b, b + clickLength);
        Assert.True(beats.Length > 8);
        Assert.True(atBeats / beats.Length > SilenceFloorRms / 4, $"the click is audible at the beats (rms {atBeats / beats.Length:0.00000}); a silent click means the bank has no metronome notes");

        // Count-in: it moves the song start by one bar and the leading time holds clicks only.
        session.Stop();
        session.SetCountIn(true);
        session.PlayOrPause();
        var withCountIn = engine.Timeline!;
        var plain = Compile(project);
        var oneBarMs = plain.Bars[0].EndMs - plain.Bars[0].StartMs;
        Assert.InRange(withCountIn.CountInMs, oneBarMs - SeekToleranceMs, oneBarMs + SeekToleranceMs);
        Assert.InRange(withCountIn.PlayFromMs - plain.PlayFromMs, oneBarMs - SeekToleranceMs, oneBarMs + SeekToleranceMs);
        Assert.DoesNotContain(withCountIn.Events.Where(e => e.TimeMs < withCountIn.CountInMs - 1), e => e.IsNoteOn && !e.IsMetronome);
        var lead = Render(withCountIn, withCountIn.CountInMs / 1000.0 + 0.3);
        Assert.True(lead.Rms(0, Frame(withCountIn.CountInMs)) > SilenceFloorRms / 4, "the count-in clicks are audible");
    }

    [Fact]
    public void SeekToABarStartsWithinAFewMillisecondsOfThatBarsTime()
    {
        if (!Available) return;
        var (project, session, engine) = Open();
        using var _s = session;
        var full = Compile(project);
        session.PlayOrPause();
        const int bar = 6;
        session.Seek(bar, 0);
        var seeked = engine.Timeline!;
        Assert.Equal((bar, 0), (engine.Options!.StartBar, engine.Options.StartCell));
        Assert.InRange(seeked.PlayFromMs, -SeekToleranceMs, SeekToleranceMs);
        // Bars run in performance order (repeats expand), so the bar to find is the first one whose source bar is the target.
        var fullBar = full.Bars.First(b => b.Bar == bar);
        Assert.Equal(bar, seeked.Bars[0].Bar);
        Assert.InRange(seeked.Bars[0].EndMs - seeked.Bars[0].StartMs, fullBar.EndMs - fullBar.StartMs - SeekToleranceMs, fullBar.EndMs - fullBar.StartMs + SeekToleranceMs);
        var firstOnset = full.Notes.Where(n => n.OnsetMs >= fullBar.StartMs - 1e-6).Min(n => n.OnsetMs) - fullBar.StartMs;
        Assert.InRange(seeked.Notes.Min(n => n.OnsetMs), firstOnset - SeekToleranceMs, firstOnset + SeekToleranceMs);
        Assert.True(Render(seeked, 4).Rms() > SilenceFloorRms, "audio plays right after the seek");
    }

    [Fact]
    public void LoopPassesTheWholeSongRangeToTheEngine()
    {
        var (project, session, engine) = Open();
        using var _s = session;
        session.PlayOrPause();
        session.SetLoop(true);
        Assert.True(engine.Options!.Loop);
        Assert.Equal(0, engine.Options.LoopStartBar);
        Assert.Equal(project.Tracks.Max(t => t.Measures.Count) - 1, engine.Options.LoopEndBar);
        Assert.True(session.Loop);
    }

    [Fact]
    public void MasterVolumeKnobScalesTheChannelVolumeAndTheRenderedLevel()
    {
        if (!Available) return;
        var project = DemoSongFactory.Create();
        var full = SendsFromRealEngine(project, 100);
        var half = SendsFromRealEngine(project, 50);
        Assert.NotEmpty(full);
        foreach (var (channel, value) in full)
            Assert.InRange(half[channel], value / 2 - 1, value / 2 + 1);

        var timeline = Compile(project);
        var window = EightBarsMs(timeline) / 1000.0;
        double RenderWith(Dictionary<int, int> volume) => Render(timeline, window, e =>
            (e.Status & 0xF0) == 0xB0 && e.Data1 == 7 && volume.TryGetValue(e.Status & 0x0F, out var v) ? (e.Status, 7, v) : (e.Status, e.Data1, e.Data2)).Rms();
        var loud = RenderWith(full);
        var soft = RenderWith(half);
        Assert.True(soft < loud * 0.8, $"half master volume renders quieter ({soft:0.0000} vs {loud:0.0000})");
    }

    /// <summary>Runs the real PlaybackEngine into a capture sink for a moment and returns the last channel volume (CC7) it sent per channel.</summary>
    private static Dictionary<int, int> SendsFromRealEngine(SongProject project, int masterPercent)
    {
        var sink = new CaptureOutput();
        using var engine = new PlaybackEngine(sink);
        engine.SetMasterVolume(project, masterPercent);
        engine.Start(project, new PlaybackOptions { LoopEndBar = 3 }, _ => { }, () => { });
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && sink.Count() < project.Tracks.Count) Thread.Sleep(20);
        engine.Stop();
        return sink.Snapshot();
    }

    /// <summary>The real PlaybackEngine behind the session: play, pause, resume, seek, loop and stop reach it and its state comes back.</summary>
    [Fact]
    public void RealPlaybackEngineThroughSessionStartsPausesResumesSeeksLoopsAndStops()
    {
        var project = DemoSongFactory.Create();
        using var engine = new PlaybackEngine(new CaptureOutput());
        using var session = new AvaloniaPlaybackSession(engine, new InlineUiThread(), () => project);
        session.PlayOrPause();
        Assert.True(WaitFor(() => session.IsPlaying), "play reaches the real engine");
        session.PlayOrPause();
        Assert.True(WaitFor(() => session.IsPaused), "pause reaches the real engine");
        session.PlayOrPause();
        Assert.True(WaitFor(() => !session.IsPaused), "resume reaches the real engine");
        session.Seek(2, 0);
        session.SetLoop(true);
        Assert.True(session.Loop);
        session.Stop();
        Assert.True(WaitFor(() => !session.IsPlaying), "stop reaches the real engine");
    }

    /// <summary>The real engine's scheduler moves the playhead from the second bar back to the loop start when the loop end is reached.</summary>
    [Fact]
    public void RealEngineLoopWrapsThePlayheadBackToTheLoopStart()
    {
        const double WrapWaitSeconds = 15;
        var project = DemoSongFactory.Create();
        using var engine = new PlaybackEngine(new CaptureOutput());
        engine.Start(project, new PlaybackOptions { Loop = true, LoopEndBar = 2 }, _ => { }, () => { });
        Assert.True(WaitFor(() => engine.Playhead().Bar == 1, WrapWaitSeconds), "playhead reaches the second bar before the loop end");
        Assert.True(WaitFor(() => engine.Playhead().Bar == 0, WrapWaitSeconds), "playhead wraps back to the loop start");
        engine.Stop();
    }

    /// <summary>Muting a track while the real engine plays sends All Notes Off on its channel at once, without a restart.</summary>
    [Fact]
    public void LiveMuteSilencesTheTrackOnTheRunningRealEngine()
    {
        var project = DemoSongFactory.Create();
        var sink = new CaptureOutput();
        using var engine = new PlaybackEngine(sink);
        engine.Start(project, new PlaybackOptions { LoopEndBar = 3 }, _ => { }, () => { });
        Assert.True(WaitFor(() => sink.Count() > 0), "engine starts and sends channel volumes");
        engine.SetMuteSolo(project);
        project.Tracks[0].Mute = true;
        engine.SetMuteSolo(project);
        Assert.True(WaitFor(() => sink.AllNotesOffChannels().Length > 0), "muting a track sends All Notes Off on its channel");
        engine.Stop();
    }

    /// <summary>Soloing a track while the real engine plays sends All Notes Off on the other tracks' channels at once, without a restart.</summary>
    [Fact]
    public void LiveSoloSilencesTheOtherTracksOnTheRunningRealEngine()
    {
        var project = DemoSongFactory.Create();
        var sink = new CaptureOutput();
        using var engine = new PlaybackEngine(sink);
        engine.Start(project, new PlaybackOptions { LoopEndBar = 3 }, _ => { }, () => { });
        Assert.True(WaitFor(() => sink.Count() > 0), "engine starts and sends channel volumes");
        engine.SetMuteSolo(project);
        project.Tracks[0].Solo = true;
        engine.SetMuteSolo(project);
        Assert.True(WaitFor(() => sink.AllNotesOffChannels().Length > 0), "soloing a track sends All Notes Off on the other channels");
        engine.Stop();
    }

    private static bool WaitFor(Func<bool> condition, double seconds = 3)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline) Thread.Sleep(20);
        return condition();
    }

    // ---------- test doubles ----------

    private sealed class CaptureOutput : IMidiOutput
    {
        private readonly Dictionary<int, int> _volumes = new();
        private readonly HashSet<int> _allNotesOffChannels = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) == 0xB0 && data1 == 7) lock (_volumes) _volumes[status & 0x0F] = data2;
            if ((status & 0xF0) == 0xB0 && data1 == 123) lock (_allNotesOffChannels) _allNotesOffChannels.Add(status & 0x0F);
        }
        public int Count() { lock (_volumes) return _volumes.Count; }
        public int[] AllNotesOffChannels() { lock (_allNotesOffChannels) return _allNotesOffChannels.ToArray(); }
        public Dictionary<int, int> Snapshot() { lock (_volumes) return new Dictionary<int, int>(_volumes); }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    /// <summary>An engine that holds no clock: each start, seek, option or loop change compiles the timeline the way the real engine does.</summary>
    private sealed class TimelineEngine : IPlaybackEngine
    {
        public PlaybackOptions? Options { get; private set; }
        public ScoreTimeline? Timeline { get; private set; }
        private readonly PlaybackOptions _pending = new();
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public event Action<ScoreTimeline>? TimelineChanged;
        public event Action<ScoreTimeline>? TimelineRevised { add { } remove { } }

        private void Recompile(SongProject project)
        {
            Timeline = MidiTimelineBuilder.Build(project, Options!);
            TimelineChanged?.Invoke(Timeline);
        }

        public void Start(SongProject project, PlaybackOptions options, Action<PlaybackPosition> onPosition, Action onFinished, bool startPaused = false)
        {
            Options = options.Clone();
            Options.CountIn = _pending.CountIn;
            Options.Metronome |= _pending.Metronome;
            IsPlaying = true;
            Recompile(project);
        }
        public void Stop() { IsPlaying = false; IsPaused = false; }
        public void Pause() => IsPaused = true;
        public void Resume() => IsPaused = false;
        public void Seek(SongProject project, int bar, int cell) { Options!.StartBar = bar; Options.StartCell = cell; Recompile(project); }
        public void SetLoop(bool loop) { if (Options is not null) Options.Loop = loop; }
        public void UpdateOptions(SongProject project, Action<PlaybackOptions> mutate)
        {
            mutate(_pending);
            if (Options is null) return;
            mutate(Options);
            Recompile(project);
        }
        public PlaybackPosition Playhead() => new();
        public PlaybackPosition AudiblePlayhead(PlaybackPosition frame) => frame;
        public void Dispose() { }
    }

    private sealed class InlineUiThread : IUiThread
    {
        public bool CheckAccess() => true;
        public bool IsShuttingDown => false;
        public void Post(Action work, UiPriority priority = UiPriority.Normal) => work();
        public Task InvokeAsync(Action work, UiPriority priority = UiPriority.Normal) { work(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> work, UiPriority priority = UiPriority.Normal) => Task.FromResult(work());
        public Task YieldAsync(UiPriority priority) => Task.CompletedTask;
        public void Invoke(Action work, UiPriority priority = UiPriority.Send) => work();
        public T Invoke<T>(Func<T> work, UiPriority priority = UiPriority.Send) => work();
        public IUiTimer CreateTimer(TimeSpan interval, UiPriority priority = UiPriority.Background) => new IdleTimer { Interval = interval };
    }

    private sealed class IdleTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
    }
}

/// <summary>The plug-in chain on/off command as the engine client sends it: the wire message keeps slot, chain index and the on/off flag, so the engine process applies the toggle the client asked for.</summary>
public class PluginChainToggleMessageTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(3, 2, false)]
    public void PluginChainOnOffSurvivesTheEngineWireRoundTrip(int slot, int index, bool enabled)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            new TabForge.Audio.Contracts.SetPluginBypassMessage(slot, index, enabled).Write(writer);
        stream.Position = 0;
        using var reader = new System.IO.BinaryReader(stream);
        var read = TabForge.Audio.Contracts.SetPluginBypassMessage.Read(reader);
        Assert.Equal(new TabForge.Audio.Contracts.SetPluginBypassMessage(slot, index, enabled), read);
        Assert.Equal(stream.Length, stream.Position);
    }
}
