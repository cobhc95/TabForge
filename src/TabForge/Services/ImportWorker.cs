using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using TabForge.Audio.Contracts;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>Settings of one out-of-process score import; <see cref="Default"/> is what the app uses.</summary>
public sealed class ImportWorkerOptions
{
    public static ImportWorkerOptions Default { get; } = new();
    /// <summary>The program started as the worker; null = this TabForge.exe.</summary>
    public string? ExecutablePath { get; init; }
    /// <summary>Committed memory of the worker process (Job Object limit); Windows ends it above this.</summary>
    public long JobMemoryLimitBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    /// <summary>Largest converted project (compressed, in the compact transfer form of <see cref="ProjectService.TransferBytes"/>) accepted back from the worker.</summary>
    public long MaxResultBytes { get; init; } = InputLimits.MaxTforgeFileBytes;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Wait after the time budget (the worker's own guard reports first) before the worker is killed.</summary>
    public TimeSpan KillGrace { get; init; } = TimeSpan.FromSeconds(2);
    /// <summary>Self-test seam: the worker receives the file and then never answers (a parse stuck inside alphaTab).</summary>
    internal bool TestHang { get; init; }
    /// <summary>Self-test seam: called with the worker process right after it started.</summary>
    internal Action<Process>? Started { get; init; }
    /// <summary>Self-test seams: the Job Object cannot be created / the started worker cannot be assigned to it.</summary>
    internal bool TestFailJobCreate { get; init; }
    internal bool TestFailJobAssign { get; init; }
}

/// <summary>
/// The import worker could not be started, did not connect, or could not be confined to its Job Object. The caller may import
/// in-process only with the user's consent for that file.
/// </summary>
public sealed class ImportWorkerUnavailableException : Exception
{
    public ImportWorkerUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

// Owns: the out-of-process import: launching the worker, its pipe protocol and its limits.
// Does not own: the conversion rules (GuitarProImporter).
// Tests: TestGuitarProImportWorker, TestLongGuitarPro35Import.
/// <summary>
/// Out-of-process score import. <c>TabForge.exe --import-worker &lt;pipe&gt; &lt;parent id&gt;</c> (the same exe, like the
/// audio engine and plug-in host modes) receives the file bytes over a current-user-only pipe with a random name, parses them with
/// alphaTab and the importer, and returns the project in the compact transfer form (gzip JSON, ProjectService.TransferBytes). Every message is a bounded frame
/// (kind byte + 32-bit length, checked before anything is allocated). The worker runs in a Job Object with a memory and a CPU-time
/// limit and is killed on Cancel or at the time budget, which frees all of its memory even while alphaTab is stuck in one call.
/// The result is validated (<see cref="ProjectValidator"/>) before use. <c>--dump</c>, <c>--exportgp</c> and the self-tests keep the
/// direct in-process <see cref="GuitarProImporter.Import"/>.
/// </summary>
public static class ImportWorker
{
    public const string Argument = "--import-worker";
    private const string PipePrefix = "TabForge.ImportWorker.";
    private const byte FrameRequest = 1, FrameData = 2, FrameResult = 3, FrameError = 4, FrameNotice = 5;
    private const int MaxRequestBytes = 64 * 1024;
    private const int MaxErrorBytes = 8 * 1024;
    private const int FlagTestHang = 1;

    /// <summary>
    /// The in-process parse, used by the background import only after the user agreed for this file (the worker or its
    /// containment could not be set up; <paramref name="reason"/> says why). The opened score carries a notice.
    /// </summary>
    public static SongProject ImportInProcess(string path, List<string> notices, string reason, ImportContext? context = null)
    {
        notices.Add($"imported inside TabForge because the protected import process could not start ({reason})");
        context ??= new ImportContext();
        var project = GuitarProImporter.Import(path, context);
        context.AddNoticesTo(notices);
        return project;
    }

