using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Autosave;

// Owns: one window's autosave pass: the cheap capture on the UI thread (one immutable state per dirty song) and the worker that rebuilds a private song
//     from it, serialises it and atomically replaces the song's recovery copy; the health record of the passes.
// Does not own: the timer and the notice (the window's autosave controller), the copy's bytes (RecoveryCopyWriter, passed in), the file names (AutosaveService).
// Tests: MainWindowAutosaveTests.
// Keys: one pass at a time (Busy). Ceiling: the songs open in the windows, one copy each. Thread: Busy and Health are UI thread only; the worker
//     thread only runs Execute, which takes each song's state Gate before it touches the state.

/// <summary>Outcome of one autosave pass.</summary>
public sealed record AutosavePassResult(int Dirty, int Written, int Unchanged, int Failed, double CaptureMs, double WriteMs);

/// <summary>Copies dirty songs to the Recovery folder. UI thread only (<see cref="RunPassAsync"/> starts there).</summary>
public sealed class AutosaveRunner
{
    private readonly AutosaveRegistry _registry;
    private readonly Func<SongProject, string, string?> _writeCopy;

    /// <param name="registry">The process's autosave registry (every window's songs share it).</param>
    /// <param name="writeCopy">Writes one copy atomically (returns the validation warning for a copy written anyway, or null).</param>
    public AutosaveRunner(AutosaveRegistry registry, Func<SongProject, string, string?> writeCopy)
    {
        _registry = registry;
        _writeCopy = writeCopy;
    }

    public AutosaveHealth Health { get; } = new();
    public bool Busy { get; private set; }
    public double LastCaptureMs { get; private set; }

    private sealed record Job(SongDocument Song, AutosaveState State, ProjectState Snapshot, string Name);

    private enum Outcome { Written, Unchanged, Skipped, Failed }

    /// <summary>Copies every dirty song of <paramref name="songs"/> into <paramref name="folder"/>; null when a pass is already running or it failed as a whole.</summary>
    public async Task<AutosavePassResult?> RunPassAsync(IReadOnlyList<SongDocument> songs, string folder, Func<DateTime>? clock = null)
    {
        if (Busy) return null;
        clock ??= () => DateTime.UtcNow;
        Busy = true;
        try
        {
            var jobs = new List<Job>();
            Exception? captureError = null;
            var watch = Stopwatch.StartNew();
            foreach (var song in songs)
            {
                var state = _registry.For(song);
                // Saved (or never changed): no copy needed. Project.IsDirty is the cheap flag; no content hash on the UI thread.
                if (!song.Project.IsDirty) { state.MarkSaved(); continue; }
                try { jobs.Add(new Job(song, state, DocumentEdits.CurrentState(song), song.Project.Title)); }
                catch (Exception ex) { captureError ??= ex; Services.Trace.Error(Services.Trace.Ui, "autosave: capture: " + ex.Message); }
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
                : await RunLowPriority(() => jobs.ConvertAll(job => Execute(job, folder, _writeCopy)).ToArray());
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
                if (!jobs[i].Song.Project.IsDirty) jobs[i].State.MarkSaved();
            }
            if (error is not null) Health.Failed(clock(), error, folder);
            else Health.Succeeded(clock());
            return new AutosavePassResult(jobs.Count, written, unchanged, failed, LastCaptureMs, writeMs);
        }
        catch (Exception ex)
        {
            Services.Trace.Error(Services.Trace.Ui, "autosave: pass: " + ex.Message);
            Health.Failed(clock(), ex, folder);
            return null;
        }
        finally { Busy = false; }
    }

    /// <summary>Worker side: never throws, and never touches the live model.</summary>
    private static (Outcome Outcome, Exception? Error) Execute(Job job, string folder, Func<SongProject, string, string?> writeCopy)
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
            writeCopy(project, path);   // temp file + atomic replace: a failure leaves the previous copy as it was
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
            Services.Trace.Error(Services.Trace.Ui, "autosave: copy: " + ex.Message);
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

    /// <summary>Runs <paramref name="work"/> on a background thread below normal priority, so a long write never stalls the window.</summary>
    private static Task<T> RunLowPriority<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "TabForge autosave" };
        thread.Start();
        return done.Task;
    }
}
