using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TabForge.AudioEngine.Plugins;

// Owns: the Reaper-only host API probe (audioMaster 0xDEADBEEF) the ReaPlugs make: a few functions with safe answers.
// Does not own: the host callback itself (Vst2Plugin.HostCallback).
internal static class Vst2ReaperHostApi
{
    /// <summary>audioMaster opcode and index of the probe; <c>ptr</c> is the function name, the answer a function pointer (0 = unknown).</summary>
    public const int Opcode = unchecked((int)0xDEADBEEF), Index = unchecked((int)0xDEADF00D);

    // Native strings the plug-in may keep a pointer to for its whole life: a static readonly reference to unmanaged text, never changed.
    private static readonly string Folder = EnsuredFolder();
    private static readonly IntPtr ResourcePath = Marshal.StringToCoTaskMemUTF8(Folder);
    private static readonly IntPtr IniFile = Marshal.StringToCoTaskMemUTF8(Path.Combine(Folder, "reaper.ini"));

    // The folder the plug-in is told to write into must exist; a folder that cannot be made is still named (the plug-in's write then fails on its own).
    private static string EnsuredFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabForge", "PluginData");
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return folder;
    }

    /// <summary>The function pointer for the function named at <paramref name="name"/> (ANSI), or 0 for a name TabForge does not provide.</summary>
    public static unsafe IntPtr Resolve(IntPtr name) => (name == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(name)) switch
    {
        "GetResourcePath" => (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr>)&GetResourcePath,
        "get_ini_file" => (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr>)&GetIniFile,
        "resolve_fn" => (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)&ResolveFn,
        _ => IntPtr.Zero,
    };

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetResourcePath() => ResourcePath;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr GetIniFile() => IniFile;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr ResolveFn(IntPtr name)
    {
        try { return Resolve(name); }
        catch { return IntPtr.Zero; } // never let an exception cross into native code
    }
}
