using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

/// <summary>
/// Owns: the one live send of a running song's per-channel state (program, volume, pan, reverb and chorus sends): the channel setup the loop, seek and resume
/// re-send is brought in line with the song as it is now, and the new values go to the output at once, each as the mix point in force at the playback position when the song has one (LatestChannelState);
/// and the one choke point every timeline event's bytes pass on their way out (Outgoing: program preview, Focus Track, master).
/// Does not own: the compiled setup (ScoreToMidiCompiler), the program preview override (PlaybackEngine.ProgramPreview.cs) or the plug-in track levels (the routing).
/// Tests: VolPanLiveProbeTests, InstrumentChoiceTests, TestFocusTrackSurvivesSeek. Thread: owner (UI) thread; the setup events are written only under the output lock, where the scheduler reads them.
/// </summary>
public sealed partial class PlaybackEngine
{
    /// <summary>Sends the song's current program, volume, pan and sends for <paramref name="track"/> (every track when null) now and keeps them in the channel setup a pause, seek or loop re-sends. Nothing while stopped: a start compiles the song as it is.</summary>
    public void PushLiveChannelState(SongProject project, int? track = null) =>
        PushChannelState(project, (_, id) => track is null || (track >= 0 && track < project.Tracks.Count && id == project.Tracks[track.Value].Id), send: true);

    /// <summary>Brings the channel setup of a paused song in line with the live song without sending, so the resume plays the values as they are now.</summary>
    private void RefreshChannelSetupFromLive()
    {
        if (LiveProject is { } project) PushChannelState(project, (_, _) => true, send: false);
    }

    private void PushChannelState(SongProject project, Func<ScoreEvent, Guid, bool> affects, bool send)
    {
        var song = Volatile.Read(ref _song);   // one read: the timeline and the track ids it is numbered by belong together
        var timeline = song?.Timeline;
        if (timeline is null || !_running) return;
        var ids = song!.TrackIds;
        var latest = LatestChannelState(timeline, timeline.PlayFromMs, Volatile.Read(ref _currentStreamMs), -1);
        foreach (var e in timeline.ChannelSetup)
        {
            if (e.TrackIndex < 0 || LiveTrackOf(project, ids, e.TrackIndex) is not { } liveTrack || !affects(e, liveTrack.Id)) continue;   // the event's own track, found by id: a list move must not hand its channel another track's sound
            if (!TryLiveValue(project, liveTrack, e, out var value)) continue;
            lock (_outputGate)
            {
                // The chased start value is the automation in force from a start after mix points: a base edit never replaces it. A flag on any other timeline is stale and cleared.
                if (!timeline.StartedAfterMixPoints) e.IsChased = false;
                if (!e.IsChased)
                {
                    if ((e.Status & 0xF0) == 0xC0) e.Data1 = value; else e.Data2 = value;
                }
                // The setup keeps the base or chased value (a loop restart begins before the mix points); what is sent is the mix point in force at the position, else the setup.
                var sent = latest.TryGetValue(ChannelStateKey(e), out var mix) && !mix.IsSetup ? mix : e;
                if (send) SendState(timeline, sent);
            }
        }
    }

    /// <summary>Sends one channel-state event of <paramref name="timeline"/> as it is now (setup, chased start value or mix point). Caller holds the output lock.</summary>
    private void SendState(ScoreTimeline timeline, ScoreEvent e)
    {
        var (data1, data2) = Outgoing(timeline, e, e.Data1, e.Data2);
        _output.Send(e.DeviceId, e.Status, data1, data2);
    }

    /// <summary>
    /// The one place a timeline event's data bytes become the bytes sent (the dispatch, the channel setup, a seek or loop restore and the live push all come here):
    /// the program preview override, then for a song track's channel volume (CC7) Focus Track for that track (found by the timeline's own track ids) and the master.
    /// The timeline always holds the song's own levels and nothing else scales them, so no start, seek, loop wrap, recompile or swap can lose or double Focus Track.
    /// The metronome and count-in clicks are never focus-scaled.
    /// </summary>
    private (int Data1, int Data2) Outgoing(ScoreTimeline timeline, ScoreEvent e, int data1, int data2)
    {
        if ((e.Status & 0xF0) == 0xB0 && data1 == 7 && !e.IsMetronome && e.TrackIndex >= 0 && Focus is { IsOn: true } focus
            && (timeline.TrackIds ?? Volatile.Read(ref _song)?.TrackIds) is { } ids && e.TrackIndex < ids.Length)
            data2 = focus.Scale(ids[e.TrackIndex], data2);
        return (ProgramFor(e.Status, data1), MasterScaled(e.Status, data1, data2));
    }

    /// <summary>The track's value for a setup message the song models; false for the others (bend range, wheel, expression).</summary>
    private bool TryLiveValue(SongProject project, TrackModel track, ScoreEvent e, out int value)
    {
        value = 0;
        switch (e.Status & 0xF0)
        {
            case 0xC0: value = track.MidiProgram; return true;
            case 0xB0 when e.Data1 == 0: value = Math.Max(0, track.MidiBankMsb); return track.MidiBankMsb >= 0;
            case 0xB0 when e.Data1 == 32: value = Math.Max(0, track.MidiBankLsb); return track.MidiBankLsb >= 0;
            case 0xB0 when e.Data1 == 7: value = MixerGroups.Volume(project, track); return true;   // the song's own level: Focus Track is applied when it is sent (Outgoing)
            case 0xB0 when e.Data1 == 10: value = MixerGroups.Pan(project, track); return true;
            case 0xB0 when e.Data1 == 91: value = Math.Clamp(track.Reverb, 0, MidiLimits.Max); return true;
            case 0xB0 when e.Data1 == 93: value = Math.Clamp(track.Chorus, 0, MidiLimits.Max); return true;
            default: return false;
        }
    }
}
