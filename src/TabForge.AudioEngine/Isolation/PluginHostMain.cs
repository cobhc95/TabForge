using System.Diagnostics;
using System.IO.Pipes;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Isolation;

/// <summary>
/// A process that runs exactly one plug-in (TabForge.exe --plugin-host id enginePid rate block instrument format path [sha256|none]).
/// Audio is processed on a dedicated thread signalled by the engine; the main thread runs the command pipe and the
/// plug-in's editor window. Exits when the engine goes away.
/// </summary>
public static class PluginHostMain
{
    public static int Run(string[] args)
    {
        if (args.Length < 8) return 2;
        var id = args[1];
        if (id.Length != 32 || !id.All(char.IsLetterOrDigit)) return 2;
        if (!int.TryParse(args[2], out var enginePid) || !int.TryParse(args[3], out var rate) || !int.TryParse(args[4], out var maxBlock)) return 2;
        var wantInstrument = args[5] == "1";
        var format = args[6];
        var path = args[7];
        var expectedSha256 = args.Length > 8 && args[8] != "none" ? args[8] : "";   // the approved hash the engine was sent; re-checked here because this process does the actual load (also after a device change)
        Process engine;
        try { engine = Process.GetProcessById(enginePid); } catch (ArgumentException) { return 3; }

        using var pipe = new NamedPipeClientStream(".", $"TabForge.PluginHost.{id}", PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        pipe.Connect(5000);
        using var block = PluginHostBlock.Open(id, maxBlock);
        IPluginInstance plugin;
        try
        {
            plugin = PluginFactory.CreateVerified(path, format, wantInstrument, rate, maxBlock, expectedSha256, required: false);
        }
        catch (Exception ex)
        {
            Frames.Write(pipe, RemotePlugin.MsgFailed, w => w.WriteString(ex.GetBaseException().Message));
            return 4;
        }
        if (plugin is Vst2Plugin vst2) vst2.Edited += () => Frames.Write(pipe, RemotePlugin.MsgEdited);
        Frames.Write(pipe, RemotePlugin.MsgLoaded, w => { w.Write(plugin.IsInstrument); w.Write(plugin.HasEditor); w.Write(plugin.LatencySamples); w.Write(plugin.TailSamples); });

        var quit = false;
        var input = new[] { new float[maxBlock], new float[maxBlock] };
        var output = new[] { new float[maxBlock], new float[maxBlock] };
        var events = new BlockMidi[PluginHostBlock.MaxEvents];
        var audio = new Thread(() =>
        {
            EngineThreads.MarkAudioThread();
            var offlineNow = false;
            while (!quit)
            {
                if (!block.TakeRequest(250, out var generation)) continue;
                if ((block.Offline != 0) != offlineNow)
                {
                    offlineNow = block.Offline != 0;
                    EngineThreads.SetOfflineFlag(offlineNow);   // this thread answers "offline process level" while rendering
                    try { plugin.SetOfflineMode(offlineNow); } catch (Exception ex) { EngineLog.Write($"offline mode failed: {ex.Message}"); }
                }
                var frames = Math.Clamp(block.Frames, 0, maxBlock);
                block.Channel(0)[..frames].CopyTo(input[0]);
                block.Channel(1)[..frames].CopyTo(input[1]);
                var count = Math.Clamp(block.EventCount, 0, events.Length);
                block.Events[..count].CopyTo(events);
                var transport = new TransportInfo
                {
                    Tempo = block.Tempo, PpqPosition = block.Ppq, Playing = block.Playing != 0,
                    Meter = new TransportMeter { Numerator = block.TimeSigNumerator, Denominator = block.TimeSigDenominator, BarStartPpq = block.BarStartPpq },
                };
                plugin.Process(input, output, frames, events.AsSpan(0, count), transport);
                output[0].AsSpan(0, frames).CopyTo(block.Channel(2));
                output[1].AsSpan(0, frames).CopyTo(block.Channel(3));
                block.Complete(generation);   // the engine accepts this answer only for the request it is waiting on
            }
        }) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "TabForge plug-in audio" };
        audio.Start();

        var reader = new Thread(() =>
        {
            try
            {
                while (!quit)
                {
                    var frame = Frames.Read(pipe);
                    if (frame is null) break;
                    var (type, r) = frame.Value;
                    if (type == RemotePlugin.CmdQuit) { quit = true; break; }
                    // Every other command starts with its request id; the reply echoes it, and a handler that throws
                    // still answers (MsgFailed) so the engine never waits out its timeout for nothing (R-11).
                    var id = r.ReadUInt32();
                    switch (type)
                    {
                        case RemotePlugin.CmdGetState:
                            EngineThreads.Post(() => PluginControlChannel.Answer(pipe, id, () =>
                            {
                                byte[] state;
                                try { state = plugin.GetState() ?? Array.Empty<byte>(); }
                                catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { EngineLog.Write($"get state: {ex.Message}"); state = Array.Empty<byte>(); }
                                return (RemotePlugin.MsgState, w => { w.Write(state.Length); w.Write(state); });
                            }));
                            break;
                        case RemotePlugin.CmdSetState:
                        {
                            var length = r.ReadInt32();
                            if (length is < 0 or > PluginStateLimits.MaxRawBytes) throw new InvalidDataException("Bad state length.");
                            var state = r.ReadBytes(length);
                            EngineThreads.Post(() => PluginControlChannel.Answer(pipe, id, () => { plugin.SetState(state); return (RemotePlugin.MsgState, w => w.Write(0)); }));
                            break;
                        }
                        case RemotePlugin.CmdOpenEditor:
                        {
                            var owner = new IntPtr(r.ReadInt64());
                            EngineThreads.Post(() => PluginControlChannel.Answer(pipe, id, () =>
                            {
                                var shown = EditorWindows.Show(plugin, Path.GetFileNameWithoutExtension(path), owner, dark: true);
                                return (RemotePlugin.MsgEditor, w => { w.Write(shown ? 1 : 0); w.Write(0); });
                            }));
                            break;
                        }
                        case RemotePlugin.CmdCloseEditor:
                            EngineThreads.Post(() => PluginControlChannel.Answer(pipe, id, () => { EditorWindows.CloseFor(plugin); return (RemotePlugin.MsgEditor, w => { w.Write(0); w.Write(0); }); }));
                            break;
                        default:
                            PluginControlChannel.Answer(pipe, id, () => throw new InvalidOperationException($"Unknown command {type}."));
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or ObjectDisposedException) { }
            quit = true;
        }) { IsBackground = true, Name = "TabForge plug-in commands" };
        reader.Start();

        var lastIdle = Stopwatch.GetTimestamp();
        Func<bool> engineAlive = () => !engine.HasExited;
        while (!quit)
        {
            EngineThreads.RunPending();
            if (!EditorWindows.PumpOnce(ref lastIdle, engineAlive)) break;
        }
        quit = true;
        EditorWindows.CloseAll();
        plugin.Dispose();
        return 0;
    }
}
