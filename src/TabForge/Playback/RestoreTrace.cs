using System.Linq;

namespace TabForge.Playback;

/// <summary>Owns the text of the playback trace line for restored channel state. Does not own the restore itself. Tests: TestMixPointsSurviveSeek.</summary>
internal static class RestoreTrace
{
    public static string Describe(double timeMs, IEnumerable<ScoreEvent> state) =>
        $"restore@{timeMs:0}ms " + string.Join(" ", state
            .Where(e => (e.Status & 0xF0) == 0xC0 || (e.Status & 0xF0) == 0xB0 && e.Data1 is 7 or 10)
            .OrderBy(e => e.Channel).ThenBy(e => e.Status)
            .Select(e => (e.Status & 0xF0) == 0xC0 ? $"ch{e.Channel}:prog={e.Data1}" : $"ch{e.Channel}:cc{e.Data1}={e.Data2}"));
}
