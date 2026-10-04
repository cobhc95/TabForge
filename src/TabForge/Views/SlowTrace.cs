using System.Diagnostics;

using Trace = TabForge.Services.Trace;

namespace TabForge.Views;

// Owns: timing of the expensive UI-thread funnels and input latency for the "ui" trace.
// Does not own: any behaviour (no-op when the trace is off).
// Tests: TestTraceSwitchAreas.

/// <summary>
/// Owns: timing of a few expensive UI-thread funnels (full renders, row rebuilds, engine sync, score relayout) and the
/// input-to-idle latency, written to the "ui" trace when <c>TABFORGE_TRACE=ui</c> is set. Does not own: any behaviour (no-op when off).
/// Tests: TestTraceSwitchAreas.
/// </summary>
internal static class SlowTrace
{
    private const string Area = "ui";
    private static long _lastZoom;

    /// <summary>A timeline zoom step happened now: frame gaps are watched for the next second.</summary>
    public static void ZoomStep() { if (Trace.IsOn(Area)) Volatile.Write(ref _lastZoom, Stopwatch.GetTimestamp()); }

    /// <summary>Times the work until disposal; logs it with a short caller chain when it took at least <paramref name="thresholdMs"/>.</summary>
    public static Scope Measure(string what, double thresholdMs = 4) => Trace.IsOn(Area) ? new Scope(what, thresholdMs, Stopwatch.GetTimestamp()) : default;

    /// <summary>Logs a call with its caller chain (for "who asked for this" questions).</summary>
    public static void Mark(string what)
    {
        if (Trace.IsOn(Area)) Trace.Write(Area, $"{what} <- {Callers()}");
    }

    public readonly struct Scope : IDisposable
    {
        private readonly string? _what; private readonly double _threshold; private readonly long _start;
        public Scope(string what, double threshold, long start) { _what = what; _threshold = threshold; _start = start; }
        public void Dispose()
        {
            if (_what is null) return;
            var ms = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            if (ms >= _threshold) Trace.Write(Area, $"{_what} {ms:0.0} ms <- {Callers()}");
        }
    }

    private static string Callers()
    {
        var frames = new StackTrace(2, false).GetFrames();
        return string.Join(" < ", frames.Select(f => f.GetMethod()).Where(m => m?.DeclaringType?.Namespace?.StartsWith("TabForge") == true && m.DeclaringType != typeof(SlowTrace))
            .Take(5).Select(m => $"{m!.DeclaringType!.Name}.{m.Name}"));
    }

    /// <summary>Input latency: from a mouse/key press to the dispatcher going idle again, logged when it is 50 ms or more.</summary>
    public static void HookInput()
    {
        if (!Trace.IsOn(Area)) return;
        void Start(object sender, System.Windows.RoutedEventArgs e)
        {
            var t = Stopwatch.GetTimestamp(); var src = e.OriginalSource?.GetType().Name ?? "?";
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                var ms = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                if (ms >= 50) Trace.Write(Area, $"INPUT {e.RoutedEvent.Name} on {src}: {ms:0} ms to idle");
            });
        }
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window), System.Windows.UIElement.PreviewMouseDownEvent, new System.Windows.Input.MouseButtonEventHandler((s, e) => Start(s, e)), true);
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window), System.Windows.UIElement.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler((s, e) => Start(s, e)), true);
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window), System.Windows.UIElement.PreviewMouseUpEvent, new System.Windows.Input.MouseButtonEventHandler((s, e) => Start(s, e)), true);
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ContextMenu), System.Windows.Controls.ContextMenu.OpenedEvent, new System.Windows.RoutedEventHandler((s, e) =>
            Trace.Write(Area, $"MENU opened {(s as System.Windows.Controls.ContextMenu)?.Items.Count} items")), true);
        // Frame pacing while a mouse button is held (drags, splitter resizes): every gap of 12 ms or more between rendered frames.
        long last = 0;
        System.Windows.Media.CompositionTarget.Rendering += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            var held = System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed;
            var zooming = Stopwatch.GetElapsedTime(Volatile.Read(ref _lastZoom), now).TotalMilliseconds < 1000;
            var watch = held || zooming;
            if (watch && last != 0) { var gap = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds; if (gap >= 12) Trace.Write(Area, $"FRAME gap {gap:0} ms ({(zooming ? "zooming" : "button held")})"); }
            last = watch ? now : 0;
        };
    }
}
