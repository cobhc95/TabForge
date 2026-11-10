using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the scheduler's dispatch position across a seek-rebuild timeline swap: the rebuilt timeline swaps in while no seek is pending any more,
///     and every note-on after the resume point is still sent (none skipped by a position left over from the old list, no read past its end).
/// Does not own: the rebuild itself (PlaybackSeekRebuild) or the session (AvaloniaPlaybackSessionTests).
/// Tests: this class.
/// </summary>
/// <remarks>[Collection(TimingCollection.Name)] runs this alone so scheduler contention cannot starve its dispatches.</remarks>
/// <seealso cref="TimingCollection"/>
[Collection(TimingCollection.Name)]   // pumps a wall-clock engine: run alone so scheduler contention cannot starve the dispatches
public class PlaybackSeekSwapTests
{
    private const int PlayBeforeSeekMs = 1500;
    private const int ListenMs = 2500;
    private const double CheckedSpanMs = 2000;
    private const int SeekBar = 126;
    private const int SwapWaitMs = 5000;

    private sealed class NoteOnRecorder : IMidiOutput
    {
        private int _noteOns;
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { if ((status & 0xF0) == 0x90 && data2 > 0) Interlocked.Increment(ref _noteOns); }
        public int NoteOns => Volatile.Read(ref _noteOns);
        public void Reset() => Interlocked.Exchange(ref _noteOns, 0);
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    [Fact]
    public void ARebuiltTimelineSwappedInWithNoSeekPendingSkipsNoNoteOns()
    {
        var song = FullDemoSongFactory.Create();
        var output = new NoteOnRecorder();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions { LoopEndBar = song.Tracks.Max(t => t.Measures.Count) - 1 }, _ => { }, () => { });
        Thread.Sleep(PlayBeforeSeekMs);
        var before = engine.Timeline;
        output.Reset();

        engine.Seek(song, SeekBar, 0);
        // The swap must cope without the pending seek (a seek taken on the old timeline just before the rebuild was queued leaves none).
        typeof(PlaybackEngine).GetField("_pendingSeek", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(engine, null);
        var wait = Stopwatch.StartNew();
        while (ReferenceEquals(engine.Timeline, before) && wait.ElapsedMilliseconds < SwapWaitMs) Thread.Sleep(1);
        var rebuilt = engine.Timeline!;
        Assert.NotSame(before, rebuilt);
        // Poll for the dispatches rather than a fixed sleep: the count must arrive, not just be due.
        var from = rebuilt.PlayFromMs;
        var expected = rebuilt.Events.Count(e => e.IsNoteOn && !e.IsMetronome && !e.IsSetup && e.TimeMs >= from && e.TimeMs < from + CheckedSpanMs);
        var listen = Stopwatch.StartNew();
        while (output.NoteOns < expected && listen.ElapsedMilliseconds < ListenMs) Thread.Sleep(10);
        var stillPlaying = engine.IsPlaying;
        engine.Stop();
        Assert.True(stillPlaying, "the scheduler kept running after the swap");
        Assert.True(expected > 0);
        Assert.True(output.NoteOns >= expected, $"sent {output.NoteOns} note-ons after the swap, the timeline has {expected} in its first {CheckedSpanMs} ms");
        Assert.Equal(0, engine.LateNotesDropped);
    }

    // Rewind pressed again while the first press still compiles: the compile that is running carries on rather than starting over.
    [Fact]
    public void ARewindPressedAgainWhileItCompilesKeepsTheRunningCompile()
    {
        var song = FullDemoSongFactory.Create();
        using var engine = StartedAtBar(song, 2);
        engine.Seek(song, 0, 0);   // bar 1 lies before the compiled start: a rebuild
        var first = Latest(engine);
        engine.Seek(song, 0, 0);
        var second = Latest(engine);
        engine.Stop();

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    // Rewind pressed again after the first press's rebuilt timeline swapped in (a song without repeats): the rebuilt schedule is reused, nothing compiles again.
    [Fact]
    public void ARewindAfterARebuiltSeekReusesTheRebuiltTimeline()
    {
        var song = new global::TabForge.Models.SongProject { Tempo = 120 };
        song.Tracks.Add(new global::TabForge.Models.TrackModel { Name = "Gtr", Measures = TemplateFactory.Measures(40) });
        using var engine = StartedAtBar(song, 2);
        var started = engine.Timeline;
        engine.Seek(song, 0, 0);
        var wait = Stopwatch.StartNew();
        while (ReferenceEquals(engine.Timeline, started) && wait.ElapsedMilliseconds < SwapWaitMs) Thread.Sleep(1);
        var rebuilt = engine.Timeline;
        Thread.Sleep(100);
        engine.Seek(song, 0, 0);
        var queued = Latest(engine);
        Thread.Sleep(300);
        var after = engine.Timeline;
        engine.Stop();

        Assert.NotSame(started, rebuilt);
        Assert.Null(queued);
        Assert.Same(rebuilt, after);
    }

    private static PlaybackEngine StartedAtBar(global::TabForge.Models.SongProject song, int bar)
    {
        var engine = new PlaybackEngine(new NoteOnRecorder());
        engine.Start(song, new PlaybackOptions { StartBar = bar, LoopEndBar = song.Tracks.Max(t => t.Measures.Count) - 1 }, _ => { }, () => { });
        var wait = Stopwatch.StartNew();
        while (engine.Timeline is null && wait.ElapsedMilliseconds < SwapWaitMs) Thread.Sleep(1);
        Thread.Sleep(200);
        return engine;
    }

    // The seek rebuild's newest request (null when none is pending).
    private static object? Latest(PlaybackEngine engine)
    {
        var rebuild = typeof(PlaybackEngine).GetField("_seekRebuild", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine)!;
        return rebuild.GetType().GetField("_latest", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(rebuild);
    }
}
