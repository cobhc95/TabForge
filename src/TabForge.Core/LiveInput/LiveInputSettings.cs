using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.LiveInput;

// Owns: what the live input feature keeps in the app settings: the last live FX chain, the input, its options, its level, gain and mute, and the tuner's reference and window.
// Does not own: loading and saving (AppSettingsStore), the running chain (LiveInputArm), the settings bounds call (LiveInputFeatureModule).
// Tests: LiveInputStripTests.
/// <summary>App-wide live input settings. Never part of a song: not saved with it, not exported, not rendered.</summary>
public sealed class LiveInputSettings
{
    public const string RestoreAsk = "Ask", RestoreAlways = "Always", RestoreNever = "Never";
    public static readonly string[] RestoreChoices = { RestoreAsk, RestoreAlways, RestoreNever };

    /// <summary>The live FX chain as the user last left it (the "previous" chain a new session can restore).</summary>
    public RigPreset Chain { get; set; } = new();
    /// <summary>The monitored audio input (an <see cref="AudioInputs"/> id, kept also while the device lacks it); empty: none (MIDI only).</summary>
    public string Input { get; set; } = AudioInputs.Input1;
    /// <summary>The MIDI device listened to beside or instead of the audio input; empty: any available device; null: none.</summary>
    public string? MidiDevice { get; set; }
    /// <summary>Open the live FX chain window each time live input is turned on.</summary>
    public bool OpenFxOnTurnOn { get; set; } = true;
    /// <summary>Whether a new session restores <see cref="Chain"/>: one of <see cref="RestoreChoices"/>.</summary>
    public string Restore { get; set; } = RestoreAlways;
    /// <summary>The live input's own volume (0-127, as a track's) and pan (0-127, 64 centre) in the mixer's live input row.</summary>
    public int Volume { get; set; } = 100;
    public int Pan { get; set; } = 64;
    /// <summary>The live input's gain into its chain in dB and its mute (the live FX window's strip); live only, never in a recording.</summary>
    public int GainDb { get; set; } = LiveStripRules.DefaultGainDb;
    public bool Muted { get; set; }
    /// <summary>The tuner's A4 reference in Hz and its analysis window in ms (see <see cref="LiveStripRules"/>).</summary>
    public int TunerReferenceHz { get; set; } = LiveStripRules.DefaultReferenceHz;
    public int TunerWindowMs { get; set; } = LiveStripRules.DefaultWindowMs;
    /// <summary>The strip's Tuner switch: on unless the user turned it off.</summary>
    public bool TunerOn { get; set; } = true;
    /// <summary>The mixer's Master row is collapsed: its live input row is hidden.</summary>
    public bool MixerCollapsed { get; set; }

    /// <summary>Brings loaded values back to valid ones.</summary>
    public void Normalize()
    {
        Chain ??= new RigPreset();
        if (Input != "" && !AudioInputs.TryParse(Input, out _, out _)) Input = AudioInputs.Input1;   // well-formed is enough: the device's list may not be known yet
        if (!RestoreChoices.Contains(Restore)) Restore = RestoreAlways;
        Volume = Math.Clamp(Volume, 0, MidiLimits.Max);
        Pan = Math.Clamp(Pan, 0, MidiLimits.Max);
        GainDb = LiveStripRules.ClampGainDb(GainDb);
        TunerReferenceHz = Math.Clamp(TunerReferenceHz, LiveStripRules.MinReferenceHz, LiveStripRules.MaxReferenceHz);
        if (!LiveStripRules.WindowChoicesMs.Contains(TunerWindowMs)) TunerWindowMs = LiveStripRules.DefaultWindowMs;
    }
}
