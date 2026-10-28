namespace TabForge.Playback;

// Owns: chasing the articulation keyswitch in force at a position: a start, seek, loop wrap or skip re-sends the last keyswitch each track sent before it.
// Does not own: the compile of the keyswitches (ScoreToMidiCompiler.Articulations) or the channel-state restore that calls it (PlaybackEngine.Scheduler.Output.cs).
// Tests: TestArticulationKeyswitches.
public sealed partial class PlaybackEngine
{
    /// <summary>
    /// For each track and channel, the latest keyswitch note (an event flagged <see cref="ScoreEvent.IsArticulation"/>) at or before <paramref name="timeMs"/>:
    /// the articulation the instrument must be in there, so a latched keyswitch sent earlier is not lost when the schedule jumps past it.
    /// A controller rule needs no help: it is a channel-state message, restored with the others.
    /// </summary>
    internal static List<ScoreEvent> LatestKeyswitches(ScoreTimeline timeline, double startMs, double timeMs, int onlyTrack)
    {
        var latest = new Dictionary<(int Device, int Channel, int Track), ScoreEvent>();
        var events = timeline.Events;
        for (var i = FirstIndexAtOrAfter(events, Math.Min(startMs, timeline.PlayFromMs)); i < events.Count && events[i].TimeMs <= timeMs; i++)
        {
            var e = events[i];
            if (e.IsArticulation && e.IsNoteOn && (onlyTrack < 0 || e.TrackIndex == onlyTrack)) latest[(e.DeviceId, e.Channel, e.TrackIndex)] = e;
        }
        return latest.Values.ToList();
    }

    /// <summary>Sends the keyswitches of <see cref="LatestKeyswitches"/> (a tap: note on, note off). Runs under the output lock.</summary>
    private void SendKeyswitchesAt(ScoreTimeline timeline, double startMs, double timeMs, int onlyTrack)
    {
        foreach (var e in LatestKeyswitches(timeline, startMs, timeMs, onlyTrack))
        {
            _output.Send(e.DeviceId, e.Status, e.Data1, e.Data2);
            _output.Send(e.DeviceId, 0x80 | (e.Status & 0x0F), e.Data1, 0);
        }
    }
}
