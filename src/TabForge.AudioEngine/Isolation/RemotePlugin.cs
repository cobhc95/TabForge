using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Isolation;

// Owns: the plug-in proxy running in its own process (crash isolation, budget bypass, lost-plug-in reporting).

/// <summary>
/// A plug-in running in its own process (Settings > Audio &amp; VST > Run each plug-in in its own process). To the
/// chain it looks like any plug-in. If that process crashes or stops answering, only this plug-in is lost: effects
/// pass the sound through unchanged, instruments go silent, and <see cref="Crashed"/> reports it. A block the process
/// does not answer within the callback's budget is bypassed; the plug-in counts as lost only when it stays busy past a
/// grace period or misses too many blocks (see <see cref="PluginHostLink"/>). This is crash isolation, not a sandbox: the plug-in's process runs as the same user with the same
/// rights as TabForge, so it can do anything TabForge can; only its crashes and hangs are kept away from the engine.
/// </summary>
public sealed partial class RemotePlugin : IPluginInstance
{
    // Host commands over the pipe.
    public const byte CmdGetState = 1, CmdSetState = 2, CmdOpenEditor = 3, CmdCloseEditor = 4, CmdDescribe = 5, CmdListParams = 6, CmdSetParam = 7, CmdQuit = 9, CmdSetProgram = 10;
    public const byte MsgLoaded = 100, MsgFailed = 101, MsgState = 102, MsgEditor = 103, MsgEdited = 104, MsgInfo = 105, MsgEditorSized = 106, MsgParams = 107, MsgHello = 108, MsgEditorClosed = 109;
    /// <summary>Prefix of the helper's MsgFailed text when its state is over <see cref="PluginStateLimits.MaxRawBytes"/>.</summary>
    internal const string StateTooLargePrefix = "state-too-large";

    // One plug-in process session; replaced as a whole by Reconfigure (main thread, audio stopped). Events from an older
    // session (its exit, its reply reader ending, its hang report) are ignored.
    private PluginHostBlock _block = null!;
    private Stream _pipe = null!;
    private TcpListener? _listener;
    private TcpClient? _tcpClient;
    private string? _blockPath;
    private System.Diagnostics.Process _process = null!;
    private Thread? _reader, _watch;
    private PluginHostLink _link = null!;
    private readonly PluginSpec _spec;
    private readonly EngineThreads _threads;
    private readonly PluginHostLaunch _launch;
    private bool _disposed;
    private volatile bool _dead;
    private int _reported;   // once: a session reports at most one crash, however many paths notice it
    private PluginControlChannel _control = null!;   // request ids: a late reply is never taken for the next request's

    public string Path { get; }
    public bool IsInstrument { get; private set; }
    public bool HasEditor { get; private set; }
    public int LatencySamples { get; private set; }
    public int TailSamples { get; private set; }
    /// <summary>When the helper wrote a crash report: its minidump path (the one-line text is beside it), else null.</summary>
    public string? CrashReportPath { get; private set; }

    /// <summary>The plug-in's own process switches its mode before the next block (an empty block carries the request); render blocks wait far longer than a realtime block.</summary>
    public void SetOfflineMode(bool offline) => _link.SetOfflineMode(offline);   // engine main thread, while audio is stopped (contract)
    /// <summary>The plug-in's process ended or hung (raised once, on the engine's main thread).</summary>
    public event Action<RemotePlugin>? Crashed;
    public event Action? Edited;
    /// <summary>The user closed the plug-in's editor in its own process (raised on the engine's main thread; wired once per instance, never per open).</summary>
    public event Action? EditorClosed;
    /// <summary>The plug-in's editor, in its own process, asked for a new size (raised on the engine's main thread; the one subscriber is the open editor's session).</summary>
    public Action<int, int>? EditorSized { get; set; }

    /// <summary>How long the process may take to connect to the control pipe.</summary>
    internal const int ConnectTimeoutMs = 15000;
    /// <summary>How long a connected TCP peer has to present a valid hello before it is dropped (mirrors <see cref="PipeAuth.PerPeerMs"/>).</summary>
    internal const int TcpHelloTimeoutMs = 2000;
    /// <summary>How long a connected process may take to finish loading (the existing 90 s load budget, not the connect timeout).</summary>
    internal const int LoadTimeoutMs = 90000;

