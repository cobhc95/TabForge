namespace TabForge.Sf2Synth;

// Owns: every switch of the engine's optional stages as one immutable set (Synthesizer.Options swaps it whole, so any thread may
//   change it during playback), and the descriptor table a settings menu can be generated from.
// Does not own: what the switches do (Synthesizer, Humanize/Humanizer, Guitar/GuitarLayer).
// Tests: HumanizerTests.
public sealed record SynthOptions
{
    /// <summary>The modelled guitar/bass layer plays its programs; off: the SoundFont plays them (ringing strings finish).</summary>
    public bool GuitarLayer { get; init; } = true;
    /// <summary>Master switch of the humanizer; off: every event passes unchanged and on time.</summary>
    public bool Humanize { get; init; }
    /// <summary>Accents, phrase arch, picking and strum dynamics, only where the incoming velocities are flat.</summary>
    public bool HumanizeVelocity { get; init; } = true;
    /// <summary>Strum spread and slight laziness (delays only), only where the incoming timing is grid-exact.</summary>
    public bool HumanizeTiming { get; init; } = true;
    /// <summary>Per-note voice variation: pick noise and position, decaying detune, brightness, release.</summary>
    public bool HumanizeVoice { get; init; } = true;
    /// <summary>Slow level drift and a gentle late vibrato on long notes (not on channels that send CC1 or pitch bend).</summary>
    public bool HumanizeDrift { get; init; } = true;

    public static readonly SynthOptions Default = new();

    /// <summary>For a generated settings menu: name, help, read and write (default = Get(Default)).</summary>
    public static readonly (string Name, string Help, Func<SynthOptions, bool> Get, Func<SynthOptions, bool, SynthOptions> Set)[] Fields =
    [
        ("Guitar layer", "Modelled guitars and basses instead of the SoundFont's", o => o.GuitarLayer, (o, v) => o with { GuitarLayer = v }),
        ("Humanize", "Add a player's dynamics and timing where the song's notes are flat", o => o.Humanize, (o, v) => o with { Humanize = v }),
        ("Dynamics", "Accents, phrasing and picking dynamics", o => o.HumanizeVelocity, (o, v) => o with { HumanizeVelocity = v }),
        ("Timing", "Strum spread and slight laziness", o => o.HumanizeTiming, (o, v) => o with { HumanizeTiming = v }),
        ("Note variation", "No two notes sound identical", o => o.HumanizeVoice, (o, v) => o with { HumanizeVoice = v }),
        ("Drift and vibrato", "Slow level drift, gentle vibrato on long notes", o => o.HumanizeDrift, (o, v) => o with { HumanizeDrift = v }),
    ];
}