    /// <summary>
    /// Imports <paramref name="path"/> in a worker process. Cancellation and the time budget come from the <paramref name="context"/>, through its
    /// <see cref="ImportGuard"/> (none: not cancellable, default budget). Throws <see cref="ImportWorkerUnavailableException"/> when
    /// the worker cannot start, <see cref="OperationCanceledException"/>, <see cref="TimeoutException"/> or <see cref="InvalidDataException"/>.
    /// </summary>
    public static SongProject Import(string path, ImportWorkerOptions? options = null, List<string>? notices = null, ImportContext? context = null)
    {
        options ??= ImportWorkerOptions.Default;
        var guard = context?.Guard;
        var token = guard?.Token ?? CancellationToken.None;
        var budget = guard?.Remaining ?? ImportGuard.DefaultTimeBudget;
        path = FilePathPolicy.ExistingFile(path, "score file", GuitarProImporter.SupportedExtensions);
        token.ThrowIfCancellationRequested();
        var raw = InputLimits.ReadBoundedBytes(path, InputLimits.MaxGuitarProFileBytes, "score file");

        var exe = options.ExecutablePath ?? Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) throw new ImportWorkerUnavailableException("its program file was not found");
        var pipeName = PipePrefix + Guid.NewGuid().ToString("N");
        var cpuLimit = TimeSpan.FromTicks(Math.Max(budget.Ticks, TimeSpan.FromSeconds(10).Ticks) * 2);
        using var job = new ChildProcessJob(options.JobMemoryLimitBytes, cpuLimit);
        // No worker without its Job Object (memory / CPU limits, kill-on-close); the caller asks before parsing in-process.
        if (!job.IsActive || options.TestFailJobCreate) throw new ImportWorkerUnavailableException("its memory and time limits could not be set up");
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Process process;
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(Argument);
            start.ArgumentList.Add(pipeName);
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            process = Process.Start(start) ?? throw new ImportWorkerUnavailableException("it did not start");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            throw new ImportWorkerUnavailableException(ex.Message, ex);
        }

        var killed = 0;
        var timedOut = false;
        void Kill()
        {
            if (Interlocked.Exchange(ref killed, 1) != 0) return;
            job.Dispose();   // kill-on-close: ends the worker and anything it started
            try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { } // Not logged: kill-on-close: the process may have exited already.
        }

