using System.Text.Json;
using System.Text.Json.Nodes;

namespace TabForge.Plugins;

public enum MidiParamKind { Number, Choice, Check, Text, MapPreset, NoteSet }

/// <summary>One parameter of a MIDI processor: the window builds its control from this. Numbers with a <see cref="Scale"/> above 1 are shown in steps of 1/Scale.</summary>
public sealed record MidiParamInfo(string Key, string Label, MidiParamKind Kind, double Min = 0, double Max = 1, double Default = 0, string Tip = "", int Scale = 1, string Unit = "",
    IReadOnlyList<(int Value, string Label)>? Choices = null, string DefaultText = "", string Group = "Main")
{
    public string Format(double v) => Kind == MidiParamKind.Choice ? Choices?.FirstOrDefault(c => c.Value == (int)Math.Round(v)).Label ?? v.ToString("0") : (Scale > 1 ? v.ToString("0.0#") : v.ToString("0")) + Unit;
}

/// <summary>A processor type of the catalog. Parameters travel to the engine as a flat JSON object keyed by <see cref="MidiParamInfo.Key"/>.</summary>
public sealed record MidiProcessorInfo(string Type, string Name, string Category, string Description, string Keywords, IReadOnlyList<MidiParamInfo> Params)
{
    /// <summary>Precomputed lowercase text the search box filters on (name, description, keywords).</summary>
    public string SearchText { get; } = $"{Name} {Category} {Description} {Keywords}".ToLowerInvariant();
}

/// <summary>
/// The MIDI processors TabForge offers in front of a plug-in (functional spec: docs/MIDI_PROCESSORS_SPEC.md). Each entry describes its
/// parameters; the engine (TabForge.AudioEngine/Midi) implements the same type names. Generative and audio-coupled processors follow later.
/// </summary>
public static class MidiProcessorCatalog
{
    private static readonly IReadOnlyList<(int, string)> AnyChannel = new[] { (0, "Any") }.Concat(Enumerable.Range(1, 16).Select(i => (i, i.ToString()))).ToArray();
    private static readonly IReadOnlyList<(int, string)> Channel16 = Enumerable.Range(1, 16).Select(i => (i, i.ToString())).ToArray();

    private static MidiParamInfo InCh(string tip = "Only this channel is processed; the others pass through untouched") =>
        new("InCh", "Input channel", MidiParamKind.Choice, 0, 16, 0, tip, Choices: AnyChannel);
    private static MidiParamInfo Num(string key, string label, double min, double max, double def, string tip = "", int scale = 1, string unit = "") =>
        new(key, label, MidiParamKind.Number, min, max, def, tip, scale, unit);
    private static MidiParamInfo Check(string key, string label, bool def, string tip = "") => new(key, label, MidiParamKind.Check, 0, 1, def ? 1 : 0, tip);
    private static MidiParamInfo Choice(string key, string label, double def, string tip, params (int, string)[] choices) =>
        new(key, label, MidiParamKind.Choice, choices.Min(c => c.Item1), choices.Max(c => c.Item1), def, tip, Choices: choices);

    private static MidiParamInfo RootParam() =>
        Choice("Root", "Key", 0, "Root note of the key", (0, "C"), (1, "C#"), (2, "D"), (3, "D#"), (4, "E"), (5, "F"), (6, "F#"), (7, "G"), (8, "G#"), (9, "A"), (10, "A#"), (11, "B"));
    private static MidiParamInfo ScaleParam() =>
        Choice("Scale", "Scale", 1, "", (0, "Chromatic"), (1, "Major"), (2, "Natural minor"), (3, "Harmonic minor"), (4, "Melodic minor"), (5, "Dorian"), (6, "Phrygian"), (7, "Lydian"),
            (8, "Mixolydian"), (9, "Pentatonic major"), (10, "Pentatonic minor"), (11, "Whole tone"));

    private static readonly IReadOnlyList<(int, string)> ChannelRows = new[] { (0, "Off") }.Concat(Enumerable.Range(1, 16).Select(i => (i, i.ToString()))).ToArray();

