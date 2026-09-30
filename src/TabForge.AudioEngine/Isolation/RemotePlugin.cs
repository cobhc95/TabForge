using System.Diagnostics;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Isolation;

/// <summary>
/// A plug-in running in its own process (Settings > Audio &amp; VST > Run each plug-in in its own process). To the
/// chain it looks like any plug-in. If that process crashes or stops answering, only this plug-in is lost: effects
/// pass the sound through unchanged, instruments go silent, and <see cref="Crashed"/> reports it. A block the process
/// does not answer within the callback's budget is bypassed; the plug-in counts as lost only when it stays busy past a
/// grace period or misses too many blocks (see <see cref="PluginHostLink"/>). This is crash isolation, not a sandbox: the plug-in's process runs as the same user with the same
/// rights as TabForge, so it can do anything TabForge can; only its crashes and hangs are kept away from the engine.
/// </summary>
public sealed class RemotePlugin : IPluginInstance
{
    // Host commands over the pipe.
    public const byte CmdGetState = 1, CmdSetState = 2, CmdOpenEditor = 3, CmdCloseEditor = 4, CmdQuit = 9;
    public const byte MsgLoaded = 100, MsgFailed = 101, MsgState = 102, MsgEditor = 103, MsgEdited = 104;

    // One plug-in process session; replaced as a whole by Reconfigure (main thread, audio stopped). Events from an older
    // session (its exit, its reply reader ending, its hang report) are ignored.
    private PluginHostBlock _block = null!;
    private NamedPipeServerStream _pipe = null!;
    private System.Diagnostics.Process _process = null!;
    private Thread? _reader;
    private PluginHostLink _link = null!;
    private readonly PluginSpec _spec;
    private bool _disposed;
    private volatile bool _dead;
    private PluginControlChannel _control = null!;   // request ids: a late reply is never taken for the next request's (R-11)

    public string Path { get; }
    public bool IsInstrument { get; private set; }
    public bool HasEditor { get; private set; }
    public int LatencySamples { get; private set; }
    public int TailSamples { get; private set; }

    /// <summary>The plug-in's own process switches its mode before the next block (an empty block carries the request); render blocks wait far longer than a realtime block.</summary>
    public void SetOfflineMode(bool offline) => _link.SetOfflineMode(offline);
    /// <summary>The plug-in's process ended or hung (raised once, on the engine's main thread).</summary>
    public event Action<RemotePlugin>? Crashed;
    public event Action? Edited;

    public RemotePlugin(PluginSpec spec, int sampleRate, int maxBlock)
    {
        Path = spec.Path;
        _spec = spec;
        StartSession(sampleRate, maxBlock);
    }

