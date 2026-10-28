using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Playback;

// Owns: the technique-to-MIDI articulation step of the compile: which rule a note takes (keyswitch / controller before the note, fixed velocity, replaced pitch) and the
//   latched state per track and channel. Only a track played by an instrument plug-in that has a map takes part; every other track compiles exactly as before.
// Does not own: the map and presets (ArticulationMap, ArticulationPresets), the note output (ScoreToMidiCompiler.Techniques.cs) or the UI.
// Tests: TestArticulationKeyswitches.
internal sealed partial class ScoreToMidiCompiler
{
    private const int KeyswitchVelocity = 100;
    private readonly Dictionary<int, ArticulationMap?> _articulationMaps = new();
    /// <summary>The articulation each (track, channel) was last switched to: a latched rule is sent only when it changes. Empty at the start of the compile.</summary>
    private readonly Dictionary<(int Track, int Channel), string> _articulationState = new();

    /// <summary>The map of the track instrument, or null: no instrument plug-in plays the track, or its slot has no rules.</summary>
    private ArticulationMap? ArticulationMapOf(TrackModel track, int trackIndex)
    {
        if (_articulationMaps.TryGetValue(trackIndex, out var cached)) return cached;
        ArticulationMap? map = null;
        if (!track.IsAudio && track.SoundSource == SoundSources.Plugins &&
            track.Rig.Plugins.FirstOrDefault(p => p.Plays && !p.Unavailable && p.Type == PluginSlotType.Instrument) is { ArticulationBindings: { Count: > 0 } bindings })
        {
            var parsed = ArticulationMap.FromBindings(bindings);
            if (!parsed.IsEmpty) map = parsed;
        }
        return _articulationMaps[trackIndex] = map;
    }

    /// <summary>
    /// Applies the track map to one attacking note: emits its keyswitch / controller just before the note (events flagged <see cref="ScoreEvent.IsArticulation"/>), sets a rule velocity,
    /// and returns the pitch to sound (the written pitch unless a Replace rule applies). A keyswitch note is not sent while the track also sounds its General MIDI sound (it would be heard there).
    /// </summary>
    private int ApplyArticulation(TrackModel track, int trackIndex, TabCell cell, TabNote note, HashSet<string> t, int channel, int device, double onset, ref int velocity, int midi)
    {
        if (ArticulationMapOf(track, trackIndex) is not { } map || channel == ChannelAllocator.PercussionChannel) return midi;
        string? messageId = null;
        var message = default(ArticulationRule);
        int? replace = null, fixedVelocity = null;
        foreach (var row in ArticulationTechniques.All)
        {
            if (row.Id == ArticulationTechniques.Normal || !map.Rules.TryGetValue(row.Id, out var rule) || !row.Matches(note, cell, t)) continue;
            if (rule.Action is ArticulationAction.Keyswitch or ArticulationAction.Cc) { if (messageId is null) (messageId, message) = (row.Id, rule); }
            else if (rule.Action == ArticulationAction.Replace) replace ??= rule.A;
            else if (rule.Action == ArticulationAction.Velocity) fixedVelocity ??= rule.A;
        }
        // No technique with a message: the instrument belongs in its normal articulation (sent once when latched).
        if (messageId is null && map.Rules.TryGetValue(ArticulationTechniques.Normal, out var normal) && normal.Action is ArticulationAction.Keyswitch or ArticulationAction.Cc)
            (messageId, message) = (ArticulationTechniques.Normal, normal);
        if (messageId is not null)
        {
            var key = (trackIndex, channel);
            if (message.Mode == ArticulationMode.PerNote || !_articulationState.TryGetValue(key, out var current) || current != messageId)
                EmitArticulationMessage(message, map.LeadMs, track.MidiSound, device, channel, onset, trackIndex);
            _articulationState[key] = messageId;
        }
        if (fixedVelocity is int fixedValue) velocity = Math.Clamp(fixedValue, 1, MidiLimits.Max);
        return replace is int pitch ? Math.Clamp(pitch, 0, MidiLimits.Max) : midi;
    }

    private void EmitArticulationMessage(ArticulationRule rule, int leadMs, bool alsoGeneralMidi, int device, int channel, double onset, int trackIndex)
    {
        var at = Math.Max(0, onset - Math.Clamp(leadMs, 0, ArticulationMap.MaxLeadMs));
        if (rule.Action == ArticulationAction.Cc)
        {
            AddArticulation(device, channel, 0xB0, rule.A, rule.B, at, trackIndex);
            return;
        }
        if (alsoGeneralMidi) return;
        AddArticulation(device, channel, 0x90, rule.A, KeyswitchVelocity, at, trackIndex);
        AddArticulation(device, channel, 0x80, rule.A, 0, Math.Max(at + 1, onset), trackIndex);
    }

    private void AddArticulation(int device, int channel, int status, int data1, int data2, double timeMs, int trackIndex)
    {
        Add(device, channel, status, data1, data2, timeMs, trackIndex);
        _timeline.Events[^1].IsArticulation = true;
    }
}
