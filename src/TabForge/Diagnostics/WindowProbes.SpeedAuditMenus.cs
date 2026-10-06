using System.Windows.Controls;
using TabForge.Views;

namespace TabForge.Diagnostics;

// Owns: the speed audit's real clip and track-row popup measurements.
// Does not own: the menu contents or normal menu commands.
// Tests: diagnostics only.
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        private object MeasureTrackRowPopup()
        {
            var menu = _w.Window.TrackRowMenu(0);
            return MeasureRealPopup("track row", menu);
        }

        private object MeasureClipPopup()
        {
            MenuPopupWarmup.Result? result = null;
            var previous = ContextMenuCapture;
            try
            {
                ContextMenuCapture = menu => result = MeasureRealPopup("clip", menu);
                Call(_w.Window, "ShowClipMenu", _audioTrack, _longClip, 20.0);
            }
            finally { ContextMenuCapture = previous; }
            return result ?? throw new InvalidOperationException("The clip action did not create a context menu.");
        }

        private MenuPopupWarmup.Result MeasureRealPopup(string kind, ContextMenu menu)
        {
            var result = MenuPopupWarmup.OpenAndClose(menu);
            Log($"real {kind} popup: HWND 0x{result.Handle.ToInt64():X}, no-activate {result.NoActivate}, focus preserved {result.FocusPreserved}, open {result.OpenMs:0.0} ms; immediate source {result.SourceWasImmediate}, dispatcher wait {result.DispatcherWaitMs:0.0} ms, layout {result.LayoutMs:0.0} ms, close flush {result.CloseFlushMs:0.0} ms");
            if (result.Handle == IntPtr.Zero) throw new InvalidOperationException($"The {kind} menu did not create a popup HWND.");
            return result;
        }
    }
}