    private static readonly MidiProcessorInfo[] Raw = new MidiProcessorInfo[]
    {
        new("channelMap", "Channel filter / remap", "Filter / routing",
            "Sixteen rows: switch a channel off or send it to another channel. Notes and every channel message follow their row; note-offs leave on the channel their note-on used.",
            "midi channel filter remap mute solo reroute midichfilter",
            new[] { Choice("Solo", "Solo channel", 0, "Only this channel gets through", AnyChannel.Select(c => (c.Item1, c.Item1 == 0 ? "Off" : c.Item2)).ToArray()) }
                .Concat(Enumerable.Range(1, 16).Select(i => new MidiParamInfo($"Ch{i}", $"Channel {i} to", MidiParamKind.Choice, 0, 16, i, "Off blocks the channel; a number sends it to that channel", Choices: ChannelRows))).ToArray()),
        new("channelRoute", "Channel router", "Filter / routing",
            "One rule: notes (and/or other messages) in a note range, or one single note, go to another channel. Stack several for more rules.",
            "route note to channel single note redirect drum kick",
            new[]
            {
                InCh(), new MidiParamInfo("OutCh", "Send to channel", MidiParamKind.Choice, 1, 16, 1, "", Choices: Channel16),
                Choice("Scope", "Moves", 1, "What the rule moves", (0, "Nothing (off)"), (1, "Notes"), (2, "Other messages"), (3, "Notes and other messages")),
                Num("Low", "Lowest note", 0, 127, 0), Num("High", "Highest note", 0, 127, 127),
                Num("Note", "Only this note (-1 = all)", -1, 127, -1, "Set a note to route just that one (Route Note To Channel)"),
            }),
        new("noteRange", "Note range filter", "Filter / routing",
            "Drops notes outside a range (with their note-offs). Other events can pass or be dropped.",
            "note filter low high range keyboard split limit",
            new[] { InCh(), Num("Low", "Lowest note", 0, 127, 21), Num("High", "Highest note", 0, 127, 108), Check("OtherPass", "Other events pass", true, "Controllers, program changes and so on") }),
        new("transpose", "Transpose", "Pitch",
            "Shifts notes by semitones, optionally scaled first (premultiply), only inside a key range. A note-off always follows its own note-on.",
            "pitch shift semitones octave interval premultiply",
            new[]
            {
                InCh(), Num("Semitones", "Semitones", -64, 64, 0, "", 1, " st"), Num("Premultiply", "Premultiply", -16, 16, 1, "Interval scaling: out = note x this + semitones", 10),
                Num("Low", "Lowest note", 0, 127, 0), Num("High", "Highest note", 0, 127, 127),
            }),
        new("noteMap", "Note map (drum map)", "Pitch",
            "Converts notes with a drum-map preset: each piece takes the note it has in the target map. Add overrides like 36>38 for anything else.",
            "key map drum map kit convert gm ezdrummer superior addictive getgood slate remap table",
            new[]
            {
                InCh(),
                new MidiParamInfo("From", "Song plays notes of", MidiParamKind.MapPreset, Default: 0, Tip: "The drum map the song's notes follow", DefaultText: "General MIDI"),
                new MidiParamInfo("To", "Instrument expects", MidiParamKind.MapPreset, Default: 0, Tip: "The drum map of the instrument", DefaultText: "General MIDI"),
                new MidiParamInfo("Overrides", "Overrides", MidiParamKind.Text, Default: 0, Tip: "Extra note changes, e.g. 36>38, 40>37 (use 42>x to drop a note)"),
            }),
        new("velocity", "Velocity", "Velocity",
            "Scales, offsets, fixes or compresses note velocities, with limits, randomness and a dry/wet mix. Note-on velocity never drops below 1.",
            "velocity scale multiply add min max fixed compressor dynamics random level",
            new[]
            {
                InCh(), Choice("Mode", "Mode", 0, "How the velocity is computed", (0, "Scale + offset"), (1, "Fixed"), (2, "Compressor")),
                Num("Mul", "Multiply", -16, 16, 1, "out = velocity x this + offset", 10), Num("Add", "Offset", -128, 128, 0),
                Num("Fixed", "Fixed velocity", 1, 127, 100), Num("Threshold", "Compressor threshold", 1, 127, 80), Num("Ratio", "Compressor ratio", 1, 20, 2, "", 10, ":1"),
                Num("Makeup", "Makeup", -64, 64, 0), Num("Min", "Minimum out", 0, 127, 1), Num("Max", "Maximum out", 0, 127, 127),
                Num("Random", "Random", 0, 100, 0, "Random velocity change, percent of the range", 1, " %"), Num("Mix", "Mix", 0, 100, 100, "Dry / wet", 1, " %"),
                Num("InLow", "Lowest velocity affected", 1, 127, 1), Num("InHigh", "Highest velocity affected", 1, 127, 127),
                Num("Low", "Lowest note", 0, 127, 0), Num("High", "Highest note", 0, 127, 127),
            }),
        new("humanize", "Humanizer", "Timing",
            "Random timing and velocity. Everything is held back by a base delay so notes can also land early; a note-off keeps its note-on's shift.",
            "humanize humanise random timing jitter swing feel velocity variation",
            new[]
            {
                InCh(), Num("TimingMs", "Timing randomness", 0, 30, 8, "Up to this many milliseconds early or late", 1, " ms"), Num("BiasMs", "Timing bias", -10, 10, 0, "Plays a little early (-) or late (+)", 1, " ms"),
                Num("BaseDelayMs", "Base delay", 0, 100, 20, "Delay that makes early jitter possible; keep it above the randomness", 1, " ms"),
                Num("VelRandom", "Velocity randomness", 0, 64, 8), Num("Baseline", "Baseline velocity (0 = keep)", 0, 127, 0),
            }),
        new("delay", "MIDI delay", "Timing",
            "Delays MIDI by milliseconds, beats and samples (added together). Notes stay paired; delayed events wait across audio blocks.",
            "delay latency offset shift ms beats samples nudge compensation",
            new[]
            {
                InCh("Only this channel is delayed"), Num("Ms", "Milliseconds", 0, 1000, 100, "", 1, " ms"), Num("Beats", "Beats", 0, 16, 0, "Follows the tempo", 4, " beats"),
                Num("Samples", "Samples", 0, 10000, 0), Check("NotesOnly", "Delay notes only", false, "Controllers and program changes pass straight through; note-ons and note-offs are delayed together"),
            }),
        new("programBank", "Program / bank select", "Controllers",
            "Sends bank select (CC0 / CC32) and a program change when the track loads and/or playback starts.",
            "program change bank msb lsb patch preset load start sound",
            new[]
            {
                new MidiParamInfo("Channel", "Channel", MidiParamKind.Choice, 1, 16, 1, "", Choices: Channel16),
                Num("Msb", "Bank MSB (-1 = none)", -1, 127, -1), Num("Lsb", "Bank LSB (-1 = none)", -1, 127, -1), Num("Program", "Program (-1 = none)", -1, 127, 0),
                Check("OnLoad", "Send when the track loads", true), Check("OnStart", "Send when playback starts", true),
            }),
        new("ccSender", "CC sender", "Controllers",
            "Sends up to four controller values on a channel when the track loads and/or playback starts, and whenever you change them.",
            "cc controller volume pan expression modulation send start",
            new[]
            {
                new MidiParamInfo("Channel", "Channel", MidiParamKind.Choice, 1, 16, 1, "", Choices: Channel16),
                Num("Cc1", "Controller 1 (-1 = off)", -1, 127, 7), Num("Value1", "Value 1", 0, 127, 100),
                Num("Cc2", "Controller 2 (-1 = off)", -1, 127, -1), Num("Value2", "Value 2", 0, 127, 64),
                Num("Cc3", "Controller 3 (-1 = off)", -1, 127, -1), Num("Value3", "Value 3", 0, 127, 64),
                Num("Cc4", "Controller 4 (-1 = off)", -1, 127, -1), Num("Value4", "Value 4", 0, 127, 64),
                Check("OnLoad", "Send when the track loads", true), Check("OnStart", "Send when playback starts", true),
            }),
        new("ccMapper", "CC mapper", "Controllers",
            "Turns one controller into another, with the value limited to a range. The source can pass on as well.",
            "cc controller map convert remap clamp limit modwheel expression",
            new[]
            {
                InCh(), Num("Source", "Source controller", 0, 127, 1), Num("Target", "Target controller", 0, 127, 1), Num("ClampLow", "Lowest value", 0, 127, 0), Num("ClampHigh", "Highest value", 0, 127, 127),
                Check("PassSource", "Source also passes", false),
            }),
        new("scaleSnap", "Snap to scale / key", "Pitch",
            "Moves out-of-scale notes to the nearest note of a key and scale (or blocks them). Note-offs follow their note-on.",
            "snap scale key map to key remap block quantize pitch major minor mode",
            new[] { InCh(), RootParam(), ScaleParam(), Choice("Mode", "Out-of-scale notes", 0, "Remap moves them to the nearest scale note (ties down)", (0, "Snap to nearest"), (1, "Block")), Num("Low", "Lowest note", 0, 127, 0), Num("High", "Highest note", 0, 127, 127) }),
        new("chord", "Chord (in key / chorderizer)", "Pitch",
            "Adds voices to each played note: fixed intervals (chorderizer) or scale steps in a key (chord in key). A note-off releases every voice.",
            "chord chorderizer chord in key triad harmony voices interval stack",
            new[]
            {
                InCh(), Choice("Mode", "Mode", 0, "Fixed semitone intervals, or diatonic steps in the key", (0, "Fixed intervals"), (1, "Diatonic (in key)")),
                Num("V1", "Voice 1 (semitones, 0 = off)", -24, 24, 5), Num("V2", "Voice 2 (semitones, 0 = off)", -24, 24, 0), Num("V3", "Voice 3 (semitones, 0 = off)", -24, 24, 0), Num("V4", "Voice 4 (semitones, 0 = off)", -24, 24, 0),
                RootParam(), ScaleParam(), Num("Step2", "Diatonic step of note 2", -24, 24, 2, "Scale steps above the played note (2 = a third)"), Num("Step3", "Diatonic step of note 3", -24, 24, 4),
                Num("VelScale", "Added voices velocity", 0, 1, 1, "", 100), Num("Low", "Lowest note", 0, 127, 0), Num("High", "Highest note", 0, 127, 127),
            }),
        new("choke", "Choke", "Filter / routing",
            "A note in the choke set (e.g. closed hi-hat) ends the sounding notes of the affected set (open hi-hat) and blocks them while it is held.",
            "choke hihat hi-hat cut mute drum exclusive open closed",
            new[]
            {
                InCh(), Num("ChokeStart", "First choke note", 0, 127, 42), Num("ChokeCount", "Choke notes", 1, 16, 1), Num("AffStart", "First affected note", 0, 127, 46), Num("AffCount", "Affected notes", 1, 16, 1),
                Choice("Action", "Affected notes during choke", 0, "Block drops them, Allow lets them play", (0, "Block"), (1, "Allow")),
                Num("Extra1", "Extra choke note 1 (-1 = none)", -1, 127, -1), Num("Extra2", "Extra choke note 2 (-1 = none)", -1, 127, -1), Num("Extra3", "Extra choke note 3 (-1 = none)", -1, 127, -1), Num("Extra4", "Extra choke note 4 (-1 = none)", -1, 127, -1),
            }),
        new("chokeGroup", "Choke group", "Filter / routing",
            "A range of notes plays one at a time: each new note ends the previous one. Note-offs in the range are ignored (one-shots).",
            "choke group mono monophonic exclusive one shot cut",
            new[] { InCh(), Num("Start", "First note", 0, 127, 60), Num("Count", "Notes in group", 1, 128, 8) }),
        new("sanitizer", "Note sanitizer / duplicate filter", "Filter / routing",
            "Drops a note-on while the same note is already on (and its extra note-off). An optional threshold retriggers instead.",
            "duplicate note filter sanitize sanitizer retrigger double stuck cleanup",
            new[] { InCh(), Num("Retrigger", "Retrigger after (1/32 notes, 0 = never)", 0, 128, 0, "Tempo-synced") }),
        new("noteHold", "Note hold", "Filter / routing",
            "Mono legato hold: a new note ends the previous one, note-offs are ignored, so the last note keeps sounding.",
            "hold sustain legato latch mono drone",
            new[] { InCh() }),
        new("repeater", "Note repeater", "Generators",
            "While a note is held it is retriggered every N beats with its original velocity.",
            "repeat repeater retrigger roll ratchet stutter beats tempo sync",
            new[] { InCh(), Num("Size", "Repeat every (beats)", 0.1, 4, 0.5, "Tempo-synced", 10) }),
        new("arp", "Arpeggiator", "Generators",
            "Turns held notes into a tempo-synced run. Steps sit on the beat grid; each note-off is scheduled.",
            "arpeggiator arp arpeggio run sequence up down alternate tempo sync",
            new[]
            {
                InCh(), Num("Rate", "Steps per beat", 0.25, 16, 2, "Tempo-synced", 100), Num("Length", "Note length", 0.01, 0.95, 0.5, "Fraction of a step", 100),
                Choice("Mode", "Direction", 1, "Order of the held notes", (0, "Down"), (1, "Up"), (2, "Down / up"), (3, "Up / down")),
                Num("Variants", "Extra passes (0-3)", 0, 3, 0, "Each pass adds another offset"), Num("VarOffset", "Offset per pass (semitones)", -64, 64, 12), Num("Velocity", "Velocity (0 = as played)", 0, 127, 0),
            }),
        new("noteRandom", "Note randomizer", "Generators",
            "Replaces a trigger note with a random note in a range (or with a probability).",
            "random note randomize randomise trigger pitch chance",
            new[] { InCh(), Num("Trigger", "Trigger note", 0, 127, 60), Num("Low", "Lowest output", 0, 127, 48), Num("High", "Highest output", 0, 127, 72), Num("Mix", "Chance", 0, 100, 100, "", 1, " %") }),
        new("modalRandom", "Modal randomizer", "Generators",
            "Every played note also seeds random modal notes (scale-step intervals with probabilities, octave, timing and velocity randomness).",
            "modal random cloud scatter generative intervals ambient",
            new[]
            {
                InCh(), Num("Notes", "Simultaneous notes (0-8)", 0, 8, 2),
                Num("Interval1", "Interval 1 (scale step)", 1, 7, 3), Num("Prob1", "Probability 1", 0, 100, 60, "", 1, " %"), Num("Interval2", "Interval 2 (scale step)", 1, 7, 5), Num("Prob2", "Probability 2", 0, 100, 60, "", 1, " %"),
                Num("Interval3", "Interval 3 (scale step)", 1, 7, 2), Num("Prob3", "Probability 3", 0, 100, 30, "", 1, " %"), Num("Interval4", "Interval 4 (scale step)", 1, 7, 7), Num("Prob4", "Probability 4", 0, 100, 30, "", 1, " %"),
                Num("OctaveRandom", "Octave randomness", 0, 100, 20, "", 1, " %"), Num("TimingRandom", "Timing randomness", 0, 100, 50, "", 1, " %"), Num("VelRandom", "Velocity randomness", 0, 100, 30, "", 1, " %"),
                Num("Length", "Note length (beats)", 0.05, 4, 0.5, "", 100),
            }),
        new("scaleVariation", "Scale / pattern randomizer", "Generators",
            "Replaces notes in a range by scale degrees over an octave range: random, or an ascending / descending run.",
            "scale variation pattern random degrees octave run generative",
            new[]
            {
                InCh(), Num("Low", "Lowest note", 0, 127, 0), Num("High", "Highest note", 0, 127, 127), RootParam(), ScaleParam(),
                Num("LowOct", "Lowest octave", 0, 10, 4), Num("HighOct", "Highest octave", 0, 10, 5), Choice("Mode", "Order", 0, "", (0, "Random"), (1, "Ascending run"), (2, "Descending run")),
            }),
        new("velVariation", "Velocity variation", "Velocity",
            "Base velocity with random variation and an accent pattern advanced by each note-on.",
            "velocity variation accent pattern random humanize dynamics",
            new[] { InCh(), Num("Base", "Base velocity (0 = keep)", 0, 127, 0), Num("Variation", "Variation", 0, 100, 20, "", 1, " %"), new MidiParamInfo("Accent", "Accent pattern (1 = accent)", MidiParamKind.Text, DefaultText: "1 0 0 0"), Num("AccentAmount", "Accent amount", 0, 127, 25), Num("Low", "Lowest note", 0, 127, 0), Num("High", "Highest note", 0, 127, 127) }),
        new("lfo", "LFO (CC / pitch wheel)", "Controllers",
            "A low-frequency oscillator into a controller or the pitch wheel, in Hz or cycles per beat. Only changes are sent.",
            "lfo cc pitch wheel bend modulation oscillator sine triangle saw square random sample hold tempo sync",
            new[]
            {
                Choice("Target", "Target", 0, "", (0, "Controller (CC)"), (1, "Pitch wheel")), new MidiParamInfo("Channel", "Channel", MidiParamKind.Choice, 1, 16, 1, "", Choices: Channel16),
                Num("Cc", "Controller", 0, 127, 1), Num("Center", "Centre", 0, 127, 64), Num("Range", "Range (+/-)", 0, 127, 63), Num("MaxBend", "Max bend (pitch wheel)", 0, 100, 50, "", 1, " %"),
                Choice("Shape", "Shape", 0, "", (0, "Sine"), (1, "Triangle"), (2, "Saw"), (3, "Square"), (4, "Sample and hold")),
                Num("Freq", "Frequency", 0, 32, 1, "", 100), Choice("Sync", "Frequency in", 1, "", (0, "Hz"), (1, "Cycles per beat")),
                Num("Updates", "Updates per beat", 1, 512, 64), Num("OffValue", "Value when stopped (-1 = none)", -1, 127, -1), Check("OnlyPlaying", "Only while playing", true), Check("Enabled", "On", true),
            }),
        new("stepSeq", "Step sequencer", "Generators",
            "Plays a step pattern (semitones from the root; . = rest) in time with the transport. Up to four patterns, chained; per-step velocity and gate; swing.",
            "step sequencer baby megababy pattern steps drum sequence gate swing chain tempo sync",
            new[]
            {
                new MidiParamInfo("Channel", "Channel", MidiParamKind.Choice, 1, 16, 1, "", Choices: Channel16), Num("Root", "Root note", 0, 127, 60),
                Num("Steps", "Steps per pattern", 4, 128, 16), Num("StepsPerBeat", "Steps per beat", 1, 16, 4), Num("Gate", "Note length", 1, 100, 50, "", 1, " %"), Num("Swing", "Swing", 0, 100, 0, "", 1, " %"),
                Num("Velocity", "Velocity", 1, 127, 100), Num("Chain", "Patterns in chain", 1, 4, 1),
                new MidiParamInfo("Pattern1", "Pattern 1", MidiParamKind.Text, DefaultText: "0 . 7 . 12 . 7 .", Tip: "Semitones from the root, . for a rest"),
                new MidiParamInfo("Pattern2", "Pattern 2", MidiParamKind.Text, DefaultText: ""), new MidiParamInfo("Pattern3", "Pattern 3", MidiParamKind.Text, DefaultText: ""), new MidiParamInfo("Pattern4", "Pattern 4", MidiParamKind.Text, DefaultText: ""),
                new MidiParamInfo("Velocities", "Step velocities", MidiParamKind.Text, DefaultText: "", Tip: "Optional, cycled: 110 80 90 80"), new MidiParamInfo("Gates", "Step gates (%)", MidiParamKind.Text, DefaultText: "", Tip: "Optional, cycled"),
                Check("PassInput", "Input notes also pass", true),
            }),
        new("audioDrum", "Audio to MIDI drum trigger", "Audio",
            "Turns hits in the audio arriving at this point into MIDI notes (velocity from the peak). Opens above one level, closes below another; the retrigger time blocks double triggers.",
            "audio to midi drum trigger transient kick snare replace sample threshold retrigger peak envelope",
            new[]
            {
                Num("Open", "Open threshold", -60, 0, -17, "Level that fires a note", 1, " dB"), Num("Close", "Close threshold", -60, 0, -18, "Level below which the note ends", 1, " dB"),
                Num("AttackMs", "Attack", 0, 50, 0.1, "Envelope rise time", 10, " ms"), Num("ReleaseMs", "Release", 0.1, 500, 20, "Envelope fall time", 10, " ms"),
                Num("Retrigger", "Retrigger time", 0, 500, 30, "Shortest time between two notes", 1, " ms"),
                Num("Note", "Note", 0, 127, 38), new MidiParamInfo("Channel", "Channel", MidiParamKind.Choice, 1, 16, 10, "", Choices: Channel16),
                Num("Mix", "Audio pass-through", 0, 100, 100, "How much of the original audio stays", 1, " %"),
            }),
        new("audioDucker", "MIDI EQ ducker", "Audio",
            "Notes duck the audio at this point: a gain change, or a band around a frequency, that fades in on note-on and out after the note-off.",
            "ducker duck eq sidechain pump gate gain band notch dip kick bass note triggered audio",
            new[]
            {
                InCh(), Num("Note", "Trigger note (-1 = all)", -1, 127, -1), Choice("Mode", "Affects", 0, "", (0, "Whole signal (gain)"), (1, "One band (frequency)")),
                Num("GainDb", "Gain change", -32, 32, -12, "Negative ducks", 1, " dB"), Num("AttackMs", "Attack", 0, 75, 5, "", 1, " ms"), Num("ReleaseMs", "Release", 0, 500, 150, "", 1, " ms"),
                Num("Freq", "Band frequency", 20, 15000, 1000, "Band mode only", 1, " Hz"), Num("Width", "Band width", 0.1, 2, 1, "Octaves; band mode only", 10, " oct"),
                Check("VelReact", "React to velocity", false),
            }),
        new("loopSampler", "Loop sampler", "Audio",
            "Records the audio at this point while the record note is held (up to the maximum length), then plays the recording back from every other note.",
            "loop sampler record playback audio trigger sample looper one shot pitch",
            new[]
            {
                InCh(), Num("RecNote", "Record note", 0, 127, 50, "Hold to record"), Num("MaxSec", "Maximum length", 1, 30, 4, "Memory is reserved for this length", 1, " s"),
                Num("Root", "Root note", 0, 127, 60, "Note that plays at original speed when pitched"), Check("Pitched", "Pitch follows note", false), Check("Loop", "Loop while held", false),
                Num("Level", "Level", -60, 12, 0, "", 1, " dB"),
            }),
        new("looper", "Synchronized looper (Super8 style)", "Audio",
            "Several loop tracks that share one loop length (the first recording, rounded to beats). Control with notes: record/overdub from the base note, play/stop 8 notes higher, clear 16 higher; track number adds one.",
            "looper super8 loop record overdub play clear tracks tempo sync synchronized audio",
            new[]
            {
                InCh(), Num("Tracks", "Loop tracks", 1, 8, 4), Num("BaseNote", "Base note (record, track 1)", 0, 103, 36, "Record: base + track; play/stop: +8; clear: +16"),
                Num("MaxSec", "Maximum length", 1, 30, 8, "Memory is reserved for every track", 1, " s"), Check("Quantize", "Snap to beats and loop start", true), Check("RestartOnPlay", "Restart loops when playback starts", true),
                Num("Level", "Loop level", -60, 12, 0, "", 1, " dB"),
            }),
        new("panic", "All notes off (Panic)", "Utility",
            "Sends all-notes-off and all-sound-off on every channel when playback stops (and/or starts). The engine also releases every note this list let through.",
            "panic all notes off stuck notes reset sound off stop",
            new[] { Check("OnStop", "When playback stops", true), Check("OnStart", "When playback starts", false) }),
        new("log", "MIDI log", "Utility",
            "Shows the MIDI that passes this point in the list (place it before and after other processors to compare). Only runs while the window shows it.",
            "monitor log examine debug view messages midi events",
            new[] { Choice("Show", "Show", 0, "Which messages are listed", (0, "Everything"), (1, "Notes"), (2, "Controllers"), (3, "Other messages")) }),
    };

