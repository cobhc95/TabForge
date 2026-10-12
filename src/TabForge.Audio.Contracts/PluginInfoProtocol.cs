using System.IO;

namespace TabForge.Audio.Contracts;

/// <summary><see cref="EngineCommand.GetPluginInfo"/>: slot, request id.</summary>
public readonly record struct GetPluginInfoMessage(int Slot, int RequestId)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(RequestId); }
    public static GetPluginInfoMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());
}

/// <summary>
/// What a loaded plug-in reports about itself (the part of an <see cref="EngineEvent.PluginInfo"/> frame after slot, request id,
/// chain index and chain count). <paramref name="Format"/>: <see cref="None"/> when the plug-in is not loaded in the engine process
/// (failed, skipped, or running in its own process), else <see cref="Vst2"/> or <see cref="Vst3"/>.
/// VST2: <paramref name="UniqueId"/> and the declared pin counts. VST3: <paramref name="ClassId"/> (32 hex digits, the class id as
/// the plug-in's maker writes it) and -1 for both pin counts (the VST3 host interface does not report them).
/// </summary>
public sealed record LoadedPluginInfo(byte Format, uint UniqueId, string ClassId, int Inputs, int Outputs, bool UsesChunks, string Vendor, string Name)
{
    public const byte None = 0, Vst2 = 1, Vst3 = 2;
    public static readonly LoadedPluginInfo Unavailable = new(None, 0, "", -1, -1, false, "", "");

    /// <summary>The VST2 unique id as its four characters (most significant byte first); bytes outside printable ASCII become '?'.</summary>
    public string FourChars => string.Create(4, UniqueId, (span, id) =>
    {
        for (var i = 0; i < 4; i++) { var c = (char)((id >> (24 - 8 * i)) & 0xFF); span[i] = c is >= ' ' and <= '~' ? c : '?'; }
    });

    public void Write(BinaryWriter w)
    {
        w.Write(Format); w.Write(UniqueId); w.WriteString(ClassId); w.Write(Inputs); w.Write(Outputs); w.Write(UsesChunks);
        w.WriteString(Vendor); w.WriteString(Name);
    }

    public static LoadedPluginInfo Read(BinaryReader r) =>
        new(r.ReadByte(), r.ReadUInt32(), r.ReadBoundedString(64), r.ReadInt32(), r.ReadInt32(), r.ReadBoolean(), r.ReadBoundedString(256), r.ReadBoundedString(256));
}
