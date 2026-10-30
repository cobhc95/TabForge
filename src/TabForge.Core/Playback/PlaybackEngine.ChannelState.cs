using TabForge.Models;
using TabForge.Services;

namespace TabForge.Playback;

/// <summary>
/// Owns: the one live send of a running song's per-channel state (program, volume, pan, reverb and chorus sends): the channel setup the loop, seek and resume
/// re-send is brought in line with the song as it is now, and the new values go to the output at once, each as the mix point in force at the playback position when the song has one (LatestChannelState).
/// Does not own: the compiled setup (ScoreToMidiCompiler), the program preview override (PlaybackEngine.ProgramPreview.cs) or the plug-in track levels (the routing).
/// Tests: VolPanLiveProbeTests, InstrumentChoiceTests. Thread: owner (UI) thread; the setup events are written only under the output lock, where the scheduler reads them.
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
                if (send) _output.Send(sent.DeviceId, sent.Status, ProgramFor(sent.Status, sent.Data1), MasterScaled(sent.Status, sent.Data1, sent.Data2));
            }
        }
    }

    /// <summary>The track's value for a setup message the song models; false for the others (bend range, wheel, expression).</summary>
    private static bool TryLiveValue(SongProject project, TrackModel track, ScoreEvent e, out int value)
    {
        value = 0;
        switch (e.Status & 0xF0)
        {
            case 0xC0: value = track.MidiProgram; return true;
            case 0xB0 when e.Data1 == 0: value = Math.Max(0, track.MidiBankMsb); return track.MidiBankMsb >= 0;
            case 0xB0 when e.Data1 == 32: value = Math.Max(0, track.MidiBankLsb); return track.MidiBankLsb >= 0;
            case 0xB0 when e.Data1 == 7: value = MixerGroups.Volume(project, track); return true;
            case 0xB0 when e.Data1 == 10: value = MixerGroups.Pan(project, track); return true;
            case 0xB0 when e.Data1 == 91: value = Math.Clamp(track.Reverb, 0, MidiLimits.Max); return true;
            case 0xB0 when e.Data1 == 93: value = Math.Clamp(track.Chorus, 0, MidiLimits.Max); return true;
            default: return false;
        }
    }
}
