using System.Diagnostics;

using Trace = TabForge.Services.Trace;

namespace TabForge.Views;

// Owns: timing of the expensive UI-thread funnels for the "ui" trace; input latency is SlowTraceInput's, in the app.
// Does not own: any behaviour (no-op when the trace is off).
// Tests: no dedicated test class.

/// <summary>
/// Owns: timing of a few expensive UI-thread funnels (full renders, row rebuilds, engine sync, score relayout), written to the "ui" trace when <c>TABFORGE_TRACE=ui</c> is set. Does not own: any behaviour (no-op when off).
/// Tests: no dedicated test class.
/// </summary>
internal static class SlowTrace
{
    private const string Area = "ui";
    private static long _lastZoom;

    /// <summary>A timeline zoom step happened now: frame gaps are watched for the next second.</summary>
    /// <summary>The trace area every slow-path line goes to.</summary>
    internal const string TraceArea = Area;

    /// <summary>Milliseconds since the last timeline zoom step at <paramref name="now"/> (a Stopwatch timestamp).</summary>
    internal static double MsSinceZoom(long now) => Stopwatch.GetElapsedTime(Volatile.Read(ref _lastZoom), now).TotalMilliseconds;

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
}
