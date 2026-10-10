using System.Diagnostics;

namespace TabForge;

/// <summary>
/// Bounded wait for a self-test condition. Owns: the poll loop. Does not own: assertions (the caller's Check reports a timeout).
/// Used instead of a fixed sleep when the test means "wait until X happens".
/// </summary>
internal static class Poll
{
    /// <summary>Returns true as soon as <paramref name="condition"/> holds, false after <paramref name="timeoutMs"/> (one last check at the end).</summary>
    public static bool Until(Func<bool> condition, int timeoutMs, int stepMs = 5)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds >= timeoutMs) return condition();
            Thread.Sleep(stepMs);
        }
        return true;
    }
}
