using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.LiveInput;

// Owns: what the live input feature keeps in the app settings: the last live FX chain, the input, and its two options.
// Does not own: loading and saving (AppSettingsStore), the running chain (LiveInputArm), the settings bounds call (LiveInputFeatureModule).
// Tests: TestLiveInput.
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
    /// <summary>The mixer's Master row is collapsed: its live input row is hidden.</summary>
    public bool MixerCollapsed { get; set; }

    /// <summary>Brings loaded values back to valid ones.</summary>
    public void Normalize()
    {
        Chain ??= new RigPreset();
        if (Input != "" && !AudioInputs.TryParse(Input, out _, out _)) Input = AudioInputs.Input1;   // well-formed is enough: the device's list may not be known yet
        if (!RestoreChoices.Contains(Restore)) Restore = RestoreAlways;
        Volume = Math.Clamp(Volume, 0, 127);
        Pan = Math.Clamp(Pan, 0, 127);
    }
}
