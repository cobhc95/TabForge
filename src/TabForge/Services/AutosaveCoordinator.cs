using System.Diagnostics;
using System.IO;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

public enum AutosaveFailureKind { None, DiskFull, AccessDenied, Other }

// Owns: the autosave health record and the pass that copies dirty songs to the recovery folder.
// Does not own: the timer and notices (AutosaveController) and the file naming rules (AutosaveService).
// Tests: TestAutosaveRecovery.
/// <summary>
/// What the autosave has achieved so far: the last attempt and the last SUCCESS are tracked separately, so a failing autosave is
/// visible (status-bar notice) instead of only reaching the debug output. Pure, so it can be tested without a window.
/// </summary>
public sealed class AutosaveHealth
{
    public DateTime? LastAttemptUtc { get; private set; }
    public DateTime? LastSuccessUtc { get; private set; }
    public AutosaveFailureKind FailureKind { get; private set; }
    public string? FailureDetail { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public bool Failing => ConsecutiveFailures > 0;

    public void Succeeded(DateTime now)
    {
        LastAttemptUtc = LastSuccessUtc = now;
        Clear();
    }

    public void Failed(DateTime now, Exception error)
    {
        LastAttemptUtc = now;
        ConsecutiveFailures++;
        FailureKind = Classify(error);
        FailureDetail = error.Message;
    }

    /// <summary>Nothing needed saving (or everything was saved by hand): nothing is at risk, so any earlier failure notice goes.</summary>
    public void Clear()
    {
        ConsecutiveFailures = 0;
        FailureKind = AutosaveFailureKind.None;
        FailureDetail = null;
    }

    public static AutosaveFailureKind Classify(Exception error)
    {
        for (Exception? e = error; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException) return AutosaveFailureKind.AccessDenied;
            if (e is not IOException io) continue;
            var code = io.HResult & 0xFFFF;   // the Win32 error inside an HRESULT
            if (code is 0x27 or 0x70) return AutosaveFailureKind.DiskFull;   // ERROR_HANDLE_DISK_FULL, ERROR_DISK_FULL
            if (code == 5) return AutosaveFailureKind.AccessDenied;          // ERROR_ACCESS_DENIED
        }
        return AutosaveFailureKind.Other;
    }

    /// <summary>The line the status bar shows while failing (null when all is well).</summary>
    public string? Describe(DateTime now)
    {
        if (!Failing) return null;
        var reason = FailureKind switch
        {
            AutosaveFailureKind.DiskFull => "the disk is full",
            AutosaveFailureKind.AccessDenied => "access to the Recovery folder was denied",
            _ => string.IsNullOrWhiteSpace(FailureDetail) ? "unknown error" : FailureDetail!.Trim(),
        };
        var last = LastSuccessUtc is { } ok ? $"Last recovery copy: {Ago(now - ok)} ago" : "No recovery copy yet";
        return $"Autosave failed: {reason}. {last}; the previous copy is kept. Retrying every {AutosavePlanner.RetryMinutes} min.";
    }

    private static string Ago(TimeSpan span) =>
        span < TimeSpan.FromMinutes(1) ? "under a minute" :
        span < TimeSpan.FromHours(1) ? $"{(int)span.TotalMinutes} min" : $"{(int)span.TotalHours} h";
}

/// <summary>Outcome of one autosave pass.</summary>
public sealed record AutosavePassResult(int Dirty, int Written, int Unchanged, int Failed, double CaptureMs, double WriteMs);

/// <summary>Everything the autosave remembers about one open document. Shared by every window, so a tab moved to another window keeps it.</summary>
internal sealed class AutosaveState
{
    public readonly Guid Id = Guid.NewGuid();
    public readonly object Gate = new();
    /// <summary>The copy this process last wrote (null: none yet).</summary>
    public string? Path;
    public string? LastFingerprint;
    /// <summary>A recovered copy (from a crashed session) the document was opened from: kept until this session has its own copy.</summary>
    public string? Adopted;
    public bool Writing, Retired;
    public DateTime? MissingSince;

    /// <summary>Deletes the copies now, or (a write in flight) lets the writer do it when it finishes.</summary>
    public void Retire()
    {
        lock (Gate)
        {
            Retired = true;
            if (!Writing) DeleteFilesLocked();
        }
    }

    /// <summary>The song was saved (or is unchanged since opening): no copy is needed any more.</summary>
    public void MarkSaved()
    {
        lock (Gate)
        {
            if (Writing) return;   // the pass re-checks after the write
            DeleteFilesLocked();
            LastFingerprint = null;
        }
    }

