using System.Diagnostics;
using System.IO;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Autosave (review item 3): recovery copies keep coming during long playback, failures are visible, a failed write keeps the previous
/// copy, and closing or moving a tab while a write is pending is safe.
/// </summary>
public static partial class SelfTest
{
    private static AutosavePassResult? RunAutosavePass(AutosaveRunner runner, IReadOnlyList<DocumentSession> docs, string folder, Func<DateTime>? clock = null) =>
        Task.Run(() => runner.RunPassAsync(docs, folder, clock)).GetAwaiter().GetResult();

    private static DocumentSession DirtyDocument(int tracks, int bars, int seed, string title)
    {
        var project = RichSong(tracks, bars, seed);
        project.Title = title;
        project.IsDirty = true;
        return DocumentSession.FromProject(project, null);
    }

    private static string? AutosavePathOf(DocumentSession doc) => AutosaveRegistry.Find(doc) is { } s ? s.Path : null;

    private static string AutosaveText(string path, int track, int bar) => ProjectService.Load(path).Tracks[track].Measures[bar].Cells[0].Text ?? "";

    private static void TestAutosaveRecovery()
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"tf-autosave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        var realWriter = AutosaveRunner.WriteCopy;
        AutosaveRegistry.ResetForTests();
        try
        {
            AutosaveSnapshotIsPrivateAndExact();
            AutosaveContinuesDuringPlayback(Path.Combine(scratch, "playback"));
            AutosaveFailuresAreVisible(Path.Combine(scratch, "failures"));
            AutosaveFailedWriteKeepsPreviousCopy(Path.Combine(scratch, "keep"));
            AutosaveCloseAndMoveDuringWrite(Path.Combine(scratch, "close"), realWriter);
            AutosaveRecoveredCopyKeptUntilDurable(Path.Combine(scratch, "recovered"));
            AutosaveRenameLeavesOneCopy(Path.Combine(scratch, "rename"));
            AutosaveLargeProjectTiming(Path.Combine(scratch, "large"));
        }
        finally
        {
            AutosaveRunner.WriteCopy = realWriter;
            FilePathPolicy.FaultInjection = null;
            AutosaveRegistry.ResetForTests();
            try { Directory.Delete(scratch, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // The worker serialises a private song rebuilt from an immutable state: identical to the live song, never the live object.
    private static void AutosaveSnapshotIsPrivateAndExact()
    {
        var live = RichSong(3, 20, 5);
        var state = new UndoController().Snapshot(live).State;
        var copy = new ProjectStateEncoder().Restore(state, live: null, validate: false);
        live.Tracks[0].Measures[0].Cells[0].Text = "changed after the capture";
        Check("autosave: the snapshot is an immutable capture; the private song the worker writes is exact and is not the live model",
            !ReferenceEquals(copy, live) && !ReferenceEquals(copy.Tracks[0], live.Tracks[0]) && copy.Tracks[0].Measures[0].Cells[0].Text != "changed after the capture"
            && ProjectService.Snapshot(copy) == ProjectService.Snapshot(new ProjectStateEncoder().Restore(state, null, false)));
    }

    private static void AutosaveContinuesDuringPlayback(string folder)
    {
        var doc = DirtyDocument(4, 40, 7, "Long playback");
        var runner = new AutosaveRunner();
        var t0 = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var planner = new AutosavePlanner(t0);
        var passes = 0;
        var lastEdit = "";
        var ok = true;
        // 20 minutes of continuous playback, a tick every 20 s, an edit every tick, a 2-minute interval (so a pass every 4 minutes).
        for (var tick = 1; tick <= 60; tick++)
        {
            var now = t0.AddSeconds(20 * tick);
            lastEdit = $"edit {tick}";
            doc.Project.Tracks[0].Measures[tick % 40].Cells[0].Text = lastEdit;
            if (!planner.Due(now, 2, playing: true, retrying: runner.Health.Failing)) continue;
            planner.Ran(now);
            var result = RunAutosavePass(runner, new[] { doc }, folder, () => now);
            passes++;
            var path = AutosavePathOf(doc);
            ok &= result is { Written: 1, Failed: 0 } && path is not null && File.Exists(path) && AutosaveText(path, 0, tick % 40) == lastEdit;
        }
        Check("autosave: during 20 minutes of continuous playback a dirty song still gets a fresh recovery copy every 2x interval, each holding the latest edit",
            passes == 5 && ok && !runner.Health.Failing && runner.Health.LastSuccessUtc == t0.AddMinutes(20), $"passes {passes}, ok {ok}");
    }

    private static void AutosaveFailuresAreVisible(string folder)
    {
        var real = AutosaveRunner.WriteCopy;
        var doc = DirtyDocument(2, 10, 3, "Failing");
        var runner = new AutosaveRunner();
        var clock = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            RunAutosavePass(runner, new[] { doc }, folder, () => clock);
            var firstSuccess = runner.Health.LastSuccessUtc;
            clock = clock.AddMinutes(10);
            doc.Project.Tracks[0].Measures[0].Cells[0].Text = "more";
            AutosaveRunner.WriteCopy = (_, _) => throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
            var full = RunAutosavePass(runner, new[] { doc }, folder, () => clock);
            var health = runner.Health;
            var line = health.Describe(clock);
            Check("autosave: a full disk is reported (kind, reason, last good copy, retry), the last attempt and the last success are tracked separately",
                full is { Failed: 1 } && health.Failing && health.FailureKind == AutosaveFailureKind.DiskFull && health.LastAttemptUtc == clock
                && health.LastSuccessUtc == firstSuccess && line is not null && line.Contains("disk is full") && line.Contains("10 min") && line.Contains("Retrying"), line);

            AutosaveRunner.WriteCopy = (_, _) => throw new UnauthorizedAccessException("Access to the path is denied.");
            clock = clock.AddMinutes(1);
            RunAutosavePass(runner, new[] { doc }, folder, () => clock);
            Check("autosave: an access-denied Recovery folder is reported, and repeated failures are counted",
                health.FailureKind == AutosaveFailureKind.AccessDenied && health.ConsecutiveFailures == 2 && health.Describe(clock)!.Contains("denied"));
            Check("autosave: the failure notice text distinguishes the two reasons and names the retry",
                AutosaveHealth.Classify(new IOException("x", unchecked((int)0x80070027))) == AutosaveFailureKind.DiskFull
                && AutosaveHealth.Classify(new IOException("x", unchecked((int)0x80070005))) == AutosaveFailureKind.AccessDenied
                && AutosaveHealth.Classify(new InvalidDataException("bad")) == AutosaveFailureKind.Other);

            AutosaveRunner.WriteCopy = real;
            clock = clock.AddMinutes(1);
            var retry = RunAutosavePass(runner, new[] { doc }, folder, () => clock);
            Check("autosave: the retry that succeeds clears the notice and moves the last success forward",
                retry is { Written: 1 } && !health.Failing && health.Describe(clock) is null && health.LastSuccessUtc == clock);
        }
        finally { AutosaveRunner.WriteCopy = real; }
    }

    private static void AutosaveFailedWriteKeepsPreviousCopy(string folder)
    {
        var doc = DirtyDocument(2, 10, 4, "Keep me");
        var runner = new AutosaveRunner();
        doc.Project.Tracks[0].Measures[1].Cells[0].Text = "first";
        RunAutosavePass(runner, new[] { doc }, folder);
        var path = AutosavePathOf(doc)!;
        var before = File.ReadAllBytes(path);
        var failures = 0;
        var kept = true;
        foreach (var stage in new[] { "staged:", "commit:" })
        {
            doc.Project.Tracks[0].Measures[1].Cells[0].Text = "second " + stage;
            FilePathPolicy.FaultInjection = s => { if (s.StartsWith(stage, StringComparison.Ordinal)) throw new IOException("injected " + stage); };
            try { if (RunAutosavePass(runner, new[] { doc }, folder) is { Failed: 1 }) failures++; }
            finally { FilePathPolicy.FaultInjection = null; }
            kept &= File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(before) && AutosaveText(path, 0, 1) == "first"
                    && Directory.GetFiles(folder, ".tabforge-*").Length == 0;
        }
        var recovered = RunAutosavePass(runner, new[] { doc }, folder);
        Check("autosave: a write failing at stage or commit is reported and leaves the previous recovery copy byte-identical (no temp file left); the next pass replaces it",
            failures == 2 && kept && recovered is { Written: 1 } && AutosaveText(path, 0, 1).StartsWith("second", StringComparison.Ordinal), $"failures {failures}, kept {kept}");
    }

    private static void AutosaveCloseAndMoveDuringWrite(string folder, Func<SongProject, string, string?> realWriter)
    {
        // Close while a write is in flight: no crash, and the writer (not the closer) removes the copy once it has finished.
        var doc = DirtyDocument(2, 10, 5, "Closing");
        var runner = new AutosaveRunner();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        AutosaveRunner.WriteCopy = (project, path) => { entered.Set(); release.Wait(TimeSpan.FromSeconds(20)); return realWriter(project, path); };
        try
        {
            var pass = Task.Run(() => runner.RunPassAsync(new[] { doc }, folder));
            var started = entered.Wait(TimeSpan.FromSeconds(20));
            AutosaveRegistry.Retire(new[] { doc });   // the tab is closed now
            var busyWhileWriting = !AutosaveRegistry.WaitForIdle(TimeSpan.FromMilliseconds(100));
            release.Set();
            AutosavePassResult? result = null;
            var threw = false;
            try { result = pass.GetAwaiter().GetResult(); } catch (Exception) { threw = true; }
            var idle = AutosaveRegistry.WaitForIdle(TimeSpan.FromSeconds(10));
            Check("autosave: closing a tab while its copy is being written does not crash, exit waits for the write, and no copy is left behind",
                started && busyWhileWriting && !threw && result is not null && idle && Directory.GetFiles(folder, "autosave-*").Length == 0 && AutosaveRegistry.Find(doc) is null,
                $"started {started}, busy {busyWhileWriting}, threw {threw}, files {Directory.GetFiles(folder, "autosave-*").Length}");
        }
        finally { AutosaveRunner.WriteCopy = realWriter; }

        AutosaveRegistry.ResetForTests();
        // Move between windows: a document missing from every window keeps its copy through the grace period, and a move keeps it for good.
        var moving = DirtyDocument(2, 10, 6, "Moving");
        var other = DirtyDocument(1, 4, 8, "Other");
        RunAutosavePass(runner, new[] { moving, other }, folder);
        var movingCopy = AutosavePathOf(moving)!;
        var t = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var pending = AutosaveRegistry.Reconcile(new[] { other }, t);                       // between windows
        var keptDuringGrace = File.Exists(movingCopy);
        AutosaveRegistry.Reconcile(new[] { other, moving }, t.AddSeconds(1));                // arrived in the other window
        AutosaveRegistry.Reconcile(new[] { other }, t.AddSeconds(30));                       // (missing again: the clock restarts)
        var keptAfterMove = File.Exists(movingCopy);
        AutosaveRegistry.Reconcile(new[] { other }, t.AddSeconds(30) + AutosaveRegistry.MissingGrace);   // really closed
        Check("autosave: a tab moved between windows keeps its recovery copy (no deletion inside the grace period); a closed tab's copy is removed after it",
            pending == 1 && keptDuringGrace && keptAfterMove && !File.Exists(movingCopy) && AutosaveRegistry.Find(moving) is null
            && AutosavePathOf(other) is { } otherCopy && File.Exists(otherCopy));
        AutosaveRegistry.Retire(new[] { other });
    }

    private static void AutosaveRecoveredCopyKeptUntilDurable(string folder)
    {
        var real = AutosaveRunner.WriteCopy;
        var runner = new AutosaveRunner();
        string Recovered(string name)
        {
            var file = AutosaveService.FileFor(folder, 999_999, Guid.NewGuid(), name);
            App.WriteRecoveryCopy(RichSong(1, 4, 9), file);
            return file;
        }
        var file1 = Recovered("Crashed one");
        var doc1 = DirtyDocument(1, 4, 9, "Recovered one");
        AutosaveRegistry.Adopt(doc1, file1);
        try
        {
            AutosaveRunner.WriteCopy = (_, _) => throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
            RunAutosavePass(runner, new[] { doc1 }, folder);
            var keptOnFailure = File.Exists(file1);
            AutosaveRunner.WriteCopy = real;
            RunAutosavePass(runner, new[] { doc1 }, folder);
            Check("autosave: an opened recovery copy stays while the session has no durable copy of its own, and goes once its own autosave is written",
                keptOnFailure && !File.Exists(file1) && AutosavePathOf(doc1) is { } own && File.Exists(own));

            var file2 = Recovered("Crashed two");
            var doc2 = DirtyDocument(1, 4, 10, "Recovered two");
            AutosaveRegistry.Adopt(doc2, file2);
            doc2.Project.IsDirty = false;   // saved by hand
            RunAutosavePass(runner, new[] { doc2 }, folder);
            var file3 = Recovered("Crashed three");
            var doc3 = DirtyDocument(1, 4, 11, "Recovered three");
            AutosaveRegistry.Adopt(doc3, file3);
            AutosaveRegistry.Retire(new[] { doc3 });   // closed, discarding
            Check("autosave: an opened recovery copy is removed when the song is saved or its tab is closed", !File.Exists(file2) && !File.Exists(file3));
        }
        finally { AutosaveRunner.WriteCopy = real; AutosaveRegistry.Retire(new[] { doc1 }); }
    }

    private static void AutosaveRenameLeavesOneCopy(string folder)
    {
        var doc = DirtyDocument(1, 4, 12, "Before");
        var runner = new AutosaveRunner();
        RunAutosavePass(runner, new[] { doc }, folder);
        doc.Project.Title = "After";
        RunAutosavePass(runner, new[] { doc }, folder);
        var files = Directory.GetFiles(folder, "autosave-*.tforge");
        Check("autosave: renaming a song leaves exactly one recovery copy (under the new name)",
            files.Length == 1 && Path.GetFileName(files[0]).Contains("After", StringComparison.Ordinal), string.Join(", ", files.Select(Path.GetFileName)));
        AutosaveRegistry.Retire(new[] { doc });
    }

    private static void AutosaveLargeProjectTiming(string folder)
    {
        var generate = Stopwatch.StartNew();
        var doc = DirtyDocument(40, 1000, 99, "Large");
        var generated = generate.Elapsed.TotalMilliseconds;
        var runner = new AutosaveRunner();
        var cold = RunAutosavePass(runner, new[] { doc }, folder)!;                   // first capture: nothing to reuse
        var warm = new List<double>();
        AutosavePassResult? last = null;
        for (var run = 0; run < 3; run++)
        {
            doc.Project.Tracks[run].Measures[500].Cells[0].Text = "warm " + run;
            last = RunAutosavePass(runner, new[] { doc }, folder)!;
            warm.Add(last.CaptureMs);
        }
        var blocking = Stopwatch.StartNew();
        ProjectService.SnapshotBytes(doc.Project);                                    // what autosave used to do on the UI thread
        var oldMs = blocking.Elapsed.TotalMilliseconds;
        var warmMs = warm.Order().ElementAt(1);
        Log.Add($"  info  autosave 40 tracks x 1000 bars (generated in {generated:0} ms): UI-thread capture cold {cold.CaptureMs:0} ms, warm median {warmMs:0} ms; " +
                $"worker rebuild+serialise+write cold {cold.WriteMs:0} ms, warm {last!.WriteMs:0} ms; the old UI-thread SnapshotBytes took {oldMs:0} ms");
        Check("autosave: on a 40-track x 1000-bar song the UI-thread part (capture) is far cheaper than the old serialise+compress it replaces, and the copy is written (this synthetic song is denser than the 128 MiB load limit allows, so it is not re-loaded)",
            cold.Written == 1 && last.Written == 1 && warmMs < oldMs && AutosavePathOf(doc) is { } big && new FileInfo(big).Length > 1_000_000,
            $"capture warm {warmMs:0} ms vs old {oldMs:0} ms");
        AutosaveRegistry.Retire(new[] { doc });
    }
}
