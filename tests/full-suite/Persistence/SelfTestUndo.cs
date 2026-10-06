using System.Diagnostics;
using System.Reflection;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

/// <summary>Audit 3 M-06: per-bar undo states (exact content, granularity, dirty flag, bar reuse, cost).</summary>
public static partial class SelfTest
{
    private static void TestUndoDeltaStates()
    {
        TestUndoBarCodecCoverage();
        TestUndoBarCodecRoundTrip();
        TestUndoMultiTrackHistory();
        TestUndoRestoreRechecksLiveBars();
        TestUndoFingerprints();
        TestPasteUndoScope();
        TestUndoLatency();
    }

    private static void TestPasteUndoScope()
    {
        var document = DocumentSession.FromProject(TemplateFactory.Blank(), null);
        DocumentSession? sibling = null;
        try
        {
            sibling = DocumentSession.FromProject(TemplateFactory.Blank(), null);
            TestPasteUndoScope(document, sibling);
        }
        finally
        {
            document.DisposePlayback();
            sibling?.DisposePlayback();
        }
    }

    private static void TestPasteUndoScope(DocumentSession document, DocumentSession sibling)
    {
        var source = TemplateFactory.Blank();
        source.Tracks[0].Measures[0].Cells[0] = new TabCell
        {
            DurationDenominator = 4,
            Notes = { new TabNote { StringIndex = 2, Fret = 7, MidiValue = source.Tracks[0].PitchOf(2, 7) } }
        };
        var clip = ClipboardService.CaptureBeats(source, 0, 0, 0, 0, 0, 0);
        document.MarkClean(); sibling.MarkClean();
        var (original, siblingOriginal) = (ProjectService.Snapshot(document.Project), ProjectService.Snapshot(sibling.Project));
        var (siblingProject, siblingRevision) = (sibling.Project, sibling.Project.TimelineRevision);
        var revision = document.Project.TimelineRevision;
        var outcome = EditCommands.RunPaste(document, clip, new PasteTarget(0, 0, 1, 0), new EditingSettings(), new RecommendedPasteAnswers());
        var pastedProject = document.Project;
        var pasted = ProjectService.Snapshot(pastedProject);
        var pasteOk = outcome.Changed && pasted != original && pastedProject.Tracks[0].Measures[1].Cells[0].Notes.Single().Fret == 7 &&
            document.Undo.UndoCount == 1 && document.Undo.RedoCount == 0 && document.IsDirty && document.HasUnsavedChanges &&
            pastedProject.TimelineRevision == revision + 1;
        Check("paste through the document path changes content, marks dirty, stores one undo step and invalidates once", pasteOk,
            $"changed {outcome.Changed}, dirty {document.IsDirty}, undo {document.Undo.UndoCount}, revision +{pastedProject.TimelineRevision - revision}");

        var undoSource = document.Project;
        revision = undoSource.TimelineRevision;
        var undo = DocumentEdits.Undo(document);
        var restored = document.Project;
        var undoRevision = ReferenceEquals(undoSource, restored) ? restored.TimelineRevision == revision + 1 : restored.TimelineRevision == 1;
        Check("document undo restores the clean pre-paste content and invalidates timing", undo is not null &&
            ProjectService.Snapshot(restored) == original && !document.IsDirty && !document.HasUnsavedChanges && undoRevision &&
            document.Undo.UndoCount == 0 && document.Undo.RedoCount == 1,
            $"clean {!document.HasUnsavedChanges}, revision {revision} -> {restored.TimelineRevision}, replaced {!ReferenceEquals(undoSource, restored)}");

        var redoSource = document.Project;
        revision = redoSource.TimelineRevision;
        var redo = DocumentEdits.Redo(document);
        var redone = document.Project;
        var redoRevision = ReferenceEquals(redoSource, redone) ? redone.TimelineRevision == revision + 1 : redone.TimelineRevision == 1;
        Check("document redo restores the pasted content, marks dirty and invalidates timing", redo is not null &&
            ProjectService.Snapshot(redone) == pasted && document.IsDirty && document.HasUnsavedChanges && redoRevision &&
            document.Undo.UndoCount == 1 && document.Undo.RedoCount == 0,
            $"dirty {document.IsDirty}, revision {revision} -> {redone.TimelineRevision}, replaced {!ReferenceEquals(redoSource, redone)}");

        Check("paste, undo and redo leave a sibling document and its history untouched",
            ReferenceEquals(sibling.Project, siblingProject) && ProjectService.Snapshot(sibling.Project) == siblingOriginal &&
            !sibling.IsDirty && !sibling.HasUnsavedChanges && sibling.Project.TimelineRevision == siblingRevision &&
            sibling.Undo.UndoCount == 0 && sibling.Undo.RedoCount == 0);
    }

