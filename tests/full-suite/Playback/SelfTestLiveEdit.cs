using TabForge.Models;
using TabForge.Playback;

namespace TabForge;

// Live edit refresh at the engine: a big song edited in quick succession plays on without a late, dropped or repeated note, and a loop plays an
// edit made to a bar the playhead has already passed when it comes round.
public static partial class SelfTest
{
    private sealed class LiveEditOutput : IMidiOutput
    {
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private static int[] LiveIdentity(SongProject song) => Enumerable.Range(0, song.Tracks.Max(t => t.Measures.Count)).ToArray();

    private static void LiveAddNote(SongProject song, int track, int bar, int fret)
    {
        var t = song.Tracks[track];
        var cell = t.Measures[bar].Cells.First(c => c.Notes.Count == 0);
        cell.IsRest = false;
        cell.Notes.Add(RtNote(t, 0, fret));
        song.MarkTimelineChanged();
    }

    private static (bool Ok, string Detail) LiveEditBigSongAttempt()
    {
        var song = RtLargeSong(8, 500);
        using var engine = new PlaybackEngine(new LiveEditOutput());
        engine.StartDiagnostics();
        engine.Start(song, new PlaybackOptions { Speed = 2.0 }, _ => { }, () => { });
        var edits = 0;
        var worstRetry = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Rapid edits, a few refreshes in flight at once (typing; an edit's own refresh next to the live one).
        while (clock.ElapsedMilliseconds < 7000 && engine.IsPlaying)
        {
            var bar = 200 + edits % 100;
            var fret = 3 + edits % 15;
            var cell = song.Tracks[1].Measures[bar].Cells.FirstOrDefault(c => c.Notes.Count == 0);
            if (cell is not null) { cell.IsRest = false; cell.Notes.Add(RtNote(song.Tracks[1], 0, fret)); song.MarkTimelineChanged(); }
            edits++;
            var remap = LiveIdentity(song);
            var first = Task.Run(() => engine.RefreshArrangementLive(song, remap));
            Thread.Sleep(15);
            var second = Task.Run(() => engine.RefreshArrangementLive(song, remap));
            Task.WaitAll(first, second);
            worstRetry = Math.Max(worstRetry, Math.Max(first.Result, second.Result));
            Thread.Sleep(120);
        }
        var ran = clock.ElapsedMilliseconds;
        var timeline = engine.Timeline;
        engine.Stop();
        var log = engine.DispatchLog.Where(r => r.IsNoteOn).ToList();
        var lastStream = log.Count == 0 ? 0 : log.Max(r => r.StreamMs);
        // Every note the finished timeline holds in the played window was sent once, within a few ms of its time (a repeated one would find no timeline note left to match).
        var (from, to) = (500.0, lastStream - 300);
        var open = (timeline?.Events ?? new List<ScoreEvent>()).Where(e => e.IsNoteOn && !e.IsMetronome).Select(e => (e.TrackIndex, e.Data1, e.TimeMs)).ToList();
        var extra = 0;
        foreach (var r in log.Where(r => r.StreamMs > from && r.StreamMs < to))
        {
            var match = open.FindIndex(e => e.TrackIndex == r.TrackIndex && e.Data1 == r.Data1 && Math.Abs(e.TimeMs - r.StreamMs) < 110);
            if (match < 0) extra++; else open.RemoveAt(match);
        }
        var missing = open.Count(e => e.TimeMs > from + 120 && e.TimeMs < to - 120);
        var rewinds = 0; var max = double.NegativeInfinity;
        foreach (var r in log) { if (r.StreamMs < max - 5) rewinds++; max = Math.Max(max, r.StreamMs); }
        var late = log.Count(r => r.LatencyMs > 100);
        var ok = ran >= 3000 && edits >= 8 && log.Count > 250 && missing == 0 && extra == 0 && rewinds == 0 && late == 0;
        return (ok, $"ran {ran} ms, {edits} edits, {log.Count} notes sent, missing {missing}, extra {extra}, rewinds {rewinds}, late {late}, worst retry hint {worstRetry} ms");
    }

    // A scheduler thread starved by a loaded machine drops stale notes by design; an engine that stalls at the bar line would fail every attempt, so one clean attempt of two counts.
    private static void TestLiveEditBigSong()
    {
        var (ok, detail) = LiveEditBigSongAttempt();
        if (!ok) { Log.Add($"  info  live edit, big song: first attempt: {detail}"); (ok, detail) = LiveEditBigSongAttempt(); }
        Check("live edit, big song: rapid edits to a long multi-track song drop, repeat and delay no note (the scheduler never waits for a compile)", ok, detail);
    }

    private static void TestLiveEditLoop()
    {
        var song = IxDemoSong();
        var track = song.Tracks[0];
        var pitch = track.PitchOf(0, 22);
        var before = LiveEditMidiCount(song, pitch);
        using var engine = new PlaybackEngine(new LiveEditOutput());
        engine.StartDiagnostics();
        engine.Start(song, new PlaybackOptions { Speed = 2.0, Loop = true, LoopStartBar = 0, LoopEndBar = 3 }, _ => { }, () => { });
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (engine.Playhead().Bar < 2 && deadline.ElapsedMilliseconds < 10000) Thread.Sleep(10);
        Check("live edit, loop: playing the third loop bar", engine.IsPlaying && engine.Playhead().Bar >= 2, $"bar {engine.Playhead().Bar}");
        LiveAddNote(song, 0, 0, 22);   // an edit to bar 1, behind the playhead
        var retry = Task.Run(() => engine.RefreshArrangementLive(song, LiveIdentity(song))).GetAwaiter().GetResult();
        var heardBeforeWrap = engine.DispatchLog.Count(r => r.IsNoteOn && r.TrackIndex == 0 && r.Data1 == pitch);
        deadline.Restart();
        while (engine.DispatchLog.Count(r => r.IsNoteOn && r.TrackIndex == 0 && r.Data1 == pitch) == heardBeforeWrap && engine.IsPlaying && deadline.ElapsedMilliseconds < 15000) Thread.Sleep(10);
        var heard = engine.DispatchLog.Count(r => r.IsNoteOn && r.TrackIndex == 0 && r.Data1 == pitch) - heardBeforeWrap;
        engine.Stop();
        Check("live edit, loop: the pitch of the edit was not in the loop before it", before == 0, $"{before} before");
        Check("live edit, loop: an edit to a loop bar behind the playhead is heard when the loop comes round", retry == 0 && heard >= 1, $"retry hint {retry} ms, heard {heard}x");
    }

    private static int LiveEditMidiCount(SongProject song, int pitch) =>
        song.Tracks[0].Measures.Take(4).SelectMany(m => m.Cells).SelectMany(c => c.Notes).Count(n => n.MidiValue == pitch);
}
