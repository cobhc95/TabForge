using TabForge.Models;

namespace TabForge.Audio;

// Owns: the click's engine destination: a hidden stand-in track with its own engine slot and a plain General MIDI synth (no plug-ins,
//   no chain, level 127 centre: the click level is the note velocity the metronome and count-in settings already give).
// Does not own: the click notes (PlaybackEngine sends them through RoutedMidiOutput.SendMetronome), the engine sync that keeps the slot
//   (AudioEngineClient.Sync.cs, AddClick), any song's track or group.
// Tests: no dedicated test class.

/// <summary>
/// Metronome and count-in clicks play here instead of through the drum channel's track: no song track's mute, solo, volume, pan, group,
/// sound source or FX chain applies. It is in no song (not in the track list, the mixer, the saved file or a render), one per audio engine
/// client, and wanted by every sync while the engine runs. It goes through the master chain like any slot (the engine has no bypass for it).
/// </summary>
public sealed class ClickDestination
{
    /// <summary>The stand-in track: a General MIDI sound on the engine, with no chain.</summary>
    public TrackModel Track { get; } = new()
    {
        Name = "Click", InstrumentName = "Click", Kind = TrackKind.Guitar, MidiSound = true, SoundSource = SoundSources.Midi,
    };

    /// <summary>True for the stand-in track (no song's rule applies to it).</summary>
    public bool Owns(TrackModel track) => ReferenceEquals(track, Track);

    /// <summary>The stand-in's level and pan: full level, centre. The velocity carries the click's volume.</summary>
    public (int Volume, int Pan) Mix => (MidiLimits.Max, 64);
}
