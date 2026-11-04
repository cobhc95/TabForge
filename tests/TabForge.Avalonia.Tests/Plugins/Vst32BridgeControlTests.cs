using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Shell.Win32;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: stage 5 through the real helper - Describe, parameters, chunk/parameter-list state, the 16 MiB limit,
//        editor open/size/close and user-close, and the same checks for every real fixture.
// Does not own: audio/MIDI (Vst32BridgeAudioTests), the shared layout (Vst32BridgeBlockTests), catalog/routing.
[Collection("Vst32Native")]
public sealed class Vst32BridgeControlTests
{
    private const int Frames = 1024;
    private readonly ITestOutputHelper _output;

    public Vst32BridgeControlTests(ITestOutputHelper output) => _output = output;

    [Vst32RealTheory]
    [InlineData("Acceleration32", false)]
    [InlineData("ADClip932", false)]
    [InlineData("Console4Channel32", false)]
    [InlineData("Density32", false)]
    [InlineData("PurestGain32", false)]
    [InlineData("TAL-NoiseMaker32", true)]
    [InlineData("TAL-BassLine-101-32", true)]
    [InlineData("Dexed32", true)]
    public void EveryRealFixtureDescribesParametersStateAndEditor(string name, bool instrument)
    {
        var path = Vst32TestAssets.FixturePath(name);
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        var (plugin, threads) = Vst32TestAssets.StartWithThreads(path, instrument, Frames);
        var pid = plugin.HostProcessId;
        using (plugin)
        {
            var info = plugin.Describe();
            Assert.Equal(LoadedPluginInfo.Vst2, info.Format);
            Assert.Equal(instrument, plugin.IsInstrument);
            Assert.False(string.IsNullOrWhiteSpace(info.Name), $"{name}: Describe name");
            Assert.False(string.IsNullOrWhiteSpace(info.Vendor), $"{name}: Describe vendor");
            Assert.True(info.Inputs >= 0 && info.Outputs >= 0, $"{name}: declared pins");
            var parameters = plugin.GetParameters();
            _output.WriteLine($"{name}: kind={(instrument ? "Instrument" : "Effect")} editor={plugin.HasEditor} vendor='{info.Vendor}' name='{info.Name}' pins={info.Inputs}/{info.Outputs} chunks={info.UsesChunks} params={parameters.Count}");
            _output.WriteLine($"{name}: first params: {string.Join(", ", parameters.Take(10).Select(p => $"{p.Id}:{p.Name}={p.Value:0.00}"))}");

            if (parameters.Count > 0)
            {
                var id = parameters[0].Id;
                var original = parameters[0].Value;
                var target = Math.Abs(original - 0.75) < 0.1 ? 0.25 : 0.75;
                Assert.True(plugin.SetParameter(id, target), $"{name}: SetParameter {id}");
                var changed = plugin.GetParameters().First(p => p.Id == id).Value;
                Assert.InRange(changed, target - 0.05, target + 0.05);
                Assert.True(plugin.SetParameter(id, original), $"{name}: restore parameter {id}");
                _output.WriteLine($"{name}: parameter {id} round-trip {original:0.000} -> {target:0.000} -> {changed:0.000}");
            }

            var state = plugin.GetState();
            Assert.NotNull(state);
            Assert.NotEmpty(state);
            var values = plugin.GetParameters().Select(p => p.Value).ToArray();
            if (values.Length > 0 && !info.UsesChunks)
            {
                var id = plugin.GetParameters()[0].Id;
                var original = values[0];
                var changed = Math.Abs(original - 0.75) < 0.1 ? 0.25 : 0.75;
                Assert.True(plugin.SetParameter(id, changed));
                plugin.SetState(state);
                var restored = plugin.GetParameters().First(p => p.Id == id).Value;
                Assert.InRange(restored, original - 0.02, original + 0.02);
            }
            else
            {
                plugin.SetState(state);
                ProcessSilence(plugin, Frames, 1);   // some plug-ins apply a chunk on the next process call
            }
            var again = plugin.GetState();
            Assert.NotNull(again);
            Assert.Equal(state.Length, again!.Length);
            if (info.UsesChunks) Assert.Equal(state, again);
            _output.WriteLine($"{name}: state {state.Length} bytes, chunks={info.UsesChunks}, restored={(info.UsesChunks ? state.SequenceEqual(again!) : true)}");

            using var owner = new TestOwner();
            if (plugin.HasEditor)
            {
                var sized = new ManualResetEventSlim();
                (int Width, int Height) last = (0, 0);
                plugin.EditorSized = (width, height) => { last = (width, height); sized.Set(); };
                var opened = plugin.OpenEditor(owner.Handle, docked: true);
                Assert.NotNull(opened);
                Assert.True(opened!.Value.Width > 0 && opened.Value.Height > 0, $"{name}: editor rect");
                Assert.True(PumpWait(threads, () => sized.IsSet, 4000), $"{name}: MsgEditorSized");
                var popup = PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero);
                Assert.NotEqual(IntPtr.Zero, popup);
                Assert.Equal(owner.Handle, GetWindow(popup, GwOwner));
                _output.WriteLine($"{name}: editor {opened.Value.Width}x{opened.Value.Height}, sized {last.Width}x{last.Height}");
                plugin.CloseEditor();
                Assert.True(PumpWait(threads, () => PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero) == IntPtr.Zero, 3000), $"{name}: editor popup gone");
            }
            else
            {
                Assert.Null(plugin.OpenEditor(owner.Handle, docked: true));
                Assert.Equal(IntPtr.Zero, PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero));
                _output.WriteLine($"{name}: no editor (clean null)");
            }

            var level = instrument ? Rms(RenderNote(plugin, Frames, 8)) : RenderEffect(plugin, Frames, 6, 2);
            Assert.True(level > 1e-5f, $"{name}: helper must still process audio after the editor");
        }
        Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), $"{name}: the helper must exit after the editor test");
    }

    [Vst32RealFact]
    public void PurestGainStateRestoresTheOutputLevel()
    {
        using var plugin = Vst32TestAssets.Start(Vst32TestAssets.FixturePath("PurestGain32"), instrument: false, Frames);
        var id = plugin.GetParameters()[0].Id;
        Assert.True(plugin.SetParameter(id, 0.25));
        var first = RenderEffect(plugin, Frames, 24, 12);
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.True(plugin.SetParameter(id, 0.75));
        var changed = RenderEffect(plugin, Frames, 24, 12);
        Assert.True(changed > first * 1.5f, $"changing the gain must change the level (first {first:0.0000}, changed {changed:0.0000})");
        plugin.SetState(state!);
        var restored = RenderEffect(plugin, Frames, 24, 12);
        var restoredParameter = plugin.GetParameters().First(p => p.Id == id).Value;
        _output.WriteLine($"PurestGain32: first {Vst32TestAssets.Db(first):0.0} dBFS, changed {Vst32TestAssets.Db(changed):0.0} dBFS, restored {Vst32TestAssets.Db(restored):0.0} dBFS (parameter {restoredParameter:0.000})");
        Assert.InRange(Vst32TestAssets.Db(restored), Vst32TestAssets.Db(first) - 0.5, Vst32TestAssets.Db(first) + 0.5);
    }

    [Vst32RealTheory]
    [InlineData("TAL-NoiseMaker32")]
    [InlineData("TAL-BassLine-101-32")]
    [InlineData("Dexed32")]
    public void EveryInstrumentRestoresTheRenderedNote(string name)
    {
        var path = Vst32TestAssets.FixturePath(name);
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        using var plugin = Vst32TestAssets.Start(path, instrument: true, Frames);
        var parameters = plugin.GetParameters();
        Assert.NotEmpty(parameters);
        _output.WriteLine($"{name}: params: {string.Join(", ", parameters.Take(16).Select(p => $"{p.Id}:{p.Name}={p.Value:0.00}"))}");
        var state = plugin.GetState();
        Assert.NotNull(state);
        _output.WriteLine($"{name}: state {state!.Length} bytes, chunk index {state[4]}");
        var first = RenderNote(plugin, Frames, 12);
        // TAL calls its master level "volume"; Dexed calls it "Output". Either way the change must move the rendered note.
        var volume = parameters.FirstOrDefault(p => p.Name.Equals("volume", StringComparison.OrdinalIgnoreCase))
                     ?? parameters.FirstOrDefault(p => p.Name.Equals("output", StringComparison.OrdinalIgnoreCase))
                     ?? parameters[0];
        var changed = volume.Value > 0.5 ? 0.0 : 1.0;
        Assert.True(plugin.SetParameter(volume.Id, changed));
        ProcessSilence(plugin, Frames, 24);   // the volume ramp and the first note's tail settle before the next render
        var second = RenderNote(plugin, Frames, 12);
        plugin.SetState(state!);
        ProcessSilence(plugin, Frames, 96);   // the restored volume must settle too, or a long ramp colours the note
        var restored = RenderNote(plugin, Frames, 12);
        var firstRms = Rms(first); var secondRms = Rms(second); var restoredRms = Rms(restored);
        var correlation = Correlation(first, restored);
        _output.WriteLine($"{name}: parameter {volume.Id}:{volume.Name} {volume.Value:0.00} -> {changed:0.00}; note RMS first {Vst32TestAssets.Db(firstRms):0.0} dB, changed {Vst32TestAssets.Db(secondRms):0.0} dB, restored {Vst32TestAssets.Db(restoredRms):0.0} dB; correlation {correlation:0.000}");
        Assert.True(firstRms > 1e-5f, $"{name}: the note must sound");
        Assert.True(Math.Abs(Vst32TestAssets.Db(secondRms) - Vst32TestAssets.Db(firstRms)) > 1.0, $"{name}: the parameter change must change the note");
        Assert.InRange(Vst32TestAssets.Db(restoredRms), Vst32TestAssets.Db(firstRms) - 1.0, Vst32TestAssets.Db(firstRms) + 1.0);
        // Dexed's state is deterministic: the restored note keeps its envelope, not just its level.
        if (name == "Dexed32") Assert.True(correlation > 0.9, $"{name}: the restored note envelope must match (correlation {correlation:0.000})");
    }

    [Fact]
    public void ABlockedControlWriteTimesOutAndStopsFurtherWrites()
    {
        using var stream = new BlockingStream();
        var channel = new PluginControlChannel(stream, new EngineLogWriter());
        var watch = Stopwatch.StartNew();
        Assert.Null(channel.Ask(RemotePlugin.CmdOpenEditor, null, RemotePlugin.MsgEditor, 100, out var timedOut));
        Assert.True(timedOut);
        Assert.True(channel.Faulted);
        Assert.True(watch.ElapsedMilliseconds < 3000, $"a blocked write must not bypass the timeout ({watch.ElapsedMilliseconds} ms)");
        var writes = stream.WriteCalls;
        Assert.Null(channel.Ask(RemotePlugin.CmdCloseEditor, null, RemotePlugin.MsgEditor, 100, out _));
        Assert.Equal(writes, stream.WriteCalls);   // a faulted channel is never written to again
    }

    [Vst32Fact]
    public void ChunkStateRoundTripsThroughTheHelper()
    {
        var path = CopyTestPlugin("passthrough32.chunk");
        try
        {
            using var plugin = Vst32TestAssets.Start(path, instrument: false, Frames);
            Assert.True(plugin.Describe().UsesChunks, "the chunk marker must make the test plug-in report chunks");
            var initial = plugin.GetState();
            Assert.NotNull(initial);
            Assert.Equal(5 + 64, initial!.Length);   // "TF32" + chunk index + the plug-in's 64 bytes
            Assert.True(plugin.SetParameter(0, 0.8));
            var changed = plugin.GetState();
            Assert.NotNull(changed);
            Assert.NotEqual(initial[5], changed![5]);
            plugin.SetState(changed);
            Assert.Equal(changed, plugin.GetState());
            plugin.SetState(new byte[] { 1, 2, 3 });   // a short chunk is applied, never fatal
            Assert.NotNull(plugin.GetState());
        }
        finally { DeleteFolder(path); }
    }

    [Vst32Fact]
    public void AStateOverTheLimitIsReportedAsTooLarge()
    {
        var path = CopyTestPlugin("passthrough32.huge");
        try
        {
            using var plugin = Vst32TestAssets.Start(path, instrument: false, Frames);
            var error = Assert.Throws<InvalidDataException>(() => plugin.GetState());
            Assert.Contains("16 MiB", error.Message);
            Assert.Equal(LoadedPluginInfo.Vst2, plugin.Describe().Format);   // the helper is alive and answering
            Assert.True(RenderEffect(plugin, Frames, 4, 2) > 1e-5f);
        }
        finally { DeleteFolder(path); }
    }

    [Vst32Fact]
    public void GarbageAndOversizedSetStateDoNotKillTheHelper()
    {
        using var plugin = Vst32TestAssets.Start(Vst32TestAssets.TestPluginPath!, instrument: false, Frames);
        var before = plugin.Describe();
        plugin.SetState(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03 });
        plugin.SetState(new byte[17 * 1024 * 1024]);   // over the 16 MiB contract: the helper must reject it
        var after = plugin.Describe();
        Assert.Equal(before.UniqueId, after.UniqueId);
        Assert.True(plugin.SetParameter(0, 0.6));
        Assert.True(RenderEffect(plugin, Frames, 4, 2) > 1e-5f);
    }

    [Vst32Fact]
    public void TheDeterministicEditorDocksReportsSizeAndCloses()
    {
        var path = CopyTestPlugin("passthrough32.editor");
        try
        {
            var (plugin, threads) = Vst32TestAssets.StartWithThreads(path, instrument: false, Frames);
            var pid = plugin.HostProcessId;
            using var owner = new TestOwner();
            using (plugin)
            {
                Assert.True(plugin.HasEditor);
                var sized = new ManualResetEventSlim();
                (int Width, int Height) last = (0, 0);
                plugin.EditorSized = (width, height) => { last = (width, height); if (width == 320 && height == 240) sized.Set(); };
                var opened = plugin.OpenEditor(owner.Handle, docked: true);
                Assert.NotNull(opened);
                Assert.True(opened!.Value.Width > 0 && opened.Value.Height > 0);
                Assert.True(PumpWait(threads, () => sized.IsSet, 4000), "the plug-in's sizeWindow request must arrive as MsgEditorSized");
                var popup = PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero);
                Assert.NotEqual(IntPtr.Zero, popup);
                Assert.Equal(owner.Handle, GetWindow(popup, GwOwner));
                _output.WriteLine($"deterministic editor: opened {opened.Value.Width}x{opened.Value.Height}, resized {last.Width}x{last.Height}");

                var closed = new ManualResetEventSlim();
                plugin.EditorClosed += () => closed.Set();
                Assert.True(PostMessageW(popup, WmClose, IntPtr.Zero, IntPtr.Zero));
                Assert.True(PumpWait(threads, () => closed.IsSet, 4000), "a user WM_CLOSE must arrive as MsgEditorClosed");
                Assert.True(PumpWait(threads, () => PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero) == IntPtr.Zero, 3000), "the closed popup must be gone");

                var floating = plugin.OpenEditor(owner.Handle, docked: false);
                Assert.NotNull(floating);
                var floatingPopup = PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero);
                Assert.NotEqual(IntPtr.Zero, floatingPopup);
                plugin.CloseEditor();
                Assert.True(PumpWait(threads, () => PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero) == IntPtr.Zero, 3000));
                Assert.True(RenderEffect(plugin, Frames, 4, 2) > 1e-5f, "the helper must still process audio after the editor");
            }
            Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), "the helper must exit after the editor test");
        }
        finally { DeleteFolder(path); }
    }

    private static float RenderEffect(RemotePlugin plugin, int frames, int blocks, int skip)
    {
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        var phase = 0.0;
        double sum = 0;
        var count = 0;
        for (var block = 0; block < blocks; block++)
        {
            for (var i = 0; i < frames; i++)
            {
                var sample = (float)(0.25 * Math.Sin(phase));
                phase += 2 * Math.PI * 1000 / 48000;
                input[0][i] = sample;
                input[1][i] = sample;
            }
            plugin.Process(input, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, frames));
            if (block < skip) continue;
            foreach (var sample in output[0]) { sum += sample * (double)sample; count++; }
        }
        return count == 0 ? 0 : (float)Math.Sqrt(sum / count);
    }

    private static float[] RenderNote(RemotePlugin plugin, int frames, int blocks)
    {
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        var all = new float[frames * blocks];
        for (var block = 0; block < blocks; block++)
        {
            Array.Clear(input[0]); Array.Clear(input[1]);
            ReadOnlySpan<BlockMidi> midi = block switch
            {
                0 => new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } },
                _ when block == blocks - 2 => new[] { new BlockMidi { Frame = 0, Status = 0x80, Data1 = 60, Data2 = 0 } },
                _ => ReadOnlySpan<BlockMidi>.Empty,
            };
            plugin.Process(input, output, frames, midi, Vst32TestAssets.Transport(block, frames));
            Array.Copy(output[0], 0, all, block * frames, frames);
        }
        return all;
    }

    private static void ProcessSilence(RemotePlugin plugin, int frames, int blocks)
    {
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        for (var block = 0; block < blocks; block++)
            plugin.Process(input, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, frames));
    }

    private static float Rms(float[] samples) => Vst32TestAssets.Rms(samples);

    private static double Correlation(float[] a, float[] b)
    {
        double ab = 0, aa = 0, bb = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) { ab += a[i] * (double)b[i]; aa += a[i] * (double)a[i]; bb += b[i] * (double)b[i]; }
        return aa <= 0 || bb <= 0 ? 0 : ab / Math.Sqrt(aa * bb);
    }

    private static bool PumpWait(EngineThreads threads, Func<bool> condition, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            threads.RunPending();
            if (condition()) return true;
            Thread.Sleep(10);
        }
        threads.RunPending();
        return condition();
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

    /// <summary>A real Win32 owner window on its own pumping thread, like TabForge's UI thread. A hidden owner without a
    /// message pump makes TAL's editors wait forever on a cross-process window message.</summary>
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
                _handle = CreateWindowExW(0, "STATIC", "TabForge Vst32 test owner", 0x00CF0000, 0, 0, 480, 320, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                _ready.Set();
                while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessageW(ref message);
                }
            }) { IsBackground = true, Name = "TabForge Vst32 test owner" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait(5000);
        }

        public IntPtr Handle => _handle;

        public void Dispose()
        {
            if (_handle != IntPtr.Zero) PostMessageW(_handle, WmClose, IntPtr.Zero, IntPtr.Zero);
            if (_threadId != 0) PostThreadMessageW(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(3000);
        }
    }

    /// <summary>A stream whose write never returns: models a helper main thread that stopped reading its pipe.</summary>
    private sealed class BlockingStream : Stream
    {
        private readonly ManualResetEventSlim _gate = new(false);
        private int _writeCalls;
        public int WriteCalls => Volatile.Read(ref _writeCalls);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override int Read(Span<byte> buffer) => 0;
        public override void Write(byte[] buffer, int offset, int count)
        {
            Interlocked.Increment(ref _writeCalls);
            _gate.Wait(5000);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Interlocked.Increment(ref _writeCalls);
            _gate.Wait(5000);
        }
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _gate.Set();
            base.Dispose(disposing);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }
    private const int WmClose = 0x0010, WmQuit = 0x0012;
    private const uint GwOwner = 4;
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(int exStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint threadId, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessageW(out Msg message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Msg message);
}