    /// <summary>Starts the plug-in's process at this rate / block size and waits for it to load (throws if it does not).</summary>
    private void StartSession(int sampleRate, int maxBlock)
    {
        var id = Guid.NewGuid().ToString("N");
        var block = _block = PluginHostBlock.Create(id, maxBlock);
        var pipe = _pipe = new NamedPipeServerStream($"TabForge.PluginHost.{id}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Engine path unknown.");
        var args = $"--plugin-host {id} {Environment.ProcessId} {sampleRate} {maxBlock} {(_spec.IsInstrument ? 1 : 0)} {_spec.Format} \"{_spec.Path}\" {(string.IsNullOrEmpty(_spec.ExpectedSha256) ? "none" : _spec.ExpectedSha256)}";
        var process = _process = System.Diagnostics.Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true })
            ?? throw new InvalidOperationException("The plug-in process did not start.");
        ChildProcessJob.ForThisProcess.Add(process);   // ends with the engine, however the engine ends
        // A late block fails the plug-in (audio thread): the kill and the report run on the engine's main thread.
        void Hung() { if (!ReferenceEquals(process, _process)) return; try { process.Kill(); } catch (InvalidOperationException) { } Die("it stopped responding"); }
        _link = new PluginHostLink(block, sampleRate, () => EngineThreads.Post(Hung));
        _dead = false;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => { if (ReferenceEquals(process, _process)) Die("its process ended"); };
        try
        {
            if (!pipe.WaitForConnectionAsync().Wait(15000)) throw new TimeoutException("The plug-in process did not answer.");
            var first = Frames.Read(pipe) ?? throw new InvalidOperationException("The plug-in process closed.");
            if (first.Type == MsgFailed) throw new InvalidOperationException(first.Reader.ReadBoundedString(2048));
            IsInstrument = first.Reader.ReadBoolean(); HasEditor = first.Reader.ReadBoolean(); LatencySamples = first.Reader.ReadInt32();
            TailSamples = first.Reader.ReadInt32();
            _link.IsInstrument = IsInstrument;
            var control = _control = new PluginControlChannel(pipe);
            _reader = new Thread(() => ReadReplies(pipe, control)) { IsBackground = true, Name = "TabForge plug-in host replies" };
            _reader.Start();
        }
        catch
        {
            _dead = true;
            _link.Stop();
            try { process.Kill(); } catch (InvalidOperationException) { }
            Release();
            throw;
        }
    }

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
            catch (TimeoutException ex) { EngineLog.Write($"isolated plug-in reconfigure: {ex.Message}"); }
        }
        EndSession();
        StartSession((int)Math.Round(sampleRate), Math.Clamp(maxBlock, 16, 8192));
        if (state is not null) SetState(state);
    }

    /// <summary>Audio thread: one block through the plug-in's process (see <see cref="PluginHostLink"/>).</summary>
    public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport) =>
        _link.Process(input, output, frames, midi, transport);

    private void Die(string why)
    {
        if (_dead) return;   // every path that sets _dead has stopped the link already
        _link.Stop();   // the shared block is not used again
        _dead = true;
        EngineLog.Write($"isolated plug-in lost ({why}): {Path}");
        EngineThreads.Post(() => Crashed?.Invoke(this));
    }

    private void ReadReplies(NamedPipeServerStream pipe, PluginControlChannel control)
    {
        try
        {
            while (!_dead && ReferenceEquals(pipe, _pipe))
            {
                var frame = Frames.Read(pipe);
                if (frame is null) break;
                var (type, r) = frame.Value;
                if (type == MsgEdited) { EngineThreads.Post(() => Edited?.Invoke()); continue; }
                if (!control.Receive(type, r)) EngineLog.Write($"isolated plug-in: dropped a late or unknown reply ({type}): {Path}");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ObjectDisposedException) { }
        if (ReferenceEquals(pipe, _pipe)) Die("its connection closed");
    }

    private byte[]? Ask(byte command, Action<BinaryWriter>? payload, byte expect, int timeoutMs = 5000) => Ask(command, payload, expect, timeoutMs, out _);

    private byte[]? Ask(byte command, Action<BinaryWriter>? payload, byte expect, int timeoutMs, out bool timedOut)
    {
        timedOut = false;
        if (_dead) return null;
        try { return _control.Ask(command, payload, expect, timeoutMs, out timedOut); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { return null; }
    }

    public byte[]? GetState()
    {
        var data = Ask(CmdGetState, null, MsgState, 5000, out var timedOut);
        // A timeout is not "no state": the caller reports it so the save warns and keeps the previous settings.
        if (timedOut) throw new TimeoutException($"{System.IO.Path.GetFileName(Path)} did not answer the state request within 5 s.");
        if (data is null || data.Length < 4) return null;
        var length = BitConverter.ToInt32(data, 0);
        return length <= 0 || length > data.Length - 4 ? null : data.AsSpan(4, length).ToArray();
    }

    public void SetState(byte[] state) => Ask(CmdSetState, w => { w.Write(state.Length); w.Write(state); }, MsgState);

    /// <summary>Isolated editors live in the plug-in's own process: the parent here is TabForge's window (owner).</summary>
    public (int Width, int Height)? OpenEditor(IntPtr owner)
    {
        var data = Ask(CmdOpenEditor, w => w.Write((long)owner), MsgEditor);
        return data is { Length: >= 8 } ? (BitConverter.ToInt32(data, 0), BitConverter.ToInt32(data, 4)) : null;
    }

    public void CloseEditor() => Ask(CmdCloseEditor, null, MsgEditor, 2000);
    public void EditorIdle() { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        EndSession();
    }

    /// <summary>Ends the current plug-in process and frees its pipe and shared block.</summary>
    private void EndSession()
    {
        _link.Stop();
        if (!_dead)
        {
            _dead = true;
            try { Frames.Write(_pipe, CmdQuit); } catch (Exception) { }
            try { if (!_process.WaitForExit(1500)) _process.Kill(true); } catch (Exception) { }
        }
        Release();
    }

    private void Release()
    {
        try { _pipe.Dispose(); } catch (Exception) { }
        try { if (!_process.HasExited) _process.Kill(true); } catch (Exception) { }
        try { _reader?.Join(500); } catch (Exception) { }
        try { _block.Dispose(); } catch (Exception) { }
        try { _process.Dispose(); } catch (Exception) { }
    }
}