    public void DeleteFilesLocked()
    {
        AutosaveService.Delete(Path);
        AutosaveService.Delete(Adopted);
        Path = null;
        Adopted = null;
    }
}

/// <summary>The documents that have autosave state, across all windows, and the bookkeeping of writes still in flight.</summary>
public static class AutosaveRegistry
{
    private static readonly Dictionary<DocumentSession, AutosaveState> States = new();
    private static readonly object Sync = new();
    private static int _inFlight;

    /// <summary>How long a document must be missing from every window before its copy is deleted (a tab move takes a moment).</summary>
    public static TimeSpan MissingGrace { get; set; } = TimeSpan.FromSeconds(2);

    internal static AutosaveState For(DocumentSession doc)
    {
        lock (Sync)
        {
            if (!States.TryGetValue(doc, out var state)) States[doc] = state = new AutosaveState();
            return state;
        }
    }

    internal static AutosaveState? Find(DocumentSession doc)
    {
        lock (Sync) return States.TryGetValue(doc, out var state) ? state : null;
    }

    /// <summary>The document was opened from a recovered copy: keep that copy until the session has a durable copy of its own.</summary>
    public static void Adopt(DocumentSession doc, string recoveredFile)
    {
        var state = For(doc);
        lock (state.Gate) state.Adopted = recoveredFile;
    }

    /// <summary>The documents are closed for good (window closing): their copies go, safely even while a write is in flight.</summary>
    public static void Retire(IEnumerable<DocumentSession> docs)
    {
        foreach (var doc in docs.ToArray())
        {
            AutosaveState? state;
            lock (Sync)
            {
                if (!States.Remove(doc, out state)) continue;
            }
            state.Retire();
        }
    }

    /// <summary>
    /// Retires the state of every document that is in no window any more (closed tab, discarded document). A document is only
    /// retired after it has been missing for <see cref="MissingGrace"/>: a tab being moved between windows is briefly in neither.
    /// Returns how many documents are missing but still within the grace period (the caller re-checks soon).
    /// </summary>
    public static int Reconcile(IReadOnlyCollection<DocumentSession> liveEverywhere, DateTime now)
    {
        var pending = 0;
        List<DocumentSession>? gone = null;
        lock (Sync)
        {
            foreach (var (doc, state) in States)
            {
                if (liveEverywhere.Contains(doc)) { state.MissingSince = null; continue; }
                state.MissingSince ??= now;
                if (now - state.MissingSince.Value >= MissingGrace) (gone ??= new()).Add(doc);
                else pending++;
            }
        }
        if (gone is not null) Retire(gone);
        return pending;
    }

    /// <summary>Waits (bounded) for writes in flight: app exit calls this before it deletes this process's copies.</summary>
    public static bool WaitForIdle(TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (Volatile.Read(ref _inFlight) > 0)
        {
            if (watch.Elapsed >= timeout) return false;
            Thread.Sleep(20);
        }
        return true;
    }

    internal static void WriteStarted() => Interlocked.Increment(ref _inFlight);
    internal static void WriteFinished() => Interlocked.Decrement(ref _inFlight);

    /// <summary>Clears all state (self-test isolation).</summary>
    internal static void ResetForTests()
    {
        lock (Sync) States.Clear();
    }
}

/// <summary>
/// One window's autosave pass. The UI thread does only the cheap part: for every dirty document one immutable
/// <see cref="ProjectState"/> (the same per-bar chunks undo keeps; unchanged bars are reused, nothing is serialised). A low-priority
/// worker then rebuilds a private song from it (never the live, mutating model), serialises, compresses and atomically replaces the
/// recovery copy, so a failed write can never destroy the previous copy. Failures are recorded in <see cref="Health"/>.
/// </summary>
public sealed class AutosaveRunner
{
    /// <summary>Writes one recovery copy (the seam self-tests replace to simulate a full disk or a denied folder).</summary>
    internal static Func<SongProject, string, string?> WriteCopy { get; set; } = (project, path) => App.WriteRecoveryCopy(project, path);

    public AutosaveHealth Health { get; } = new();
    public bool Busy { get; private set; }
    public double LastCaptureMs { get; private set; }

    private sealed record Job(DocumentSession Doc, AutosaveState State, ProjectState Snapshot, string Name);

    private enum Outcome { Written, Unchanged, Skipped, Failed }