    private static void TestUndoBarCodecCoverage()
    {
        foreach (var (type, handled) in BarCodec.Handled)
        {
            var contract = ProjectService.CompactPropertyNames(type);
            var missing = contract.Except(handled).ToList();
            var stale = handled.Except(contract).ToList();
            Check($"undo bar codec covers every JSON property of {type.Name}", missing.Count == 0 && stale.Count == 0,
                $"missing [{string.Join(", ", missing)}] stale [{string.Join(", ", stale)}]");
        }

        // Every model type a bar can hold must have a codec entry (a new nested type fails here, not silently in undo).
        var seen = new HashSet<Type>();
        var pending = new Queue<Type>(new[] { typeof(MeasureModel) });
        var uncovered = new List<string>();
        while (pending.Count > 0)
        {
            var type = pending.Dequeue();
            if (!seen.Add(type)) continue;
            if (!BarCodec.Handled.ContainsKey(type)) { uncovered.Add(type.Name); continue; }
            foreach (var name in ProjectService.CompactPropertyNames(type))
            {
                var propertyType = type.GetProperty(name)?.PropertyType;
                if (propertyType is null) continue;
                propertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
                if (propertyType.IsGenericType) propertyType = propertyType.GetGenericArguments()[0];
                if (propertyType.Namespace?.StartsWith("TabForge", StringComparison.Ordinal) == true && !propertyType.IsEnum &&
                    type.GetProperty(name)?.SetMethod is not null)
                    pending.Enqueue(propertyType);
            }
        }
        Check("undo bar codec has an entry for every model type a bar holds", uncovered.Count == 0, string.Join(", ", uncovered));
    }

    private static void TestUndoBarCodecRoundTrip()
    {
        var rng = new Random(20260929);
        var failures = new List<string>();
        var writer = new BarWriter();
        for (var index = 0; index < 300; index++)
        {
            var bar = (MeasureModel)RandomModel(typeof(MeasureModel), rng, 0)!;
            if (!BarRoundTrips(writer, bar)) failures.Add($"random bar {index}");
        }

        // Edge cases: null lists and elements, negative zero, a set holding two spellings of one technique, a null bar.
        var edge = new MeasureModel { Number = 9, MidBarTempos = new List<TempoPoint> { new(-0.0, 90, 0.5), null! } };
        edge.Cells[0].Notes.Add(new TabNote { GraceOnsetOffsetSlots = -0.0, Techniques = new HashSet<string>(StringComparer.Ordinal) { "Bend", "bend" } });
        edge.Cells[0].Notes.Add(null!);
        edge.Cells[1] = null!;
        edge.Cells[2].WhammyPoints = null!;
        edge.Cells[3].Mix = new MixChange { Tempo = 100, AllTracks = true };
        edge.Voice2Cells = null!;
        if (!BarRoundTrips(writer, edge)) failures.Add("edge bar");
        writer.Reset();
        BarCodec.Write(writer, null);
        if (BarCodec.Read(writer.Written) is not null) failures.Add("null bar");
        Check("undo bar codec round-trips 300 random bars and the edge cases exactly (same compact JSON)", failures.Count == 0,
            string.Join("; ", failures.Take(5)));
    }