    /// <summary>Types that act on notes and therefore get the common "Applies to" note selector.</summary>
    public static readonly IReadOnlySet<string> NoteScoped = new HashSet<string> { "channelRoute", "noteRange", "transpose", "noteMap", "velocity", "humanize", "delay",
        "scaleSnap", "chord", "sanitizer", "noteHold", "repeater", "arp", "noteRandom", "modalRandom", "scaleVariation", "velVariation" };

    private static readonly Dictionary<string, string[]> AdvancedKeys = new()
    {
        ["velocity"] = new[] { "Makeup", "Min", "Max", "Random", "Mix", "InLow", "InHigh", "Low", "High" },
        ["transpose"] = new[] { "Premultiply", "Low", "High" },
        ["humanize"] = new[] { "BaseDelayMs", "Baseline" },
        ["delay"] = new[] { "Samples", "NotesOnly" },
        ["channelMap"] = Enumerable.Range(1, 16).Select(i => $"Ch{i}").ToArray(),
        ["channelRoute"] = new[] { "Low", "High", "Note" },
        ["noteMap"] = new[] { "Overrides" },
    };

    public static readonly IReadOnlyList<MidiProcessorInfo> All = Raw.Select(Scope).ToArray();

    private static MidiProcessorInfo Scope(MidiProcessorInfo info)
    {
        var adv = AdvancedKeys.GetValueOrDefault(info.Type) ?? Array.Empty<string>();
        var list = info.Params.Select(p => adv.Contains(p.Key) ? p with { Group = "Advanced" } : p).ToList();
        if (NoteScoped.Contains(info.Type))
        {
            list.Add(new MidiParamInfo("NoteMode", "Applies to", MidiParamKind.Choice, 0, 2, 0, "Which notes this processor touches; the rest pass through unchanged (a note-off always follows its note-on)",
                Choices: new[] { (0, "All notes"), (1, "Only these notes"), (2, "All except these notes") }, Group: "Applies to"));
            list.Add(new MidiParamInfo("NoteSet", "Notes", MidiParamKind.NoteSet, Tip: "Note numbers or names: 36, 38, 40-45, C2, D#3. Learn adds the next note you play", Group: "Applies to"));
        }
        return info with { Params = list };
    }

