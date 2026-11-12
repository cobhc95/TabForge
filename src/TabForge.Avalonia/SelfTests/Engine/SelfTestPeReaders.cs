using System;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Audio.Contracts;
using TabForge.Plugins;

namespace TabForge;

// Owns: the pins for the two PE readers (PeImports: local import closure; PeExports: VST2 and VST3 entry points) on small synthetic PE32 and PE32+ files.
// Does not own: the plug-in trust decision (PluginDependencyTests) or the scan of real plug-ins.
// Tests: TestPeHeaderReaders.
public static partial class SelfTest
{
    private static void TestPeHeaderReaders()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-pepin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            string Write(string name, byte[] bytes) { var path = Path.Combine(folder, name); File.WriteAllBytes(path, bytes); return path; }
            var none = Array.Empty<string>();

            var vst2x64 = Write("v2x64.dll", SyntheticPe(true, new[] { "other", "VSTPluginMain" }, none, none));
            var vst2x86 = Write("v2x86.dll", SyntheticPe(false, new[] { "main" }, none, none));
            var vst3x64 = Write("v3x64.vst3", SyntheticPe(true, new[] { "GetPluginFactory" }, none, none));
            var vst3x86 = Write("v3x86.vst3", SyntheticPe(false, new[] { "GetPluginFactory" }, none, none));
            var plain = Write("plain.dll", SyntheticPe(true, new[] { "Something" }, none, none));
            var noExports = Write("none.dll", SyntheticPe(true, none, none, none));
            var truncated = Write("cut.dll", SyntheticPe(true, new[] { "VSTPluginMain" }, none, none).Take(300).ToArray());
            var garbage = Write("junk.dll", Enumerable.Range(0, 2000).Select(i => (byte)(i * 31)).ToArray());
            Check("exports: a 64-bit VSTPluginMain is a 64-bit VST2", PeExports.IsVst2Plugin(vst2x64) && PeExports.IsVst2Plugin64(vst2x64) && !PeExports.IsVst2Plugin32(vst2x64));
            Check("exports: a 32-bit main is a 32-bit VST2 only", PeExports.IsVst2Plugin32(vst2x86) && !PeExports.IsVst2Plugin64(vst2x86) && !PeExports.IsVst2Plugin(vst2x86));
            Check("exports: GetPluginFactory is a VST3 of its own bitness only", PeExports.IsVst3Plugin64(vst3x64) && !PeExports.IsVst3Plugin32(vst3x64) && PeExports.IsVst3Plugin32(vst3x86) && !PeExports.IsVst3Plugin64(vst3x86));
            Check("exports: a VST3 is no VST2 and a VST2 is no VST3", !PeExports.IsVst2Plugin64(vst3x64) && !PeExports.IsVst3Plugin64(vst2x64));
            Check("exports: another export name is no plug-in", !PeExports.IsVst2Plugin64(plain) && !PeExports.IsVst3Plugin64(plain));
            Check("exports: a PE with no export table is no plug-in", !PeExports.IsVst2Plugin64(noExports) && !PeExports.IsVst3Plugin64(noExports));
            Check("exports: a truncated PE is no plug-in", !PeExports.IsVst2Plugin64(truncated));
            Check("exports: a file that is not a PE is no plug-in", !PeExports.IsVst2Plugin64(garbage) && !PeExports.IsVst3Plugin64(garbage));
            Check("exports: a missing file is no plug-in", !PeExports.IsVst2Plugin64(Path.Combine(folder, "missing.dll")));