    private static bool BarRoundTrips(BarWriter writer, MeasureModel bar)
    {
        writer.Reset();
        BarCodec.Write(writer, bar);
        var decoded = BarCodec.Read(writer.Written);
        if (decoded is null) return false;
        decoded.Number = bar.Number;
        return ProjectService.CompactJson(bar).AsSpan().SequenceEqual(ProjectService.CompactJson(decoded));
    }

    /// <summary>Fills every JSON property of a bar-level model type with random values (nulls included where the type allows).</summary>
    private static object? RandomModel(Type type, Random rng, int depth)
    {
        if (type == typeof(TempoPoint)) return new TempoPoint(RandomDouble(rng), rng.Next(20, 400), RandomDouble(rng));
        var instance = Activator.CreateInstance(type)!;
        foreach (var name in ProjectService.CompactPropertyNames(type))
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.SetMethod is null) continue;
            property.SetValue(instance, RandomValue(property.PropertyType, rng, depth));
        }
        return instance;
    }

    private static object? RandomValue(Type type, Random rng, int depth)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return rng.Next(4) == 0 ? null : RandomValue(underlying, rng, depth);
        if (type == typeof(int)) return rng.Next(8) == 0 ? rng.Next(int.MinValue, int.MaxValue) : rng.Next(-3, 130);
        if (type == typeof(bool)) return rng.Next(2) == 0;
        if (type == typeof(double)) return RandomDouble(rng);
        if (type == typeof(string)) return rng.Next(6) switch { 0 => null, 1 => "", 2 => "Ωé♯\n\"x\"", _ => $"s{rng.Next(1000)}" };
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(rng.Next(values.Length));
        }
        if (type == typeof(HashSet<string>))
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = rng.Next(4); index > 0; index--) set.Add(GpEffects.All[rng.Next(GpEffects.All.Length)]);
            return set;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            if (rng.Next(10) == 0) return null;
            var element = type.GetGenericArguments()[0];
            var list = (System.Collections.IList)Activator.CreateInstance(type)!;
            var count = depth > 2 ? 0 : rng.Next(element == typeof(TabCell) ? 5 : 3);
            for (var index = 0; index < count; index++)
                list.Add(rng.Next(25) == 0 ? null : RandomModel(element, rng, depth + 1));
            return list;
        }
        if (type.IsClass) return rng.Next(3) == 0 ? null : RandomModel(type, rng, depth + 1);
        throw new InvalidOperationException($"No random value for {type.Name}");
    }

    private static double RandomDouble(Random rng) => rng.Next(6) switch
    {
        0 => 0d,
        1 => -0d,
        2 => rng.NextDouble() * 1e-9,
        _ => Math.Round(rng.NextDouble() * 64, rng.Next(5)),
    };

    /// <summary>A generated multi-track song shaped like a real one (≈8 notes per bar, techniques, bends, voice 2, mix and tempo points).</summary>
    private static SongProject RichSong(int tracks, int bars, int seed)
    {
        var rng = new Random(seed);
        var song = new SongProject { Title = "Undo bench", Tempo = 140 };
        for (var t = 0; t < tracks; t++)
        {
            var track = new TrackModel { Name = $"Track {t + 1}", Measures = TemplateFactory.Measures(bars), Volume = 90 + t };
            for (var b = 0; b < bars; b++)
            {
                var measure = track.Measures[b];
                for (var c = 0; c < 16; c += 2)
                {
                    var cell = measure.Cells[c];
                    cell.DurationDenominator = 16;
                    var fret = rng.Next(0, 22);
                    var note = new TabNote { StringIndex = rng.Next(6), Fret = fret, MidiValue = 40 + fret, Velocity = 80 + rng.Next(40) };
                    if (rng.Next(4) == 0) note.Techniques.Add("PalmMute");
                    if (rng.Next(9) == 0) note.Techniques.Add("Vibrato");
                    if (rng.Next(12) == 0)
                    {
                        note.Techniques.Add("Bend");
                        note.BendPoints.Add(new BendPointModel { Offset = 0, Value = 0 });
                        note.BendPoints.Add(new BendPointModel { Offset = 30, Value = 4 });
                    }
                    cell.Notes.Add(note);
                }
                if (b % 9 == 4) measure.CellsForVoice(1, create: true)[0].Notes.Add(new TabNote { StringIndex = 5, Fret = 3, MidiValue = 43 });
                if (b % 50 == 0) measure.Cells[0].Mix = new MixChange { Volume = 12, Pan = -2 };
                if (b % 40 == 3) measure.MidBarTempos = new List<TempoPoint> { new(8, 150) };
                if (b % 16 == 0) measure.Cells[0].Lyrics = $"line {b}";
            }
            song.Tracks.Add(track);
        }
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        song.Markers.Add(new MarkerModel { MeasureIndex = Math.Min(8, bars - 1), Title = "Verse" });
        return song;
    }

    private static void TestUndoMultiTrackHistory()
    {
        var doc = DocumentSession.FromProject(RichSong(4, 40, 1), null);
        doc.MarkClean();
        var arrangement = new ArrangementController();
        var edits = new (string Name, Action<SongProject> Apply)[]
        {
            ("fret edit", p => p.Tracks[0].Measures[3].Cells[2].Notes[0].Fret = 17),
            ("two tracks in one step", p =>
            {
                p.Tracks[1].Measures[10].Cells[1].Notes.Add(new TabNote { StringIndex = 2, Fret = 5, MidiValue = 60 });
                p.Tracks[3].Measures[10].Cells[4].IsRest = true;
            }),
            ("insert a bar on every track", p => arrangement.InsertBar(p, 5, 5, moveMarkers: true)),
            ("delete a bar on every track", p => arrangement.DeleteBar(p, 12, -1, allTracks: true, moveMarkers: true)),
            ("renumber bars", p => { foreach (var track in p.Tracks) for (var i = 0; i < track.Measures.Count; i++) track.Measures[i].Number = i + 101; }),
            ("move a track", p => p.MoveTrack(3, 0)),
            ("track header", p => { p.Tracks[2].Volume = 77; p.Tracks[2].Name = "Renamed"; }),
            ("song header", p => { p.Title = "Delta"; p.Tempo = 133; p.Markers[1].Title = "Chorus"; }),
            ("mid-bar tempo, mix and technique", p =>
            {
                p.Tracks[1].Measures[20].MidBarTempos = new List<TempoPoint> { new(4, 120, 2) };
                p.Tracks[1].Measures[20].Cells[2].Mix = new MixChange { Tempo = 120 };
                p.Tracks[1].Measures[21].Cells[0].Notes[0].Techniques.Add("WideVibrato");
            }),
            ("duplicate content in two bars", p => p.Tracks[0].Measures[30].Cells = p.Tracks[0].Measures[31].Cells.Select(c => c.Clone()).ToList()),
        };

        var before = new List<string>();
        var stored = 0;
        var noOps = new List<string>();
        foreach (var (name, apply) in edits)
        {
            before.Add(ProjectService.Snapshot(doc.Project));
            if (doc.Undo.Capture(doc.Project).Stored) stored++;
            apply(doc.Project);
            doc.Project.IsDirty = true;
            if (ProjectService.Snapshot(doc.Project) == before[^1]) noOps.Add(name);
        }
        var final = ProjectService.Snapshot(doc.Project);
        Check("delta undo keeps one level per edit (same granularity)",
            noOps.Count == 0 && stored == edits.Length && doc.Undo.UndoCount == edits.Length && doc.Undo.RedoCount == 0,
            $"stored {stored} of {edits.Length}; no-op edits [{string.Join(", ", noOps)}]");

        var wrong = new List<string>();
        var dirtyMismatch = new List<string>();
        var reusedAfterFirstUndo = -1;
        var totalBars = 0;
        for (var index = edits.Length - 1; index >= 0; index--)
        {
            var live = doc.Project;
            var liveBars = new HashSet<MeasureModel>(live.Tracks.SelectMany(t => t.Measures), ReferenceEqualityComparer.Instance);
            if (!UndoStep(doc, redo: false, out var target)) { wrong.Add($"undo {edits[index].Name}: nothing to undo"); break; }
            if (ProjectService.Snapshot(doc.Project) != before[index]) wrong.Add($"undo {edits[index].Name}");
            if (doc.IsCleanContent(target) != (index == 0) || doc.IsCleanContent(target) == doc.HasUnsavedChanges)
                dirtyMismatch.Add(edits[index].Name);
            if (index == edits.Length - 1)
            {
                totalBars = doc.Project.Tracks.Sum(t => t.Measures.Count);
                reusedAfterFirstUndo = doc.Project.Tracks.SelectMany(t => t.Measures).Count(liveBars.Contains);
            }
        }
        for (var index = 0; index < edits.Length; index++)
        {
            if (!UndoStep(doc, redo: true, out var target)) { wrong.Add($"redo {edits[index].Name}: nothing to redo"); break; }
            var expected = index + 1 < edits.Length ? before[index + 1] : final;
            if (ProjectService.Snapshot(doc.Project) != expected) wrong.Add($"redo {edits[index].Name}");
            if (doc.IsCleanContent(target) || doc.IsCleanContent(target) == doc.HasUnsavedChanges) dirtyMismatch.Add($"redo {edits[index].Name}");
        }
        Check("delta undo and redo restore every multi-track state exactly (bars, tracks, headers, numbers, markers)",
            wrong.Count == 0, string.Join("; ", wrong));
        Check("the undo dirty check matches the full content hash at every step (clean only at the saved state)",
            dirtyMismatch.Count == 0, string.Join("; ", dirtyMismatch));
        Check("undoing a one-bar edit moves every other bar over instead of rebuilding it",
            totalBars > 0 && reusedAfterFirstUndo >= totalBars - 2, $"{reusedAfterFirstUndo} of {totalBars} bars reused");

        // A reused bar now belongs to the restored song: editing it must not reach the stored history.
        UndoStep(doc, redo: false, out _);
        doc.Project.Tracks[0].Measures[0].Cells[0].Notes.Clear();
        doc.Project.Tracks[0].Measures[1].Number = 99;
        UndoStep(doc, redo: false, out _);
        Check("history states stay immutable when the restored song is edited",
            ProjectService.Snapshot(doc.Project) == before[edits.Length - 2]);
    }

    /// <summary>What MainWindow's Undo_Click / Redo_Click and RestoreSnapshot do, without the window.</summary>
    private static bool UndoStep(DocumentSession doc, bool redo, out UndoSnapshot target)
    {
        var current = doc.Undo.Snapshot(doc.Project);
        if (!(redo ? doc.Undo.TryRedo(current, out target) : doc.Undo.TryUndo(current, out target))) return false;
        doc.Project = doc.Undo.Restore(target, doc.Project);
        doc.Project.IsDirty = true;
        if (doc.IsCleanContent(target)) doc.Project.IsDirty = false;
        return true;
    }

    private static void TestUndoRestoreRechecksLiveBars()
    {
        var undo = new UndoController();
        var song = RichSong(2, 12, 3);
        var target = undo.Snapshot(song);
        var expected = ProjectService.Snapshot(song);
        song.Tracks[0].Measures[4].Cells[0].Notes[0].Fret = 23;
        song.Tracks[1].Measures[7].Cells[6].Notes.Clear();
        var current = undo.Snapshot(song);
        // Misuse on purpose: the live song changes after its state was taken; restore must not trust the stale bars.
        song.Tracks[0].Measures[2].Cells[0].Notes[0].Fret = 1;
        song.Tracks[1].Measures[3].RepeatStart = !song.Tracks[1].Measures[3].RepeatStart;
        var restored = undo.Restore(target, song);
        Check("restore re-checks every reused bar against the stored bytes", ProjectService.Snapshot(restored) == expected);
        Check("a state keeps its content after the song it came from is edited",
            ProjectService.Snapshot(new UndoController().Restore(current)) != expected);
    }

    private static void TestUndoFingerprints()
    {
        var song = RichSong(3, 20, 5);
        var a = new UndoController().Snapshot(song);
        var b = new UndoController().Snapshot(ProjectService.Restore(ProjectService.Snapshot(song)));
        song.Tracks[2].Measures[19].Cells[15].Text = "x";
        var c = new UndoController().Snapshot(song);
        Check("equal songs give equal undo fingerprints and content; an edit changes both",
            a.Fingerprint == b.Fingerprint && a.State.ContentEquals(b.State) && a.Fingerprint != c.Fingerprint && !a.State.ContentEquals(c.State));
    }

    private static void TestUndoLatency()
    {
        foreach (var (tracks, bars, target) in new[] { (7, 167, 16.0), (20, 500, 50.0) })
        {
            var song = RichSong(tracks, bars, tracks * bars);
            var notes = song.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count) + m.Voice2Cells.Sum(c => c.Notes.Count)));
            var undo = new UndoController();
            undo.Capture(song);
            var captures = new List<double>();
            var undos = new List<double>();
            for (var run = 0; run < 9; run++)
            {
                song.Tracks[run % tracks].Measures[(run * 13) % bars].Cells[0].Notes[0].Fret = run;
                var watch = Stopwatch.StartNew();
                undo.Capture(song);
                captures.Add(watch.Elapsed.TotalMilliseconds);
            }
            for (var run = 0; run < 5; run++)
            {
                var watch = Stopwatch.StartNew();
                if (undo.TryUndo(undo.Snapshot(song), out var level)) song = undo.Restore(level, song);
                undos.Add(watch.Elapsed.TotalMilliseconds);
            }
            // Best of several runs: machine load only ever adds time, so the fastest run is the cost of the code; a real regression
            // makes every run slow and still fails.
            var full = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                var fullWatch = Stopwatch.StartNew();
                ProjectService.SnapshotBytes(song);
                ProjectService.ContentHash(song);
                full = Math.Min(full, fullWatch.Elapsed.TotalMilliseconds);
            }
            var capture = captures.Min();
            var undoMs = undos.Min();
            Log.Add($"  info  undo cost {tracks}x{bars} ({notes} notes): capture {capture:0.0} ms, undo {undoMs:0.0} ms (best of the runs); " +
                    $"old full snapshot + hash {full:0.0} ms");
            // Tolerant: the audit target doubled for a loaded machine, and relative to the old path measured here.
            Check($"per-edit undo capture on a {tracks}x{bars} song ({notes} notes) stays near the {target:0} ms target",
                capture < target * 2 || capture * 4 < full, $"{capture:0.0} ms (old path {full:0.0} ms)");
            // Guard for the largest song (a felt hitch per edit if it creeps up). Relative, not an absolute time: the median capture
            // must stay far cheaper than the old full snapshot + hash measured in the same run, so a slow, loaded or throttled machine
            // (a CI runner, a BelowNormal run) scales both sides and cannot fail it, and the check never needs to be skipped.
            if (tracks == 20)
                Check("per-edit undo capture on the 20x500 song is at least 4x cheaper than a full snapshot + hash (same run, best of the runs)",
                    capture * 4 <= full, $"capture {capture:0.0} ms, full snapshot + hash {full:0.0} ms");
            Check($"undo (state + restore) on a {tracks}x{bars} song is faster than the old full snapshot + hash",
                undoMs < full || undoMs < target * 2, $"{undoMs:0.0} ms vs {full:0.0} ms");
        }
    }
}
