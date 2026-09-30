using System.Diagnostics;
using System.Threading;

namespace TabForge.Diagnostics;

/// <summary>Quiet DEBUG-only latest-frame probes, readable from the debugger without log spam.</summary>
internal static class RenderPerformance
{
#if DEBUG
    private static long _arrangementTicks;
    private static long _arrangementBytes;
    private static long _scoreTicks;
    private static long _scoreBytes;
    private static long _dragTicks;
    private static long _dragBytes;
    private static long _scoreLayoutTicks;
    private static long _scoreLayoutBytes;

    internal static PerformanceSnapshot Snapshot => new(
        Milliseconds(Interlocked.Read(ref _arrangementTicks)), Interlocked.Read(ref _arrangementBytes),
        Milliseconds(Interlocked.Read(ref _scoreTicks)), Interlocked.Read(ref _scoreBytes),
        Milliseconds(Interlocked.Read(ref _dragTicks)), Interlocked.Read(ref _dragBytes),
        Milliseconds(Interlocked.Read(ref _scoreLayoutTicks)), Interlocked.Read(ref _scoreLayoutBytes));

    internal static Scope Measure(PerformanceCategory category)
        => new(category, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    internal readonly record struct PerformanceSnapshot(
        double ArrangementRenderMs, long ArrangementRenderBytes,
        double ScoreRenderMs, long ScoreRenderBytes,
        double DragFrameMs, long DragFrameBytes,
        double ScoreLayoutRebuildMs, long ScoreLayoutRebuildBytes);

    internal enum PerformanceCategory { Arrangement, Score, Drag, ScoreLayout }

    internal readonly struct Scope : IDisposable
    {
        private readonly PerformanceCategory _category;
        private readonly long _started;
        private readonly long _allocated;

        internal Scope(PerformanceCategory category, long started, long allocated)
        {
            _category = category;
            _started = started;
            _allocated = allocated;
        }

        public void Dispose()
        {
            var ticks = Stopwatch.GetTimestamp() - _started;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - _allocated;
            switch (_category)
            {
                case PerformanceCategory.Arrangement:
                    Interlocked.Exchange(ref _arrangementTicks, ticks);
                    Interlocked.Exchange(ref _arrangementBytes, bytes);
                    break;
                case PerformanceCategory.Score:
                    Interlocked.Exchange(ref _scoreTicks, ticks);
                    Interlocked.Exchange(ref _scoreBytes, bytes);
                    break;
                case PerformanceCategory.Drag:
                    Interlocked.Exchange(ref _dragTicks, ticks);
                    Interlocked.Exchange(ref _dragBytes, bytes);
                    break;
                case PerformanceCategory.ScoreLayout:
                    Interlocked.Exchange(ref _scoreLayoutTicks, ticks);
                    Interlocked.Exchange(ref _scoreLayoutBytes, bytes);
                    break;
            }
        }
    }
#else
    internal static object Snapshot => new();
#endif
}
