# Playback/Articulations

Technique to MIDI translation for an instrument plug-in (keyswitch or controller before the note, fixed velocity, replaced pitch).
The map lives in `PluginSlot.ArticulationBindings` (technique id to encoded rule, plus `@LeadMs`); `RigPreset.ArticulationMap` holds the preset name.

## Files
| File | Purpose |
| --- | --- |
| `ArticulationRule.cs` | One rule, its stored text, the technique rows (priority order) and note names (C0 = MIDI 24) |
| `ArticulationMap.cs` | The rules of one instrument and how they are kept in the slot |
| `ArticulationPresets.cs` | The built-in presets (Generic, Ample Sound guitar), the .json file form and the user folder |
| `PlaybackEngine.Articulations.cs` | Chase: the latest keyswitch per track and channel at a position, sent with the channel-state restore |
| `ScoreToMidiCompiler.Articulations.cs` | The compile step (called from `EmitNote`): latched state per track and channel, events flagged `ScoreEvent.IsArticulation` |

## Pathway
Only a track played by an instrument plug-in with a map takes part. Keyswitch events are not muted with the track (the latched state must stay right), are not sent while the track also sounds General MIDI, and are left out of a MIDI file export.
A start, seek or loop wrap re-sends the keyswitch in force at that position (`PlaybackEngine.Articulations.cs`, called from `RestoreChannelStateAt`).

## Tests
`TestArticulationKeyswitches`
