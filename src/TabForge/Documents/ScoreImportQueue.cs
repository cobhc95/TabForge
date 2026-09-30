using System.IO;
using TabForge.Services;

namespace TabForge.Documents;

/// <summary>One Guitar Pro file being imported in the background.</summary>
public sealed class ScoreImportJob
{
    internal ScoreImportJob(string path) => Path = path;
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    internal CancellationTokenSource Cancellation { get; } = new();
    public bool IsCancelled => Cancellation.IsCancellationRequested;
    /// <summary>Completes once the job was applied, failed or cancelled (its callback has run).</summary>
    public Task Completion { get; internal set; } = Task.CompletedTask;
}

/// <summary>
/// Background Guitar Pro import (audit A5-07). Parsing and conversion run on the thread pool under an <see cref="ImportGuard"/>;
/// the result is handed back on the caller's synchronization context (the UI thread), in the order the opens were started, so
/// tab order matches the selection. A cancelled or failed job never reaches <c>apply</c>, so it can leave no half-open tab.
/// </summary>
public sealed class ScoreImportQueue
{
    private readonly Func<string, OpenedScore> _open;
    private readonly List<ScoreImportJob> _pending = new();
    private Task _tail = Task.CompletedTask;

    /// <param name="open">The synchronous open (DocumentController.Open); a test passes a synthetic slow one.</param>
    public ScoreImportQueue(Func<string, OpenedScore> open) => _open = open;

    public TimeSpan TimeBudget { get; set; } = ImportGuard.DefaultTimeBudget;
    public long MemoryBudgetBytes { get; set; } = ImportGuard.DefaultMemoryBudgetBytes;
    /// <summary>Extra wait after the time budget before an import stuck inside alphaTab is abandoned.</summary>
    public TimeSpan StuckGrace { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Raised on the caller's context whenever the pending list changes.</summary>
    public event Action? Changed;
    public IReadOnlyList<ScoreImportJob> Pending => _pending;

    /// <summary>Guitar Pro files import in the background; .tforge projects stay synchronous (bounded JSON, fast).</summary>
    public static bool RunsInBackground(string path) =>
        GuitarProImporter.SupportedExtensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Starts importing <paramref name="path"/> and returns at once. Exactly one callback runs later on this context:
    /// <paramref name="apply"/> with the opened score, or <paramref name="fail"/> with the error (null when cancelled).
    /// </summary>
    public ScoreImportJob Start(string path, Action<ScoreImportJob, OpenedScore> apply, Action<ScoreImportJob, Exception?> fail)
    {
        var job = new ScoreImportJob(path);
        _pending.Add(job);
        Changed?.Invoke();
        var previous = _tail;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tail = finished.Task;
        job.Completion = RunAsync(job, previous, finished, apply, fail);
        return job;
    }

    public void Cancel(ScoreImportJob job) => job.Cancellation.Cancel();

    public void CancelAll()
    {
        foreach (var job in _pending.ToArray()) job.Cancellation.Cancel();
    }

    private async Task RunAsync(ScoreImportJob job, Task previous, TaskCompletionSource finished,
        Action<ScoreImportJob, OpenedScore> apply, Action<ScoreImportJob, Exception?> fail)
    {
        OpenedScore opened = default;
        Exception? error = null;
        try { opened = await ImportAsync(job.Path, _open, job.Cancellation.Token, TimeBudget, MemoryBudgetBytes, StuckGrace); }
        catch (Exception ex) { error = ex; }
        try
        {
            // Results are applied in start order; a cancelled or failed job is reported as soon as it ends.
            if (!job.IsCancelled && error is null) await previous;
            _pending.Remove(job);
            Changed?.Invoke();
            if (job.IsCancelled) fail(job, null);
            else if (error is not null) fail(job, error is OperationCanceledException ? null : error);
            else apply(job, opened);
        }
        finally { finished.TrySetResult(); }
    }

    /// <summary>
    /// Runs <paramref name="open"/> on the thread pool under an ambient <see cref="ImportGuard"/>. Returns (or throws
    /// <see cref="OperationCanceledException"/>) as soon as the token is cancelled, and throws <see cref="TimeoutException"/>
    /// once the time budget plus <paramref name="stuckGrace"/> passed with alphaTab still busy; that worker is then abandoned
    /// (its eventual result or error is observed and dropped). Never blocks the calling thread.
    /// </summary>
    public static async Task<OpenedScore> ImportAsync(string path, Func<string, OpenedScore> open, CancellationToken token,
        TimeSpan timeBudget, long memoryBudgetBytes, TimeSpan stuckGrace)
    {
        token.ThrowIfCancellationRequested();
        var work = Task.Run(() =>
        {
            var guard = new ImportGuard(token, timeBudget, memoryBudgetBytes);
            using (guard.Enter())
            {
                guard.Check();
                var result = open(path);
                guard.Check();
                return result;
            }
        }, CancellationToken.None);
        var watchdog = Task.Delay(timeBudget + stuckGrace, token);
        var first = await Task.WhenAny(work, watchdog).ConfigureAwait(false);
        if (first == work) return await work.ConfigureAwait(false);
        _ = work.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        token.ThrowIfCancellationRequested();
        throw new TimeoutException($"Importing {System.IO.Path.GetFileName(path)} took longer than {timeBudget.TotalSeconds:0} seconds, so it was stopped.");
    }
}