            var host = Write("host.dll", SyntheticPe(true, none, new[] { "KERNEL32.dll", "dep1.dll", "unrelated-system.dll" }, new[] { "dep2.dll" }));
            Write("dep1.dll", SyntheticPe(false, none, new[] { "dep3.dll" }, none));
            Write("dep2.dll", SyntheticPe(true, none, none, none));
            var dep3 = Write("dep3.dll", SyntheticPe(true, none, new[] { "host.dll" }, none));   // a cycle back to the host
            var loose = Write("loose.dll", SyntheticPe(true, none, none, none));
            Eq("imports: the closure is the imports and delay imports found in the folder, recursively, sorted",
                "dep1.dll,dep2.dll,dep3.dll,host.dll", string.Join(",", PeImports.LocalDependencies(host).Select(Path.GetFileName)));
            var hash = PeImports.DependencyHash(host);
            Check("imports: the dependency hash is stable", hash == PeImports.DependencyHash(host) && hash.Length == 64);
            File.AppendAllText(dep3, "x");
            Check("imports: the dependency hash follows a dependency's bytes", hash != PeImports.DependencyHash(host));
            Eq("imports: a PE with no local imports has an empty closure", 0, PeImports.LocalDependencies(loose).Count);
            Check("imports: an empty closure still has a non-empty hash", PeImports.DependencyHash(loose).Length == 64);
            Eq("imports: a file that is not a PE has an empty closure", 0, PeImports.LocalDependencies(garbage).Count);
            Eq("imports: a truncated PE has an empty closure", 0, PeImports.LocalDependencies(truncated).Count);
            Eq("imports: a missing file has an empty closure", 0, PeImports.LocalDependencies(Path.Combine(folder, "missing.dll")).Count);
        }
        finally { Directory.Delete(folder, true); }
    }

    /// <summary>A minimal PE32 or PE32+ image: one section at RVA 0x1000 holding an export table (names), an import table and a delay-import table.</summary>
    private static byte[] SyntheticPe(bool pe32Plus, string[] exports, string[] imports, string[] delayImports)
    {
        const int peAt = 0x80, sectionVa = 0x1000, sectionRaw = 0x400, sectionSize = 0x600;
        var file = new byte[sectionRaw + sectionSize];
        void U16(int at, int v) { file[at] = (byte)v; file[at + 1] = (byte)(v >> 8); }
        void U32(int at, long v) { for (var i = 0; i < 4; i++) file[at + i] = (byte)(v >> (8 * i)); }
        int Raw(int rva) => sectionRaw + (rva - sectionVa);
        file[0] = (byte)'M'; file[1] = (byte)'Z';
        U32(0x3C, peAt);
        U32(peAt, 0x00004550);
        U16(peAt + 4, pe32Plus ? 0x8664 : 0x14C);
        U16(peAt + 6, 1);
        var optionalSize = pe32Plus ? 240 : 224;
        U16(peAt + 20, optionalSize);
        var optional = peAt + 24;
        U16(optional, pe32Plus ? 0x20B : 0x10B);
        var directories = optional + (pe32Plus ? 112 : 96);
        var section = optional + optionalSize;
        Encoding.ASCII.GetBytes(".data").CopyTo(file, section);
        U32(section + 8, sectionSize); U32(section + 12, sectionVa); U32(section + 16, sectionSize); U32(section + 20, sectionRaw);
        var next = 0x1500;
        int Text(string s) { var rva = next; Encoding.ASCII.GetBytes(s).CopyTo(file, Raw(rva)); next += s.Length + 1; return rva; }
        if (exports.Length > 0)
        {
            U32(directories, 0x1000); U32(directories + 4, 0x100);
            U32(Raw(0x1000) + 24, exports.Length); U32(Raw(0x1000) + 32, 0x1100);
            for (var i = 0; i < exports.Length; i++) U32(Raw(0x1100) + 4 * i, Text(exports[i]));
        }
        if (imports.Length > 0)
        {
            U32(directories + 8, 0x1200); U32(directories + 12, 20 * (imports.Length + 1));
            for (var i = 0; i < imports.Length; i++) { var d = Raw(0x1200) + 20 * i; U32(d, 1); U32(d + 12, Text(imports[i])); U32(d + 16, 1); }
        }
        if (delayImports.Length > 0)
        {
            U32(directories + 13 * 8, 0x1300); U32(directories + 13 * 8 + 4, 32 * (delayImports.Length + 1));
            for (var i = 0; i < delayImports.Length; i++) { var d = Raw(0x1300) + 32 * i; U32(d, 1); U32(d + 4, Text(delayImports[i])); }
        }
        return file;
    }
}
