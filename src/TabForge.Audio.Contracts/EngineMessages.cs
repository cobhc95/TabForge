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

/// <summary><see cref="EngineCommand.SetLiveLimiter"/>: on.</summary>
public readonly record struct SetLiveLimiterMessage(bool On)
{
    public void Write(BinaryWriter w) => w.Write(On);
    public static SetLiveLimiterMessage Read(BinaryReader r) => new(r.ReadBoolean());
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

/// <summary><see cref="EngineCommand.SetArm"/>: slot, armed, input mode, monitor.</summary>
public readonly record struct SetArmMessage(int Slot, bool Armed, int Mode, bool Monitor)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Armed); w.Write(Mode); w.Write(Monitor); }
    public static SetArmMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadBoolean(), r.ReadInt32(), r.ReadBoolean());
}
