using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Avalonia.Shell.Win32;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: preset/state/program/parameter changes and the plug-in's own preset menu while its editor is open, for every
//   editor-bearing 32-bit fixture, plus the helper's crash report/minidump and the "only the plug-in is lost" rule.
// Does not own: the raw block/audio matrix (Vst32BridgeAudioTests, Vst32AudioMatrixTests) or the helper source.
[Collection("Vst32Native")]
public sealed class Vst32PresetChangeTests
{
    private const int Frames = 512;
    private const string CrashMarker = "passthrough32.crashwindow";
    private const string ModalMarker = "passthrough32.modal";
    private readonly ITestOutputHelper _output;
    public Vst32PresetChangeTests(ITestOutputHelper output) => _output = output;

    [Vst32RealTheory]
    [InlineData("TDRNova-TokyoDawn32", false)]
    [InlineData("TDRKotelnikov-TokyoDawn32", false)]
    [InlineData("Dexed32", true)]
    [InlineData("TAL-NoiseMaker32", true)]
    [InlineData("TAL-BassLine-101-32", true)]
    [InlineData("Synth1-Daichi32", true)]
    [InlineData("PurestGain32", false)]
    public void PresetChangesWithTheEditorOpenNeverTakeTheHelperDown(string name, bool instrument)
    {
        var path = Vst32TestAssets.FixturePath(name);
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        var (plugin, threads) = Vst32TestAssets.StartWithThreads(path, instrument, Frames);
        var pid = plugin.HostProcessId;
        var crashes = 0;
        var crashSignal = new ManualResetEventSlim();
        plugin.Crashed += _ => { Interlocked.Increment(ref crashes); crashSignal.Set(); };
        using var owner = new TestOwner();
        try
        {
            byte[]? first = plugin.GetState();
            var parameters = plugin.GetParameters();
            var moved = parameters.FirstOrDefault(p => !p.ReadOnly);
            if (moved is not null) plugin.SetParameter(moved.Id, moved.Value > 0.5 ? 0.2 : 0.8);
            var second = plugin.GetState();
            Assert.NotNull(first);
            Assert.NotNull(second);

            if (plugin.HasEditor)
            {
                Assert.NotNull(plugin.OpenEditor(owner.Handle, docked: true));
                Assert.NotEqual(IntPtr.Zero, PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero));
            }
            else
            {
                Assert.Null(plugin.OpenEditor(owner.Handle, docked: true));
            }

            var input = Enumerable.Repeat(0.25f, Frames).ToArray();
            var output = new[] { new float[Frames], new float[Frames] };
            for (var i = 0; i < 12 && Alive(pid); i++)
            {
                plugin.SetState(i % 2 == 0 ? first! : second!);
                plugin.SetProgram(i % 3);
                if (moved is not null) plugin.SetParameter(moved.Id, (i % 5) / 4.0);
                plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(i, Frames));
                threads.RunPending();
                Thread.Sleep(5);
            }

