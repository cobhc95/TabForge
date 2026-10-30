using System.IO;

namespace TabForge.Audio.Contracts;

// Owns: the wire layout of the fixed-shape engine commands, one record per command; each Write / Read pair is the ONLY place that field order is spelled out.
// Does not own: validation (the engine bounds and range-checks what Read returns), framing (Frames), lists and tails (EngineMessageLists.cs), the commands that already
// have a pair (Configure: EngineConfig, RenderOffline: RenderSpec, LoadChain / ChainState / ChainCommit: ChainLoadProtocol).
// Tests: TestEngineMessagesRoundTrip, TestEngineMessagesGolden.
// Record structs: a message costs no allocation beyond the writer lambda the callers already use. Reads return the raw values.

/// <summary><see cref="EngineCommand.Ping"/>: sequence number.</summary>
public readonly record struct PingMessage(int Seq)
{
    public void Write(BinaryWriter w) => w.Write(Seq);
    public static PingMessage Read(BinaryReader r) => new(r.ReadInt32());
}

/// <summary><see cref="EngineCommand.TestHang"/>: seconds.</summary>
public readonly record struct TestHangMessage(int Seconds)
{
    public void Write(BinaryWriter w) => w.Write(Seconds);
    public static TestHangMessage Read(BinaryReader r) => new(r.ReadInt32());
}

/// <summary><see cref="EngineCommand.SetMasterTap"/>: on.</summary>
public readonly record struct SetMasterTapMessage(bool On)
{
    public void Write(BinaryWriter w) => w.Write(On);
    public static SetMasterTapMessage Read(BinaryReader r) => new(r.ReadBoolean());
}

/// <summary><see cref="EngineCommand.SetInputGain"/>: slot, gain in dB.</summary>
public readonly record struct SetInputGainMessage(int Slot, float Db)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Db); }
    public static SetInputGainMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadSingle());
}

/// <summary><see cref="EngineCommand.SetTunerWindow"/>: window in ms.</summary>
public readonly record struct SetTunerWindowMessage(int Ms)
{
    public void Write(BinaryWriter w) => w.Write(Ms);
    public static SetTunerWindowMessage Read(BinaryReader r) => new(r.ReadInt32());
}

/// <summary><see cref="EngineCommand.SetLiveLimiter"/>: on.</summary>
public readonly record struct SetLiveLimiterMessage(bool On)
{
    public void Write(BinaryWriter w) => w.Write(On);
    public static SetLiveLimiterMessage Read(BinaryReader r) => new(r.ReadBoolean());
}

/// <summary><see cref="EngineCommand.SetSoundBank"/>: the SoundFont 2 file path (empty: Windows GM; "None": silence, the synths render nothing).</summary>
public readonly record struct SetSoundBankMessage(string Path)
{
    public void Write(BinaryWriter w) => w.Write(Path);
    public static SetSoundBankMessage Read(BinaryReader r) => new(r.ReadBoundedString(1024));
}

/// <summary><see cref="EngineCommand.SetWindowsPathOffset"/>: dB as a float32.</summary>
public readonly record struct SetWindowsPathOffsetMessage(float Db)
{
    public void Write(BinaryWriter w) => w.Write(Db);
    public static SetWindowsPathOffsetMessage Read(BinaryReader r) => new(r.ReadSingle());
}

/// <summary><see cref="EngineCommand.RemoveTrack"/>: slot.</summary>
public readonly record struct RemoveTrackMessage(int Slot)
{
    public void Write(BinaryWriter w) => w.Write(Slot);
    public static RemoveTrackMessage Read(BinaryReader r) => new(r.ReadInt32());
}

/// <summary><see cref="EngineCommand.GetStates"/>: slot, request id.</summary>
public readonly record struct GetStatesMessage(int Slot, int RequestId)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(RequestId); }
    public static GetStatesMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());
}

/// <summary><see cref="EngineCommand.CloseEditor"/>: slot, chain index.</summary>
public readonly record struct CloseEditorMessage(int Slot, int Index)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); }
    public static CloseEditorMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());
}

/// <summary><see cref="EngineCommand.GetPrograms"/>: slot, chain index.</summary>
public readonly record struct GetProgramsMessage(int Slot, int Index)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); }
    public static GetProgramsMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());
}

