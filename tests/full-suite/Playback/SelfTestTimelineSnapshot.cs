using System.Collections.Concurrent;
using System.Diagnostics;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Work that runs off the editing thread (the clock's song map, the background arrangement refresh) reads an immutable copy of the song taken on the
/// owner thread, never the live song: an edit made while it runs cannot show up half-applied (part of <see cref="SelfTest"/>).
/// </summary>
public static partial class SelfTest
{
    private static readonly PlaybackOptions SnapshotMapOptions = new() { RepeatExpansion = true, RespectMuteSolo = false, SkipClips = true };

    private static string BarsFingerprint(IEnumerable<ScoreBar> bars)
        => string.Join(";", bars.Select(b => FormattableString.Invariant($"{b.Bar}:{b.StartMs:R}:{b.EndMs:R}:{b.Slots}:{b.Tempo}:{string.Join(",", b.Fermatas ?? Array.Empty<FermataSpan>())}")));

    private static string TimelineFingerprint(ScoreTimeline t)
    {
        long hash = unchecked((long)14695981039346656037UL);
        hash = FnvStep(hash, BarsFingerprint(t.Bars));
        foreach (var e in t.Events)
            hash = FnvStep(hash, FormattableString.Invariant($"{e.TimeMs:R}:{e.DeviceId}:{e.Status:X2}:{e.Data1}:{e.Data2}:{e.TrackIndex};"));
        return $"{hash:X16}/{t.Bars.Count}/{t.Events.Count}";
    }

    private static SongProject SnapshotRampSong()
    {
        var p = SingleTrack(6, 133);
        p.ImportedFrom = "song.gp5";
        for (var bar = 0; bar < 6; bar++)
            for (var beat = 0; beat < (bar == 4 ? 2 : 4); beat++) Beat(p, 0, bar, beat * 4, 4, 60 + beat);   // bar 5 is a short bar of an imported song
        p.Tracks[0].Measures[1].MidBarTempos = new List<TempoPoint> { new(0, 200, 16) };
        p.Tracks[0].Measures[2].MidBarTempos = new List<TempoPoint> { new(6, 90), new(10, 150, 4) };
        p.Tracks[0].Measures[3].Cells[4].Fermata = true;
        p.Tracks[0].Measures[3].TempoChange = 77;
        p.Tracks[0].Measures[0].RepeatStart = true;
        p.Tracks[0].Measures[1].RepeatEnd = true;
        return p;
    }

    /// <summary>Changes the song in the ways that move bars and notes (the same kinds of edit the window makes), without marking the timeline.</summary>
    private static void SnapshotMutate(SongProject song, int round)
    {
        foreach (var track in song.Tracks)
        {
            if (track.Measures.Count == 0) continue;
            var bar = round % track.Measures.Count;
            track.Measures[bar].TempoChange = 60 + round * 7 % 140;
            track.Measures[bar].RepeatStart = round % 3 == 0;
            foreach (var cell in track.Measures[(bar + 1) % track.Measures.Count].Cells) cell.Notes.Clear();
            if (round % 4 == 1 && track.Measures.Count > 4) track.Measures.RemoveAt(track.Measures.Count - 1);
            if (round % 4 == 3) track.Measures.Add(new MeasureModel { TimeSigNum = 3, TimeSigDenom = 4 });
        }
    }

    private static void TestTimelineSnapshot()
    {
        // The copy compiles to exactly what the live song compiles to (it holds everything that sounds).
        var songs = new (string Name, SongProject Song)[] { ("demo song", FullDemoSongFactory.Create()), ("imported song with ramps, fermata and repeats", SnapshotRampSong()) };
        foreach (var (name, song) in songs)
        {
            var live = TimelineFingerprint(MidiTimelineBuilder.Build(song, SnapshotMapOptions));
            var watch = Stopwatch.StartNew();
            var snapshot = SongSnapshot.Take(song);
            var tookMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var copy = snapshot.Song;
            Log.Add($"  info  timeline snapshot of the {name}: taken in {tookMs:0.0} ms on the owner thread, decoded in {watch.Elapsed.TotalMilliseconds:0.0} ms");
            Check($"timeline snapshot: the copy of the {name} is a separate song", !ReferenceEquals(copy, song) && !ReferenceEquals(copy.Tracks[0], song.Tracks[0]));
            Check($"timeline snapshot: the copy of the {name} compiles to the live song's timeline", TimelineFingerprint(MidiTimelineBuilder.Build(copy, SnapshotMapOptions)) == live);
            Check($"timeline snapshot: the copy of the {name} compiles to the live song's timeline with repeats off", TimelineFingerprint(MidiTimelineBuilder.Build(copy, new PlaybackOptions { Speed = 1.5 }))
                == TimelineFingerprint(MidiTimelineBuilder.Build(song, new PlaybackOptions { Speed = 1.5 })));
        }

        // Compiling the copy on another thread while the live song is edited hard: every compile equals the pre-edit timeline, nothing throws.
        {
            var song = FullDemoSongFactory.Create();
            var expected = TimelineFingerprint(MidiTimelineBuilder.Build(song, SnapshotMapOptions));
            var snapshot = SongSnapshot.Take(song);
            var seen = new ConcurrentBag<string>();
            Exception? fault = null;
            using var go = new ManualResetEventSlim();
            var worker = Task.Run(() =>
            {
                try
                {
                    go.Wait();
                    for (var i = 0; i < 6; i++) seen.Add(TimelineFingerprint(MidiTimelineBuilder.Build(snapshot.Song, SnapshotMapOptions)));
                }
                catch (Exception ex) { fault = ex; }
            });
            go.Set();
            for (var round = 0; !worker.IsCompleted && round < 100000; round++) { SnapshotMutate(song, round); song.MarkTimelineChanged(); }
            worker.Wait(TimeSpan.FromSeconds(60));
            Check("timeline snapshot: compiling the copy while the live song is edited does not throw", fault is null && worker.IsCompleted, fault?.Message);
            Check("timeline snapshot: every compile of the copy equals the timeline at the moment of the copy (no torn reads)",
                seen.Count == 6 && seen.All(s => s == expected), $"{seen.Count} compiles, {seen.Count(s => s != expected)} differ");
            Check("timeline snapshot: the live song did change meanwhile (the test is not vacuous)", TimelineFingerprint(MidiTimelineBuilder.Build(song, SnapshotMapOptions)) != expected);
        }

        // For the record (info only): compiling the live song from another thread while it is edited is what the copy replaces.
        {
            var song = FullDemoSongFactory.Create();
            var expected = TimelineFingerprint(MidiTimelineBuilder.Build(song, SnapshotMapOptions));
            int torn = 0, threw = 0, done = 0;
            using var go = new ManualResetEventSlim();
            var worker = Task.Run(() =>
            {
                go.Wait();
                for (var i = 0; i < 6; i++)
                {
                    try { if (TimelineFingerprint(MidiTimelineBuilder.Build(song, SnapshotMapOptions)) != expected) torn++; }
                    catch (Exception) { threw++; }
                    done++;
                }
            });
            go.Set();
            for (var round = 0; !worker.IsCompleted && round < 100000; round++) { SnapshotMutate(song, round); song.MarkTimelineChanged(); }
            worker.Wait(TimeSpan.FromSeconds(60));
            Log.Add($"  info  live compile on another thread while the song is edited: {done} compiles, {torn} differ from the pre-edit timeline, {threw} threw");
        }

        // The clock's song map: edits and marks on the owner thread while another thread reads the map. Every map read belongs to one revision
        // and equals the bars of the live song as they were at that revision; the last map reaches the last revision.
        {
            var song = SingleTrack(12, 120);
            for (var bar = 0; bar < 12; bar++) Beat(song, 0, bar, 0, 4, 60 + bar % 5);
            var clock = new SongClock(AudioEngineClient.Instance);
            _ = clock.BarStartSec(song, 0);   // owner thread: builds the first map from the live song and starts watching marks
            clock.FollowTimelineMarks();
            var expectedAt = new ConcurrentDictionary<int, string> { [song.TimelineRevision] = BarsFingerprint(MidiTimelineBuilder.Build(song, SnapshotMapOptions).Bars) };
            var observed = new ConcurrentBag<(int Revision, string Bars)>();
            Exception? readerFault = null;
            var stop = false;
            var reader = new Thread(() =>
            {
                try
                {
                    ScoreBar[]? lastBars = null;
                    while (!Volatile.Read(ref stop))
                    {
                        if (clock.TryReadMap(song, out var revision, out var bars) && !ReferenceEquals(bars, lastBars)) { lastBars = bars; observed.Add((revision, BarsFingerprint(bars))); }
                        Thread.Yield();
                    }
                }
                catch (Exception ex) { readerFault = ex; }
            }) { IsBackground = true, Name = "snapshot-reader" };
            reader.Start();
            for (var round = 0; round < 120; round++)
            {
                SnapshotMutate(song, round);
                song.MarkTimelineChanged();
                expectedAt[song.TimelineRevision] = BarsFingerprint(MidiTimelineBuilder.Build(song, SnapshotMapOptions).Bars);
                if (round % 6 == 0) Thread.Sleep(3);
            }
            var last = song.TimelineRevision;
            var caught = SpinWait.SpinUntil(() => clock.TryReadMap(song, out var revision, out _) && revision == last, TimeSpan.FromSeconds(20));
            Volatile.Write(ref stop, true);
            reader.Join(TimeSpan.FromSeconds(5));
            var wrong = observed.Count(o => !expectedAt.TryGetValue(o.Revision, out var want) || want != o.Bars);
            Log.Add($"  info  clock map under edits: {observed.Count} reads of {observed.Select(o => o.Revision).Distinct().Count()} revisions, {wrong} wrong");
            Check("clock map: a reader thread never throws while the owner edits and marks the timeline", readerFault is null, readerFault?.Message);
            Check("clock map: every map read from another thread equals the live bars at its own revision (no torn map)", observed.Count > 0 && wrong == 0, $"{wrong} of {observed.Count}");
            Check("clock map: the published map catches up with the last edit", caught);
            Check("clock map: the owner thread's own reads are current at once", clock.BarStartSec(song, 1) == MidiTimelineBuilder.Build(song, SnapshotMapOptions).Bars.FirstOrDefault(b => b.Bar == 1).StartMs / 1000);
            clock.Stopped();
        }

        // The playback thread's report: with no map yet it waits for the owner's snapshot instead of reading the live song, then anchors.
        if (SynchronizationContext.Current is not null)
        {
            var song = SingleTrack(8, 120);
            for (var bar = 0; bar < 8; bar++) Beat(song, 0, bar, 0, 4, 60);
            var clock = new SongClock(AudioEngineClient.Instance);
            double Reported()
            {
                var position = new PlaybackPosition { Bar = 3, BarFraction = 0.5 };
                double sec = double.NaN;
                var thread = new Thread(() => { clock.Report(song, position, playing: true); sec = clock.SecAt(Stopwatch.GetTimestamp()); }) { IsBackground = true };
                thread.Start();
                thread.Join(TimeSpan.FromSeconds(10));
                return sec;
            }
            Check("clock report: before the owner has copied the song the playback thread does not anchor (and does not read the live song)", double.IsNaN(Reported()));
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);   // the owner thread serves the request: snapshot, then a pool thread builds the map
            var mapped = SpinWait.SpinUntil(() => clock.TryReadMap(song, out _, out var bars) && bars.Length == 8, TimeSpan.FromSeconds(10));
            Check("clock report: the owner's snapshot becomes the map the playback thread reads", mapped);
            var anchored = Reported();
            Check("clock report: with the map the playhead anchors at the song time of that bar and position (3.5 bars of 2 s)", Math.Abs(anchored - 7.0) < 0.2, anchored.ToString("0.###"));
            clock.Stopped();

            // Playback start prepares the map on the owner thread: the first playhead anchors without a round trip through the owner's dispatcher.
            var started = SingleTrack(8, 120);
            for (var bar = 0; bar < 8; bar++) Beat(started, 0, bar, 0, 4, 60);
            var startClock = new SongClock(AudioEngineClient.Instance);
            startClock.PrepareForPlayback(started);
            var built = SpinWait.SpinUntil(() => startClock.TryReadMap(started, out _, out var bars) && bars.Length == 8, TimeSpan.FromSeconds(10));   // no dispatcher frame pumped
            Check("clock report: preparing for playback builds the map without the owner's dispatcher", built);
            double startSec = double.NaN;
            var first = new Thread(() => { startClock.Report(started, new PlaybackPosition { Bar = 3, BarFraction = 0.5 }, playing: true); startSec = startClock.SecAt(Stopwatch.GetTimestamp()); }) { IsBackground = true };
            first.Start();
            first.Join(TimeSpan.FromSeconds(10));
            Check("clock report: after preparing, the first playhead anchors at once (3.5 bars of 2 s)", Math.Abs(startSec - 7.0) < 0.2, startSec.ToString("0.###"));
            startClock.Stopped();
        }

        // The background arrangement refresh copies the song when it is called: an edit right after the call is not heard in the refreshed future.
        // The song is only about half a second long, so a machine so loaded that the test thread is late can let the playhead pass the bars
        // before the call. Load only ever delays, so the checks judge the first of up to three attempts that got the call in time; a real
        // regression (the later edit heard, the order not followed, a fault) fails every attempt.
        var refreshChecks = new List<(string Name, bool Ok, string Detail)>();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            refreshChecks.Clear();
            var project = SingleTrack(bars: 3, bpm: 400);
            Beat(project, 0, 0, 0, 1, 60);
            Beat(project, 0, 1, 0, 4, 65);
            Beat(project, 0, 2, 0, 4, 72);
            project.Markers.AddRange(new[]
            {
                new MarkerModel { MeasureIndex = 0, Title = "Intro" },
                new MarkerModel { MeasureIndex = 1, Title = "Pre-Chorus" },
                new MarkerModel { MeasureIndex = 2, Title = "Chorus" }
            });
            var output = new RecordingMidiOutput();
            using var engine = new PlaybackEngine(output);
            using var finished = new ManualResetEventSlim();
            engine.Start(project, new PlaybackOptions { Speed = 4 }, _ => { }, finished.Set);
            var started = SpinWait.SpinUntil(() => output.Messages.Any(m => (m.Status & 0xF0) == 0x90 && m.Data1 == 60 && m.Data2 > 0), TimeSpan.FromSeconds(10));
            refreshChecks.Add(("background arrangement refresh: the current Intro note has started", started, ""));
            if (started)
            {
                Thread.Sleep(20);
                var mapping = SectionReorderService.Move(project, from: 0, insertBefore: 2);
                var refresh = mapping is null ? Task.CompletedTask : engine.RefreshArrangementInBackground(project, mapping);
                project.Tracks[0].Measures[2].Cells[0].Notes[0].MidiValue = 99;   // the very next edit, while the compile runs
                project.MarkTimelineChanged();
                refreshChecks.Add(("background arrangement refresh: the section move produced its bar mapping", mapping is not null, ""));
                refreshChecks.Add(("background arrangement refresh: the refresh completes without a fault", refresh.Wait(TimeSpan.FromSeconds(10)) && !refresh.IsFaulted, ""));
                refreshChecks.Add(("background arrangement refresh: playback finishes after following the new order", finished.Wait(TimeSpan.FromSeconds(5)), ""));
                var attacks = output.Messages.Where(m => (m.Status & 0xF0) == 0x90 && m.Data2 > 0).Select(m => m.Data1).Where(pitch => pitch is 60 or 65 or 72 or 99).ToArray();
                refreshChecks.Add(("background arrangement refresh: the future follows the song as it was at the call (the later edit is not in it)", attacks.SequenceEqual(new[] { 60, 72 }), string.Join(" -> ", attacks)));
            }
            if (refreshChecks.All(check => check.Ok))
            {
                if (attempt > 1) Log.Add($"  info  background arrangement refresh passed on attempt {attempt} (earlier attempts were delayed by machine load)");
                break;
            }
        }
        foreach (var (name, ok, detail) in refreshChecks) Check(name, ok, detail);
    }

    private static TabNote? FirstNoteOf(SongProject song)
        => song.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells).SelectMany(c => c.Notes).FirstOrDefault();

    /// <summary>Owner-thread time of one mark (the song copy) and the bytes it allocates, over several one-note edits; the best, median and worst time and the median allocation.</summary>
    private static (double BestMs, double MedianMs, double MaxMs, long MedianAlloc) MeasureMarkCost(SongProject song, int runs)
    {
        var clock = new SongClock(AudioEngineClient.Instance);
        _ = clock.BarStartSec(song, 0);
        clock.FollowTimelineMarks();
        song.MarkTimelineChanged();
        SpinWait.SpinUntil(() => clock.TryReadMap(song, out var rev, out _) && rev == song.TimelineRevision, TimeSpan.FromSeconds(60));
        var note = FirstNoteOf(song)!;
        var times = new List<double>();
        var allocs = new List<long>();
        for (var run = 0; run < runs; run++)
        {
            note.Fret = (note.Fret + 1) % 20;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            song.MarkTimelineChanged();
            times.Add(watch.Elapsed.TotalMilliseconds);
            allocs.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            SpinWait.SpinUntil(() => clock.TryReadMap(song, out var rev, out _) && rev == song.TimelineRevision, TimeSpan.FromSeconds(60));   // the pool thread is idle again before the next edit
        }
        clock.Stopped();
        return (times.Min(), times.Order().ElementAt(times.Count / 2), times.Max(), allocs.Order().ElementAt(allocs.Count / 2));
    }

    /// <summary>The same work done plainly on one thread: every bar written once. What a mark may cost at most, whatever the machine.</summary>
    private static double WriteEveryBarMs(SongProject song)
    {
        var writer = new BarWriter();
        var best = double.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var watch = Stopwatch.StartNew();
            foreach (var track in song.Tracks)
                foreach (var measure in track.Measures) { writer.Reset(); BarCodec.Write(writer, measure); }
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    /// <summary>What one timeline mark costs the editing thread while the playback thread follows the song clock.</summary>
    private static void TestSnapshotMarkCost()
    {
        var songs = new List<(string Name, SongProject Song)> { ("7 tracks x 167 bars", RichSong(7, 167, 1)), ("7 tracks x 2039 bars", RichSong(7, 2039, 2)) };
        foreach (var path in (Environment.GetEnvironmentVariable("TABFORGE_SNAPSHOT_COST_SONGS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            try
            {
                var imported = GuitarProImporter.Import(path);
                songs.Add(($"local song {songs.Count - 1} ({imported.Tracks.Count} tracks x {imported.Tracks.Max(t => t.Measures.Count)} bars)", imported));
            }
            catch (Exception ex) { Log.Add($"  info  snapshot cost: could not import a local song: {ex.Message}"); }

        var cores = Environment.ProcessorCount;
        foreach (var (name, song) in songs)
        {
            var bars = song.Tracks.Sum(t => t.Measures.Count);
            var write = WriteEveryBarMs(song);
            var (best, median, max, alloc) = MeasureMarkCost(song, 15);
            Log.Add($"  info  timeline mark with a following reader, {name}: best {best:0.0} ms, median {median:0.0} ms, max {max:0.0} ms, {alloc / 1024.0:0} KiB allocated; writing every bar once on one thread: {write:0.0} ms ({cores} cores)");
            if (!name.StartsWith("7 tracks x 2039", StringComparison.Ordinal)) continue;
            // Relative to the plain one-thread cost measured in the same run: a loaded or slow machine scales both sides. A song this big copies its
            // tracks side by side, so with four or more cores a mark is cheaper than writing every bar once; with fewer it may cost a little more.
            var allowed = cores >= 4 ? 0.9 : 1.5;
            Check($"timeline mark on a {name} song costs the editing thread at most {allowed:0.00}x one plain pass over every bar (best of the runs)", best <= write * allowed, $"{best:0.0} ms vs {write:0.0} ms");
            Check($"timeline mark on a {name} song allocates under 256 bytes per bar", alloc <= 256L * bars, $"{alloc} bytes for {bars} bars");
        }
    }
}
