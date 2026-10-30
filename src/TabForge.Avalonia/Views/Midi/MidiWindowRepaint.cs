using System;
using TabForge.Avalonia.Platform;
using TabForge.Platform;

namespace TabForge.Avalonia.Views.Midi;

// Owns: the defensive repaint of the MIDI processing window after it is shown, activated, moved or resized. A docked plug-in editor is another
//   process's top-level window and can leave a stale image over a window opened on top of the FX chain window; a full native redraw clears it.
//   Requests coalesce to one background pass, so showing or dragging the window never waits on the plug-in.
// Does not own: the native redraw (INativeWindowHandles / PluginEditorWin32) or the window's events (MidiProcessingWindow).
// Tests: MidiWindowRepaintTests (a fake INativeWindowHandles records the redraw).
// Threads: UI thread only.
internal sealed class MidiWindowRepaint
{
    private readonly INativeWindowHandles _native;
    private readonly Func<IntPtr> _handle;
    private readonly NextFrameCoalescer _queued;

    /// <param name="native">The platform's native window calls (a test passes a fake).</param>
    /// <param name="ui">The UI thread the repaint is posted to.</param>
    /// <param name="handle">The window's native handle; zero skips the redraw.</param>
    public MidiWindowRepaint(INativeWindowHandles native, IUiThread ui, Func<IntPtr> handle)
    {
        _native = native;
        _handle = handle;
        _queued = new NextFrameCoalescer(ui, Repaint, UiPriority.Background);
    }

    /// <summary>Asks for one full repaint on the next background pass; calls within a pass add nothing.</summary>
    public void Request() => _queued.Request();

    /// <summary>Drops a queued repaint (the window closed).</summary>
    public void Cancel() => _queued.Cancel();

    private void Repaint()
    {
        var handle = _handle();
        if (handle != IntPtr.Zero) _native.Redraw(handle);
    }
}
