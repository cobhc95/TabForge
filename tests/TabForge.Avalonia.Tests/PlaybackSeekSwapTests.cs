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

        engine.Seek(song, SeekBar, 0);
        // The swap must cope without the pending seek (a seek taken on the old timeline just before the rebuild was queued leaves none).
        typeof(PlaybackEngine).GetField("_pendingSeek", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(engine, null);
        var wait = Stopwatch.StartNew();
        while (ReferenceEquals(engine.Timeline, before) && wait.ElapsedMilliseconds < SwapWaitMs) Thread.Sleep(1);
        output.Reset();
        var rebuilt = engine.Timeline!;
        Assert.NotSame(before, rebuilt);
        Thread.Sleep(ListenMs);
        var stillPlaying = engine.IsPlaying;
        engine.Stop();

        var from = rebuilt.PlayFromMs;
        var expected = rebuilt.Events.Count(e => e.IsNoteOn && !e.IsMetronome && !e.IsSetup && e.TimeMs >= from && e.TimeMs < from + CheckedSpanMs);
        Assert.True(stillPlaying, "the scheduler kept running after the swap");
        Assert.True(expected > 0);
        Assert.True(output.NoteOns >= expected, $"sent {output.NoteOns} note-ons after the swap, the timeline has {expected} in its first {CheckedSpanMs} ms");
        Assert.Equal(0, engine.LateNotesDropped);
    }
}
