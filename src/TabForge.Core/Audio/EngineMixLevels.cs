using System;
using TabForge.Models;

namespace TabForge.Audio;

// Owns: the per-track engine level and pan a song sends to the audio engine (mute/solo rule, group levels, master scaling).
// Does not own: the engine sync itself (AudioEngineClient.Sync), the MIDI routing (AudioRouting in the app).
// Tests: TabForge.Avalonia.Tests.TransportRecordingTests (engine sync of arm state).
public static class EngineMixLevels
{
    /// <summary>Per-track engine level and pan: the mute/solo rule and group levels, with the same master scaling as the CC7 the playback sends.</summary>
    /// <param name="liveSolo">The live input is soloed: only the song's soloed tracks play beside it.</param>
    public static Func<TrackModel, (int Volume, int Pan)> Levels(SongProject project, int masterPercent, bool liveSolo = false) => t =>
    {
        var level = Math.Clamp((int)Math.Round(MixerGroups.Volume(project, t) * Math.Clamp(masterPercent, 0, 100) / 100.0), 0, MidiLimits.Max);
        return (EngineLevel(MixerGroups.IsAudible(project, t, liveSolo), level), MixerGroups.Pan(project, t));
    };

    /// <summary>
    /// The level sent to the engine. Level 0 is the engine's mute gate (no MIDI volume message lifts it), so it is reserved for a track the
    /// mute/solo rule silences; an audible track with its fader at 0 sends 1 (about -84 dB) so a Mix Table volume change can still bring it in.
    /// </summary>
    public static int EngineLevel(bool audible, int level) => audible ? Math.Max(1, level) : 0;
}
