// Owns: the Wiring window's logic without UI types: the choice lists (pins, MIDI sources, channels, instrument audio, track targets)
//       with their labels and values, the reason a link control is disabled, the choice that matches a plug-in's saved value, which
//       controls are enabled, the loop check for sidechain and MIDI-output links, and the window's texts.
// Does not own: the window and its controls, the undoable change to a plug-in (the window hands each change to the FX window's Edit),
//       and engine routing (the app applies the links to the audio engine).
// Tests: no dedicated test class.
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Presenters;

/// <summary>One entry of a Wiring choice list: the label shown and the value stored in the plug-in.</summary>
public sealed record WiringChoice(string Label, string Value)
{
    /// <summary>Shows the label in the choice list.</summary>
    public override string ToString() => Label;
}

/// <summary>One track entry of a Wiring list: its stable id ("N" format, "" for none) and its name.</summary>
public sealed record WiringTrackChoice(string Id, string Label)
{
    /// <summary>Shows the label in the choice list.</summary>
    public override string ToString() => Label;
}

/// <summary>The Wiring window's logic without UI types: choice lists, enablement, loop checks and texts.</summary>
public static class WiringPresenter
{
    /// <summary>The highest index of <see cref="Channels"/>: 0 is "All", 1..16 is that MIDI channel.</summary>
    public const int LastChannelIndex = 16;

    /// <summary>Stored in <see cref="PluginSlot.InstrumentAudio"/> when the instrument replaces the chain's audio.</summary>
    public const string ReplaceAudio = "Replace";

    /// <summary>Stored in <see cref="PluginSlot.InstrumentAudio"/> when the instrument's audio is added to the chain's audio.</summary>
    public const string AddAudio = "Add";

    /// <summary>The pin choices: stereo, mono sum, left, right, swap.</summary>
    public static readonly IReadOnlyList<WiringChoice> Pins = new[]
    {
        new WiringChoice("L+R stereo", PluginPins.Stereo), new WiringChoice("Mono sum (L+R)", PluginPins.Mono),
        new WiringChoice("Left only", PluginPins.Left), new WiringChoice("Right only", PluginPins.Right),
        new WiringChoice("Swap L/R", PluginPins.Swap),
    };

    /// <summary>The MIDI source choices: this track, another track, none.</summary>
    public static readonly IReadOnlyList<WiringChoice> MidiSources = new[]
    {
        new WiringChoice("This track's MIDI", PluginMidiIn.Own), new WiringChoice("Another track", PluginMidiIn.OtherTrack),
        new WiringChoice("None", PluginMidiIn.None),
    };

    /// <summary>The MIDI channel list: "All", then channels 1 to 16.</summary>
    public static readonly IReadOnlyList<string> Channels = new[] { "All" }.Concat(Enumerable.Range(1, LastChannelIndex).Select(i => i.ToString())).ToList();

    /// <summary>Instrument output modes: index 0 adds to the chain's audio, index 1 replaces it.</summary>
    public static readonly IReadOnlyList<string> InstrumentAudioModes = new[] { "Add to chain audio", "Replace chain audio" };

    /// <summary>The sidechain choices: "Not connected", then every other track.</summary>
    public static IReadOnlyList<WiringTrackChoice> SidechainTargets(IEnumerable<TrackModel> tracks, TrackModel track) =>
        new[] { new WiringTrackChoice("", Texts.NotConnected) }.Concat(OtherTracks(tracks, track)).ToList();

    /// <summary>The MIDI forward choices: "Off", then every other track.</summary>
    public static IReadOnlyList<WiringTrackChoice> MidiOutTargets(IEnumerable<TrackModel> tracks, TrackModel track) =>
        new[] { new WiringTrackChoice("", Texts.Off) }.Concat(OtherTracks(tracks, track)).ToList();

    /// <summary>The MIDI input track choices: every track except this one.</summary>
    public static IReadOnlyList<WiringTrackChoice> OtherTracks(IEnumerable<TrackModel> tracks, TrackModel track) =>
        tracks.Where(t => !ReferenceEquals(t, track)).Select(t => new WiringTrackChoice(t.Id.ToString("N"), t.Name)).ToList();

    /// <summary>
    /// Why the sidechain choice is disabled, or null when it can be used. Sidechain needs a track plug-in with a sidechain input
    /// bus: not a bus, not a VST3, not in its own process, and not an instrument.
    /// </summary>
    public static string? SidechainDisabledReason(TrackModel track, PluginSlot slot, bool separateProcessPerPlugin) =>
        track.IsBus ? Texts.SidechainBusReason
        : slot.Format == "VST3" || slot.Path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? Texts.SidechainVst3Reason
        : separateProcessPerPlugin ? Texts.SidechainSeparateProcessReason
        : slot.Type == PluginSlotType.Instrument ? Texts.SidechainInstrumentReason
        : null;