    public async Task<AutosavePassResult?> RunPassAsync(IReadOnlyList<DocumentSession> docs, string folder, Func<DateTime>? clock = null)
    {
        if (Busy) return null;
        clock ??= () => DateTime.UtcNow;
        Busy = true;
        try
        {
            var jobs = new List<Job>();
            Exception? captureError = null;
            var watch = Stopwatch.StartNew();
            foreach (var doc in docs)
            {
                var state = AutosaveRegistry.For(doc);
                // Saved (or never changed): no copy needed. Project.IsDirty is the cheap flag; no content hash on the UI thread.
                if (!doc.Project.IsDirty) { state.MarkSaved(); continue; }
                try { jobs.Add(new Job(doc, state, doc.Undo.Snapshot(doc.Project).State, doc.DisplayName)); }
                catch (Exception ex) { captureError ??= ex; Trace.Error(Trace.Ui, "autosave: capture: " + ex.Message); Debug.WriteLine($"Autosave capture failed for {doc.DisplayName}: {ex}"); }
            }
            LastCaptureMs = watch.Elapsed.TotalMilliseconds;
            if (jobs.Count == 0 && captureError is null)
            {
                Health.Clear();
                return new AutosavePassResult(0, 0, 0, 0, LastCaptureMs, 0);
            }

            var writeWatch = Stopwatch.StartNew();
            var results = jobs.Count == 0
                ? Array.Empty<(Outcome Outcome, Exception? Error)>()
                : await RunLowPriority(() => jobs.Select(job => Execute(job, folder)).ToArray());
            var writeMs = writeWatch.Elapsed.TotalMilliseconds;

            var error = captureError;
            int written = 0, unchanged = 0, failed = captureError is null ? 0 : 1;
            for (var i = 0; i < jobs.Count; i++)
            {
                switch (results[i].Outcome)
                {
                    case Outcome.Written: written++; break;
                    case Outcome.Unchanged: unchanged++; break;
                    case Outcome.Failed: failed++; error ??= results[i].Error; break;
                }
                // Saved by hand while the write was running: the copy just written is stale.
                if (!jobs[i].Doc.Project.IsDirty) jobs[i].State.MarkSaved();
            }
            if (error is not null) Health.Failed(clock(), error);
            else Health.Succeeded(clock());
            return new AutosavePassResult(jobs.Count, written, unchanged, failed, LastCaptureMs, writeMs);
        }
        catch (Exception ex)
        {
            Trace.Error(Trace.Ui, "autosave: pass: " + ex.Message);
            Debug.WriteLine($"Autosave pass failed: {ex}");
            Health.Failed(clock(), ex);
            return null;
        }
        finally { Busy = false; }
    }

    /// <summary>Worker side: never throws, and never touches the live model.</summary>
    private static (Outcome Outcome, Exception? Error) Execute(Job job, string folder)
    {
        var state = job.State;
        var started = false;
        try
        {
            var fingerprint = job.Snapshot.Fingerprint;
            lock (state.Gate)
            {
                if (state.Retired) return (Outcome.Skipped, null);   // closed or moved away while queued
                if (state.LastFingerprint == fingerprint && state.Path is not null && File.Exists(state.Path)) return (Outcome.Unchanged, null);
                state.Writing = started = true;
            }
            var path = AutosaveService.FileFor(folder, Environment.ProcessId, state.Id, job.Name);
            // A private song rebuilt from the immutable state: the live model is never read here.
            var project = new ProjectStateEncoder().Restore(job.Snapshot, live: null, validate: false);
            WriteCopy(project, path);   // temp file + atomic replace: a failure leaves the previous copy as it was
            string? old, adopted;
            lock (state.Gate)
            {
                old = state.Path;
                adopted = state.Adopted;
                state.Path = path;
                state.Adopted = null;
                state.LastFingerprint = fingerprint;
            }
            // Renamed song: the copy under the old name goes. A recovered copy goes now that this session has its own.
            if (old is not null && !string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) AutosaveService.Delete(old);
            AutosaveService.Delete(adopted);
            return (Outcome.Written, null);
        }
        catch (Exception ex)
        {
            Trace.Error(Trace.Ui, "autosave: copy: " + ex.Message);
            Debug.WriteLine($"Autosave copy failed: {ex.Message}");
            return (Outcome.Failed, ex);
        }
        finally
        {
            if (started)
                lock (state.Gate)
                {
                    state.Writing = false;
                    if (state.Retired) state.DeleteFilesLocked();   // closed while writing: the writer cleans up
                }
        }
    }

    private static Task<T> RunLowPriority<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        AutosaveRegistry.WriteStarted();
        var thread = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
            finally { AutosaveRegistry.WriteFinished(); }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "TabForge autosave" };
        thread.Start();
        return done.Task;
    }
}