/// <summary><see cref="EngineCommand.SetProgram"/>: slot, chain index, program.</summary>
public readonly record struct SetProgramMessage(int Slot, int Index, int Program)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); w.Write(Program); }
    public static SetProgramMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
}

/// <summary><see cref="EngineCommand.SetState"/>: slot, chain index, base64 state (bounded by <see cref="PluginStateLimits.MaxBase64Chars"/>).</summary>
public readonly record struct SetStateMessage(int Slot, int Index, string State)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); w.WriteString(State); }
    public static SetStateMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadBoundedString(PluginStateLimits.MaxBase64Chars));
}

/// <summary><see cref="EngineCommand.OpenEditor"/>: slot, chain index, window handle (int64), dark, docked, on top.</summary>
public readonly record struct OpenEditorMessage(int Slot, int Index, long Window, bool Dark, bool Docked, bool OnTop)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); w.Write(Window); w.Write(Dark); w.Write(Docked); w.Write(OnTop); }
    public static OpenEditorMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt64(), r.ReadBoolean(), r.ReadBoolean(), r.ReadBoolean());
}

/// <summary><see cref="EngineCommand.SetTrackMix"/>: slot, volume 0..127, pan 0..127.</summary>
public readonly record struct SetTrackMixMessage(int Slot, int Volume, int Pan)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Volume); w.Write(Pan); }
    public static SetTrackMixMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
}

/// <summary><see cref="EngineCommand.SetPluginGain"/>: slot, chain index, dB (double).</summary>
public readonly record struct SetPluginGainMessage(int Slot, int Index, double Db)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); w.Write(Db); }
    public static SetPluginGainMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadDouble());
}

/// <summary><see cref="EngineCommand.SetPluginBypass"/>: slot, chain index, enabled.</summary>
public readonly record struct SetPluginBypassMessage(int Slot, int Index, bool Enabled)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); w.Write(Enabled); }
    public static SetPluginBypassMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadBoolean());
}

/// <summary><see cref="EngineCommand.SetSynth"/>: slot, on.</summary>
public readonly record struct SetSynthMessage(int Slot, bool On)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(On); }
    public static SetSynthMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadBoolean());
}

/// <summary><see cref="EngineCommand.SetMidiRoute"/>: slot, chain index, source slot (-1 own, -2 none), channel mask.</summary>
public readonly record struct SetMidiRouteMessage(int Slot, int Index, int Source, int Mask)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); w.Write(Source); w.Write(Mask); }
    public static SetMidiRouteMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
}

/// <summary><see cref="EngineCommand.SetPluginWiring"/>: slot, chain index, flags.</summary>
public readonly record struct SetPluginWiringMessage(int Slot, int Index, int Flags)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); w.Write(Flags); }
    public static SetPluginWiringMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
}

/// <summary><see cref="EngineCommand.SetMidiLogWatch"/>: slot, watching.</summary>
public readonly record struct SetMidiLogWatchMessage(int Slot, bool Watch)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Watch); }
    public static SetMidiLogWatchMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadBoolean());
}

/// <summary>
/// The input a track records and monitors, as the one integer <see cref="SetArmMessage.Mode"/> carries: 0 = input 1 (mono),
/// 1 = input 2 (mono), 2 = inputs 1+2 (stereo); from 3 on, <c>mode - 3</c> holds the first channel (zero-based) in its upper
/// bits and "stereo pair with the next channel" in bit 0. Channels 1, 2 and the pair 1+2 always encode as 0, 1, 2.
/// </summary>
public static class AudioInputAddress
{
    /// <summary>The most input channels a device is opened with.</summary>
    public const int MaxChannels = 64;
    public const int MaxMode = 3 + (MaxChannels - 1) * 2 + 1;

    /// <param name="firstChannel">Zero-based channel (the left one of a pair).</param>
    public static int Encode(int firstChannel, bool stereo) =>
        firstChannel == 0 ? (stereo ? 2 : 0) : firstChannel == 1 && !stereo ? 1 : 3 + firstChannel * 2 + (stereo ? 1 : 0);