    public sealed record SearchHit(MidiProcessorInfo Info, MidiParamInfo? Param, string Display);

    private static readonly List<(SearchHit Hit, string Text)> Index = BuildIndex();

    private static List<(SearchHit, string)> BuildIndex()
    {
        var index = new List<(SearchHit, string)>();
        foreach (var info in All)
        {
            index.Add((new SearchHit(info, null, info.Name), info.SearchText));
            foreach (var p in info.Params)
            {
                var choices = p.Choices is null ? "" : string.Join(' ', p.Choices.Select(c => c.Label));
                index.Add((new SearchHit(info, p, $"{info.Name} › {p.Label}"), $"{info.Name} {p.Label} {p.Tip} {choices} {p.Key}".ToLowerInvariant()));
            }
        }
        return index;
    }

    /// <summary>Processor matches first, then "Processor › Parameter" matches; every word of the query must appear (all processors when empty).</summary>
    public static IReadOnlyList<SearchHit> SearchAll(string query)
    {
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return All.Select(i => new SearchHit(i, null, i.Name)).ToList();
        var hits = new List<SearchHit>();
        foreach (var (hit, text) in Index)
            if (hit.Param is null && words.All(w => text.Contains(w, StringComparison.Ordinal))) hits.Add(hit);
        foreach (var (hit, text) in Index)
            if (hit.Param is not null && words.All(w => text.Contains(w, StringComparison.Ordinal))) hits.Add(hit);
        return hits;
    }