    /// <summary>The MIDI forward choice is for track plug-ins only.</summary>
    public static string? MidiOutDisabledReason(TrackModel track) => track.IsBus ? Texts.MidiOutBusReason : null;

    /// <summary>The track list under MIDI input is used only when the source is another track.</summary>
    public static bool MidiTrackEnabled(PluginMidiIn midiIn) => midiIn.Source == PluginMidiIn.OtherTrack;

    /// <summary>Only instruments have the instrument output row.</summary>
    public static bool ShowsInstrumentAudio(PluginSlot slot) => slot.Type == PluginSlotType.Instrument;

    /// <summary>The pin choice matching the plug-in's saved value, else the first.</summary>
    public static WiringChoice PinChoice(PluginSlot slot) => Pins.FirstOrDefault(p => p.Value == slot.Pins) ?? Pins[0];

    /// <summary>The source choice matching the saved value, else the first.</summary>
    public static WiringChoice SourceChoice(PluginMidiIn midiIn) => MidiSources.FirstOrDefault(s => s.Value == midiIn.Source) ?? MidiSources[0];

    /// <summary>The MIDI track choice for the saved track id, or null when that track is not in the list.</summary>
    public static WiringTrackChoice? MidiTrackChoice(IReadOnlyList<WiringTrackChoice> targets, PluginMidiIn midiIn) =>
        targets.FirstOrDefault(t => t.Id == midiIn.TrackId);

    /// <summary>The index in <see cref="Channels"/> for the saved channel (0 = All).</summary>
    public static int ChannelIndex(PluginMidiIn midiIn) => Math.Clamp(midiIn.Channel, 0, LastChannelIndex);

    /// <summary>The sidechain target matching the plug-in's saved track id, else the first.</summary>
    public static WiringTrackChoice SidechainChoice(IReadOnlyList<WiringTrackChoice> targets, PluginSlot slot) =>
        targets.FirstOrDefault(t => t.Id == (slot.SidechainTrackId ?? "")) ?? targets[0];

    /// <summary>The MIDI-forward target matching the plug-in's saved track id, else the first.</summary>
    public static WiringTrackChoice MidiOutChoice(IReadOnlyList<WiringTrackChoice> targets, PluginSlot slot) =>
        targets.FirstOrDefault(t => t.Id == (slot.MidiOutTrackId ?? "")) ?? targets[0];

    /// <summary>Index in <see cref="InstrumentAudioModes"/> for the saved mode.</summary>
    public static int InstrumentAudioIndex(PluginSlot slot) => slot.InstrumentAudio == ReplaceAudio ? 1 : 0;

    /// <summary>The stored value for an index in <see cref="InstrumentAudioModes"/>.</summary>
    public static string InstrumentAudioValue(int index) => index == 1 ? ReplaceAudio : AddAudio;

    /// <summary>The text of the refusal shown when a link would make a loop.</summary>
    public static string RefusalText(string targetLabel) => string.Format(Texts.RefusalFormat, targetLabel);

    /// <summary>
    /// True when linking this plug-in's sidechain (or its MIDI forward when <paramref name="sidechain"/> is false) to
    /// <paramref name="targetId"/> would form a loop. The check reads every track's links with this plug-in's edge being replaced
    /// left out, and its other link kept. An empty target (not connected, Off) never loops.
    /// </summary>
    public static bool WouldLoop(IEnumerable<TrackModel> tracks, TrackModel track, PluginSlot slot, bool sidechain, string targetId)
    {
        if (targetId.Length == 0) return false;
        var selfId = track.Id.ToString("N");
        var links = tracks.Select(t => LinkSnapshot(t, slot, sidechain)).ToList();
        return sidechain
            ? RoutingLinks.WouldCycle(links, targetId, selfId)
            : RoutingLinks.WouldCycle(links, selfId, targetId);
    }

    /// <summary>A light copy of a track carrying only its id and links; the edited plug-in's edge is blank in the copy.</summary>
    private static TrackModel LinkSnapshot(TrackModel track, PluginSlot edited, bool sidechain)
    {
        var rig = new RigPreset();
        foreach (var plugin in track.Rig.Plugins)
        {
            var isEdited = ReferenceEquals(plugin, edited);
            rig.Plugins.Add(new PluginSlot
            {
                SidechainTrackId = isEdited && sidechain ? "" : plugin.SidechainTrackId,
                MidiOutTrackId = isEdited && !sidechain ? "" : plugin.MidiOutTrackId,
            });
        }
        return new TrackModel { Id = track.Id, Rig = rig };
    }

