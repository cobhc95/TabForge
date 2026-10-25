using System.Diagnostics;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Isolation;

// Owns: the single-plug-in host process (dedicated audio thread, command pipe and editor on the main thread).

/// <summary>
/// A process that runs exactly one plug-in (TabForge.exe --plugin-host id enginePid rate block instrument format path [sha256|none] [logPath|none]).
/// Audio is processed on a dedicated thread signalled by the engine; the main thread runs the command pipe and the
/// plug-in's editor window. Exits when the engine goes away.
/// </summary>
public static class PluginHostMain
{
    /// <summary>Sample rates the host accepts; a request outside them is clamped, so a bad value cannot size the buffers unboundedly.</summary>
    private const int MinSampleRate = 8_000, MaxSampleRate = 384_000;
    /// <summary>Largest block the host sizes its buffers for, and the smallest one a plug-in is asked for.</summary>
    private const int MinBlockFrames = 16, MaxBlockFrames = 8_192;

    public static int Run(string[] args)
    {
        if (args.Length < 8) return 2;
        var log = new EngineLogWriter();   // composition root of the plug-in host process: its own log writer and main-thread queue
        var threads = new EngineThreads(log);
        var id = args[1];
        if (id.Length != 32 || !id.All(char.IsLetterOrDigit)) return 2;
        if (!int.TryParse(args[2], out var enginePid) || !int.TryParse(args[3], out var requestedRate) || !int.TryParse(args[4], out var requestedBlock)) return 2;
        var rate = Math.Clamp(requestedRate, MinSampleRate, MaxSampleRate);
        var maxBlock = Math.Clamp(requestedBlock, MinBlockFrames, MaxBlockFrames);
        var wantInstrument = args[5] == "1";
        var format = args[6];
        if (format is not ("" or "VST2" or "VST3")) return 2;
        var path = args[7];
        var expectedSha256 = args.Length > 8 && args[8] != "none" ? args[8] : "";   // the approved hash the engine was sent; re-checked here because this process does the actual load (also after a device change)
        // The engine's log file, shared with the engine so this process's lines land in the same place (none: this process keeps no log).
        if (args.Length > 9 && args[9] != "none") log.Attach(args[9]);
        // "location": the binary lies in a folder only an administrator can write, so no hash is sent; "checked": the hash above is required.
        var locationTrusted = args.Length > 10 && args[10] == "location";
        Process engine;
        try { engine = Process.GetProcessById(enginePid); } catch (ArgumentException) { return 3; }

        using var pipe = new NamedPipeClientStream(".", $"TabForge.PluginHost.{id}", PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        pipe.Connect(5000);
        using var block = PluginHostBlock.Open(id, maxBlock);
        IPluginInstance plugin;
        try
        {
            plugin = PluginFactory.CreateVerified(path, format, wantInstrument, rate, maxBlock, expectedSha256, locationTrusted, threads);
        }
        catch (Exception ex)
        {
            Frames.Write(pipe, RemotePlugin.MsgFailed, w => w.WriteString(ex.GetBaseException().Message));
            return 4;
        }
        if (plugin is Vst2Plugin vst2) vst2.Edited += () => Frames.Write(pipe, RemotePlugin.MsgEdited);
        Frames.Write(pipe, RemotePlugin.MsgLoaded, w => { w.Write(plugin.IsInstrument); w.Write(plugin.HasEditor); w.Write(plugin.LatencySamples); w.Write(plugin.TailSamples); });

        var editors = new EditorWindows(log);
        var host = new HostSession(log, threads, pipe, block, plugin, path, maxBlock, editors);
        host.StartAudioThread();
        host.StartCommandThread();

        var lastIdle = Stopwatch.GetTimestamp();
        Func<bool> engineAlive = () => !engine.HasExited;
        while (!host.Quit)
        {
            threads.RunPending();
            if (!editors.PumpOnce(ref lastIdle, engineAlive)) break;
        }
        host.Quit = true;
        editors.CloseAll();
        plugin.Dispose();
        return 0;
    }

    /// <summary>What the host process runs while the plug-in is loaded: the audio thread, the command thread and the quit flag they share.</summary>
    private sealed class HostSession
    {
        private readonly EngineLogWriter _log;
        private readonly EngineThreads _threads;
        private readonly NamedPipeClientStream _pipe;
        private readonly PluginHostBlock _block;
        private readonly IPluginInstance _plugin;
        private readonly string _path;
        private readonly int _maxBlock;
        private readonly EditorWindows _editors;
        private readonly float[][] _input;
        private readonly float[][] _output;
        private readonly BlockMidi[] _events = new BlockMidi[PluginHostBlock.MaxEvents];
        private volatile bool _quit;

        public HostSession(EngineLogWriter log, EngineThreads threads, NamedPipeClientStream pipe, PluginHostBlock block, IPluginInstance plugin, string path, int maxBlock, EditorWindows editors)
        {
            _log = log; _threads = threads; _pipe = pipe; _block = block; _plugin = plugin; _path = path; _maxBlock = maxBlock; _editors = editors;
            _input = new[] { new float[maxBlock], new float[maxBlock] };
            _output = new[] { new float[maxBlock], new float[maxBlock] };
        }

        public bool Quit { get => _quit; set => _quit = value; }

        public void StartAudioThread() =>
            new Thread(AudioLoop) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "TabForge plug-in audio" }.Start();

        public void StartCommandThread() =>
            new Thread(CommandLoop) { IsBackground = true, Name = "TabForge plug-in commands" }.Start();

        private void AudioLoop()
        {
            _threads.MarkAudioThread();
            var offlineNow = false;
            while (!_quit)
            {
                if (!_block.TakeRequest(250, out var generation)) continue;
                if ((_block.Offline != 0) != offlineNow)
                {
                    offlineNow = _block.Offline != 0;
                    _threads.SetOfflineFlag(offlineNow);   // this thread answers "offline process level" while rendering
                    try { _plugin.SetOfflineMode(offlineNow); } catch (Exception ex) { _log.Write($"offline mode failed: {ex.Message}"); }
                }
                var frames = Math.Clamp(_block.Frames, 0, _maxBlock);
                _block.Channel(0)[..frames].CopyTo(_input[0]);
                _block.Channel(1)[..frames].CopyTo(_input[1]);
                var count = Math.Clamp(_block.EventCount, 0, _events.Length);
                _block.Events[..count].CopyTo(_events);
                var transport = new TransportInfo
                {
                    Tempo = _block.Tempo, PpqPosition = _block.Ppq, Playing = _block.Playing != 0,
                    Meter = new TransportMeter { Numerator = _block.TimeSigNumerator, Denominator = _block.TimeSigDenominator, BarStartPpq = _block.BarStartPpq },
                };
                _plugin.Process(_input, _output, frames, _events.AsSpan(0, count), transport);
                _output[0].AsSpan(0, frames).CopyTo(_block.Channel(2));
                _output[1].AsSpan(0, frames).CopyTo(_block.Channel(3));
                _block.Complete(generation);   // the engine accepts this answer only for the request it is waiting on
            }
        }

        private void CommandLoop()
        {
            try
            {
                while (!_quit)
                {
                    var next = Frames.Read(_pipe, IsolatedFrameLimits.ForCommand);
                    if (next is null) break;
                    using var frame = next.Value;
                    var (type, r) = frame;
                    if (type == RemotePlugin.CmdQuit) { _quit = true; break; }
                    // Every other command starts with its request id; the reply echoes it, and a handler that throws
                    // still answers (MsgFailed) so the engine never waits out its timeout for nothing.
                    HandleCommand(type, r, r.ReadUInt32());
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ObjectDisposedException) { }
            _quit = true;
        }

        private void HandleCommand(byte type, BinaryReader r, uint id)
        {
            var pipe = _pipe; var log = _log; var plugin = _plugin; var editors = _editors; var threads = _threads; var path = _path;
            switch (type)
            {
                case RemotePlugin.CmdGetState:
                    threads.Post(() => PluginControlChannel.Answer(pipe, id, log, () =>
                    {
                        byte[] state;
                        try { state = plugin.GetState() ?? Array.Empty<byte>(); }
                        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { log.Write($"get state: {ex.Message}"); state = Array.Empty<byte>(); }
                        return (RemotePlugin.MsgState, w => { w.Write(state.Length); w.Write(state); });
                    }));
                    break;
                case RemotePlugin.CmdSetState:
                {
                    var length = r.ReadInt32();
                    if (length is < 0 or > PluginStateLimits.MaxRawBytes) throw new InvalidDataException("Bad state length.");
                    var state = r.ReadBytes(length);
                    threads.Post(() => PluginControlChannel.Answer(pipe, id, log, () => { plugin.SetState(state); return (RemotePlugin.MsgState, w => w.Write(0)); }));
                    break;
                }
                case RemotePlugin.CmdOpenEditor:
                {
                    var (owner, docked) = RemotePlugin.ReadOpenEditor(r);
                    threads.Post(() => PluginControlChannel.Answer(pipe, id, log, () =>
                    {
                        (int Width, int Height)? size = null;
                        var replied = false;
                        // The size at open goes in the reply; a later resize request of the editor goes to the engine as an event.
                        void Sized(int w, int h) { if (replied) Frames.Write(pipe, RemotePlugin.MsgEditorSized, x => RemotePlugin.WriteEditorSize(x, (w, h))); else size = (w, h); }
                        editors.Show(plugin, Path.GetFileNameWithoutExtension(path), owner, dark: true, docked: docked, sized: Sized);
                        replied = true;
                        return (RemotePlugin.MsgEditor, w => RemotePlugin.WriteEditorSize(w, size));
                    }));
                    break;
                }
                case RemotePlugin.CmdCloseEditor:
                    threads.Post(() => PluginControlChannel.Answer(pipe, id, log, () => { editors.CloseFor(plugin); return (RemotePlugin.MsgEditor, w => { w.Write(0); w.Write(0); }); }));
                    break;
                case RemotePlugin.CmdDescribe:
                    threads.Post(() => PluginControlChannel.Answer(pipe, id, log, () => (RemotePlugin.MsgInfo, w => Vst3ClassInfo.Describe(plugin).Write(w))));
                    break;
                default:
                    PluginControlChannel.Answer(pipe, id, log, () => throw new InvalidOperationException($"Unknown command {type}."));
                    break;
            }
        }
    }
}
