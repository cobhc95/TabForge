namespace TabForge.Controllers;

// Owns: the shape only (named GM program plus rig and map).
// Does not own: the instrument lists or the track that takes the sound.
// Tests: InstrumentChoiceTests.

/// <summary>One named sound a track can take: its General MIDI program, the rig preset and the articulation map. Owns: the shape only. Tests: TestInstrumentChoiceStrings.</summary>
public sealed record TrackInstrumentPreset(string Name, int Program, string Rig, string Map);
