using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Plugins;

namespace TabForge.Avalonia.Diagnostics;

// Owns: the Linux package's headless editor run (--wine-editor): opens a real Windows plug-in's helper-owned
// top-level editor with owner 0, keeps audio running, records the window size, waits for the user close
// (MsgEditorClosed), reopens the editor and exits. The workflow screenshots and resizes the window externally.
// Does not own: the helper protocol (RemotePlugin/Vst32Bridge), the editor window itself (tfvst32), first-run setup.
// Tests: run by the wine-stage7 workflow under Xvfb; skips nothing and fails readably when anything is missing.
internal static class WineEditor
{
    public const string Argument = "--wine-editor";
    private const int Rate = 48000, Frames = 512;

    public static int Run(string[] args)
    {
        // --wine-editor <dll-or-vst3> <instrument 0|1> <out-dir> [hold-ms]
        if (args.Length < 4 || !int.TryParse(args[2], out var instrument)) return Usage();
        var path = Path.GetFullPath(args[1]);
        if (!File.Exists(path) && !Directory.Exists(path)) return Fail($"not found: {path}");
        var outDir = Path.GetFullPath(args[3]);
        var holdMs = args.Length > 4 && int.TryParse(args[4], out var hold) ? Math.Clamp(hold, 2000, 120000) : 30000;
        Directory.CreateDirectory(outDir);
        try
        {
            var spec = new PluginSpec(path, path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? "VST3" : "VST2",
                instrument != 0, Enabled: true, Wet: 100, State: null, Name: Path.GetFileNameWithoutExtension(path),
                Id: "wine-editor", LocationTrusted: false, ExpectedSha256: Sha256(path));
            var launch = Vst32Bridge.Launch(spec);
            var threads = new EngineThreads(new EngineLogWriter());
            threads.MarkMainThread();
            using var plugin = new RemotePlugin(spec, Rate, Frames, threads, launch);
            plugin.SetOfflineMode(true);
            var closed = new ManualResetEventSlim();
            var sized = "";
            plugin.EditorClosed += closed.Set;
            plugin.EditorSized = (width, height) => sized = $"{width}x{height}";
            var opened = plugin.OpenEditor(IntPtr.Zero, docked: false);
            if (opened is not { } size || size.Width <= 0 || size.Height <= 0)
                return Fail("the helper did not open a helper-owned editor (owner 0)");
            File.WriteAllText(Path.Combine(outDir, "editor-open.txt"),
                $"open width={size.Width} height={size.Height} helper={Path.GetFileName(launch.HelperPath)}");
            Console.Out.WriteLine($"wine-editor open width={size.Width} height={size.Height} helper={Path.GetFileName(launch.HelperPath)}");

            var input = new[] { new float[Frames], new float[Frames] };
            var output = new[] { new float[Frames], new float[Frames] };
            var deadline = Environment.TickCount64 + holdMs;
            var blocks = 0;
            while (!closed.IsSet && Environment.TickCount64 < deadline)
            {
                plugin.Process(input, output, Frames, Array.Empty<BlockMidi>(), Transport(blocks));
                threads.RunPending();
                blocks++;
                Thread.Sleep(15);
            }
            if (!closed.IsSet) return Fail("MsgEditorClosed did not arrive after the external window close");
            threads.RunPending();
            if (sized.Length == 0) Console.Out.WriteLine("wine-editor warning: the window manager resize was not reported");
            else File.WriteAllText(Path.Combine(outDir, "editor-sized.txt"), $"editor-sized={sized}");
            File.WriteAllText(Path.Combine(outDir, "editor-closed.txt"), "editor-closed=yes");
            Console.Out.WriteLine($"wine-editor closed blocks={blocks} sized={sized}");

            var reopened = plugin.OpenEditor(IntPtr.Zero, docked: false);
            if (reopened is not { } again || again.Width <= 0 || again.Height <= 0) return Fail("the editor did not reopen");
            File.WriteAllText(Path.Combine(outDir, "editor-reopen.txt"),
                $"reopen width={again.Width} height={again.Height}");
            plugin.CloseEditor();
            Console.Out.WriteLine($"wine-editor ok path={path} blocks={blocks} width={again.Width} height={again.Height} sized={sized}");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex.GetBaseException().Message);
        }
    }

    private static TransportInfo Transport(int block) => new()
    {
        Tempo = 120, PpqPosition = block * (double)Frames / (2 * Rate), Playing = true,
        Meter = new TransportMeter { Numerator = 4, Denominator = 4, BarStartPpq = 0 },
    };

    private static string Sha256(string path)
    {
        if (Directory.Exists(path))
        {
            var binary = PluginIdentity.BinaryOf(path) ?? throw new InvalidOperationException($"No loadable binary in {path}");
            path = binary;
        }
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: TabForge --wine-editor <dll-or-vst3> <instrument 0|1> <out-dir> [hold-ms]");
        return 2;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"wine-editor FAILED: {message}");
        return 1;
    }
}
