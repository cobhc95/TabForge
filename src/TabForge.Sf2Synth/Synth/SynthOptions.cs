namespace TabForge.Sf2Synth;

/// <summary>The amp of the distorted guitar programs (29, 30) when no user .nam is set.</summary>
public enum DistAmp
{
    /// <summary>The built-in captured amp (a learned model of a real amp).</summary>
    Captured,
    /// <summary>The built-in stage amp (modelled tube stages) and its cabinet.</summary>
    Stage,
    /// <summary>The clean pickup signal, no amp or cabinet, for an amp plug-in after the synth.</summary>
    CleanOnly,
}

/// <summary>How loosely the humanizer plays (values in Humanize/HumanTable.Styles).</summary>
public enum Feel
{
    /// <summary>A studio player: small spread, quick strums.</summary>
    Tight,
    /// <summary>A good live player (the measured owner's playing): the default.</summary>
    Natural,
    /// <summary>Relaxed and laid back: wider dynamics and timing, slow strums.</summary>
    Loose,
}

// Owns: every switch of the engine's optional stages as one immutable set (Synthesizer.Options swaps it whole, so any thread may
//   change it during playback), and the descriptor tables a settings menu can be generated from.
// Does not own: what the switches do (Synthesizer, Humanize/Humanizer and VoiceVariation, Guitar/GuitarLayer).
// Tests: HumanizerTests, GuitarLayerTests.
public sealed record SynthOptions
{
    /// <summary>The modelled guitar/bass layer plays its programs; off: the SoundFont plays them (ringing strings finish).</summary>
    public bool GuitarLayer { get; init; } = true;
    /// <summary>Master switch of the humanizer (the host's note pass, Humanize/Humanizer.Apply, and the synth's voice variation); off: notes play as written.</summary>
    public bool Humanize { get; init; }
    /// <summary>How loosely the humanizer plays.</summary>
    public Feel Feel { get; init; } = Feel.Natural;
    /// <summary>Accents from meter, riff, phrase, melody and picking, and strum dynamics, only where the written velocities are flat.</summary>
    public bool HumanizeVelocity { get; init; } = true;
    /// <summary>Early and late notes, strum spread and note lengths, only where the written timing is on the grid.</summary>
    public bool HumanizeTiming { get; init; } = true;
    /// <summary>Per-note voice variation: pick noise and position, decaying detune, brightness, release.</summary>
    public bool HumanizeVoice { get; init; } = true;
    /// <summary>Slow tempo wander and a gentle late vibrato on long notes (not on channels that send CC1 or pitch bend).</summary>
    public bool HumanizeDrift { get; init; } = true;
    /// <summary>The distorted guitars' amp; a user .nam still wins over Captured. Switching fades over two short blocks.</summary>
    public DistAmp DistAmp { get; init; } = DistAmp.Captured;
    /// <summary>The built-in cabinet after the captured amp (off: its capture is a full amp output).</summary>
    public bool CapturedAmpCabinet { get; init; }
    /// <summary>The captured amp's output EQ, fitted to the real amp it was captured from (Guitar/Voicings); not with a cabinet.</summary>
    public bool CapturedAmpEq { get; init; } = true;

    public static readonly SynthOptions Default = new();

    /// <summary>For a generated settings menu: name, help, read and write (default = Get(Default)).</summary>
    public static readonly (string Name, string Help, Func<SynthOptions, bool> Get, Func<SynthOptions, bool, SynthOptions> Set)[] Fields =
    [
        ("Guitar layer", "Modelled guitars and basses instead of the SoundFont's", o => o.GuitarLayer, (o, v) => o with { GuitarLayer = v }),
        ("Humanize", "Add a player's dynamics and timing where the song's notes are flat", o => o.Humanize, (o, v) => o with { Humanize = v }),
        ("Dynamics", "Accents from the meter, the riff, the phrase and the picking", o => o.HumanizeVelocity, (o, v) => o with { HumanizeVelocity = v }),
        ("Timing", "Early and late notes, strum spread, note lengths", o => o.HumanizeTiming, (o, v) => o with { HumanizeTiming = v }),
        ("Note variation", "No two notes sound identical", o => o.HumanizeVoice, (o, v) => o with { HumanizeVoice = v }),
        ("Drift and vibrato", "Slow tempo drift, gentle vibrato on long notes", o => o.HumanizeDrift, (o, v) => o with { HumanizeDrift = v }),
        ("Captured amp cabinet", "Add the built-in cabinet after the captured amp", o => o.CapturedAmpCabinet, (o, v) => o with { CapturedAmpCabinet = v }),
        ("Captured amp EQ", "Tone of the captured amp matched to the real amp's recording", o => o.CapturedAmpEq, (o, v) => o with { CapturedAmpEq = v }),
    ];

    /// <summary>For a generated settings menu: name, help, the choices' labels, read and write as the choice's index.</summary>
    public static readonly (string Name, string Help, string[] Labels, Func<SynthOptions, int> Get, Func<SynthOptions, int, SynthOptions> Set)[] Choices =
    [
        ("Distorted guitar amp", "Amp of the overdriven and distortion guitars when no amp file is set", ["Captured amp", "Stage amp", "Clean (for an amp plug-in)"],
            o => (int)o.DistAmp, (o, v) => o with { DistAmp = (DistAmp)v }),
        ("Feel", "How loosely the humanizer plays", ["Tight", "Natural", "Loose"], o => (int)o.Feel, (o, v) => o with { Feel = (Feel)v }),
    ];
}
