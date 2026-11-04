using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Plugins;

namespace TabForge.Avalonia.Diagnostics;

// Owns: the Linux package's headless NATIVE VST3 editor run (--native-editor): loads the bundle in this process,
// opens its editor through the real X11EditorHost (a top-level X11 window, X11EmbedWindowID attach, the
// VST3 run loop on the main thread), keeps audio running, waits for the user close, reopens, and exits.
// Does not own: the editor host (X11EditorHost), the VST3 shim (native/tfvst3/posix), or the window manager.
// Tests: run by the wine-stage8 workflow under Xvfb; the script screenshots, resizes and closes the window.
internal static class NativeEditor
{
    public const string Argument = "--native-editor";
    private const int Rate = 48000, Frames = 512;

    public static int Run(string[] args)
    {
        // --native-editor <vst3> <instrument 0|1> <out-dir> [hold-ms]
        if (args.Length < 4 || !int.TryParse(args[2], out var instrument)) return Usage();
        var path = Path.GetFullPath(args[1]);
        if (!Directory.Exists(path) && !File.Exists(path)) return Fail("not found: " + path, null);
        var outDir = Path.GetFullPath(args[3]);
        var holdMs = args.Length > 4 && int.TryParse(args[4], out var hold) ? Math.Clamp(hold, 5000, 120000) : 60000;
        Directory.CreateDirectory(outDir);
        try
        {
            var log = new EngineLogWriter();
            log.CreateForRun(Path.Combine(outDir, "engine.log"));
            var threads = new EngineThreads(log);
            threads.MarkMainThread();
            using var plugin = new Vst3Plugin(path, instrument != 0, Rate, Frames, log);
            if (!plugin.HasEditor) return Fail("the plug-in has no X11 VST3 editor", outDir);

            var host = new X11EditorHost(log);
            var closed = new ManualResetEventSlim();
            host.UserClosed = closed.Set;
            (int Width, int Height)? shown = null;
            void Sized(int width, int height)
            {
                shown = (width, height);
                File.WriteAllText(Path.Combine(outDir, "native-editor-sized.txt"), $"editor-sized={width}x{height}");
            }

            var title = "TabForge - " + Path.GetFileNameWithoutExtension(path);
            if (!host.Show(plugin, title, IntPtr.Zero, dark: false, docked: false, onTop: false, sized: Sized))
                return Fail("X11EditorHost.Show returned false", outDir);
            if (shown is not { } open) return Fail("the editor reported no size", outDir);
            File.WriteAllText(Path.Combine(outDir, "native-editor-open.txt"),
                $"open width={open.Width} height={open.Height} host=x11-native");
            Console.Out.WriteLine($"native-editor open width={open.Width} height={open.Height}");

            var input = new[] { new float[Frames], new float[Frames] };
            var output = new[] { new float[Frames], new float[Frames] };
            var start = Stopwatch.GetTimestamp();
            var lastIdle = start;
            var blocks = 0;
            while (!closed.IsSet && Stopwatch.GetElapsedTime(start).TotalMilliseconds < holdMs)
            {
                threads.RunPending();
                plugin.Process(input, output, Frames, Array.Empty<BlockMidi>(), Transport(blocks));
                blocks++;
                if (!host.PumpOnce(ref lastIdle, () => true)) break;
            }
            if (!closed.IsSet) return Fail("the editor window was not closed (WM_DELETE_WINDOW did not arrive)", outDir);
            File.WriteAllText(Path.Combine(outDir, "native-editor-closed.txt"), "editor-closed=yes");
            Console.Out.WriteLine($"native-editor closed blocks={blocks} sized={shown?.Width}x{shown?.Height}");

            closed.Reset();
            if (!host.Show(plugin, title, IntPtr.Zero, dark: false, docked: false, onTop: false, sized: Sized))
                return Fail("the editor did not reopen", outDir);
            if (shown is not { } again) return Fail("the reopened editor reported no size", outDir);
            File.WriteAllText(Path.Combine(outDir, "native-editor-reopen.txt"),
                $"reopen width={again.Width} height={again.Height}");
            Console.Out.WriteLine($"native-editor reopen width={again.Width} height={again.Height}");

            // A short idle run so the reopened window is really mapped and painted before this process closes it.
            var reopenStart = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(reopenStart).TotalMilliseconds < 1500)
            {
                plugin.Process(input, output, Frames, Array.Empty<BlockMidi>(), Transport(blocks++));
                if (!host.PumpOnce(ref lastIdle, () => true)) break;
            }
            host.CloseFor(plugin);
            host.CloseAll();
            Console.Out.WriteLine($"native-editor ok path={path} blocks={blocks} width={again.Width} height={again.Height}");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex.GetBaseException().Message, outDir);
        }
    }

    private static TransportInfo Transport(int block) => new()
    {
        Tempo = 120, PpqPosition = block * (double)Frames / (2 * Rate), Playing = true,
        Meter = new TransportMeter { Numerator = 4, Denominator = 4, BarStartPpq = 0 },
    };

    private static int Usage()
    {
        Console.Error.WriteLine("usage: TabForge --native-editor <vst3> <instrument 0|1> <out-dir> [hold-ms]");
        return 2;
    }

    private static int Fail(string message, string? outDir)
    {
        if (outDir is not null)
            try { File.WriteAllText(Path.Combine(outDir, "native-editor-failed.txt"), message); } catch (IOException) { /* swallow: best-effort failure marker; console already reports the failure. */ }
        Console.Error.WriteLine("native-editor FAILED: " + message);
        return 1;
    }
}
