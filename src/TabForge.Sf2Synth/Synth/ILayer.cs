using TabForge.Sf2Synth.Humanize;

namespace TabForge.Sf2Synth;

// Owns: the contract of an instrument layer that plays chosen channels instead of the SoundFont (e.g. Guitar/GuitarLayer).
// Does not own: the routing (Synthesizer sends a layer the note-ons of the channels it owns and every other channel message).
// Tests: GuitarLayerTests, HumanizerTests.
public interface ILayer
{
    /// <summary>True: note-ons on this channel go to the layer, not to the SoundFont.</summary>
    bool Owns(int channel);
    void ProcessMidi(int channel, int command, int data1, int data2);
    /// <summary>Adds the layer's sound; any length; no allocation.</summary>
    void Render(Span<float> left, Span<float> right);
    void Reset();
    /// <summary>The voice variation of the channel's next note-on (sent just before it); default = none.</summary>
    void Vary(int channel, in Nuance nuance) { }
    /// <summary>The live options, on the audio thread just before each Render; default = ignored.</summary>
    void Apply(SynthOptions options) { }
}