    public RemotePlugin(PluginSpec spec, int sampleRate, int maxBlock, EngineThreads threads)
        : this(spec, sampleRate, maxBlock, threads, null) { }

    /// <summary>A plug-in in its own process; <paramref name="launch"/> selects the existing 64-bit host or the 32-bit helper.</summary>
    public RemotePlugin(PluginSpec spec, int sampleRate, int maxBlock, EngineThreads threads, PluginHostLaunch? launch)
        : this(spec, sampleRate, maxBlock, threads, launch, ConnectTimeoutMs, LoadTimeoutMs, null, null) { }

    /// <summary>Test seam: short bounded waits, a known session id and an optional block folder; production uses the public constructors above.</summary>
    internal RemotePlugin(PluginSpec spec, int sampleRate, int maxBlock, EngineThreads threads, PluginHostLaunch? launch,
        int connectTimeoutMs, int loadTimeoutMs, string? sessionId, Func<ProcessStartInfo, Process>? startProcess,
        string? blockFolder = null)
    {
        Path = spec.Path;
        _spec = spec;
        _threads = threads;
        _launch = launch ?? PluginHostLaunch.ForCurrentProcess();
        StartSession(sampleRate, maxBlock, connectTimeoutMs, loadTimeoutMs, sessionId, startProcess, blockFolder);
    }

    /// <summary>The helper's process id once started (diagnostics and tests; the engine never needs it).</summary>
    public int HostProcessId => _process.Id;

    /// <summary>Test diagnostics: blocks bypassed because the child missed its deadline or was still busy.</summary>
    internal long MissedBlocks => _link.MissedBlocks;
    /// <summary>Test diagnostics: blocks bypassed without a request because the callback budget was already spent.</summary>
    internal long BudgetSkippedBlocks => _link.BudgetSkippedBlocks;
    /// <summary>Test diagnostics: the link failed for good (the process is being killed and reported).</summary>
    internal bool LinkFailed => _link.Failed;
    /// <summary>Test diagnostics: the explicit file block of a TCP session (null on the named-pipe path).</summary>
    internal string? BlockFilePath => _blockPath;

