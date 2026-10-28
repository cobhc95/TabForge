using System.Runtime.InteropServices;

namespace TabForge.AudioEngine.Plugins;

// Owns: the native entry points of the VST3 shim's parameter exports (tfv3_param_*; the shim is tfvst3.dll / libtfvst3.so, same names).
// Does not own: how they are used (Vst3Plugin.Parameters.cs). An older shim lacks them (see HasParamApi).
public sealed unsafe partial class Vst3Plugin
{
    [DllImport(Lib)] private static extern int tfv3_param_count(IntPtr p);
    [DllImport(Lib)] private static extern int tfv3_param_info(IntPtr p, int index, uint* id, byte* name, byte* units, int* stepCount, int* flags, double* defaultNormalized, double* valueNormalized);
    [DllImport(Lib)] private static extern int tfv3_param_text(IntPtr p, uint id, double normalized, byte* text, int capacity);
    [DllImport(Lib)] private static extern int tfv3_param_set(IntPtr p, uint id, double normalized);
}
