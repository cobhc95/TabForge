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
    /// <summary>A studio player: small spread, quick strums, 20 ms look-ahead.</summary>
    Tight,
    /// <summary>A good live player (the measured owner's playing): the default, 35 ms look-ahead.</summary>
    Natural,
    /// <summary>Relaxed and laid back: wide dynamics, slow strums, more ghost notes, 50 ms look-ahead.</summary>
    Loose,
}

// Owns: every switch of the engine's optional stages as one immutable set (Synthesizer.Options swaps it whole, so any thread may
//   change it during playback), and the descriptor tables a settings menu can be generated from.
// Does not own: what the switches do (Synthesizer, Humanize/Humanizer, Guitar/GuitarLayer).
// Tests: HumanizerTests, GuitarLayerTests.
public sealed record SynthOptions
{
    /// <summary>The modelled guitar/bass layer plays its programs; off: the SoundFont plays them (ringing strings finish).</summary>
    public bool GuitarLayer { get; init; } = true;
    /// <summary>Master switch of the humanizer; off: every event passes unchanged and on time.</summary>
    public bool Humanize { get; init; }
    /// <summary>How loosely the humanizer plays; also sets its look-ahead (reported as latency while on).</summary>
    public Feel Feel { get; init; } = Feel.Natural;
    /// <summary>Accents, bar pattern, phrasing, picking, ghost and strum dynamics, only where the incoming velocities are flat.</summary>
    public bool HumanizeVelocity { get; init; } = true;
    /// <summary>Early and late notes, strum spread and note lengths, only where the incoming timing is grid-exact.</summary>
    public bool HumanizeTiming { get; init; } = true;
    /// <summary>Per-note voice variation: pick noise and position, decaying detune, brightness, release.</summary>
    public bool HumanizeVoice { get; init; } = true;
    /// <summary>Slow tempo drift and a gentle late vibrato on long notes (not on channels that send CC1 or pitch bend).</summary>
    public bool HumanizeDrift { get; init; } = true;
    /// <summary>The distorted guitars' amp; a user .nam still wins over Captured. Switching fades over two short blocks.</summary>
    public DistAmp DistAmp { get; init; } = DistAmp.Captured;
    /// <summary>The built-in cabinet after the captured amp (off: its capture is a full amp output).</summary>
    public bool CapturedAmpCabinet { get; init; }

    public static readonly SynthOptions Default = new();

    /// <summary>For a generated settings menu: name, help, read and write (default = Get(Default)).</summary>
    public static readonly (string Name, string Help, Func<SynthOptions, bool> Get, Func<SynthOptions, bool, SynthOptions> Set)[] Fields =
    [
        ("Guitar layer", "Modelled guitars and basses instead of the SoundFont's", o => o.GuitarLayer, (o, v) => o with { GuitarLayer = v }),
        ("Humanize", "Add a player's dynamics and timing where the song's notes are flat", o => o.Humanize, (o, v) => o with { Humanize = v }),
        ("Dynamics", "Accents, phrasing, picking and ghost notes", o => o.HumanizeVelocity, (o, v) => o with { HumanizeVelocity = v }),
        ("Timing", "Early and late notes, strum spread, note lengths", o => o.HumanizeTiming, (o, v) => o with { HumanizeTiming = v }),
        ("Note variation", "No two notes sound identical", o => o.HumanizeVoice, (o, v) => o with { HumanizeVoice = v }),
        ("Drift and vibrato", "Slow tempo drift, gentle vibrato on long notes", o => o.HumanizeDrift, (o, v) => o with { HumanizeDrift = v }),
        ("Captured amp cabinet", "Add the built-in cabinet after the captured amp", o => o.CapturedAmpCabinet, (o, v) => o with { CapturedAmpCabinet = v }),
    ];

    /// <summary>For a generated settings menu: name, help, the choices' labels, read and write as the choice's index.</summary>
    public static readonly (string Name, string Help, string[] Labels, Func<SynthOptions, int> Get, Func<SynthOptions, int, SynthOptions> Set)[] Choices =
    [
        ("Distorted guitar amp", "Amp of the overdriven and distortion guitars when no amp file is set", ["Captured amp", "Stage amp", "Clean (for an amp plug-in)"],
            o => (int)o.DistAmp, (o, v) => o with { DistAmp = (DistAmp)v }),
        ("Feel", "How loosely the humanizer plays", ["Tight", "Natural", "Loose"], o => (int)o.Feel, (o, v) => o with { Feel = (Feel)v }),
    ];
}
