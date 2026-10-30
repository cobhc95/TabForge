namespace TabForge.Avalonia.Views;

// Owns: the defensive repaint half of the MIDI processing window: one full native repaint after it is shown, activated, moved or resized.
// A docked plug-in editor is another process's top-level window and can leave a stale image over this window (the owner saw it until moving it).
// Does not own: the repaint itself (MidiWindowRepaint, INativeWindowHandles) or the rest of the window (MidiProcessingWindow).
// Tests: TabForge.Avalonia.Tests.MidiWindowRepaintTests, MidiProcessingWindowTests.
public sealed partial class MidiProcessingWindow
{
    private void WireRepaint()
    {
        Opened += (_, _) => _repaint.Request();
        Activated += (_, _) => _repaint.Request();
        PositionChanged += (_, _) => _repaint.Request();
        SizeChanged += (_, _) => _repaint.Request();
        Closed += (_, _) => _repaint.Cancel();   // a queued paint for a closed window is dropped
    }
}
