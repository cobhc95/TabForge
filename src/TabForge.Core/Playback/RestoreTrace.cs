using System.Linq;

namespace TabForge.Playback;

/// <summary>Owns the text of the playback trace line for restored channel state. Does not own the restore itself. Tests: TestMixPointsSurviveSeek.</summary>
internal static class RestoreTrace
{
    public static string Describe(double timeMs, IEnumerable<ScoreEvent> state) =>
        $"restore@{timeMs:0}ms " + string.Join(" ", state
            .Where(e => (e.Status & 0xF0) == MidiStatus.ProgramChange || (e.Status & 0xF0) == MidiStatus.ControlChange && e.Data1 is MidiCc.Volume or MidiCc.Pan)
            .OrderBy(e => e.Channel).ThenBy(e => e.Status)
            .Select(e => (e.Status & 0xF0) == MidiStatus.ProgramChange ? $"ch{e.Channel}:prog={e.Data1}" : $"ch{e.Channel}:cc{e.Data1}={e.Data2}"));
}
