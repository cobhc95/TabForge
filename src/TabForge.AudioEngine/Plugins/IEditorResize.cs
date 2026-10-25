namespace TabForge.AudioEngine.Plugins;

// Owns: the two optional editor capabilities of a plug-in: asking the host to resize its editor window, and being told the size the window got.
// Does not own: the window (EditorWindows), the plug-in formats (Vst2Plugin, Vst3Plugin).
// Tests: TabForge.Avalonia.Tests.EditorWindowsResizeTests.

/// <summary>A plug-in whose editor asks the host to resize its window. Raised on the main thread (from <see cref="IPluginInstance.EditorIdle"/> or the main loop).</summary>
public interface IEditorResizeSource
{
    event Action<int, int>? ResizeRequested;
}

/// <summary>A plug-in that wants to know the size its editor window has (VST3: the view's checkSizeConstraint and onSize). Main thread.</summary>
public interface IEditorSizeSink
{
    void SetEditorSize(int width, int height);
}
