using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: that a plug-in's resize request reaches the editor-size callback (the EditorSized event of the UI) for every format, the size handshake of the
/// docked placement, and the open-editor command of an isolated plug-in carrying its docked flag and answering the real editor size.
/// Does not own: the native VST3 view (needs tfvst3.dll and a real plug-in).
/// Tests: this class.
/// </summary>
public class EditorWindowsResizeTests
{
    private sealed class ResizablePlugin : IPluginInstance, IEditorResizeSource, IEditorSizeSink
    {
        public string Path => "resizable.dll";
        public bool IsInstrument => false;
        public bool HasEditor => true;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport) { }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => (300, 200);
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
        public event Action<int, int>? ResizeRequested;
        public void SetEditorSize(int width, int height) { }
        public void Request(int width, int height) => ResizeRequested?.Invoke(width, height);
    }

    [AvaloniaFact]
    public void AResizeRequestFromThePluginReachesTheSizedCallbackUntilTheEditorCloses()
    {
        var editors = new EditorWindows(new EngineLogWriter());
        var plugin = new ResizablePlugin();
        var sizes = new List<(int, int)>();
        Assert.True(editors.Show(plugin, "Resizable", IntPtr.Zero, dark: true, sized: (w, h) => sizes.Add((w, h))));
        plugin.Request(500, 400);
        Assert.Equal(new[] { (300, 200), (500, 400) }, sizes);
        editors.CloseFor(plugin);
        plugin.Request(600, 500);
        Assert.Equal(2, sizes.Count);
    }

    [AvaloniaFact]
    public void ARepeatedResizeRequestWithinOnePixelIsIgnored()
    {
        var editors = new EditorWindows(new EngineLogWriter());
        var plugin = new ResizablePlugin();
        var sizes = new List<(int, int)>();
        Assert.True(editors.Show(plugin, "Resizable", IntPtr.Zero, dark: true, sized: (w, h) => sizes.Add((w, h))));
        plugin.Request(300, 200);
        plugin.Request(301, 199);
        plugin.Request(500, 400);
        plugin.Request(500, 400);
        Assert.Equal(new[] { (300, 200), (500, 400) }, sizes);
        editors.CloseFor(plugin);
    }

    [Fact]
    public void TheOpenEditorCommandCarriesTheDockedFlagAndThePlace()
    {
        foreach (var (docked, place) in new (bool, (int X, int Y)?)[] { (true, null), (false, null), (false, (-1200, 40)) })
        {
            using var stream = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) RemotePlugin.WriteOpenEditor(writer, (IntPtr)0x1234, docked, place);
            stream.Position = 0;
            using var reader = new System.IO.BinaryReader(stream);
            Assert.Equal(((IntPtr)0x1234, docked, place), RemotePlugin.ReadOpenEditor(reader));
        }
    }

    [Fact]
    public void TheEngineOpenEditorMessageCarriesThePlace()
    {
        foreach (var place in new (int X, int Y)?[] { null, (-1200, 40) })
        {
            var message = new TabForge.Audio.Contracts.OpenEditorMessage(1, 2, 0x1234, true, false, false, place);
            using var stream = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) message.Write(writer);
            stream.Position = 0;
            using var reader = new System.IO.BinaryReader(stream);
            Assert.Equal(message, TabForge.Audio.Contracts.OpenEditorMessage.Read(reader));
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool GetWindowRect(IntPtr h, out Rect r);
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);

    // A real Win32 window: what a tab switch does to a floating editor (closed, then opened again where the user left it).
    [AvaloniaFact]
    public void AFloatingWindowReportsWhereItIsMovedAndOpensThereAgain()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string title = "TabForge place test";
        var editors = new EditorWindows(new EngineLogWriter());
        var plugin = new ResizablePlugin();
        var moves = new List<(int, int)>();
        Assert.True(editors.Show(plugin, title, IntPtr.Zero, dark: true, moved: (x, y) => moves.Add((x, y))));
        Assert.Empty(moves);   // opening is not the user moving it
        var window = FindWindow(TabForge.Avalonia.Shell.Win32.PluginEditorWin32.EditorPopupClass, title);
        Assert.NotEqual(IntPtr.Zero, window);
        const uint noSize = 1, noZOrder = 4, noActivate = 0x10;
        Assert.True(SetWindowPos(window, IntPtr.Zero, 137, 151, 0, 0, noSize | noZOrder | noActivate));
        Assert.Equal(new[] { (137, 151) }, moves);
        editors.CloseFor(plugin);

        Assert.True(editors.Show(plugin, title, IntPtr.Zero, dark: true, place: (137, 151), moved: (x, y) => moves.Add((x, y))));
        window = FindWindow(TabForge.Avalonia.Shell.Win32.PluginEditorWin32.EditorPopupClass, title);
        Assert.True(GetWindowRect(window, out var again));
        Assert.Equal((137, 151), (again.Left, again.Top));
        Assert.Single(moves);   // opening at the saved place reports nothing
        editors.CloseFor(plugin);
    }

    [AvaloniaFact]
    public void AFloatingWindowWhoseMonitorHasGoneOpensWhereWindowsPutsIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string title = "TabForge gone monitor test";
        const int nowhere = 100000;
        var editors = new EditorWindows(new EngineLogWriter());
        var plugin = new ResizablePlugin();
        Assert.True(editors.Show(plugin, title, IntPtr.Zero, dark: true, place: (nowhere, nowhere)));
        Assert.True(GetWindowRect(FindWindow(TabForge.Avalonia.Shell.Win32.PluginEditorWin32.EditorPopupClass, title), out var opened));
        Assert.True(opened.Left < nowhere && opened.Top < nowhere);
        editors.CloseFor(plugin);
    }

    [Fact]
    public void TheOpenEditorReplyCarriesTheRealSizeAndNothingWhenItDidNotOpen()
    {
        byte[] Reply((int Width, int Height)? size)
        {
            using var stream = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) RemotePlugin.WriteEditorSize(writer, size);
            return stream.ToArray();
        }
        Assert.Equal((640, 480), RemotePlugin.ReadEditorSize(Reply((640, 480))));
        Assert.Null(RemotePlugin.ReadEditorSize(Reply(null)));
    }
}
