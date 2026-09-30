namespace TabForge.Plugins;

// Per-track plug-in rig as stored in .tforge files (property names are the file format: do not rename).

public sealed class RigPreset
{
    public string Name { get; set; } = "Clean";
    public string ArticulationMap { get; set; } = "Generic Guitar";
    public List<PluginSlot> Plugins { get; set; } = new();
    /// <summary>Automatic pitch matching of this chain's instruments: null = the global default (Settings), else on / off.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoPitchMatch { get; set; }
}

public sealed class PluginSlot
{
    /// <summary>Stable identity of this plug-in in its chain (the audio engine keeps its live instance by it).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public PluginSlotType Type { get; set; } = PluginSlotType.Effect;
    public bool Enabled { get; set; } = true;
    /// <summary>"VST2" or "VST3" (from the file: .dll or .vst3).</summary>
    public string Format { get; set; } = "";
    /// <summary>"Auto" (detected, the default), "Instrument" or "Effect" — the user can override the detection.</summary>
    public string RoleMode { get; set; } = PluginRoles.Auto;
    /// <summary>Plug-in vendor (display).</summary>
    public string Vendor { get; set; } = "";
    /// <summary>Channel wiring into the plug-in (pin connector): see <see cref="PluginPins"/>.</summary>
    public string Pins { get; set; } = PluginPins.Stereo;
    /// <summary>Where this plug-in's MIDI comes from (see <see cref="PluginMidiIn"/>); the default is the track's own MIDI, so old files load unchanged.</summary>
    public PluginMidiIn MidiIn { get; set; } = new();
    /// <summary>The MIDI processors in front of this plug-in, in order (see <see cref="MidiProcessorCatalog"/>); empty by default, so old files load unchanged.</summary>
    public List<PluginMidiProcessor> MidiProcessors { get; set; } = new();
    /// <summary>Serial chain: the MIDI arriving at this plug-in also continues to the next one (default on, so old files load unchanged).</summary>
    public bool PassMidiThrough { get; set; } = true;
    /// <summary>Serial chain: this plug-in's own MIDI output events are sent on to the next plug-in (default on).</summary>
    public bool MidiOutToNext { get; set; } = true;
    /// <summary>Instrument audio in the chain: "Add" (default) mixes its output into the incoming audio, "Replace" swaps it.</summary>
    public string InstrumentAudio { get; set; } = "Add";
    /// <summary>Sidechain: stable id (TrackModel.Id, "N") of the track whose post-fader audio feeds inputs 3/4; "" = not connected.</summary>
    public string SidechainTrackId { get; set; } = "";
    /// <summary>MIDI output forwarding: stable id of the track whose chain input receives this plug-in's MIDI output (next block); "" = off.</summary>
    public string MidiOutTrackId { get; set; } = "";
    /// <summary>Output volume of this plug-in in dB (-60 = silent … +12), 0 = unchanged.</summary>
    public double OutputDb { get; set; }
    /// <summary>Wet/dry mix in percent (100 = fully processed). Kept for songs saved with it; not shown any more.</summary>
    public int Wet { get; set; } = 100;
    /// <summary>The plug-in's own saved state (base64), restored when the song opens.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; set; }
    public Dictionary<string, string> ArticulationBindings { get; set; } = new();
    /// <summary>Instruments: the measured automatic pitch-match transpose in semitones (null: not measured yet). Applied right before the plug-in.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? AutoPitchOffset { get; set; }
}

/// <summary>A plug-in's MIDI input routing (property names are the file format).</summary>
public sealed class PluginMidiIn
{
    public const string Own = "Own";
    public const string OtherTrack = "Track";
    public const string None = "None";
    /// <summary>"Own" (this track's MIDI, default), "Track" (another track, by <see cref="TrackId"/>) or "None".</summary>
    public string Source { get; set; } = Own;
    /// <summary>Stable id (TrackModel.Id, "N" format) of the source track when <see cref="Source"/> is "Track".</summary>
    public string TrackId { get; set; } = "";
    /// <summary>0 = all channels, 1..16 = only that channel.</summary>
    public int Channel { get; set; }
}

/// <summary>One MIDI processor of a plug-in's list (property names are the file format).</summary>
public sealed class PluginMidiProcessor
{
    public const int MaxParamsChars = 32 * 1024;
    /// <summary>Catalog id, e.g. "transpose" (see <see cref="MidiProcessorCatalog"/>); unknown ids are kept but ignored by the engine.</summary>
    public string Type { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Parameters as a flat JSON object of numbers and strings.</summary>
    public string Params { get; set; } = "{}";
}

public static class PluginRoles
{
    public const string Auto = "Auto";
    public static readonly string[] All = { Auto, "Instrument", "Effect" };
}

/// <summary>How the chain's stereo signal enters a plug-in (an "in+out" pin connector, simplified).</summary>
public static class PluginPins
{
    public const string Stereo = "Stereo";
    public const string Mono = "Mono (L+R)";
    public const string Left = "Left only";
    public const string Right = "Right only";
    public const string Swap = "Swap L/R";
    public static readonly string[] All = { Stereo, Mono, Left, Right, Swap };
}

public enum PluginSlotType
{
    Instrument,
    Effect
}
