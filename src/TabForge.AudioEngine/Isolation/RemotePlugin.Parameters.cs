using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine.Isolation;

// Owns: the parameter list and setter of an isolated plug-in over the control pipe (additive messages CmdListParams / CmdSetParam, reply MsgParams).
// Does not own: the parameter model (IPluginParameters, ClapPlugin), the wire layout (PluginParameterWire), the pipe (PluginControlChannel).
// Tests: ClapProtocolTests (wire round trip).
public sealed partial class RemotePlugin : IPluginParameters
{
    /// <summary>The most parameters one reply carries (the reply must stay under the control frame limit).</summary>
    internal const int MaxWireParameters = PluginParameterWire.MaxCount;

    public IReadOnlyList<PluginParameterInfo> GetParameters()
    {
        var data = Ask(CmdListParams, null, MsgParams, 5000);
        return data is null ? Array.Empty<PluginParameterInfo>() : ReadParameters(data);
    }

    public bool SetParameter(uint id, double value)
    {
        var data = Ask(CmdSetParam, w => { w.Write(id); w.Write(value); }, MsgParams, 2000);
        return data is { Length: >= 4 } && BitConverter.ToInt32(data, 0) == 1;
    }

    // The wire layout of the parameter list: written in PluginHostMain, read here, nowhere else.
    internal static void WriteParameters(BinaryWriter w, IReadOnlyList<PluginParameterInfo> list) => PluginParameterWire.Write(w, list);

    internal static List<PluginParameterInfo> ReadParameters(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        return PluginParameterWire.Read(r);
    }
}