    /// <summary>Starts the plug-in's process at this rate / block size and waits for it to load (throws if it does not).</summary>
    private void StartSession(int sampleRate, int maxBlock, int connectTimeoutMs, int loadTimeoutMs, string? sessionId,
        Func<ProcessStartInfo, Process>? startProcess, string? blockFolder = null)
    {
        var id = sessionId ?? Guid.NewGuid().ToString("N");
        var tcp = _launch.IsTcp;
        var port = 0;
        var token = "";
        if (tcp)
        {
            // The Wine transport: an explicit file block (never delete-on-close) and a loopback listener. The port is
            // ephemeral; the token goes through the child's environment, never the command line.
            var path = _blockPath = WineRuntime.BlockPath(id, blockFolder);
            try
            {
                _block = PluginHostBlock.CreateFile(path, maxBlock, _launch.BufferSlots);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // A full disk or read-only runtime folder is a load failure with one readable sentence, not a raw
                // FileStream exception from deep in the mapping code. Release deletes any partial file this call made.
                Release();
                throw new InvalidOperationException(
                    $"The plug-in's audio block could not be created at {path} (the disk may be full or the folder read-only): {ex.Message}", ex);
            }
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }
        else
        {
            _block = PluginHostBlock.Create(id, maxBlock, _launch.BufferSlots);
            _pipe = new NamedPipeServerStream(PipeNames.PluginHost(id), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }
        var block = _block;
        var exe = _launch.ExecutablePath;
        if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException("Engine path unknown.");
        // Each argument is its own list entry, so a path with quotes or spaces cannot split into more arguments.
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        if (_launch.Environment is { } extra) foreach (var (key, value) in extra) start.Environment[key] = value;
        if (tcp) start.Environment[TokenVariable] = token;
        var blockPath = tcp && _blockPath is { } native ? WineRuntime.ToWindowsPath(native) : null;
        // The helper is a Windows process even when the engine is not: it must receive Windows paths for the plug-in and
        // its log too, not only for the block. On Windows ToWindowsPath returns the path unchanged.
        var launchSpec = tcp ? _spec with { Path = WineRuntime.ToWindowsPath(_spec.Path) } : _spec;
        var logPath = _threads.Log.LogPath;
        if (tcp && !string.IsNullOrWhiteSpace(logPath)) logPath = WineRuntime.ToWindowsPath(logPath);
        foreach (var argument in _launch.Arguments(id, Environment.ProcessId, sampleRate, maxBlock, launchSpec, logPath, port, blockPath)) start.ArgumentList.Add(argument);
        System.Diagnostics.Process process;
        try
        {
            process = _process = (startProcess is null ? System.Diagnostics.Process.Start(start) : startProcess(start))
                ?? throw new InvalidOperationException("The plug-in process did not start.");
        }
        catch
        {
            Release();
            throw;
        }
        ChildProcessGuards.ForThisProcess.Add(process);   // ends with the engine, however the engine ends (Windows: the Job Object; elsewhere the engine kills it on exit and its parent-pid watchdog covers a hard crash)
        // A late block only sets the link's flag on the audio thread; this watchdog observes it and does the kill and report on the main thread.
        var link = _link = new PluginHostLink(block, sampleRate);
        _dead = false;
        Interlocked.Exchange(ref _reported, 0);   // a reconfigure's fresh session may report its own crash once
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => { if (ReferenceEquals(process, _process)) Die("its process ended", ExitCodeIfExited(process)); };
        try
        {
            ReceivedFrame first;
            if (tcp)
            {
                var listener = _listener ?? throw new InvalidOperationException("The control listener is missing.");
                // Accept until a peer presents the session token. A local squatter that finds the ephemeral port first must
                // not be able to keep the real helper out (the named-pipe path has the same accept loop in PipeAuth).
                _tcpClient = AcceptTcpWithHello(listener, token, sampleRate, maxBlock, connectTimeoutMs, out var helperVersion, out var wineVersion);
                _pipe = _tcpClient.GetStream();
                listener.Stop();
                if (wineVersion.Length > 0) _threads.Log.Write($"Windows plug-in bridge: {helperVersion} under {wineVersion} ({System.IO.Path.GetFileName(Path)}).");
                first = ReadFirstFrame(_pipe, loadTimeoutMs);   // the loaded (or failed) frame follows the validated hello
            }
            else
            {
                if (!((NamedPipeServerStream)_pipe).WaitForConnectionAsync().Wait(connectTimeoutMs))
                    throw new TimeoutException("The plug-in process did not answer.");
                first = ReadFirstFrame(_pipe, loadTimeoutMs);
                if (first.Type == MsgHello)
                {
                    using (first) ReadBridgeHello(first.Reader, sampleRate, maxBlock, _launch.BufferSlots);
                    first = ReadFirstFrame(_pipe, loadTimeoutMs);   // the loaded (or failed) frame follows the hello
                }
            }
            using var loaded = first;
            if (loaded.Type == MsgFailed) throw new InvalidOperationException(loaded.Reader.ReadBoundedString(Frames.MaxPathChars));
            IsInstrument = loaded.Reader.ReadBoolean(); HasEditor = loaded.Reader.ReadBoolean(); LatencySamples = loaded.Reader.ReadInt32();
            TailSamples = loaded.Reader.ReadInt32();
            _link.IsInstrument = IsInstrument;
            var control = _control = new PluginControlChannel(_pipe, _threads.Log);
            _reader = new Thread(() => ReadReplies(_pipe, control)) { IsBackground = true, Name = "TabForge plug-in host replies" };
            _reader.Start();
            _watch = new Thread(() => WatchLink(link, process)) { IsBackground = true, Name = "TabForge plug-in link watchdog" };
            _watch.Start();
        }
        catch
        {
            _dead = true;
            Interlocked.Exchange(ref _reported, 1);
            _link.Stop();
            try { process.Kill(); } catch (InvalidOperationException) { } // swallow: the plug-in process already exited.
            Release();
            throw;
        }
    }

    /// <summary>
    /// Accepts TCP peers until one presents this session's TFH1 hello and token, dropping silent or wrong peers after a
    /// short window so a local port squatter cannot keep the helper out. Mirrors <see cref="PipeAuth.AcceptAsync"/>.
    /// </summary>
    private TcpClient AcceptTcpWithHello(TcpListener listener, string token, int sampleRate, int maxBlock, int connectTimeoutMs,
        out string helperVersion, out string wineVersion)
    {
        var clock = Stopwatch.StartNew();
        helperVersion = "";
        wineVersion = "";
        while (true)
        {
            var left = connectTimeoutMs - (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
            if (left <= 0) throw new TimeoutException("The plug-in process did not answer.");
            var accept = Task.Run(() => listener.AcceptTcpClientAsync());
            if (!accept.Wait(left)) { ObserveFault(accept); throw new TimeoutException("The plug-in process did not answer."); }
            var client = accept.GetAwaiter().GetResult();
            client.NoDelay = true;
            var stream = client.GetStream();
            ReceivedFrame hello;
            try { hello = ReadFirstFrame(stream, Math.Min(TcpHelloTimeoutMs, left)); }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidDataException or EndOfStreamException or ObjectDisposedException)
            {
                client.Dispose();
                Thread.Sleep(25);   // a squatter that reconnects at once does not spin this loop
                continue;
            }
            if (hello.Type != MsgHello)
            {
                hello.Dispose();
                client.Dispose();
                Thread.Sleep(25);
                continue;
            }
            try
            {
                using (hello) ReadBridgeTcpHello(hello.Reader, token, sampleRate, maxBlock, _launch.BufferSlots, _launch.BridgeBitness,
                    out helperVersion, out wineVersion);
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or EndOfStreamException or IOException or ObjectDisposedException or FormatException)
            {
                client.Dispose();
                Thread.Sleep(25);
                continue;
            }
            return client;
        }
    }

    /// <summary>
    /// The first frame after connecting (loaded or failed), read on the thread pool so a helper that connects and then hangs in
    /// its plug-in load cannot block the engine main thread forever. Production allows the existing 90 s load budget; on a
    /// timeout the caller kills the process and disposes the pipe.
    /// </summary>
    private static ReceivedFrame ReadFirstFrame(Stream pipe, int timeoutMs)
    {
        var read = Task.Run(() => Frames.Read(pipe, IsolatedFrameLimits.ForReply));
        try
        {
            if (!read.Wait(timeoutMs))
            {
                ObserveFault(read);
                throw new TimeoutException($"The plug-in process connected but did not finish loading within {timeoutMs / 1000.0:0.###} s.");
            }
            return read.GetAwaiter().GetResult() ?? throw new InvalidOperationException("The plug-in process closed.");
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void ObserveFault(Task task) =>
        task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    /// <summary>
    /// Device change (main thread, audio stopped): the plug-in's process is restarted at the new rate / block size (the
    /// host protocol has no command for it) and its current state is carried over. Throws if the new process does not load.
    /// </summary>
    public void Reconfigure(double sampleRate, int maxBlock)
    {
        if (_disposed) return;
        byte[]? state = null;
        if (!_dead)
        {
            try { state = GetState(); }
            catch (TimeoutException ex) { _threads.Log.Write($"isolated plug-in reconfigure: {ex.Message}"); }
        }
        EndSession();
        StartSession((int)Math.Round(sampleRate), Math.Clamp(maxBlock, 16, 8192), ConnectTimeoutMs, LoadTimeoutMs, null, null);
        if (state is not null && !SetStateCore(state))
            _threads.Log.Write($"isolated plug-in reconfigure: the new process did not accept its saved settings: {Path}");
    }

    /// <summary>Audio thread: one block through the plug-in's process (see <see cref="PluginHostLink"/>).</summary>
    public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport) =>
        _link.Process(input, output, frames, midi, transport);

    // Off the audio thread: polls the link's flag (the audio path never Posts or formats) and reports the failure on the main thread.
    private void WatchLink(PluginHostLink link, Process process)
    {
        while (!link.Stopped && ReferenceEquals(link, _link))
        {
            if (link.TryTakeFailure(out var reason))
            {
                if (ReferenceEquals(link, _link) && !_dead) _threads.Post(() => ReportLinkFailure(process, reason));
                return;
            }
            // A helper whose main thread stopped answering a control command is hung even when audio still flows: it must
            // not be left orphaned while every later command is refused (C9).
            if (_control is { Faulted: true } && ReferenceEquals(link, _link) && !_dead)
            {
                _threads.Post(() => ReportLinkFailure(process, PluginHostLink.FailureReason.ControlTimeout));
                return;
            }
            Thread.Sleep(20);
        }
    }

    // Engine main thread: the link gave up on a plug-in that is still alive, so end it and report once.
    private void ReportLinkFailure(Process process, PluginHostLink.FailureReason reason)
    {
        if (!ReferenceEquals(process, _process)) return;
        try { process.Kill(); } catch (InvalidOperationException) { /* swallow: the plug-in process already exited. */ }
        Die(LinkFailureText(reason));
    }

    private static string LinkFailureText(PluginHostLink.FailureReason reason) => reason switch
    {
        PluginHostLink.FailureReason.MissWindow => "it missed too many blocks",
        PluginHostLink.FailureReason.OfflineTimeout => "it did not answer a render block",
        PluginHostLink.FailureReason.ModeSwitchTimeout => "it did not answer a mode switch",
        PluginHostLink.FailureReason.ControlTimeout => "it stopped answering control commands",
        _ => "it stopped responding",
    };

    private void Die(string why, int? exitCode = null)
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0) return;   // the exit, the pipe EOF and a hang may all notice: report once
        if (_dead) return;   // disposed or reconfigured while this callback was queued
        _link.Stop();   // the shared block is not used again
        _dead = true;
        CrashReportPath = CrashReportFor(_process);
        // A pipe EOF can be noticed while the helper is still alive (it stopped reading but did not exit): end it here,
        // or it loops TakeRequest forever and keeps the shared block and pipe open. Also release the block file now
        // instead of waiting for a later Dispose that may never come.
        try { if (!_process.HasExited) _process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { } // swallow: already gone or cannot be killed
        DeleteBlockFile();
        var code = exitCode is { } value ? $", exit code 0x{unchecked((uint)value):X8}" : "";
        var report = CrashReportPath is null ? "" : $", report {CrashReportPath}";
        _threads.Log.Write($"isolated plug-in lost ({why}{code}{report}): {Path}");
        _threads.Post(() => Crashed?.Invoke(this));
    }

    /// <summary>The helper's minidump for this process, when its crash handler wrote one (the one-line text sits beside it). On Linux the Wine prefix's Windows path is mapped back to a Linux path.</summary>
    private string? CrashReportFor(Process process)
    {
        try
        {
            var helper = System.IO.Path.GetFileNameWithoutExtension(_launch.HelperPath ?? _launch.ExecutablePath);
            if (string.IsNullOrEmpty(helper)) return null;
            if (OperatingSystem.IsLinux() && _launch.IsTcp)
            {
                // The Wine helper writes under its Windows LOCALAPPDATA (inside the private prefix); map it back.
                var prefix = TabForge.Plugins.WineBridgeSupport.PrefixPath;
                return WineRuntime.LinuxCrashDump(prefix, helper, process.Id);
            }
            if (!OperatingSystem.IsWindows()) return null;   // the helper's minidump path is a Windows LOCALAPPDATA path
            // The helper writes under LOCALAPPDATA; read the same variable so a test profile (or a redirected folder) finds it.
            var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            var diagnostics = string.IsNullOrEmpty(local)
                ? TabForge.Platform.AppFolders.Current.Logs   // the same %LOCALAPPDATA%\TabForge\Diagnostics layout
                : System.IO.Path.Combine(local, "TabForge", "Diagnostics");
            var dump = System.IO.Path.Combine(diagnostics, $"{helper}-{process.Id}.dmp");
            return File.Exists(dump) ? dump : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    private static int? ExitCodeIfExited(Process process)
    {
        try { return process.HasExited ? process.ExitCode : null; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    private void ReadReplies(Stream pipe, PluginControlChannel control)
    {
        try
        {
            while (!_dead && ReferenceEquals(pipe, _pipe))
            {
                var next = Frames.Read(pipe, IsolatedFrameLimits.ForReply);
                if (next is null) break;
                using var frame = next.Value;
                var (type, r) = frame;
                if (type == MsgEdited) { _threads.Post(() => Edited?.Invoke()); continue; }
                if (type == MsgEditorClosed) { _threads.Post(() => EditorClosed?.Invoke()); continue; }
                if (type == MsgEditorSized) { var (width, height) = (r.ReadInt32(), r.ReadInt32()); _threads.Post(() => EditorSized?.Invoke(width, height)); continue; }
                if (!control.Receive(type, r)) _threads.Log.Write($"isolated plug-in: dropped a late or unknown reply ({type}): {Path}");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ObjectDisposedException) { }
        if (ReferenceEquals(pipe, _pipe)) Die("its connection closed", ExitCodeIfExited(_process));
    }

    private byte[]? Ask(byte command, Action<BinaryWriter>? payload, byte expect, int timeoutMs = 5000) => Ask(command, payload, expect, timeoutMs, out _, out _);

    private byte[]? Ask(byte command, Action<BinaryWriter>? payload, byte expect, int timeoutMs, out bool timedOut) => Ask(command, payload, expect, timeoutMs, out timedOut, out _);

    private byte[]? Ask(byte command, Action<BinaryWriter>? payload, byte expect, int timeoutMs, out bool timedOut, out string? failure)
    {
        timedOut = false;
        failure = null;
        if (_dead) return null;
        try { return _control.Ask(command, payload, expect, timeoutMs, out timedOut, out failure); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { return null; }
    }

    public byte[]? GetState()
    {
        var data = Ask(CmdGetState, null, MsgState, 5000, out var timedOut, out var failure);
        // A timeout is not "no state": the caller reports it so the save warns and keeps the previous settings.
        if (timedOut) throw new TimeoutException($"{System.IO.Path.GetFileName(Path)} did not answer the state request within 5 s.");
        if (failure is not null && failure.StartsWith(StateTooLargePrefix, StringComparison.Ordinal))
            throw new InvalidDataException($"{System.IO.Path.GetFileName(Path)}'s state is larger than the {PluginStateLimits.MaxRawBytes / 1048576} MiB limit.");
        if (data is null || data.Length < 4) return null;
        var length = BitConverter.ToInt32(data, 0);
        if (length <= 0 || length > data.Length - 4) return null;
        // The helper enforces the same 16 MiB cap (tfvst32_protocol.h kMaxStateBytes) and PluginStateLimits.MaxRawBytes is
        // the managed source of truth: the three values are one contract, so change them together. A helper built against
        // an older protocol must not sneak a huge state past the engine.
        if (length > PluginStateLimits.MaxRawBytes)
            throw new InvalidDataException($"{System.IO.Path.GetFileName(Path)}'s state is larger than the {PluginStateLimits.MaxRawBytes / 1048576} MiB limit.");
        return data.AsSpan(4, length).ToArray();
    }

    public void SetState(byte[] state) => SetStateCore(state);

    /// <summary>Sends a VST2 program change (effSetProgram); true when the helper accepted it, false when the plug-in has no such program or did not answer.</summary>
    public bool SetProgram(int program)
    {
        if (_dead) return false;
        var data = Ask(CmdSetProgram, w => w.Write(program), MsgParams);
        return data is { Length: >= 4 } && BitConverter.ToInt32(data, 0) != 0;
    }

    // True when the plug-in process accepted the state (a MsgState reply); Reconfigure logs a rejection instead of dropping it silently.
    private bool SetStateCore(byte[] state) => Ask(CmdSetState, w => { w.Write(state.Length); w.Write(state); }, MsgState) is not null;

    public (int Width, int Height)? OpenEditor(IntPtr owner) => OpenEditor(owner, docked: false);

    /// <summary>Isolated editors live in the plug-in's own process: the window is owned by TabForge's window (<paramref name="owner"/>), docked over its area or floating.
    /// Returns the editor size, or null when the editor did not open.</summary>
    public (int Width, int Height)? OpenEditor(IntPtr owner, bool docked)
    {
        var data = Ask(CmdOpenEditor, w => WriteOpenEditor(w, owner, docked), MsgEditor);
        return data is null ? null : ReadEditorSize(data);
    }

    // The wire layout of the open-editor command and its reply: written here and read in PluginHostMain, nowhere else.
    internal static void WriteOpenEditor(BinaryWriter w, IntPtr owner, bool docked) { w.Write((long)owner); w.Write(docked); }
    internal static (IntPtr Owner, bool Docked) ReadOpenEditor(BinaryReader r) => (new IntPtr(r.ReadInt64()), r.ReadBoolean());
    internal static void WriteEditorSize(BinaryWriter w, (int Width, int Height)? size) { w.Write(size?.Width ?? 0); w.Write(size?.Height ?? 0); }
    internal static (int Width, int Height)? ReadEditorSize(byte[] data) =>
        data.Length >= 8 && BitConverter.ToInt32(data, 0) is > 0 and var width ? (width, BitConverter.ToInt32(data, 4)) : null;

    /// <summary>What the plug-in reports about itself (read in its own process); unavailable when it does not answer within a second.</summary>
    public LoadedPluginInfo Describe()
    {
        var data = Ask(CmdDescribe, null, MsgInfo, 1000);
        if (data is null) return LoadedPluginInfo.Unavailable;
        using var r = new BinaryReader(new MemoryStream(data));
        return LoadedPluginInfo.Read(r);
    }

    // Closing an editor is not a realtime command: Wine can spend ~2 s in the plug-in's effEditClose and
    // X11 teardown before the helper sends MsgEditor. A timeout here also faults the whole control channel,
    // so a close that merely crossed the old 2 s budget made every following reopen fail. Keep this well
    // above the measured close time; a helper that is really stuck still times out and fails the check.
    public void CloseEditor() => Ask(CmdCloseEditor, null, MsgEditor, 15000);
    public void EditorIdle() { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        EndSession();
    }

    /// <summary>Ends the current plug-in process and frees its pipe and shared block. Engine main thread, while audio is stopped.</summary>
    private void EndSession()
    {
        _link.Stop();
        if (!_dead)
        {
            _dead = true;
            Interlocked.Exchange(ref _reported, 1);   // an orderly end is not a crash
            // A helper whose main thread timed out is not reading: a quit write could block teardown, so just kill it.
            if (_control is null || !_control.Faulted)
                try { Frames.Write(_pipe, CmdQuit); } catch (Exception) { } // swallow: best-effort quit signal during teardown.
            try { if (!_process.WaitForExit(1500)) _process.Kill(true); } catch (Exception) { } // swallow: the plug-in process already exited or cannot be killed.
        }
        Release();
    }

    /// <summary>Unmaps the audio block and deletes its file; idempotent, safe from the crash path and from Release.</summary>
    private void DeleteBlockFile()
    {
        // The block refuses new calls and waits for one already in flight before unmapping its view/events.
        try { _block?.Dispose(); } catch (Exception) { } // swallow: best-effort dispose during teardown.
        var blockPath = _blockPath;
        _blockPath = null;
        if (blockPath is not null) try { File.Delete(blockPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // swallow: a stale runtime file is swept later.
    }

    private void Release()
    {
        try { _listener?.Stop(); } catch (Exception) { } // swallow: best-effort listener stop during teardown.
        try { _pipe?.Dispose(); } catch (Exception) { } // swallow: best-effort dispose during teardown.
        try { _tcpClient?.Dispose(); } catch (Exception) { } // swallow: the stream may already own the socket.
        try { if (_process is not null && !_process.HasExited) _process.Kill(true); } catch (Exception) { } // swallow: the plug-in process already exited.
        try { _reader?.Join(500); } catch (Exception) { } // swallow: best-effort join of the reader thread during teardown.
        var watch = _watch;
        _watch = null;
        try { watch?.Join(100); } catch (Exception) { } // swallow: the watchdog polls and exits once the link is stopped.
        DeleteBlockFile();
        try { _process?.Dispose(); } catch (Exception) { } // swallow: best-effort dispose during teardown.
    }
}
