using System.Runtime.InteropServices;

namespace TabForge.AudioEngine.Plugins;

// Owns: the Windows module-load call of the VST2 host (LoadLibraryEx). VST2 stays Windows-only (cross-platform decision 6).
public sealed unsafe partial class Vst2Plugin
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadLibraryExW")]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);
}
