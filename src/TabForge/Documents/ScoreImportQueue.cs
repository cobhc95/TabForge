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
/// Background score import. Parsing and conversion run on the thread pool under an <see cref="ImportGuard"/>;
/// the result is handed back on the caller's synchronization context (the UI thread), in the order the opens were started, so
/// tab order matches the selection. A cancelled or failed job never reaches <c>apply</c>, so it can leave no half-open tab.
/// </summary>
public sealed class ScoreImportQueue
{
    /// <summary>Imports running at once by default; the others wait (in order) for a free slot.</summary>
    public const int DefaultMaxConcurrent = 2;
    /// <summary>All running import workers together stay within this committed memory (split evenly across the slots).</summary>
    public const long TotalWorkerMemoryBytes = 3L * 1024 * 1024 * 1024;

    private readonly Func<string, ImportContext, OpenedScore> _open;
    private readonly Func<string, string, ImportContext, OpenedScore>? _openInProcess;
    private readonly SemaphoreSlim _slots;
    private readonly List<ScoreImportJob> _pending = new();
    private Task _tail = Task.CompletedTask;

    /// <param name="open">The synchronous open (DocumentController.Open), given the import's context; a test passes a synthetic slow one.</param>
    /// <param name="maxConcurrent">How many opens run at once; later ones wait for a slot (results still apply in start order).</param>
    /// <param name="openInProcess">The unprotected in-process open (path, reason) used only when <paramref name="open"/> threw
    /// <see cref="ImportWorkerUnavailableException"/> and <see cref="ConfirmInProcess"/> agreed for that file.</param>
    public ScoreImportQueue(Func<string, ImportContext, OpenedScore> open, int maxConcurrent = DefaultMaxConcurrent, Func<string, string, ImportContext, OpenedScore>? openInProcess = null)
    {
        _open = open;
        _openInProcess = openInProcess;
        _slots = new SemaphoreSlim(Math.Max(1, maxConcurrent));
    }

    /// <summary>
    /// The app's queue: each Guitar Pro file is parsed in the import worker process (<see cref="ImportWorker"/>), killed on Cancel
    /// or at the time budget, at most <see cref="DefaultMaxConcurrent"/> at once with <see cref="TotalWorkerMemoryBytes"/> split
    /// between them. When the worker or its Job Object cannot be set up, the file opens in this process only if
    /// <see cref="ConfirmInProcess"/> says yes for that file.
    /// </summary>
    public static ScoreImportQueue WithImportWorker(DocumentController documents, ImportWorkerOptions? options = null)
    {
        options ??= new ImportWorkerOptions { JobMemoryLimitBytes = TotalWorkerMemoryBytes / DefaultMaxConcurrent };
        return new((path, context) => documents.Open(path, (file, notices) => ImportWorker.Import(file, options, notices, context), context), DefaultMaxConcurrent,
            (path, reason, context) => documents.Open(path, (file, notices) => ImportWorker.ImportInProcess(file, notices, reason, context), context));
    }

    /// <summary>
    /// Asked on the caller's context (the UI thread) with the job and the reason when the protected import could not start;
    /// true = open this one file inside TabForge. Null or false: the file is not opened (reported as cancelled).
    /// </summary>
    public Func<ScoreImportJob, string, bool>? ConfirmInProcess { get; set; }

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
        var holdsSlot = false;
        try
        {
            // A bounded pool; a job cancelled while it waits never starts.
            await _slots.WaitAsync(job.Cancellation.Token);
            holdsSlot = true;
            try { opened = await ImportAsync(job.Path, _open, job.Cancellation.Token, TimeBudget, MemoryBudgetBytes, StuckGrace); }
            catch (ImportWorkerUnavailableException unavailable) when (_openInProcess is not null && !job.IsCancelled)
            {
                // Never a silent in-process parse; the user decides per file (this runs on the caller's context).
                var reason = unavailable.Message;
                if (ConfirmInProcess?.Invoke(job, reason) != true || job.IsCancelled) throw new OperationCanceledException();
                opened = await ImportAsync(job.Path, (path, context) => _openInProcess(path, reason, context), job.Cancellation.Token, TimeBudget, MemoryBudgetBytes, StuckGrace);
            }
        }
        catch (Exception ex) { error = ex; } // Not logged: the error is handed to the caller that awaits the import
        finally { if (holdsSlot) _slots.Release(); }
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
    /// Runs <paramref name="open"/> on the thread pool with an <see cref="ImportContext"/> that holds an <see cref="ImportGuard"/>. Returns (or throws
    /// <see cref="OperationCanceledException"/>) as soon as the token is cancelled, and throws <see cref="TimeoutException"/>
    /// once the time budget plus <paramref name="stuckGrace"/> passed with alphaTab still busy; that worker is then abandoned
    /// (its eventual result or error is observed and dropped). Never blocks the calling thread.
    /// </summary>
    public static async Task<OpenedScore> ImportAsync(string path, Func<string, ImportContext, OpenedScore> open, CancellationToken token,
        TimeSpan timeBudget, long memoryBudgetBytes, TimeSpan stuckGrace)
    {
        token.ThrowIfCancellationRequested();
        var work = Task.Run(() =>
        {
            var guard = new ImportGuard(token, timeBudget, memoryBudgetBytes);
            guard.Check();
            var result = open(path, new ImportContext(guard));
            guard.Check();
            return result;
        }, CancellationToken.None);
        var watchdog = Task.Delay(timeBudget + stuckGrace, token);
        var first = await Task.WhenAny(work, watchdog).ConfigureAwait(false);
        if (first == work) return await work.ConfigureAwait(false);
        _ = work.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        token.ThrowIfCancellationRequested();
        throw new TimeoutException($"Importing {System.IO.Path.GetFileName(path)} took longer than {timeBudget.TotalSeconds:0} seconds, so it was stopped.");
    }
}
