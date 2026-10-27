using System.IO;

namespace TabForge.Audio.Contracts;

// Owns: the description of one plug-in parameter and its wire form, shared by the engine (list reply to TabForge, and the plug-in host pipe) and TabForge (generic parameter panel).
// Does not own: any one format's parameter model (ClapPlugin) or the messages that carry the list (EngineCommand.GetParams / SetParam, EngineEvent.Params).
// Tests: ClapProtocolTests (wire round trip), ClapParameterTests.

/// <summary>One plug-in parameter: a stable id, display name, plain-value range, the current value and the plug-in's own text for it ("-6.0 dB"; empty when it gave none).</summary>
public sealed record PluginParameterInfo(uint Id, string Name, string Module, double Min, double Max, double Default, double Value, bool Stepped, bool ReadOnly, string Text = "");

/// <summary>The wire layout of a parameter list: count, then per parameter id, name, module, min, max, default, value, stepped, read-only, value text.</summary>
public static class PluginParameterWire
{
    /// <summary>The most parameters one list carries (the reply must stay under the frame limit).</summary>
    public const int MaxCount = 2048;
    private const int MaxNameChars = 128;

    public static void Write(BinaryWriter w, IReadOnlyList<PluginParameterInfo> list)
    {
        var count = Math.Min(list.Count, MaxCount);
        w.Write(count);
        for (var i = 0; i < count; i++)
        {
            var p = list[i];
            w.Write(p.Id); w.WriteString(Cut(p.Name)); w.WriteString(Cut(p.Module));
            w.Write(p.Min); w.Write(p.Max); w.Write(p.Default); w.Write(p.Value); w.Write(p.Stepped); w.Write(p.ReadOnly); w.WriteString(Cut(p.Text));
        }
    }

    public static List<PluginParameterInfo> Read(BinaryReader r)
    {
        var count = r.ReadInt32();
        if (count is < 0 or > MaxCount) throw new InvalidDataException("Bad parameter count.");
        var list = new List<PluginParameterInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var id = r.ReadUInt32(); var name = r.ReadBoundedString(MaxNameChars * 4); var module = r.ReadBoundedString(MaxNameChars * 4);
            list.Add(new PluginParameterInfo(id, name, module, r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadBoolean(), r.ReadBoolean(), r.ReadBoundedString(MaxNameChars * 4)));
        }
        return list;
    }

    private static string Cut(string text) => text.Length > MaxNameChars ? text[..MaxNameChars] : text;
}

/// <summary><see cref="EngineCommand.GetParams"/>: slot, chain index.</summary>
public readonly record struct GetParamsMessage(int Slot, int Index)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); }
    public static GetParamsMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());
}

/// <summary><see cref="EngineCommand.SetParam"/>: slot, chain index, parameter id, plain value.</summary>
public readonly record struct SetParamMessage(int Slot, int Index, uint ParamId, double Value)
{
    public void Write(BinaryWriter w) { w.Write(Slot); w.Write(Index); w.Write(ParamId); w.Write(Value); }
    public static SetParamMessage Read(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadUInt32(), r.ReadDouble());
}
