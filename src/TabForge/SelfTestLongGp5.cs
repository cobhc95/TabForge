using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Long-song Guitar Pro 3-5 import (audit R4, required group "long-import"). alphaTab 1.8.4 refuses a Guitar Pro 3-5 file over 1,000
/// bars with a hard-coded "'bar count' ... internal safety threshold of 1000". TabForge ships alphaTab 1.8.4 built from source with
/// one patch (package and assembly TabForge.AlphaTab, vendor/alphatab/README.md) that makes the threshold a setting of the one
/// import, and <see cref="AlphaTabBoundary"/> sets it to TabForge's own limit (<see cref="InputLimits.MaxMeasuresPerTrack"/>).
/// These checks use files generated here for every supported version (GP3.00, GP4.00, GP5.00, GP5.10), never a real song.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Registered in the required group "long-import" (SelfTest.Run: GuardGroup; minimum and CI membership in SelfTestRequirements.cs).</summary>
    private static void TestLongGuitarPro35Import()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-long-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            LongImportBoundary();
            LongImportShortFilesUnchanged();
            LongImportBarCounts();
            LongImportRefusals();
            LongImportTotalLimits();
            LongImportConcurrent();
            LongImportMissingDependency();
            LongImportDenseSong(folder);
            LongImportWorker(folder);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string ImportExtension(int version) => version >= 500 ? ".gp5" : version >= 400 ? ".gp4" : ".gp3";

    /// <summary>The alphaTab in use is the patched build, the limit is per Settings object and the default is upstream's.</summary>
    private static void LongImportBoundary()
    {
        var alphaTab = typeof(AlphaTab.Importer.ScoreLoader).Assembly;
        Check("long import: the alphaTab in use is the patched TabForge.AlphaTab build, not the upstream AlphaTab package",
            alphaTab.GetName().Name == AlphaTabBoundary.RequiredAssemblyName && alphaTab.GetName().Version >= AlphaTabBoundary.RequiredVersion,
            alphaTab.GetName().ToString());
        Check("long import: the reader component passes the boundary check (identity, version, bar-limit setting, percussion table)",
            AlphaTabBoundary.Problem is null, AlphaTabBoundary.Problem);
        var untouched = new AlphaTab.Settings();
        var raised = new AlphaTab.Settings();
        raised.Importer.MaxGp3To5BarCount = InputLimits.MaxMeasuresPerTrack;
        Check("long import: the bar limit belongs to its Settings object (another Settings keeps upstream's 1,000)",
            untouched.Importer.MaxGp3To5BarCount == 1000 && raised.Importer.MaxGp3To5BarCount == InputLimits.MaxMeasuresPerTrack);
        Check("long import: the limit handed to a read can never exceed TabForge's per-track limit",
            AlphaTabBoundary.CreateImportSettings(int.MaxValue).Importer.MaxGp3To5BarCount == InputLimits.MaxMeasuresPerTrack &&
            AlphaTabBoundary.CreateImportSettings(0).Importer.MaxGp3To5BarCount == 1);
        // The undetermined default still refuses what upstream refuses, with upstream's message.
        var refusedByDefault = false;
        try { AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(SyntheticGuitarPro35.Write(500, 1_001), new AlphaTab.Settings()); }
        catch (Exception ex) { refusedByDefault = AlphaTabBoundary.IsBarCountRefusal(ex); }
        Check("long import: a default Settings object still refuses 1,001 bars exactly like upstream alphaTab", refusedByDefault);
    }

    /// <summary>SHA-256 prefix (16 hex characters) of the full alphaTab model dump of each short fixture, taken from the UNPATCHED upstream alphaTab 1.8.4 package.</summary>
    private static readonly (int Version, int Bars, int Tracks, bool Lyrics, string Hash)[] UpstreamModelHashes =
    {
        (300, 3, 1, false, "89fa329257ddb300"), (300, 40, 1, false, "280013128f70054c"), (300, 12, 1, true, "a4e9449f9da56530"), (300, 50, 3, false, "e0b3df91c7b59adc"),
        (400, 3, 1, false, "89fa329257ddb300"), (400, 40, 1, false, "280013128f70054c"), (400, 12, 1, true, "6ef5bacb918150f9"), (400, 50, 3, false, "e0b3df91c7b59adc"),
        (500, 3, 1, false, "7b98c49a248e175a"), (500, 40, 1, false, "4597d2b0623e7e6b"), (500, 12, 1, true, "f7e0d0c2a9fd40a7"), (500, 50, 3, false, "81e35576a61951bc"),
        (510, 3, 1, false, "7b98c49a248e175a"), (510, 40, 1, false, "4597d2b0623e7e6b"), (510, 12, 1, true, "f7e0d0c2a9fd40a7"), (510, 50, 3, false, "81e35576a61951bc"),
    };
    private const string UpstreamDemoModelHash = "9f6b76c57e1e0f8a";

    private static string ModelHash(AlphaTab.Model.Score score) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FullScoreDump(score)))).ToLowerInvariant()[..16];

    /// <summary>Valid short files: the whole model (every property alphaTab serialises, including lyrics and the demo song) is what the unpatched upstream package produced.</summary>
    private static void LongImportShortFilesUnchanged()
    {
        foreach (var (version, bars, tracks, lyrics, expected) in UpstreamModelHashes)
        {
            var bytes = SyntheticGuitarPro35.Write(version, bars, tracks, lyrics: lyrics);
            var viaBoundary = AlphaTabBoundary.LoadScore(bytes);
            var viaPlain = AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(bytes, new AlphaTab.Settings());
            var dump = FullScoreDump(viaBoundary);
            Check($"long import: {SyntheticGuitarPro35.VersionName(version)} {bars} bars x {tracks} track(s){(lyrics ? " with lyrics" : "")}: the full model equals the unpatched upstream result and the default-limit read",
                ModelHash(viaBoundary) == expected && dump == FullScoreDump(viaPlain), $"expected {expected}, got {ModelHash(viaBoundary)}");
            if (lyrics && version >= 400)   // Guitar Pro 3 files have no lyrics block
                Check($"long import: the {SyntheticGuitarPro35.VersionName(version)} lyrics fixture really carries its lyrics", dump.Contains("lalala", StringComparison.Ordinal));
        }
        var demo = FuzzFindSample("TabForge Demo - Ashen Meridian.gp5");
        if (demo is null) { Check("long import: the demo GP5 is present", false); return; }
        var demoModel = AlphaTabBoundary.LoadScore(demo);
        Check("long import: the demo GP5's full model equals the unpatched upstream result", ModelHash(demoModel) == UpstreamDemoModelHash, ModelHash(demoModel));
        Check("long import: the demo GP5 imports through GuitarProImporter as before (tracks, bars)",
            GuitarProImporter.ImportBytes(demo, "demo.gp5") is { Tracks.Count: > 0 } demoSong && demoSong.Tracks.All(t => t.Measures.Count == demoModel.MasterBars.Count));
    }

    private static string Describe(SongProject? song, string? error) => error ?? $"{song?.Tracks.Count} tracks, {song?.Tracks.FirstOrDefault()?.Measures.Count} bars";

    /// <summary>999, 1,000, 1,001 and 1,200 bars and the configured maximum, for GP3, GP4 and GP5 (5.00 and 5.10), imported through the real importer.</summary>
    private static void LongImportBarCounts()
    {
        foreach (var version in SyntheticGuitarPro35.Versions)
            foreach (var bars in new[] { 999, 1_000, 1_001, 1_200, InputLimits.MaxMeasuresPerTrack })
            {
                var name = $"{SyntheticGuitarPro35.VersionName(version)} with {bars:N0} bars";
                string? error = null;
                SongProject? song = null;
                var watch = Stopwatch.StartNew();
                try { song = GuitarProImporter.ImportBytes(SyntheticGuitarPro35.Write(version, bars), "long" + ImportExtension(version)); }
                catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
                var elapsed = watch.Elapsed.TotalSeconds;
                var lastNoteBar = bars % 2 == 0 ? bars - 1 : bars - 2;   // odd bars hold a note, even bars a rest
                var intact = song is { Tracks.Count: 1 } && song.Tracks[0].Measures.Count == bars &&
                    song.Tracks[0].Measures[lastNoteBar].Cells.Any(cell => cell.Notes.Any(note => note.Fret == SyntheticGuitarPro35.FretAt(lastNoteBar, 0))) &&
                    song.Tracks[0].Measures.Count(measure => measure.Cells.Any(cell => cell.Notes.Count > 0)) == bars / 2;
                Check($"long import: {name} opens with all of its bars and notes", intact, Describe(song, error));
                if (elapsed >= 2) Log.Add($"  time  long import {name}: {elapsed:0.0} s");
            }
    }

    /// <summary>A header above the limit is refused before any large allocation; a header that over-declares a short file is refused as truncated.</summary>
    private static void LongImportRefusals()
    {
        // What a full-size import allocates, for scale (the refusals below must be a small fraction of it).
        var fullBytes = SyntheticGuitarPro35.Write(500, InputLimits.MaxMeasuresPerTrack);
        var scaleBefore = GC.GetAllocatedBytesForCurrentThread();
        GuitarProImporter.ImportBytes(fullBytes, "full.gp5");
        var fullImportAllocation = GC.GetAllocatedBytesForCurrentThread() - scaleBefore;
        const long refusalBudget = 8L * 1024 * 1024;
        Check("long import: (scale) a maximum-length import allocates far more than a refusal may", fullImportAllocation > 16 * refusalBudget, $"{fullImportAllocation:N0} bytes");

        foreach (var version in SyntheticGuitarPro35.Versions)
        {
            var name = SyntheticGuitarPro35.VersionName(version);
            foreach (var declared in new[] { InputLimits.MaxMeasuresPerTrack + 1, 100_000, int.MaxValue })
            {
                var file = SyntheticGuitarPro35.Write(version, 3, declaredBars: declared);
                var before = GC.GetAllocatedBytesForCurrentThread();
                var refused = ThrowsInvalidData(() => GuitarProImporter.ImportBytes(file, "declared" + ImportExtension(version)), out var message);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check($"long import: a {name} header declaring {declared:N0} bars is refused as too long, before any large allocation",
                    refused && message.Contains("too many measures", StringComparison.Ordinal) && allocated < refusalBudget, $"{message} ({allocated:N0} bytes allocated)");
            }
            foreach (var declared in new[] { 1_001, 15_000, InputLimits.MaxMeasuresPerTrack })
            {
                var file = SyntheticGuitarPro35.Write(version, 3, declaredBars: declared);
                var before = GC.GetAllocatedBytesForCurrentThread();
                var refused = ThrowsInvalidData(() => GuitarProImporter.ImportBytes(file, "forged" + ImportExtension(version)), out var message);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check($"long import: a {name} file declaring {declared:N0} bars but holding 3 is refused as truncated, without a huge allocation",
                    refused && message.Contains("invalid, truncated", StringComparison.Ordinal) && allocated < refusalBudget, $"{message} ({allocated:N0} bytes allocated)");
            }
            var negative = SyntheticGuitarPro35.Write(version, 3, declaredBars: -5);
            Check($"long import: a {name} header declaring a negative bar count is refused", ThrowsInvalidData(() => GuitarProImporter.ImportBytes(negative, "negative" + ImportExtension(version)), out _));
        }
    }

    /// <summary>Many tracks cannot get past the cumulative limits just because each track is below its own limit.</summary>
    private static void LongImportTotalLimits()
    {
        foreach (var version in new[] { 300, 500 })
        {
            var name = SyntheticGuitarPro35.VersionName(version);
            var tracks = InputLimits.MaxTotalMeasures / InputLimits.MaxMeasuresPerTrack + 1;   // 6 tracks of 20,000 bars: each at its own limit, together over the total
            var manyBars = SyntheticGuitarPro35.Write(version, InputLimits.MaxMeasuresPerTrack, tracks);
            Check($"long import: {name} with {tracks} tracks of {InputLimits.MaxMeasuresPerTrack:N0} bars (each at its own limit) is refused for the total",
                ThrowsInvalidData(() => GuitarProImporter.ImportBytes(manyBars, "total" + ImportExtension(version)), out var totalError) &&
                totalError.Contains("too many measures overall", StringComparison.Ordinal), totalError);
        }
        // 100 tracks (alphaTab's own track limit) of 1,001 bars: 100,100 bars in total, one over.
        var crowded = SyntheticGuitarPro35.Write(500, 1_001, 100);
        Check("long import: 100 tracks of 1,001 bars (100,100 bars in all) is refused for the total",
            ThrowsInvalidData(() => GuitarProImporter.ImportBytes(crowded, "crowded.gp5"), out var crowdedError) &&
            crowdedError.Contains("too many measures overall", StringComparison.Ordinal), crowdedError);
        // alphaTab's own limit of 100 tracks still stands.
        var tooManyTracks = SyntheticGuitarPro35.Write(500, 3, 101);
        Check("long import: 101 tracks is refused (alphaTab's track threshold is untouched)",
            ThrowsInvalidData(() => GuitarProImporter.ImportBytes(tooManyTracks, "tracks.gp5"), out _));
    }

    /// <summary>Imports running at the same time with different limits and different files never see each other's limit or result.</summary>
    private static void LongImportConcurrent()
    {
        var long1200 = SyntheticGuitarPro35.Write(500, 1_200);
        var long1001 = SyntheticGuitarPro35.Write(300, 1_001);
        var short40 = SyntheticGuitarPro35.Write(400, 40, 2);
        const int rounds = 30;
        var problems = new ConcurrentBag<string>();
        using var barrier = new Barrier(5);
        void Loop(string name, Action<int> body) => Task.Run(() =>
        {
            for (var i = 0; i < rounds; i++)
            {
                barrier.SignalAndWait();
                try { body(i); }
                catch (Exception ex) { problems.Add($"{name} round {i}: {ex.GetType().Name}: {ex.Message}"); }
            }
        });
        var finished = new CountdownEvent(5);
        void Run(string name, Action<int> body) { Loop(name, i => { try { body(i); } finally { if (i == rounds - 1) finished.Signal(); } }); }
        Run("1,200 bars at limit 20,000", round => { if (AlphaTabBoundary.LoadScore(long1200, InputLimits.MaxMeasuresPerTrack).MasterBars.Count != 1_200) problems.Add("1,200-bar result changed"); });
        Run("1,200 bars at limit 1,000", round => { if (!ThrowsInvalidData(() => AlphaTabBoundary.LoadScore(long1200, 1_000), out var m) || !m.Contains("too many measures", StringComparison.Ordinal)) problems.Add("limit 1,000 accepted 1,200 bars"); });
        Run("1,001-bar GP3 at limit 1,001", round => { if (AlphaTabBoundary.LoadScore(long1001, 1_001).MasterBars.Count != 1_001) problems.Add("1,001-bar result changed"); });
        Run("1,001-bar GP3 at limit 1,000", round => { if (!ThrowsInvalidData(() => AlphaTabBoundary.LoadScore(long1001, 1_000), out _)) problems.Add("limit 1,000 accepted 1,001 bars"); });
        Run("short GP4 at default settings", round => { if (AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(short40, new AlphaTab.Settings()).MasterBars.Count != 40) problems.Add("short result changed"); });
        var allDone = finished.Wait(TimeSpan.FromSeconds(120));
        Check("long import: five concurrent imports with different files and limits finish, each with its own result and its own limit",
            allDone && problems.IsEmpty, allDone ? string.Join("; ", problems.Take(5)) : "timed out");
        Check("long import: after the concurrent imports a new Settings object is still at upstream's default", new AlphaTab.Settings().Importer.MaxGp3To5BarCount == 1000);
    }

    /// <summary>
    /// A realistic long song: 8 tracks x 2,000 bars, four notes a bar and a whole-bar rest in the second voice (as real Guitar Pro 5 files hold; a real
    /// 2,039-bar song is 8 tracks, 523,592 beat cells of which 86,272 carry content). A song keeps 16 cells per bar and voice, so most cells are empty. The
    /// project used to be written with every property of every cell (about 530 bytes per empty cell): such a song came to 265 MiB of JSON,
    /// over the 128 MiB limit although the file is 1 MB, so the import worker's reply, a .tforge save and an embedded .gp save all failed.
    /// </summary>
    private static void LongImportDenseSong(string folder)
    {
        const int tracks = 8, bars = 2_000, beats = 4;
        var bytes = SyntheticGuitarPro35.Write(500, bars, tracks, beatsPerBar: beats, secondVoiceRest: true);
        var path = Path.Combine(folder, "dense.gp5");
        File.WriteAllBytes(path, bytes);
        var project = GuitarProImporter.Import(path);
        var allCells = project.Tracks.Sum(t => t.Measures.Sum(m => (long)m.Cells.Count + m.Voice2Cells.Count));
        var content = project.Tracks.Sum(t => t.Measures.Sum(m => (long)m.Cells.Concat(m.Voice2Cells).Count(c => c.Notes.Count > 0 || c.IsRest || c.HasAnnotation)));
        var notes = project.Tracks.Sum(t => t.Measures.Sum(m => (long)m.Cells.Sum(c => c.Notes.Count)));
        Check("long import: the dense fixture is a small file with a real song's shape (8 tracks x 2,000 bars, 4 notes a bar, most cells empty)",
            bytes.Length < 4 * 1024 * 1024 && project.Tracks.Count == tracks && project.Tracks.All(t => t.Measures.Count == bars) && notes == (long)tracks * bars * beats && content * 5 < allCells,
            $"{bytes.Length:N0} bytes, {project.Tracks.Count} tracks, {notes:N0} notes, {content:N0} of {allCells:N0} cells with content");

        // The old representation (every property of every cell) for this song, from one empty cell: over the limit. Without this the fixture proves nothing.
        var fullEmptyCell = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new TabCell()).Length;
        Check("long import: with every property of every cell written (the old form) this song would be over the 128 MiB limit",
            allCells * (fullEmptyCell + 1) > InputLimits.MaxTforgeFileBytes, $"{allCells:N0} cells x {fullEmptyCell} bytes = {allCells * (fullEmptyCell + 1) / 1048576.0:0} MiB");
        var (disk, compact) = ProjectService.MeasureJsonBytes(project);
        Check("long import: the written form of the same song is far below the limit, and the compact transfer form below that",
            disk < InputLimits.MaxTforgeFileBytes / 2 && compact < disk, $"disk {disk / 1048576.0:0.0} MiB, compact {compact / 1048576.0:0.0} MiB, limit {InputLimits.MaxTforgeFileBytes / 1048576} MiB");

        // The out-of-process import hands the song back (what failed with "The TabForge project exceeds the 128 MiB size limit").
        SongProject? viaWorker = null;
        string? error = null;
        try { viaWorker = ImportWorker.Import(path); }
        catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
        Check("long import: the dense song opens through the import worker and equals the in-process import", viaWorker is not null && SameImportedSong(viaWorker, project), error);

        // Saving it: a .tforge, and the project embedded in a .gp (the same writer), and reading both back.
        var tforge = Path.Combine(folder, "dense.tforge");
        string? saveError = null;
        try { ProjectService.Save(tforge, project); }
        catch (Exception ex) { saveError = ex.GetType().Name + ": " + ex.Message; }
        var reloaded = saveError is null ? ProjectService.Load(tforge) : null;
        Check("long import: the dense song saves as a .tforge and reads back identically", reloaded is not null && ProjectService.ContentHash(reloaded).AsSpan().SequenceEqual(ProjectService.ContentHash(project)), saveError);
        SongProject? embedded = null;
        try { embedded = ProjectService.RestorePersistedBytes(ProjectService.PersistBytes(project)); }
        catch (Exception ex) { saveError = ex.GetType().Name + ": " + ex.Message; }
        Check("long import: the dense song's embedded-project form (a .gp saved with the project) writes and reads back identically",
            embedded is not null && ProjectService.ContentHash(embedded).AsSpan().SequenceEqual(ProjectService.ContentHash(project)), saveError);

        // Files written by earlier releases (every cell property present) still read, and a cell's defaults stay frozen: an empty cell is written as {}.
        var small = GuitarProImporter.Import(WriteDense(folder, "dense-small.gp5", 2, 300));
        var legacyJson = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(small, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = InputLimits.MaxJsonDepth });
        var legacyPath = Path.Combine(folder, "legacy-full.tforge");
        using (var file = File.Create(legacyPath))
        using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
            gzip.Write(legacyJson);
        var legacy = ProjectService.Load(legacyPath);
        Check("long import: a .tforge written with every cell property (an earlier release) still reads as the same song",
            ProjectService.ContentHash(legacy).AsSpan().SequenceEqual(ProjectService.ContentHash(small)));
        var plain = new TabCell();
        Check("long import: the beat-cell defaults the compact written form relies on are frozen (changing one needs a FormatVersion bump and a migration)",
            plain.Notes.Count == 0 && plain.RhythmicPosition is null && plain.DurationDenominator == 8 && plain.Dots == 0 && !plain.IsTriplet && plain.TupletNumerator == 0 && plain.TupletDenominator == 0
            && !plain.IsRest && !plain.IsTied && plain.SoundDurationPercent == 100 && plain.OctaveShiftSemitones == 0 && !plain.BreakSecondaryBeamBefore && !plain.IsGrace && plain.GraceBeforeBeat
            && !plain.Fermata && plain.Accent == 0 && !plain.Staccato && !plain.Tenuto && plain.WhammyPoints.Count == 0 && plain.TremoloPickDenominator == 0 && plain.BrushStepSlots == 0
            && plain.ChordName is null && plain.Text is null && plain.Lyrics == "" && plain.Mix is null);
        Check("long import: an empty beat cell is written as {} and a cell with a note keeps every field of its note",
            System.Text.Encoding.UTF8.GetString(ProjectService.MeasureCell(new TabCell())) == "{}"
            && System.Text.Encoding.UTF8.GetString(ProjectService.MeasureCell(new TabCell { Notes = { new TabNote { StringIndex = 2, Fret = 7 } } })).Contains("\"Velocity\":100", StringComparison.Ordinal),
            System.Text.Encoding.UTF8.GetString(ProjectService.MeasureCell(new TabCell())) + " | " + System.Text.Encoding.UTF8.GetString(ProjectService.MeasureCell(new TabCell { Notes = { new TabNote { StringIndex = 2, Fret = 7 } } })));

        // The message names what was too large (the expanded data), not the file.
        using var tiny = new HashingLimitStream(Stream.Null, 1_000, hash: false, what: "imported song (8 tracks, 2,039 bars)");
        string? sizeMessage = null;
        try { tiny.Write(new byte[2_000]); } catch (InvalidDataException ex) { sizeMessage = ex.Message; }
        Check("long import: the size message names the imported song's expanded project data and says the limit is not on the file",
            sizeMessage is not null && sizeMessage.Contains("imported song (8 tracks, 2,039 bars)", StringComparison.Ordinal) && sizeMessage.Contains("expanded project data", StringComparison.Ordinal)
            && sizeMessage.Contains("not apply to the size of the file", StringComparison.Ordinal), sizeMessage);
    }

    private static string WriteDense(string folder, string name, int tracks, int bars)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, SyntheticGuitarPro35.Write(500, bars, tracks, beatsPerBar: 4, secondVoiceRest: true));
        return path;
    }

    private static Assembly DynamicAssembly(string name, Version version) =>
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name) { Version = version }, AssemblyBuilderAccess.Run);

    /// <summary>A reader component that is missing, the wrong build or lacks what TabForge needs fails in a controlled, explained way.</summary>
    private static void LongImportMissingDependency()
    {
        Check("long import: the installed reader component is accepted by the boundary check", AlphaTabBoundary.Inspect(typeof(AlphaTab.Importer.ScoreLoader).Assembly) is null);
        var upstream = AlphaTabBoundary.Inspect(DynamicAssembly("AlphaTab", new Version(1, 8, 4, 34)));
        Check("long import: an upstream-named AlphaTab assembly is refused as the wrong component", upstream?.Contains("is not the one TabForge needs", StringComparison.Ordinal) == true, upstream);
        var other = AlphaTabBoundary.Inspect(typeof(object).Assembly);
        Check("long import: an unrelated assembly is refused as the wrong component", other?.Contains("is not the one TabForge needs", StringComparison.Ordinal) == true, other);
        var old = AlphaTabBoundary.Inspect(DynamicAssembly(AlphaTabBoundary.RequiredAssemblyName, new Version(1, 8, 3, 1)));
        Check("long import: the patched assembly at another alphaTab version is refused", old?.Contains("wrong version", StringComparison.Ordinal) == true, old);
        var oldPatch = AlphaTabBoundary.Inspect(DynamicAssembly(AlphaTabBoundary.RequiredAssemblyName, new Version(1, 8, 4, 0)));
        Check("long import: an unpatched build number of the patched assembly is refused", oldPatch?.Contains("wrong version", StringComparison.Ordinal) == true, oldPatch);
        var secondPatch = AlphaTabBoundary.Inspect(DynamicAssembly(AlphaTabBoundary.RequiredAssemblyName, new Version(1, 8, 4, 2)));
        Check("long import: the second patch revision (1.8.4.2, no trill speed) is refused", secondPatch?.Contains("wrong version", StringComparison.Ordinal) == true, secondPatch);
        var firstPatch =AlphaTabBoundary.Inspect(DynamicAssembly(AlphaTabBoundary.RequiredAssemblyName, new Version(1, 8, 4, 1)));
        Check("long import: the first patch revision (1.8.4.1, no exact mixer values) is refused", firstPatch?.Contains("wrong version", StringComparison.Ordinal) == true, firstPatch);
        var noLimit = AlphaTabBoundary.Inspect(DynamicAssembly(AlphaTabBoundary.RequiredAssemblyName, AlphaTabBoundary.RequiredVersion));
        Check("long import: the right name and version without the bar-limit setting is refused", noLimit?.Contains("lacks the per-file bar limit", StringComparison.Ordinal) == true, noLimit);

        // Through the real import: a refused component is a clear import problem, never a wrong or empty song.
        var good = SyntheticGuitarPro35.Write(500, 5);
        AlphaTabBoundary.ProblemOverride = "The Guitar Pro reader component is not the one TabForge needs (test). Reinstall TabForge.";
        try
        {
            string? message = null;
            SongProject? song = null;
            try { song = GuitarProImporter.ImportBytes(good, "component.gp5"); }
            catch (InvalidDataException ex) { message = ex.Message; }
            Check("long import: with an unusable reader component the import fails with that explanation and opens nothing",
                song is null && message?.Contains("Reinstall TabForge", StringComparison.Ordinal) == true, message ?? "opened a song");
        }
        finally { AlphaTabBoundary.ProblemOverride = null; }
        Check("long import: after the seam is cleared the same file imports again", GuitarProImporter.ImportBytes(good, "component.gp5").Tracks.Count == 1);
    }

    /// <summary>The worker process: the same result as in-process, a refused header reaches the caller, Cancel and the deadline end the worker, nothing is left running.</summary>
    private static void LongImportWorker(string folder)
    {
        var long1200 = Path.Combine(folder, "long-1200.gp5");
        File.WriteAllBytes(long1200, SyntheticGuitarPro35.Write(500, 1_200));
        var direct = GuitarProImporter.Import(long1200);
        SongProject? viaWorker = null;
        string? error = null;
        try { viaWorker = ImportWorker.Import(long1200); }
        catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
        Check("long import: a 1,200-bar GP5 imported in the worker process equals the in-process import (no fallback)",
            viaWorker is { Tracks.Count: 1 } && viaWorker.Tracks[0].Measures.Count == 1_200 && SameImportedSong(viaWorker, direct), error);

        var tooLong = Path.Combine(folder, "too-long.gp3");
        File.WriteAllBytes(tooLong, SyntheticGuitarPro35.Write(300, 3, declaredBars: InputLimits.MaxMeasuresPerTrack + 1));
        string? tooLongMessage = null;
        try { ImportWorker.Import(tooLong); }
        catch (InvalidDataException ex) { tooLongMessage = ex.Message; }
        Check("long import: a header above the limit is refused inside the worker and the message reaches the caller",
            tooLongMessage?.Contains("too many measures", StringComparison.Ordinal) == true, tooLongMessage);

        // Cancel ends a worker holding a long file (the parse is simulated as stuck), and its process is gone.
        var controller = new DocumentController();
        Process? hung = null;
        using (var cancel = new CancellationTokenSource())
        {
            var hangOptions = new ImportWorkerOptions { TestHang = true, Started = p => hung = Process.GetProcessById(p.Id) };
            var stuck = ScoreImportQueue.ImportAsync(long1200, path => controller.Open(path, (file, _) => ImportWorker.Import(file, hangOptions)),
                cancel.Token, ImportGuard.DefaultTimeBudget, ImportGuard.DefaultMemoryBudgetBytes, TimeSpan.FromSeconds(5));
            var startWatch = Stopwatch.StartNew();
            while (hung is null && startWatch.ElapsedMilliseconds < 15_000) Thread.Sleep(20);
            Thread.Sleep(300);
            var alive = hung is { HasExited: false };
            cancel.Cancel();
            var cancelled = false;
            try { stuck.Wait(TimeSpan.FromSeconds(5)); }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException) { cancelled = true; }
            var gone = hung?.WaitForExit(5_000) == true;
            Check("long import: Cancel ends the worker that holds a long file and its process is gone", alive && cancelled && gone, $"alive {alive}, cancelled {cancelled}, gone {gone}");
            hung?.Dispose();
        }

        // The deadline ends it too.
        hung = null;
        var budgetOptions = new ImportWorkerOptions { TestHang = true, KillGrace = TimeSpan.FromMilliseconds(100), Started = p => hung = Process.GetProcessById(p.Id) };
        var timed = ScoreImportQueue.ImportAsync(long1200, path => controller.Open(path, (file, _) => ImportWorker.Import(file, budgetOptions)),
            CancellationToken.None, TimeSpan.FromMilliseconds(1_500), ImportGuard.DefaultMemoryBudgetBytes, TimeSpan.FromSeconds(3));
        var timedOut = false;
        try { timed.Wait(TimeSpan.FromSeconds(15)); }
        catch (AggregateException ex) when (ex.InnerException is TimeoutException) { timedOut = true; }
        var timedGone = hung?.WaitForExit(5_000) == true;
        Check("long import: the deadline ends the worker that holds a long file and its process is gone", timedOut && timedGone, $"timed out {timedOut}, gone {timedGone}");
        hung?.Dispose();

        // A real, heavy parse (not simulated) against a short deadline: stopped, with no worker left behind.
        var heavy = Path.Combine(folder, "heavy.gp5");
        var heavyBytes = SyntheticGuitarPro35.Write(500, InputLimits.MaxMeasuresPerTrack, 5);
        File.WriteAllBytes(heavy, heavyBytes);
        // The deadline is a quarter of what this machine needs for the same import in-process, so the parse overruns it however fast the machine is.
        var heavyWatch = Stopwatch.StartNew();
        GuitarProImporter.ImportBytes(heavyBytes, "heavy.gp5");
        var heavyDeadline = TimeSpan.FromMilliseconds(Math.Max(50, heavyWatch.Elapsed.TotalMilliseconds / 4));
        Log.Add($"  info  long import: the heavy 5 x 20,000-bar import takes {heavyWatch.Elapsed.TotalSeconds:0.0} s in-process; the worker deadline test uses {heavyDeadline.TotalSeconds:0.00} s");
        hung = null;
        var heavyOptions = new ImportWorkerOptions { KillGrace = TimeSpan.FromMilliseconds(100), Started = p => hung = Process.GetProcessById(p.Id) };
        string? heavyOutcome = null;
        var heavyGuard = new ImportGuard(CancellationToken.None, heavyDeadline);
        try { using (heavyGuard.Enter()) ImportWorker.Import(heavy, heavyOptions); heavyOutcome = "finished"; }
        catch (TimeoutException) { heavyOutcome = "timeout"; }
        catch (InvalidDataException ex) when (ex.Message.Contains("took longer", StringComparison.Ordinal) || ex.Message.Contains("stopped unexpectedly", StringComparison.Ordinal)) { heavyOutcome = "stopped"; }
        var heavyGone = hung?.WaitForExit(5_000) == true;
        Check("long import: a real heavy parse that overruns its deadline is stopped and its worker process is gone", heavyOutcome is "timeout" or "stopped" && heavyGone, $"{heavyOutcome}, gone {heavyGone}");
        hung?.Dispose();
    }

    /// <summary>Every property of the score as alphaTab serialises it (JsonConverter.ScoreToJsObject), as text.</summary>
    private static string FullScoreDump(AlphaTab.Model.Score score)
    {
        var text = new StringBuilder();
        void Dump(object? value, int depth)
        {
            if (depth > 64) { text.Append("<deep>"); return; }
            switch (value)
            {
                case null: text.Append("null"); return;
                case string s: text.Append('"').Append(s).Append('"'); return;
                case IFormattable f when value.GetType().IsPrimitive || value is decimal || value.GetType().IsEnum:
                    text.Append(f.ToString(null, System.Globalization.CultureInfo.InvariantCulture)); return;
                case bool b: text.Append(b ? "true" : "false"); return;
                case System.Collections.IEnumerable items:
                    text.Append('[');
                    foreach (var item in items)
                    {
                        // The two properties patch 0002 adds to PlaybackInformation do not exist in upstream alphaTab, whose dump the recorded hashes come from.
                        if (item?.GetType().GetProperty("Key")?.GetValue(item) is string entryKey &&
                            (entryKey.Equals("volumeFraction", StringComparison.OrdinalIgnoreCase) || entryKey.Equals("balanceFraction", StringComparison.OrdinalIgnoreCase))) continue;
                        Dump(item, depth + 1); text.Append(',');
                    }
                    text.Append(']'); return;
            }
            var type = value.GetType();
            if (type.GetProperty("Key") is { } key && type.GetProperty("Value") is { } val)
            {
                Dump(key.GetValue(value), depth + 1); text.Append(':'); Dump(val.GetValue(value), depth + 1); return;
            }
            text.Append(type.Name).Append(':').Append(value);
        }
        Dump(AlphaTab.Model.JsonConverter.ScoreToJsObject(score), 0);
        return text.ToString();
    }
}