    /// <summary>Built-in whole-list presets (name, processors).</summary>
    public static IReadOnlyList<(string Name, Func<List<PluginMidiProcessor>> Build)> BuiltInPresets { get; } = new (string, Func<List<PluginMidiProcessor>>)[]
    {
        ("Drums: GM → kit map", () => new() { Make("noteMap", o => { o["From"] = "General MIDI"; o["To"] = "Superior Drummer 3 (unverified)"; }) }),
        ("Humanize light", () => new() { Make("humanize", o => { o["TimingMs"] = 4; o["VelRandom"] = 5; o["BaseDelayMs"] = 10; }) }),
        ("Drums: GM → kit map + humanize", () => new() { Make("noteMap", o => { o["From"] = "General MIDI"; o["To"] = "Superior Drummer 3 (unverified)"; }), Make("humanize", o => { o["TimingMs"] = 4; o["VelRandom"] = 6; o["BaseDelayMs"] = 10; }) }),
        ("Kick and snare: softer", () => new() { Make("velocity", o => { o["Mul"] = 0.8; o["NoteMode"] = 1; o["NoteSet"] = "36,38"; }) }),
    };

    private static PluginMidiProcessor Make(string type, Action<JsonObject> set)
    {
        var p = Create(Find(type)!);
        var o = JsonNode.Parse(p.Params)!.AsObject();
        set(o);
        p.Params = o.ToJsonString();
        return p;
    }

