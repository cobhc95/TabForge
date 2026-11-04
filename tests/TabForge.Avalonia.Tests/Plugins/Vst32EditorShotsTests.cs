using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Avalonia.Shell.Win32;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: the headless editor screenshot for every 32-bit fixture: docked and floating open, a PrintWindow capture
//   (BitBlt fallback), size and non-blank paint checks, PNGs under C:\tfrel-tmp\shots, close/reopen after a host
//   parameter change, and audio that still works after. Fixtures without an editor assert HasEditor false.
// Does not own: parameters/state/audio (Vst32BridgeControlTests, Vst32AudioMatrixTests) or the helper (native\tfvst32).
[Collection("Vst32Native")]
public sealed class Vst32EditorShotsTests
{
    private const int Frames = 1024;
    private const string ShotsFolder = "C:\\tfrel-tmp\\shots";
    private static readonly List<string> Table = new();
    private static readonly object TableLock = new();
    private readonly ITestOutputHelper _output;

    public Vst32EditorShotsTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> AllDlls()
    {
        var data = new TheoryData<string>();
        try
        {
            if (!Directory.Exists(Vst32TestAssets.FixturesFolder)) { data.Add("__no-fixtures__"); return data; }
            foreach (var file in Directory.GetFiles(Vst32TestAssets.FixturesFolder, "*.dll").OrderBy(Path.GetFileName))
                data.Add(Path.GetFileNameWithoutExtension(file)!);
            if (data.Count == 0) data.Add("__no-fixtures__");
        }
        catch (IOException) { data.Add("__no-fixtures__"); }
        return data;
    }

