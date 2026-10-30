using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Playback;

// Owns: the technique-to-MIDI articulation step of the compile: which rule a note takes (keyswitch / controller before the note, fixed velocity, replaced pitch) and the
//   latched state per track and channel. A track played by an instrument plug-in that has a map takes part, and so does a built-in-sound track whose
//   map the export explicitly picked; every other track compiles exactly as before.
// Does not own: the map and presets (ArticulationMap, ArticulationPresets), the note output (ScoreToMidiCompiler.Techniques.cs) or the UI.
// Tests: TestArticulationKeyswitches, MidiTechniqueExportTests.

internal sealed partial class ScoreToMidiCompiler
{
    private const int KeyswitchVelocity = 100;
    private readonly Dictionary<int, ArticulationMap?> _articulationMaps = new();
    /// <summary>The articulation each (track, channel) was last switched to: a latched rule is sent only when it changes. Empty at the start of the compile.</summary>
    private readonly Dictionary<(int Track, int Channel), string> _articulationState = new();
    /// <summary>The articulation of the note being emitted (null: no map): its rule, and the techniques a map covers for the suppression of their own effects.</summary>
    private NoteArticulation? _noteArticulation;

    /// <summary>What a map says about one note: the message it sends, the pitch/velocity it sets, and which techniques it covers.</summary>
    private sealed class NoteArticulation
    {
        public required ArticulationMap Map { get; init; }
        public HashSet<string> Covered { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Unmapped { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? MessageId;
        public ArticulationRule Message;
        public int? Replace;
        public int? FixedVelocity;

        /// <summary>True when the technique's own pitch bend, cut or attack must not run: a rule covers it, or the export skips unmapped techniques.</summary>
        public bool Suppress(string id, bool skipUnmapped) => Covered.Contains(id) || (skipUnmapped && Unmapped.Contains(id));

        /// <summary>True for a technique that has no rule row at all (tremolo, trill, whammy): only the Skip choice affects it.</summary>
        public bool SuppressUnmapped(bool skipUnmapped) => skipUnmapped;
    }

    /// <summary>The map of the track instrument, or null: no instrument plug-in plays the track, or its slot has no rules.</summary>
    private ArticulationMap? ArticulationMapOf(TrackModel track, int trackIndex)
    {
        if (_opt.ArticulationExport is { MapsEnabled: false }) return null;
        if (_opt.ArticulationExport?.Overrides.TryGetValue(trackIndex, out var chosen) == true)
            return chosen.IsEmpty ? null : chosen;
        if (_articulationMaps.TryGetValue(trackIndex, out var cached)) return cached;
        return _articulationMaps[trackIndex] = ArticulationExportOptions.DefaultMapOf(track);
    }

    /// <summary>Reads the track map for one attacking note: its message, replaced pitch/velocity and the techniques it covers. Counts the export report.
    /// A track with no instrument plug-in (a built-in General MIDI sound) takes no map by default, so it exports exactly as the As-played choice;
    /// a map the export explicitly picks for that track is applied like a plug-in track's map.</summary>
    private NoteArticulation? ArticulationFor(TrackModel track, int trackIndex, TabCell cell, TabNote note, HashSet<string> t, int channel)
    {
        if (channel == ChannelAllocator.PercussionChannel) return null;
        var picked = _opt.ArticulationExport is { MapsEnabled: true } pickedExport && pickedExport.Overrides.ContainsKey(trackIndex);
        if (!picked && ArticulationExportOptions.InstrumentSlotOf(track) is null) return null;
        var map = ArticulationMapOf(track, trackIndex);
        if (map is null && _opt.ArticulationExport?.SkipUnmapped != true) return null;
        var state = new NoteArticulation { Map = map ?? new ArticulationMap() };
        foreach (var row in ArticulationTechniques.All)
        {
            if (row.Id == ArticulationTechniques.Normal || !row.Matches(note, cell, t)) continue;
            if (!state.Map.Rules.TryGetValue(row.Id, out var rule))
            {
                state.Unmapped.Add(row.Id);
                continue;
            }
            state.Covered.Add(row.Id);
            if (rule.Action is ArticulationAction.Keyswitch or ArticulationAction.Cc) { if (state.MessageId is null) (state.MessageId, state.Message) = (row.Id, rule); }
            else if (rule.Action == ArticulationAction.Replace) state.Replace ??= rule.A;
            else if (rule.Action == ArticulationAction.Velocity) state.FixedVelocity ??= rule.A;
        }
        // No technique with a message: the instrument belongs in its normal articulation (sent once when latched).
        if (state.MessageId is null && state.Map.Rules.TryGetValue(ArticulationTechniques.Normal, out var normal) && normal.Action is ArticulationAction.Keyswitch or ArticulationAction.Cc)
            (state.MessageId, state.Message) = (ArticulationTechniques.Normal, normal);
        if (_opt.ArticulationExport is { } export)
        {
            export.Mapped += state.Covered.Count;
            if (export.SkipUnmapped) export.Skipped += state.Unmapped.Count;
            else export.Kept += state.Unmapped.Count;
        }
        return state;
    }

    /// <summary>Applies the note's articulation: emits its keyswitch / controller just before the note (events flagged <see cref="ScoreEvent.IsArticulation"/>), sets a rule velocity,
    /// and returns the pitch to sound (the written pitch unless a Replace rule applies). Playback does not send a keyswitch note while the track also
    /// sounds its General MIDI sound (it would be heard there); an export that picked the map writes it even then, because the file is for another program.</summary>
    private int ApplyArticulation(TrackModel track, int trackIndex, int channel, int device, double onset, ref int velocity, int midi)
    {
        if (_noteArticulation is not { } state) return midi;
        if (state.MessageId is not null)
        {
            var key = (trackIndex, channel);
            if (state.Message.Mode == ArticulationMode.PerNote || !_articulationState.TryGetValue(key, out var current) || current != state.MessageId)
                EmitArticulationMessage(state.Message, state.Map.LeadMs, track.MidiSound && _opt.ArticulationExport is null, device, channel, onset, trackIndex);
            _articulationState[key] = state.MessageId;
        }
        if (state.FixedVelocity is int fixedValue) velocity = Math.Clamp(fixedValue, 1, MidiLimits.Max);
        return state.Replace is int pitch ? Math.Clamp(pitch, 0, MidiLimits.Max) : midi;
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

    /// <summary>True when an articulation map covers this technique (or the export skips unmapped ones): its own effect must not also run.</summary>
    private bool ArticulationSuppressed(string id) => _noteArticulation?.Suppress(id, _opt.ArticulationExport?.SkipUnmapped == true) == true;

    /// <summary>True when the export's Skip choice leaves out techniques that have no rule row (tremolo, trill, whammy, trem bar).</summary>
    private bool SkipUnmappedArticulation => _opt.ArticulationExport?.SkipUnmapped == true;
}
