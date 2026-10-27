using System.Runtime.InteropServices;
using System.Text;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Plugins;

// Owns: what a loaded plug-in reports about itself for the chain exchange file (LoadedPluginInfo): VST2 from the instance,
//       VST3 class id, name and vendor from the module's class factory.
// Does not own: loading or trust (the plug-in is already loaded: PluginLoading), pin counts of a VST3 plug-in (the VST3 host
//       interface does not report them), anything on the audio thread.
// Tests: none dedicated(the wire record); the factory read is checked against real plug-ins by a probe outside the suite.
public static unsafe class Vst3ClassInfo
{
    /// <summary>Main thread, while <paramref name="plugin"/> is alive. Anything but a VST2 instance or a live VST3 instance of this process: unavailable (the module is never loaded fresh here).</summary>
    public static LoadedPluginInfo Describe(IPluginInstance plugin) => plugin switch
    {
        Vst2Plugin v => new(LoadedPluginInfo.Vst2, unchecked((uint)v.UniqueId), "", v.DeclaredInputs, v.DeclaredOutputs, v.UsesChunks, v.Vendor, v.ProductName),
        Vst3Plugin v when v.IsLive && Read(v.Path, v.ClassIndex) is { } c => new(LoadedPluginInfo.Vst3, 0, c.ClassId, -1, -1, true, c.Vendor, c.Name),
        Clap.ClapPlugin c => new(LoadedPluginInfo.Clap, 0, c.Descriptor.Id, -1, -1, true, c.Vendor, c.ProductName),
        _ => LoadedPluginInfo.Unavailable,
    };

    /// <summary>
    /// The class id (32 hex digits), name and vendor of the file's <paramref name="classIndex"/>-th audio class, in the order the
    /// VST3 host lists them. Only for a module this process already holds loaded and initialised (a live instance exists): the
    /// library handle taken here is a second reference to it and is given back. Null when the factory does not answer.
    /// </summary>
    public static (string ClassId, string Name, string Vendor)? Read(string path, int classIndex)
    {
        var file = Directory.Exists(path) ? System.IO.Path.Combine(path, "Contents", "x86_64-win", System.IO.Path.GetFileName(path)) : path;
        if (!NativeLibrary.TryLoad(file, out var library)) return null;
        try
        {
            if (!NativeLibrary.TryGetExport(library, "GetPluginFactory", out var export)) return null;
            var factory = ((delegate* unmanaged[Stdcall]<IntPtr>)export)();
            if (factory == IntPtr.Zero) return null;
            // IPluginFactory: queryInterface, addRef, release, getFactoryInfo, countClasses, getClassInfo.
            var table = *(IntPtr**)factory;
            try
            {
                var factoryInfo = stackalloc byte[452];   // PFactoryInfo: vendor[64], url[256], email[128], flags
                var vendor = ((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)table[3])(factory, factoryInfo) == 0 ? Text(factoryInfo, 64) : "";
                var count = Math.Clamp(((delegate* unmanaged[Stdcall]<IntPtr, int>)table[4])(factory), 0, 4096);
                var info = stackalloc byte[116];          // PClassInfo: cid[16], cardinality, category[32], name[64]
                var audio = 0;
                for (var i = 0; i < count; i++)
                {
                    if (((delegate* unmanaged[Stdcall]<IntPtr, int, byte*, int>)table[5])(factory, i, info) != 0) continue;
                    if (Text(info + 20, 32) != "Audio Module Class") continue;
                    if (audio++ == classIndex) return (ClassIdText(new ReadOnlySpan<byte>(info, 16)), Text(info + 52, 64), vendor);
                }
                return null;
            }
            finally { ((delegate* unmanaged[Stdcall]<IntPtr, uint>)table[2])(factory); }
        }
        finally { NativeLibrary.Free(library); }
    }

    /// <summary>The 16 id bytes as their maker writes them: on Windows the first three groups (4, 2, 2 bytes) are stored low byte first.</summary>
    public static string ClassIdText(ReadOnlySpan<byte> id)
    {
        ReadOnlySpan<int> order = stackalloc int[] { 3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15 };
        var text = new StringBuilder(32);
        foreach (var i in order) text.Append(id[i].ToString("X2"));
        return text.ToString();
    }

    private static string Text(byte* p, int max)
    {
        var n = 0;
        while (n < max && p[n] != 0) n++;
        return Encoding.UTF8.GetString(p, n).Trim();
    }
}