    [Vst32RealTheory]
    [MemberData(nameof(AllDlls))]
    public void EveryFixtureEditorShotMatchesReportedSizeAndPaints(string name)
    {
        if (name == "__no-fixtures__") return;
        var path = Vst32TestAssets.FixturePath(name);
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        Assert.Equal(0x14C, PeMachine(path));
        var instrument = ProbeKind(path, name);
        var (plugin, threads) = Vst32TestAssets.StartWithThreads(path, instrument, Frames);
        var pid = plugin.HostProcessId;
        using var owner = new TestOwner();
        var verdict = "FAIL";
        var detail = "not run";
        try
        {
            using (plugin)
            {
                var parameters = plugin.GetParameters();
                _output.WriteLine($"{name}: kind={(instrument ? "Instrument" : "Effect")} editor={plugin.HasEditor} params={parameters.Count}");
                if (!plugin.HasEditor)
                {
                    Assert.Null(plugin.OpenEditor(owner.Handle, docked: true));
                    Assert.Equal(IntPtr.Zero, PluginEditorWin32.FindEditorPopup(owner.Handle, IntPtr.Zero));
                    verdict = "no editor";
                    detail = "HasEditor false, OpenEditor null (generic UI is the host's job)";
                }
                else
                {
                    var docked = ShotOne(plugin, threads, owner.Handle, name, docked: true);
                    var floating = ShotOne(plugin, threads, owner.Handle, name, docked: false);
                    ReopenAfterParameterChange(plugin, threads, owner.Handle, name);
                    verdict = "PASS";
                    detail = $"editor {docked.Size}/{floating.Size} distinct {docked.Distinct}/{floating.Distinct} {docked.Png} {floating.Png}";
                }
                // One fixture leaves its filter state uninitialised and is silent at startup (see Vst32AudioMatrixTests), so the
                // effect check measures after a settling pre-roll instead of the first blocks.
                var level = instrument ? Rms(RenderNote(plugin, Frames, 8)) : RenderEffect(plugin, Frames, 6 + 262144 / Frames, 2 + 262144 / Frames);
                Assert.True(level > 1e-5f, $"{name}: helper must still process audio after the editor");
                _output.WriteLine($"{name}: post-editor audio {Vst32TestAssets.Db(level):0.0} dBFS");
            }
            Assert.True(Vst32TestAssets.WaitsForExit(pid, 4000), $"{name}: the helper must exit");
        }
        catch (Exception ex)
        {
            Record(name, "FAIL", ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
            throw;
        }
        Record(name, verdict, detail);
    }

    private (string Size, int Distinct, string Png) ShotOne(RemotePlugin plugin, EngineThreads threads, IntPtr owner, string name, bool docked)
    {
        var mode = docked ? "docked" : "floating";
        var sized = new ManualResetEventSlim();
        (int Width, int Height) size = (0, 0);
        plugin.EditorSized = (width, height) => { size = (width, height); sized.Set(); };
        var opened = plugin.OpenEditor(owner, docked: docked);
        Assert.NotNull(opened);
        Assert.True(opened!.Value.Width > 0 && opened.Value.Height > 0, $"{name} {mode}: editor rect");
        Assert.True(PumpWait(threads, () => sized.IsSet, 4000), $"{name} {mode}: MsgEditorSized");
        var popup = PluginEditorWin32.FindEditorPopup(owner, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, popup);
        Assert.Equal(owner, GetWindow(popup, GwOwner));
        var scale = DpiScale(popup);
        PluginEditorWin32.Redraw(popup);
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 500) { threads.RunPending(); Thread.Sleep(10); }
        threads.RunPending();
        var reported = size.Width > 0 && size.Height > 0 ? size : opened.Value;
        Assert.True(GetWindowRect(popup, out var outer), $"{name} {mode}: window rect");
        Assert.True(GetClientRect(popup, out var client), $"{name} {mode}: client rect");
        // The test runner is DPI-unaware, so the rect APIs return logical units: scale back to the
        // physical pixels the per-monitor-aware helper sizes its window in.
        var clientWidth = Scale(client.Right - client.Left, scale);
        var clientHeight = Scale(client.Bottom - client.Top, scale);
        var captureWidth = Scale(outer.Right - outer.Left, scale);
        var captureHeight = Scale(outer.Bottom - outer.Top, scale);
        Assert.InRange(clientWidth, reported.Width - 4, reported.Width + 4);
        Assert.InRange(clientHeight, reported.Height - 4, reported.Height + 4);
        Assert.InRange(captureWidth, reported.Width - 4, reported.Width + 48);
        Assert.InRange(captureHeight, reported.Height - 4, reported.Height + 80);
        var pixels = CaptureWindow(popup, captureWidth, captureHeight, name, mode);
        var origin = new WinPoint();
        Assert.True(ClientToScreen(popup, ref origin), $"{name} {mode}: client origin");
        var stats = Analyze(pixels, captureWidth, Scale(origin.X - outer.Left, scale), Scale(origin.Y - outer.Top, scale), clientWidth, clientHeight);
        var png = Path.Combine(Directory.CreateDirectory(ShotsFolder).FullName, name + "-" + mode + ".png");
        WritePng(png, captureWidth, captureHeight, pixels);
        _output.WriteLine($"{name} {mode}: reported {reported.Width}x{reported.Height} client {clientWidth}x{clientHeight} capture {captureWidth}x{captureHeight} distinct {stats.Distinct} top {stats.Top * 100:0.0}% non-black {stats.NonBlack} -> {png}");
        Assert.True(stats.Distinct >= 8, $"{name} {mode}: painted editor has >= 8 colours (distinct {stats.Distinct})");
        Assert.True(stats.Top < 0.99, $"{name} {mode}: no single colour may cover >= 99% (top {stats.Top * 100:0.00}%)");
        Assert.True(stats.NonBlack > 0, $"{name} {mode}: some pixels must be non-black");
        plugin.CloseEditor();
        Assert.True(PumpWait(threads, () => PluginEditorWin32.FindEditorPopup(owner, IntPtr.Zero) == IntPtr.Zero, 3000), $"{name} {mode}: popup gone");
        return ($"{reported.Width}x{reported.Height}", stats.Distinct, png);
    }

