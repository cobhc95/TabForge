using System;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Midi;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the defensive repaint of the MIDI processing window: one full native redraw per UI pass, posted (never done inline),
/// nothing for a zero handle, and no redraw after a cancel.
/// Does not own: the platform redraw itself (PluginEditorWin32) or the window events (MidiProcessingWindowTests).
/// Tests: this class.
/// </summary>
public class MidiWindowRepaintTests
{
    private static (MidiWindowRepaint Repaint, FakeNativeWindowHandles Native) Make(IntPtr handle)
    {
        var native = new FakeNativeWindowHandles { Handle = handle };
        return (new MidiWindowRepaint(native, AvaloniaUiThread.ForApplication(), () => handle), native);
    }

    [AvaloniaFact]
    public void RequestsCoalesceIntoOnePostedFullRepaint()
    {
        var (repaint, native) = Make((IntPtr)42);
        repaint.Request();
        repaint.Request();
        Assert.Empty(native.Redraws);   // posted, not painted during the show/move handler
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(new[] { (IntPtr)42 }, native.Redraws);
    }

    [AvaloniaFact]
    public void AZeroHandleRequestsNothing()
    {
        var (repaint, native) = Make(IntPtr.Zero);
        repaint.Request();
        DialogTestKit.RunQueuedUiWork();
        Assert.Empty(native.Redraws);
    }

    [AvaloniaFact]
    public void CancelDropsTheQueuedRepaint()
    {
        var (repaint, native) = Make((IntPtr)7);
        repaint.Request();
        repaint.Cancel();
        DialogTestKit.RunQueuedUiWork();
        Assert.Empty(native.Redraws);
        repaint.Request();   // a new request after a cancel still runs
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(new[] { (IntPtr)7 }, native.Redraws);
    }
}
