using System.Runtime.InteropServices;
using System.Text;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Plugins;

// Owns: the parameter list of a VST3 plug-in for the generic plug-in controls panel (tfv3_param_* of the shim): names, ranges, values with the plug-in's own text, and live changes.
// Does not own: the editor, processing and state (Vst3Plugin.cs), the panel (TabForge.Avalonia Views/Fx), the messages that carry the list (EngineSession.Plugins.cs).
// Tests: Vst3ParameterMapTests (the plain/normalized mapping); the native calls need tfvst3 and a real plug-in.
// Threads: main thread only. A change is queued to the processor by the shim's edit ring, so the audio thread never allocates or locks for it.
public sealed unsafe partial class Vst3Plugin : IPluginParameters
{
    private const int ParamNameBytes = 256, ParamUnitsBytes = 64, ParamTextBytes = 128, FlagReadOnly = 1, FlagHidden = 2, FlagProgramChange = 4;

    /// <summary>True when the loaded tfvst3 has the tfv3_param_* exports; an older one lists no parameters (probed once).</summary>
    private static readonly Lazy<bool> HasParamApi = new(() =>
    {
        if (!NativeLibrary.TryLoad(LibPath, out var lib)) return false;
        var found = NativeLibrary.TryGetExport(lib, "tfv3_param_count", out _) && NativeLibrary.TryGetExport(lib, "tfv3_param_info", out _)
            && NativeLibrary.TryGetExport(lib, "tfv3_param_text", out _) && NativeLibrary.TryGetExport(lib, "tfv3_param_set", out _);
        NativeLibrary.Free(lib);
        return found;
    });

    private Dictionary<uint, (int Steps, bool ReadOnly)>? _parameterIndex;

    public IReadOnlyList<PluginParameterInfo> GetParameters()
    {
        if (!IsLive || !HasParamApi.Value) return Array.Empty<PluginParameterInfo>();
        // Processor output (automation, meters) reaches the controller in the shim's idle call; it runs for the editor when one is open.
        if (!_editorOpen && HasIdleApi.Value && tfv3_editor_idle(_handle) is var restart and not 0) RestartRequested?.Invoke(restart);
        var count = Math.Min(tfv3_param_count(_handle), PluginParameterWire.MaxCount * 4);
        var list = new List<PluginParameterInfo>(Math.Min(count, PluginParameterWire.MaxCount));
        var index = new Dictionary<uint, (int, bool)>();
        var name = stackalloc byte[ParamNameBytes];
        var units = stackalloc byte[ParamUnitsBytes];
        var text = stackalloc byte[ParamTextBytes];
        for (var i = 0; i < count && list.Count < PluginParameterWire.MaxCount; i++)
        {
            uint id; int steps, flags; double def, value;
            if (tfv3_param_info(_handle, i, &id, name, units, &steps, &flags, &def, &value) == 0) continue;
            if ((flags & (FlagHidden | FlagProgramChange)) != 0) continue;
            var readOnly = (flags & FlagReadOnly) != 0;
            index[id] = (steps, readOnly);
            var shown = tfv3_param_text(_handle, id, value, text, ParamTextBytes) != 0 ? Utf8(text, ParamTextBytes) : "";
            var unit = Utf8(units, ParamUnitsBytes);
            if (unit.Length > 0 && shown.Length > 0 && !shown.EndsWith(unit, StringComparison.Ordinal)) shown += " " + unit;
            var (min, max) = Vst3ParameterMap.Range(steps);
            list.Add(new PluginParameterInfo(id, Utf8(name, ParamNameBytes), "", min, max, Vst3ParameterMap.ToPlain(def, steps), Vst3ParameterMap.ToPlain(value, steps), steps > 0, readOnly, shown));
        }
        _parameterIndex = index;
        return list;
    }

    public bool SetParameter(uint id, double value)
    {
        if (!IsLive || !HasParamApi.Value || !double.IsFinite(value)) return false;
        if (_parameterIndex is null) GetParameters();
        if (_parameterIndex is null || !_parameterIndex.TryGetValue(id, out var p) || p.ReadOnly) return false;
        return tfv3_param_set(_handle, id, Vst3ParameterMap.ToNormalized(value, p.Steps)) != 0;
    }

    private static string Utf8(byte* p, int max)
    {
        var length = new ReadOnlySpan<byte>(p, max).IndexOf((byte)0);
        return Encoding.UTF8.GetString(p, length < 0 ? max : length).Trim();
    }
}

/// <summary>The panel shows a stepped VST3 parameter as 0..steps (whole numbers) and a continuous one as its normalized 0..1; this converts both ways.</summary>
public static class Vst3ParameterMap
{
    public static (double Min, double Max) Range(int steps) => (0, steps > 0 ? steps : 1);

    public static double ToPlain(double normalized, int steps) => steps > 0 ? Math.Round(Math.Clamp(normalized, 0, 1) * steps) : Math.Clamp(normalized, 0, 1);

    public static double ToNormalized(double plain, int steps) => steps > 0 ? Math.Clamp(Math.Round(plain), 0, steps) / steps : Math.Clamp(plain, 0, 1);
}