            AssertSurvivesOrIsReported(plugin, threads, pid, crashSignal, ref crashes, name);
        }
        finally
        {
            if (Alive(pid))
            {
                try { plugin.CloseEditor(); } catch (Exception) { }
            }
            var disposeAt = Stopwatch.GetTimestamp();
            plugin.Dispose();
            Assert.True(Stopwatch.GetElapsedTime(disposeAt).TotalMilliseconds < 3000, $"{name}: Dispose must stay bounded");
            if (Alive(pid)) Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), $"{name}: the helper must exit");
        }
    }

    [Vst32Fact]
    public void AHostCommandDuringThePluginsOwnMenuLoopIsRefusedQuickly()
    {
        var path = CopyTestPlugin(ModalMarker);
        try
        {
            var (plugin, threads) = Vst32TestAssets.StartWithThreads(path, instrument: false, Frames);
            var pid = plugin.HostProcessId;
            using var owner = new TestOwner();
            using (plugin)
            {
                var state = plugin.GetState();
                Assert.NotNull(state);
                Assert.NotNull(plugin.OpenEditor(owner.Handle, docked: true));
                var popup = PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero);
                Assert.NotEqual(IntPtr.Zero, popup);
                var editor = FindWindowExW(popup, IntPtr.Zero, "Passthrough32ModalEditor", null);
                Assert.NotEqual(IntPtr.Zero, editor);
                Assert.True(PostMessageW(editor, WmApp + 1, IntPtr.Zero, IntPtr.Zero));
                Assert.True(PumpUntil(threads, () => MenuExists(pid), 2000), "the plug-in's own menu loop did not start");
                // The plug-in's menu owns the helper's main thread. A host command must get an immediate readable refusal
                // (the nested service answers), never wait out the 5 s control timeout and fault the channel.
                var at = Stopwatch.GetTimestamp();
                plugin.SetState(state!);
                var elapsed = (int)Stopwatch.GetElapsedTime(at).TotalMilliseconds;
                _output.WriteLine($"a command during the plug-in menu answered in {elapsed} ms");
                Assert.True(elapsed < 1500, $"a command during the plug-in menu took {elapsed} ms");
                Assert.True(PumpUntil(threads, () => !MenuExists(pid), 4000), "the plug-in's menu must close");
                Assert.True(Alive(pid), "the helper must survive its own menu loop");
                Assert.Equal(LoadedPluginInfo.Vst2, plugin.Describe().Format);   // the pipe was serviced, not faulted
                plugin.Process(new[] { new float[Frames], new float[Frames] }, new[] { new float[Frames], new float[Frames] },
                    Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));
            }
            Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), "the helper must exit");
        }
        finally
        {
            DeleteFolder(path);
        }
    }

    [Vst32Fact]
    public void AnUnguardedPlugInFaultWritesAReportAndOnlyLosesTheHelper()
    {
        var path = CopyTestPlugin(CrashMarker);
        var localRoot = Directory.CreateTempSubdirectory("tf-vst32-local").FullName;
        var localBefore = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", localRoot);   // never prune the owner's real crash reports
        string? dump = null, text = null;
        try
        {
            var (plugin, threads) = Vst32TestAssets.StartWithThreads(path, instrument: false, Frames);
            var pid = plugin.HostProcessId;
            var crashSignal = new ManualResetEventSlim();
            var crashes = 0;
            plugin.Crashed += _ => { Interlocked.Increment(ref crashes); crashSignal.Set(); };
            using var owner = new TestOwner();
            using (plugin)
            {
                Assert.NotNull(plugin.OpenEditor(owner.Handle, docked: true));
                var popup = PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero);
                var editor = FindWindowExW(popup, IntPtr.Zero, "Passthrough32CrashEditor", null);
                Assert.NotEqual(IntPtr.Zero, editor);
                Assert.True(PostMessageW(editor, 0x0201, (IntPtr)1, IntPtr.Zero));   // WM_LBUTTONDOWN in the plug-in's WndProc
                Assert.True(PumpUntil(threads, () => crashSignal.IsSet, 8000), "the helper crash must be reported");
                Assert.Equal(1, crashes);
                Assert.True(plugin.LinkFailed);
                Assert.True(Vst32TestAssets.WaitsForExit(pid, 5000), "the crashed helper must be gone");
                dump = plugin.CrashReportPath;
                Assert.False(string.IsNullOrEmpty(dump), "the helper must name its minidump");
                Assert.True(File.Exists(dump), $"missing minidump: {dump}");
                text = Path.ChangeExtension(dump, ".txt");
                Assert.True(File.Exists(text), $"missing report: {text}");
                var report = File.ReadAllText(text);
                Assert.Contains("code=0xC0000005", report, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("plugin=passthrough32.dll", report, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("dump=", report, StringComparison.OrdinalIgnoreCase);
                // The engine-side objects are intact: the broken plug-in is bypassed, never thrown into the caller.
                var input = Enumerable.Repeat(0.3f, Frames).ToArray();
                var output = new[] { new float[Frames], new float[Frames] };
                plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));
                Assert.Equal(input, output[0]);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", localBefore);
            TryDelete(dump);
            TryDelete(text);
            try { Directory.Delete(localRoot, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            DeleteFolder(path);
        }
    }

    private static void AssertSurvivesOrIsReported(RemotePlugin plugin, EngineThreads threads, int pid, ManualResetEventSlim crashSignal, ref int crashes, string name)
    {
        if (Alive(pid))
        {
            Assert.False(crashSignal.IsSet, $"{name}: a live helper must not report a crash");
            Assert.Equal(LoadedPluginInfo.Vst2, plugin.Describe().Format);
            return;
        }
        Assert.True(PumpUntil(threads, () => crashSignal.IsSet, 5000), $"{name}: a dead helper must raise Crashed");
        Assert.Equal(1, crashes);
        Assert.True(plugin.LinkFailed, $"{name}: the link must be down");
        Assert.True(Vst32TestAssets.WaitsForExit(pid, 5000), $"{name}: the helper must be gone");
        var input = Enumerable.Repeat(0.2f, Frames).ToArray();
        var output = new[] { new float[Frames], new float[Frames] };
        plugin.Process(new[] { input, input }, output, Frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(0, Frames));
        if (plugin.IsInstrument) Assert.All(output[0], sample => Assert.Equal(0f, sample));
        else Assert.Equal(input, output[0]);
    }

    private static bool Alive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static bool PumpUntil(EngineThreads threads, Func<bool> condition, int timeoutMs) =>
        PumpUntil(() => { threads.RunPending(); return condition(); }, timeoutMs);

    private static bool PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(15);
        }
        return condition();
    }

    // A Win32 popup menu is a top-level "#32768" window owned by the helper: the test reads its class only and never
    // sends input to anything outside the process it started.
    private static bool MenuExists(int pid)
    {
        var found = false;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != pid || !IsWindowVisible(window)) return true;
            var cls = new StringBuilder(32);
            GetClassNameW(window, cls, cls.Capacity);
            if (cls.ToString() != "#32768") return true;
            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static string CopyTestPlugin(params string[] markers)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-vst32-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var dll = Path.Combine(folder, "passthrough32.dll");
        File.Copy(Vst32TestAssets.TestPluginPath!, dll);
        foreach (var marker in markers) File.WriteAllText(Path.Combine(folder, marker), "1");
        return dll;
    }

    private static void DeleteFolder(string dll)
    {
        try { Directory.Delete(Path.GetDirectoryName(dll)!, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDelete(string? file)
    {
        if (string.IsNullOrEmpty(file)) return;
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private const int WmApp = 0x8000;
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string className, string? title);

    private sealed class TestOwner : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private uint _threadId;
        private IntPtr _handle;
        public TestOwner()
        {
            _thread = new Thread(() =>
            {
                _threadId = GetCurrentThreadId();
                _handle = CreateWindowExW(0, "STATIC", "TabForge Vst32 preset test owner", 0x00CF0000, 0, 0, 1200, 900,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                _ready.Set();
                while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessageW(ref message); }
            }) { IsBackground = true, Name = "TabForge Vst32 preset test owner" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait(5000);
        }
        public IntPtr Handle => _handle;
        public void Dispose()
        {
            if (_handle != IntPtr.Zero) PostMessageW(_handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
            if (_threadId != 0) PostThreadMessageW(_threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(3000);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(int ex, string cls, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern int GetMessageW(out Msg message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Msg message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint id, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