    private static readonly Dictionary<string, MidiProcessorInfo> ByType = All.ToDictionary(p => p.Type, StringComparer.Ordinal);

    public static MidiProcessorInfo? Find(string type) => ByType.GetValueOrDefault(type);

    /// <summary>Catalog entries matching every word of <paramref name="query"/> in name, description or keywords (all when empty).</summary>
    public static IEnumerable<MidiProcessorInfo> Search(string query)
    {
        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? All : All.Where(p => words.All(w => p.SearchText.Contains(w, StringComparison.Ordinal)));
    }

    /// <summary>A new list entry with the catalog's default parameters.</summary>
    public static PluginMidiProcessor Create(MidiProcessorInfo info) => new() { Type = info.Type, Enabled = true, Params = DefaultParams(info).ToJsonString() };

    public static JsonObject DefaultParams(MidiProcessorInfo info)
    {
        var o = new JsonObject();
        foreach (var p in info.Params)
        {
            if (p.Kind is MidiParamKind.MapPreset or MidiParamKind.Text or MidiParamKind.NoteSet) o[p.Key] = p.DefaultText;
            else o[p.Key] = p.Default;
        }
        return o;
    }

    /// <summary>The processor's parameters as an editable object (never null; a broken or missing value falls back to the default).</summary>
    public static JsonObject ParseParams(PluginMidiProcessor processor, MidiProcessorInfo info)
    {
        JsonObject? o = null;
        try { if (processor.Params.Length is > 0 and <= PluginMidiProcessor.MaxParamsChars) o = JsonNode.Parse(processor.Params) as JsonObject; }
        catch (JsonException) { }
        o ??= new JsonObject();
        foreach (var p in info.Params)
        {
            var ok = o.TryGetPropertyValue(p.Key, out var v) && v is JsonValue jv && (p.Kind is MidiParamKind.MapPreset or MidiParamKind.Text or MidiParamKind.NoteSet ? jv.TryGetValue<string>(out _) : jv.TryGetValue<double>(out _));
            if (!ok) o[p.Key] = p.Kind is MidiParamKind.MapPreset or MidiParamKind.Text or MidiParamKind.NoteSet ? p.DefaultText : p.Default;
        }
        return o;
    }

    /// <summary>The JSON the engine gets: the stored parameters, with drum-map presets resolved into a note table (the engine reads no files).</summary>
    public static string EngineParams(PluginMidiProcessor processor)
    {
        if (processor.Type != "noteMap") return processor.Params.Length is > 0 and <= PluginMidiProcessor.MaxParamsChars ? processor.Params : "{}";
        var info = Find("noteMap")!;
        var o = ParseParams(processor, info);
        var table = DrumMapLibrary.BuildTable(o["From"]!.GetValue<string>(), o["To"]!.GetValue<string>(), o["Overrides"]!.GetValue<string>());
        return new JsonObject { ["InCh"] = o["InCh"]!.GetValue<double>(), ["NoteMode"] = o["NoteMode"]!.GetValue<double>(), ["NoteSet"] = o["NoteSet"]!.GetValue<string>(), ["Map"] = new JsonArray(table.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()) }.ToJsonString();
    }
}