    private void ReopenAfterParameterChange(RemotePlugin plugin, EngineThreads threads, IntPtr owner, string name)
    {
        var sized = new ManualResetEventSlim();
        plugin.EditorSized = (width, height) => sized.Set();
        Assert.NotNull(plugin.OpenEditor(owner, docked: true));
        Assert.True(PumpWait(threads, () => sized.IsSet, 4000), $"{name}: reopen MsgEditorSized");
        if (plugin.GetParameters().FirstOrDefault(p => !p.ReadOnly) is { } moved)
        {
            var target = Math.Abs(moved.Value - 0.75) < 0.1 ? 0.25 : 0.75;
            Assert.True(plugin.SetParameter(moved.Id, target), $"{name}: set parameter {moved.Id} with the editor open");
            Assert.True(plugin.SetParameter(moved.Id, moved.Value), $"{name}: restore parameter {moved.Id}");
        }
        plugin.CloseEditor();
        Assert.True(PumpWait(threads, () => PluginEditorWin32.FindEditorPopup(owner, IntPtr.Zero) == IntPtr.Zero, 3000), $"{name}: editor closed");
        Assert.NotNull(plugin.OpenEditor(owner, docked: true));
        Assert.NotEqual(IntPtr.Zero, PluginEditorWin32.FindEditorPopup(owner, IntPtr.Zero));
        _output.WriteLine($"{name}: editor reopened after a host parameter change (no leaked window)");
        plugin.CloseEditor();
        Assert.True(PumpWait(threads, () => PluginEditorWin32.FindEditorPopup(owner, IntPtr.Zero) == IntPtr.Zero, 3000), $"{name}: reopened popup gone");
    }

    private void Record(string name, string verdict, string detail)
    {
        lock (TableLock)
        {
            Table.Add($"{name} | {detail} | {verdict}");
            _output.WriteLine("editor shots so far:");
            foreach (var row in Table) _output.WriteLine("  " + row);
        }
    }

    private static byte[] CaptureWindow(IntPtr popup, int width, int height, string name, string mode)
    {
        var screen = GetDC(IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, screen);
        try
        {
            var memory = CreateCompatibleDC(screen);
            Assert.NotEqual(IntPtr.Zero, memory);
            try
            {
                var bitmap = CreateCompatibleBitmap(screen, width, height);
                Assert.NotEqual(IntPtr.Zero, bitmap);
                try
                {
                    var previous = SelectObject(memory, bitmap);
                    try
                    {
                        if (!PrintWindow(popup, memory, PwRenderFullContent))
                        {
                            var source = GetWindowDC(popup);
                            Assert.NotEqual(IntPtr.Zero, source);
                            try
                            {
                                Assert.True(BitBlt(memory, 0, 0, width, height, source, 0, 0, Srccopy), $"{name} {mode}: fallback BitBlt");
                            }
                            finally { ReleaseDC(popup, source); }
                        }
                        var pixels = new byte[width * height * 4];
                        var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
                        Assert.NotEqual(0, GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref header, DibRgbColors));
                        return pixels;
                    }
                    finally { SelectObject(memory, previous); }
                }
                finally { DeleteObject(bitmap); }
            }
            finally { DeleteDC(memory); }
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
    }

    private static (int Distinct, double Top, int NonBlack) Analyze(byte[] pixels, int stride, int x0, int y0, int width, int height)
    {
        var counts = new Dictionary<int, int>();
        var nonBlack = 0;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = ((y0 + y) * stride + x0 + x) * 4;
                var key = (pixels[i + 2] << 16) | (pixels[i + 1] << 8) | pixels[i];
                if (key != 0) nonBlack++;
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
        return (counts.Count, counts.Count == 0 ? 1 : (double)counts.Values.Max() / (width * height), nonBlack);
    }

