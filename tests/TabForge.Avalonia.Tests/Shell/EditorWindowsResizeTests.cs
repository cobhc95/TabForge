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
    public void TheOpenEditorCommandCarriesTheDockedFlag()
    {
        foreach (var docked in new[] { true, false })
        {
            using var stream = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) RemotePlugin.WriteOpenEditor(writer, (IntPtr)0x1234, docked);
            stream.Position = 0;
            using var reader = new System.IO.BinaryReader(stream);
            Assert.Equal(((IntPtr)0x1234, docked), RemotePlugin.ReadOpenEditor(reader));
        }
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
