using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TabForge.Views;

namespace TabForge.Diagnostics;

// Owns: the delete-bars prompt's timing in the speed audit: the real modal show and answer calls, repeated.
// Does not own: the rest of the speed audit (WindowProbes.SpeedAudit) and the prompt itself.
// Tests: listed in docs/DEBUGGING.md (--speed-audit).
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        private ThemedConfirmDialog? _deletePromptInstance;
        private IntPtr _deletePromptHandle;

        // Measures real modal show/answer calls so a prompt-window reuse change cannot pass on constructor-only timings.
        private async Task DeleteBarsPromptAsync()
        {
            var wasActivated = _w.Window.ShowActivated;
            _w.Window.ShowActivated = false;
            _w.Window.Left = CaptureOffscreen;
            _w.Window.Top = CaptureOffscreen;
            try { await TimeAsync("Delete bars prompt", ShowAndCancelDeletePrompt, reps: 5); }
            finally { _w.Window.ShowActivated = wasActivated; }
        }

        private object? ShowAndCancelDeletePrompt()
        {
            SelectBars(5, 6);
            var priorCapture = DialogHost.Capture;
            DialogHost.Capture = null;
            ThemedConfirmDialog? shown = null;
            Exception? failure = null;
            var started = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
            EventHandler tick = (_, _) =>
            {
                if (Application.Current?.Windows.OfType<ThemedConfirmDialog>()
                    .FirstOrDefault(dialog => dialog.IsVisible && ReferenceEquals(dialog.Owner, _w.Window)) is { } dialog)
                {
                    shown = dialog;
                    if (!DialogHost.PressCancel(dialog))
                    {
                        failure = new InvalidOperationException("The visible delete-bars prompt has no cancel action.");
                        dialog.Close();
                    }
                    timer.Stop();
                }
                else if (started.Elapsed > TimeSpan.FromSeconds(5))
                {
                    failure = new TimeoutException("The delete-bars prompt did not become visible within five seconds.");
                    timer.Stop();
                }
            };
            timer.Tick += tick;

            timer.Start();
            try { Hotkey("Range.Delete"); }
            finally
            {
                timer.Stop();
                timer.Tick -= tick;
                DialogHost.Capture = priorCapture;
            }
            if (failure is not null) throw failure;
            if (shown is null) throw new InvalidOperationException("Delete did not show its modal bar-range prompt.");
            var handle = new WindowInteropHelper(shown).Handle;
            if (handle == IntPtr.Zero) throw new InvalidOperationException("The visible delete-bars prompt has no window handle.");
            if (_deletePromptInstance is not null
                && (!ReferenceEquals(shown, _deletePromptInstance) || handle != _deletePromptHandle))
                throw new InvalidOperationException("Repeated delete-bars prompts did not reuse the same window and native handle.");
            _deletePromptInstance = shown;
            _deletePromptHandle = handle;
            return null;
        }
    }
}