    private static void WritePng(string path, int width, int height, byte[] bgra)
    {
        var raw = new byte[height * (width * 3 + 1)];
        for (var y = 0; y < height; y++)
        {
            raw[y * (width * 3 + 1)] = 0;
            for (var x = 0; x < width; x++)
            {
                var s = (y * width + x) * 4;
                var d = y * (width * 3 + 1) + 1 + x * 3;
                raw[d] = bgra[s + 2];
                raw[d + 1] = bgra[s + 1];
                raw[d + 2] = bgra[s];
            }
        }
        byte[] deflated;
        using (var zipped = new MemoryStream())
        {
            using (var deflate = new DeflateStream(zipped, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(raw, 0, raw.Length);
            deflated = zipped.ToArray();
        }
        var zlib = new byte[deflated.Length + 6];
        zlib[0] = 0x78;
        zlib[1] = 0x01;
        Buffer.BlockCopy(deflated, 0, zlib, 2, deflated.Length);
        var adler = Adler32(raw);
        zlib[zlib.Length - 4] = (byte)(adler >> 24);
        zlib[zlib.Length - 3] = (byte)(adler >> 16);
        zlib[zlib.Length - 2] = (byte)(adler >> 8);
        zlib[zlib.Length - 1] = (byte)adler;
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        var head = new byte[13];
        head[0] = (byte)(width >> 24); head[1] = (byte)(width >> 16); head[2] = (byte)(width >> 8); head[3] = (byte)width;
        head[4] = (byte)(height >> 24); head[5] = (byte)(height >> 16); head[6] = (byte)(height >> 8); head[7] = (byte)height;
        head[8] = 8; head[9] = 2;
        PngChunk(file, "IHDR", head);
        PngChunk(file, "IDAT", zlib);
        PngChunk(file, "IEND", Array.Empty<byte>());
    }

    private static void PngChunk(Stream file, string type, byte[] data)
    {
        var head = new byte[8];
        head[0] = (byte)(data.Length >> 24); head[1] = (byte)(data.Length >> 16); head[2] = (byte)(data.Length >> 8); head[3] = (byte)data.Length;
        head[4] = (byte)type[0]; head[5] = (byte)type[1]; head[6] = (byte)type[2]; head[7] = (byte)type[3];
        file.Write(head, 0, head.Length);
        file.Write(data, 0, data.Length);
        var crc = Crc32(head, 4, 4);
        crc = Crc32(data, 0, data.Length, crc) ^ 0xFFFFFFFFu;
        file.Write(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc }, 0, 4);
    }

    private static uint Crc32(byte[] data, int offset, int count, uint crc = 0xFFFFFFFFu)
    {
        for (var i = 0; i < count; i++)
        {
            crc ^= data[offset + i];
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var v in data) { a = (a + v) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }

    private static double DpiScale(IntPtr window)
    {
        if (GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) != DpiUnaware) return 1;
        var dpi = GetDpiForWindow(window);
        if (dpi < 48 || dpi > 960) dpi = 96;
        return dpi / 96.0;
    }

    private static int Scale(int logical, double scale) => (int)Math.Round(logical * scale);

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

    private static bool ProbeKind(string path, string name)
    {
        var (exit, output, error) = Vst32TestAssets.RunHelperCaptured("--probe", path, Vst32TestAssets.Sha256(path));
        Assert.True(exit == 0, $"{name}: probe exit {exit}: {error}");
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).FirstOrDefault();
        Assert.NotNull(line);
        return line!.Split('|')[0] == "Instrument";
    }

    private static int PeMachine(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Seek(0x3C, SeekOrigin.Begin);
        var pe = reader.ReadInt32();
        stream.Seek(pe + 4, SeekOrigin.Begin);
        return reader.ReadUInt16();
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
            Array.Clear(input[0]);
            Array.Clear(input[1]);
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

    private static float Rms(float[] samples) => Vst32TestAssets.Rms(samples);

    /// <summary>A real Win32 owner window on its own pumping thread, like TabForge's UI. A hidden owner without a
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
            })
            { IsBackground = true, Name = "TabForge Vst32 shots owner" };
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

        [StructLayout(LayoutKind.Sequential)] private struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }
        private const int WmClose = 0x0010, WmQuit = 0x0012;
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(int exStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint threadId, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern int GetMessageW(out Msg message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg message);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Msg message);
    }

    [StructLayout(LayoutKind.Sequential)] private struct WinRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct WinPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size, Width, Height;
        public ushort Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }
    private const uint GwOwner = 4;
    private const int DpiUnaware = 0;
    private const uint PwRenderFullContent = 0x2;
    private const int Srccopy = 0x00CC0020;
    private const uint DibRgbColors = 0;
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WinRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out WinRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref WinPoint point);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int xSrc, int ySrc, int rop);
}