    /// <summary>The window's fixed texts, used by the Avalonia windows.</summary>
    public static class Texts
    {
        /// <summary>Section label for the audio row.</summary>
        public const string SectionAudio = "Audio";
        /// <summary>Section label for the MIDI input rows.</summary>
        public const string SectionMidiInput = "MIDI input";
        /// <summary>Section label for the MIDI output rows.</summary>
        public const string SectionMidiOutput = "MIDI output";
        /// <summary>Row label for the plug-in input choice.</summary>
        public const string RowInput = "Input 1/2";
        /// <summary>Tip for the plug-in input choice.</summary>
        public const string RowInputTip = "How the track's stereo signal enters this plug-in";
        /// <summary>Row label for the plug-in output.</summary>
        public const string RowOutput = "Output";
        /// <summary>Output text: the stereo output continues along the chain.</summary>
        public const string OutputText = "Stereo → track";
        /// <summary>Tip for the plug-in output.</summary>
        public const string OutputTip = "The plug-in's stereo output continues along the chain";
        /// <summary>Row label for the sidechain choice.</summary>
        public const string RowSidechain = "Sidechain 3/4";
        /// <summary>Tip for the sidechain choice.</summary>
        public const string RowSidechainTip =
            "Another track's post-fader audio feeds this plug-in's inputs 3/4 (VST2 plug-ins with four or more inputs; the source track must play through the audio engine)";
        /// <summary>Row label for the MIDI source choice.</summary>
        public const string RowSource = "Source";
        /// <summary>Tip for the MIDI source choice.</summary>
        public const string RowSourceTip = "Where this plug-in takes its MIDI from";
        /// <summary>Row label for the MIDI track choice.</summary>
        public const string RowTrack = "Track";
        /// <summary>Tip for the MIDI track choice.</summary>
        public const string RowTrackTip = "The track whose MIDI notes this plug-in plays (stays linked if the track is renamed or moved)";
        /// <summary>Row label for the MIDI channel choice.</summary>
        public const string RowChannel = "Channel";
        /// <summary>Tip for the MIDI channel choice.</summary>
        public const string RowChannelTip = "Only this MIDI channel gets through";
        /// <summary>Label for passing incoming MIDI to the next plug-in.</summary>
        public const string PassMidiLabel = "Pass incoming MIDI to next plug-in";
        /// <summary>Tip for passing incoming MIDI on.</summary>
        public const string PassMidiTip = "The MIDI arriving here also continues to the next plug-in in the chain";
        /// <summary>Label for sending this plug-in's MIDI output on.</summary>
        public const string SendMidiLabel = "Send this plug-in's MIDI output to next plug-in";
        /// <summary>Tip for sending this plug-in's MIDI output on.</summary>
        public const string SendMidiTip = "Notes and messages this plug-in generates (e.g. a chord plug-in) go on to the next plug-in";
        /// <summary>Row label for the MIDI forward target.</summary>
        public const string RowForward = "Forward to";
        /// <summary>Tip for the MIDI forward target.</summary>
        public const string RowForwardTip =
            "Forward this plug-in's MIDI output (VST2) to another track's chain input (heard from the next audio block)";
        /// <summary>Row label for the instrument output mode.</summary>
        public const string RowInstrumentOutput = "Instrument output";
        /// <summary>Tip for the instrument output mode.</summary>
        public const string RowInstrumentOutputTip = "Instrument output: add to / replace the audio arriving from earlier plug-ins";
        /// <summary>Close button text.</summary>
        public const string Close = "Close";
        /// <summary>Choice text for no sidechain source.</summary>
        public const string NotConnected = "Not connected";
        /// <summary>Choice text for MIDI forwarding off.</summary>
        public const string Off = "Off";
        /// <summary>Reason: sidechain is for track plug-ins, not buses.</summary>
        public const string SidechainBusReason = "Sidechain is for track plug-ins (not group buses or the master)";
        /// <summary>Reason: no sidechain bus on VST3 plug-ins yet.</summary>
        public const string SidechainVst3Reason = "Not available for VST3 plug-ins (the VST3 bridge has no sidechain input bus yet)";
        /// <summary>Reason: sidechain unavailable with per-plug-in processes.</summary>
        public const string SidechainSeparateProcessReason = "Not available while plug-ins run in their own process (Settings > Audio & Plug-ins)";
        /// <summary>Reason: instruments take no audio input.</summary>
        public const string SidechainInstrumentReason = "Instruments take no audio input";
        /// <summary>Reason: MIDI forwarding is for track plug-ins.</summary>
        public const string MidiOutBusReason = "MIDI forwarding is for track plug-ins";
        /// <summary>Loop refusal dialog title.</summary>
        public const string RefusalTitle = "Wiring";
        /// <summary>Loop refusal message format (takes the target label).</summary>
        public const string RefusalFormat = "Connecting to \"{0}\" would make a loop (sidechain / MIDI links must not lead back to this track).";

        /// <summary>The window title: "Wiring: {plug-in name}".</summary>
        public static string Title(string slotName) => $"Wiring: {slotName}";
    }
}