        using (process)
        {
            try
            {
                // The worker only parses after it received the file below, so nothing ran unconfined before this check.
                if (options.TestFailJobAssign || !job.Add(process))
                    throw new ImportWorkerUnavailableException("it could not be placed under its memory and time limits");
                options.Started?.Invoke(process);
                using var onCancel = token.Register(Kill);

                var connect = pipe.WaitForConnectionAsync(token);
                var exited = process.WaitForExitAsync(token);
                try { Task.WaitAny(new Task[] { connect, exited }, options.ConnectTimeout); }
                catch (ObjectDisposedException ex) { Services.Trace.Error(Services.Trace.Import, "import worker: wait for connection: " + ex.Message); }
                token.ThrowIfCancellationRequested();
                if (!connect.IsCompletedSuccessfully)
                    throw new ImportWorkerUnavailableException(process.HasExited ? "it ended at start" : "it did not answer");

                using var deadline = new Timer(_ => { timedOut = true; Kill(); }, null, budget + options.KillGrace, Timeout.InfiniteTimeSpan);
                try
                {
                    var request = new byte[8 + Encoding.UTF8.GetByteCount(path)];
                    BinaryPrimitives.WriteInt32LittleEndian(request, options.TestHang ? FlagTestHang : 0);
                    BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(4), (int)Math.Min(int.MaxValue, budget.TotalMilliseconds));
                    Encoding.UTF8.GetBytes(path, request.AsSpan(8));
                    if (request.Length > MaxRequestBytes) throw new InvalidDataException("The score file's path is too long.");
                    WriteFrame(pipe, FrameRequest, request);
                    WriteFrame(pipe, FrameData, raw);
                    pipe.Flush();

                    var kind = ReadHeader(pipe, out var length);
                    // The worker reports an embedded TabForge project it found but could not use, before its result.
                    while (kind == FrameNotice)
                    {
                        if (length > MaxErrorBytes) throw new InvalidDataException("The import process sent an invalid reply.");
                        notices?.Add(Encoding.UTF8.GetString(ReadPayload(pipe, length)));
                        kind = ReadHeader(pipe, out length);
                    }
                    if (kind == FrameError)
                    {
                        if (length > MaxErrorBytes) throw new InvalidDataException("The import process sent an invalid reply.");
                        var message = Encoding.UTF8.GetString(ReadPayload(pipe, length));
                        throw new InvalidDataException(message.Length == 0 ? "This score file is invalid, truncated, or unsupported." : message);
                    }
                    if (kind != FrameResult) throw new InvalidDataException("The import process sent an invalid reply.");
                    if (length > options.MaxResultBytes)
                        throw new InvalidDataException($"The imported song is larger than the {options.MaxResultBytes / (1024.0 * 1024):0.#} MiB limit, so it was refused.");
                    var result = ReadPayload(pipe, length);
                    Kill();   // the answer is in: the worker's memory is released now, not when it gets round to exiting
                    // The same checks as a .tforge from disk: bounded gunzip, JSON shape, ProjectValidator.
                    return ProjectService.RestoreTransferBytes(result);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    token.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref timedOut))
                        throw new TimeoutException($"Importing {Path.GetFileName(path)} took longer than {budget.TotalSeconds:0} seconds, so it was stopped.");
                    throw new InvalidDataException("The import process stopped unexpectedly: this score file needed more memory or time than allowed, or it is damaged.", ex);
                }
            }
            finally
            {
                Kill();
                try { process.WaitForExit(5_000); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or SystemException) { } // Not logged: finally after kill: the process may have exited.
            }
        }
    }

    /// <summary>Worker process entry (<c>--import-worker &lt;pipe&gt; &lt;parent id&gt;</c>): one request, one reply, then exit. No WPF, no settings.</summary>
    public static int Run(string[] args)
    {
        if (args.Length < 2 || !args[1].StartsWith(PipePrefix, StringComparison.Ordinal) || args[1].Length > PipePrefix.Length + 32) return 2;
        if (args.Length >= 3 && int.TryParse(args[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parentId))
            WatchParent(parentId);
        try
        {
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(10_000);
            if (ReadHeader(pipe, out var requestLength) != FrameRequest || requestLength is < 8 or > MaxRequestBytes) return 3;
            var request = ReadPayload(pipe, requestLength);
            if (ReadHeader(pipe, out var dataLength) != FrameData || dataLength > InputLimits.MaxGuitarProFileBytes) return 3;
            var data = ReadPayload(pipe, dataLength);
            var flags = BinaryPrimitives.ReadInt32LittleEndian(request);
            var budgetMs = Math.Max(1, BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(4)));
            var path = Encoding.UTF8.GetString(request, 8, request.Length - 8);
            if ((flags & FlagTestHang) != 0) Thread.Sleep(Timeout.Infinite);

            byte kind;
            byte[] reply;
            try
            {
                if (!GuitarProImporter.SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("This is not a score file.");
                var context = new ImportContext(new ImportGuard(CancellationToken.None, TimeSpan.FromMilliseconds(budgetMs)));
                SongProject project;
                project = GuitarProImporter.ImportBytes(data, path, context);
                data = Array.Empty<byte>();
                reply = ProjectService.TransferBytes(project, $"imported song ({project.Tracks.Count} tracks, {project.Tracks.Select(t => t.Measures.Count).DefaultIfEmpty(0).Max():N0} bars)");
                kind = FrameResult;
                if (context.EmbeddedRejection is { } rejected)
                {
                    var text = Encoding.UTF8.GetBytes(rejected.Length > 2_000 ? rejected[..2_000] : rejected);
                    WriteFrame(pipe, FrameNotice, text);
                }
                if (context.DamageNotice is { } damaged)
                    WriteFrame(pipe, FrameNotice, Encoding.UTF8.GetBytes(damaged.Length > 2_000 ? damaged[..2_000] : damaged));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Services.Trace.Error(Services.Trace.Import, "import worker: damage notice: " + ex.Message);
                var message = ex is InvalidDataException ? ex.Message : "This score file is invalid, truncated, or unsupported.";
                if (message.Length > 2_000) message = message[..2_000];
                reply = Encoding.UTF8.GetBytes(message);
                kind = FrameError;
            }
            WriteFrame(pipe, kind, reply);
            pipe.Flush();
            return 0;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Services.Trace.Error(Services.Trace.Import, "import worker: pipe: " + ex.Message);
            return 1;
        }
    }

    /// <summary>The worker ends with its parent even outside a job (the parent normally kills it through the job first).</summary>
    private static void WatchParent(int parentId)
    {
        new Thread(() =>
        {
            try { using var parent = Process.GetProcessById(parentId); parent.WaitForExit(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { } // Not logged: parent watch: the parent may have exited already.
            Environment.Exit(4);
        }) { IsBackground = true, Name = "TabForge import worker parent watch" }.Start();
    }

    private static void WriteFrame(Stream stream, byte kind, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[5];
        header[0] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(header[1..], payload.Length);
        stream.Write(header);
        stream.Write(payload);
    }

    private static byte ReadHeader(Stream stream, out int length)
    {
        Span<byte> header = stackalloc byte[5];
        stream.ReadExactly(header);
        length = BinaryPrimitives.ReadInt32LittleEndian(header[1..]);
        if (length < 0) throw new InvalidDataException("The import process sent an invalid reply.");
        return header[0];
    }

    private static byte[] ReadPayload(Stream stream, int length)
    {
        var payload = new byte[length];
        stream.ReadExactly(payload);
        return payload;
    }
}
