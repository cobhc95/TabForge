using System.Text;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Plugins;

// Owns: the parameter list of a VST2 plug-in for the generic plug-in controls panel: names, 0..1 values with the plug-in's own display text and label, and live changes.
// Does not own: the saved parameter list state (Vst2Plugin.ParameterState.cs), the editor, processing, the panel.
// Tests: none (needs a real VST2 plug-in).
// Threads: main thread only; setParameter is the call VST2 allows from any thread while the plug-in processes.
public sealed unsafe partial class Vst2Plugin : IPluginParameters
{
    private const int EffGetParamLabel = 6, EffGetParamDisplay = 7, EffGetParamName = 8, ParamTextBytes = 256;

    public IReadOnlyList<PluginParameterInfo> GetParameters()
    {
        if (_disposed || _effect->getParameter == IntPtr.Zero) return Array.Empty<PluginParameterInfo>();
        var count = Math.Min(ParameterCount, PluginParameterWire.MaxCount);
        var list = new List<PluginParameterInfo>(count);
        var buffer = stackalloc byte[ParamTextBytes];
        string Ask(int opcode, int index)
        {
            new Span<byte>(buffer, ParamTextBytes).Clear();
            Dispatch(opcode, index, IntPtr.Zero, (IntPtr)buffer, 0);
            var length = new ReadOnlySpan<byte>(buffer, ParamTextBytes - 1).IndexOf((byte)0);
            return Encoding.Latin1.GetString(buffer, length < 0 ? ParamTextBytes - 1 : length).Trim();
        }
        for (var i = 0; i < count; i++)
        {
            var value = GetParameter(i);
            var shown = Ask(EffGetParamDisplay, i);
            var label = Ask(EffGetParamLabel, i);
            if (label.Length > 0 && shown.Length > 0 && !shown.EndsWith(label, StringComparison.Ordinal)) shown += " " + label;
            var name = Ask(EffGetParamName, i);
            list.Add(new PluginParameterInfo((uint)i, name.Length > 0 ? name : $"Parameter {i + 1}", "", 0, 1, 0, Math.Clamp(float.IsFinite(value) ? value : 0, 0, 1), false, false, shown));
        }
        return list;
    }

    public bool SetParameter(uint id, double value)
    {
        if (_disposed || _effect->setParameter == IntPtr.Zero || id >= (uint)ParameterCount || !double.IsFinite(value)) return false;
        ((delegate* unmanaged[Cdecl]<AEffect*, int, float, void>)_effect->setParameter)(_effect, (int)id, (float)Math.Clamp(value, 0, 1));
        return true;
    }
}