    /// <summary>The zero-based channels a mode reads (left and right are the same channel for a mono input). Any integer decodes to a channel inside 0..<see cref="MaxChannels"/>.</summary>
    public static (int Left, int Right) Decode(int mode)
    {
        if (mode <= 0) return (0, 0);
        if (mode == 1) return (1, 1);
        if (mode == 2) return (0, 1);
        var first = Math.Min((mode - 3) >> 1, MaxChannels - 1);
        return (first, ((mode - 3) & 1) == 1 ? first + 1 : first);
    }
}

/// <summary><see cref="EngineCommand.SetArm"/>: slot, armed, input mode (<see cref="AudioInputAddress"/>), monitor.</summary>
public readonly record struct SetArmMessage(int Slot, bool Armed, int Mode, bool Monitor)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Armed); w.Write(Mode); w.Write(Monitor); }
    public static SetArmMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadBoolean(), r.ReadInt32(), r.ReadBoolean());
}

/// <summary><see cref="EngineCommand.ProbeAsio"/>: request id, driver name (empty: the first installed).</summary>
public readonly record struct ProbeAsioMessage(int RequestId, string Driver)
{
    public void Write(BinaryWriter w) { w.Write(RequestId); w.Write(Driver); }
    public static ProbeAsioMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadBoundedString(256));
}

/// <summary><see cref="EngineEvent.AsioPanelShown"/>: request id, why the control panel could not be shown (empty: it was shown).</summary>
public readonly record struct AsioPanelShownMessage(int RequestId, string Error)
{
    public void Write(BinaryWriter w) { w.Write(RequestId); w.Write(Error); }
    public static AsioPanelShownMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadBoundedString(512));
}

/// <summary><see cref="EngineEvent.AsioProbed"/>: request id, preferred buffer size (0: unknown), the accepted rates of <see cref="AudioDriverNames.SampleRates"/>, then the driver's input and output channel names (none: unknown).</summary>
public readonly record struct AsioProbedMessage(int RequestId, int PreferredBuffer, IReadOnlyList<int> Rates, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs)
{
    public void Write(BinaryWriter w)
    {
        w.Write(RequestId); w.Write(PreferredBuffer); w.Write(Rates.Count); foreach (var rate in Rates) w.Write(rate);
        AsioChannelNames.Write(w, Inputs); AsioChannelNames.Write(w, Outputs);
    }

    public static AsioProbedMessage Read(BinaryReader r)
    {
        var id = r.ReadInt32(); var buffer = r.ReadInt32();
        var rates = new List<int>();
        for (var i = Math.Clamp(r.ReadInt32(), 0, AudioDriverNames.SampleRates.Count); i > 0; i--) rates.Add(r.ReadInt32());
        if (r.BaseStream.Position >= r.BaseStream.Length) return new(id, buffer, rates, Array.Empty<string>(), Array.Empty<string>());   // an engine from before the names: unknown
        return new(id, buffer, rates, AsioChannelNames.Read(r), AsioChannelNames.Read(r));
    }
}

/// <summary>An ASIO driver's channel names on the wire (the probe answer and the Ready event): a count, then that many names. The writer keeps to <see cref="Max"/> names of <see cref="MaxNameChars"/> characters and the reader accepts exactly that, so the two always agree on how many bytes belong to the list.</summary>
public static class AsioChannelNames
{
    /// <summary>The most names per direction (the settings allow channels 0 to 62).</summary>
    public const int Max = 128;

    /// <summary>The longest name; longer ones are cut by the writer.</summary>
    public const int MaxNameChars = 128;

    public static void Write(BinaryWriter w, IReadOnlyList<string> names)
    {
        var count = Math.Min(names.Count, Max);
        w.Write(count);
        for (var i = 0; i < count; i++) { var name = names[i] ?? ""; w.Write(name.Length > MaxNameChars ? name[..MaxNameChars] : name); }
    }

    public static string[] Read(BinaryReader r)
    {
        var names = new string[Math.Clamp(r.ReadInt32(), 0, Max)];
        for (var i = 0; i < names.Length; i++) names[i] = r.ReadBoundedString(MaxNameChars);
        return names;
    }
}
