using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.AudioEngine.Plugins.Clap;
using TabForge.Platform;
using TabForge.Plugins;

namespace TabForge;

// Owns: the CLAP real-plug-in self-test: loads .clap files installed in the system-wide CLAP folders (only those: nothing from a user-writable folder is executed
//   without an approval) and runs a few blocks through each. Reports "not available" as a pass with a note when none is installed.
// Does not own: the host logic (ClapHostTests, with a fake plug-in).

public static partial class SelfTest
{
    /// <summary>The .clap files in the administrator-only standard CLAP folders (at most <paramref name="max"/>).</summary>
    private static List<string> InstalledSystemClaps(int max)
    {
        var inputs = PluginScanInputs.Current();
        var files = new List<string>();
        foreach (var root in PluginScanFolders.Clap(inputs))
        {
            // User-writable roots (the home folder, the per-user Programs\Common folder) are not trusted to run unasked.
            if ((inputs.Home.Length > 0 && root.StartsWith(inputs.Home, StringComparison.OrdinalIgnoreCase))
                || (inputs.UserPluginRoot.Length > 0 && root.StartsWith(inputs.UserPluginRoot, StringComparison.OrdinalIgnoreCase)) || !Directory.Exists(root)) continue;
            try { files.AddRange(Directory.EnumerateFileSystemEntries(root, "*.clap", SearchOption.AllDirectories).Take(max)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Add($"  note  CLAP folder not readable: {root}: {ex.Message}"); }
        }
        return files.Take(max).ToList();
    }

    /// <summary>CLAP (cross-platform Stage 4): every .clap installed system-wide loads, processes finite audio, and saves and restores its state.</summary>
    private static void TestClapRealPlugin()
    {
        var files = InstalledSystemClaps(3);
        if (files.Count == 0)
        {
            Check("CLAP real plug-in: not available (no .clap installed in the system CLAP folders), nothing to check - pass with note", true);
            return;
        }
        var log = new TabForge.AudioEngine.EngineLogWriter();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            try
            {
                using var plugin = new ClapPlugin(file, wantInstrument: false, 48000, 256, log);
                var input = new[] { new float[256], new float[256] };
                var output = new[] { new float[256], new float[256] };
                var random = new Random(7);
                var finite = true;
                for (var block = 0; block < 8; block++)
                {
                    for (var i = 0; i < 256; i++) input[0][i] = input[1][i] = (float)(random.NextDouble() - 0.5) * 0.2f;
                    plugin.Process(input, output, 256, plugin.IsInstrument ? new[] { new TabForge.AudioEngine.Plugins.BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } } : Array.Empty<TabForge.AudioEngine.Plugins.BlockMidi>(),
                        new TabForge.AudioEngine.Plugins.TransportInfo { Tempo = 120, Playing = true });
                    finite &= output.All(channel => channel.All(float.IsFinite));
                }
                Check($"CLAP real plug-in {name}: loads and 8 blocks come out finite", finite);
                var state = plugin.GetState();
                if (state is { Length: > 0 }) { plugin.SetState(state); Check($"CLAP real plug-in {name}: its saved state is accepted back ({state.Length} bytes)", true); }
                else Check($"CLAP real plug-in {name}: no saved state to round-trip (pass with note)", true);
            }
            catch (Exception ex)
            {
                Check($"CLAP real plug-in {name}: loads and processes", false, ex.GetBaseException().Message);
            }
        }
    }
}
