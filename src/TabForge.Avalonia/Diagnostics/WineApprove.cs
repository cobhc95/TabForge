// Owns: the Linux package's headless approve gate (--wine-approve): the packaged app approves
// each named Windows plug-in (same trust record as Approve all) and identifies it through the real
// bridged probe path with the bridged ceiling, sweeping stale shm and preparing the prefix once.
// Does not own: the probe (PluginCatalog), trust (PluginTrust) or first-run setup (WineRuntime).
// Tests: wine-stage9.yml runs it from the extracted tar.xz on a clean runner.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Isolation;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Diagnostics;

internal static class WineApprove
{
    public const string Argument = "--wine-approve";

    public static async Task<int> RunAsync(string[] args)
    {
        // --wine-approve <out-dir> <plug-in> [<plug-in> ...]
        if (args.Length < 3) return Usage();
        var outDir = args[1];
        var paths = args[2..].Select(Path.GetFullPath).ToList();
        try { Directory.CreateDirectory(outDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Fail(ex.Message); }
        try
        {
            foreach (var path in paths)
                if (!File.Exists(path) && !Directory.Exists(path)) return Fail($"not found: {path}");
            Console.Out.WriteLine("wine-approve: sweeping stale tf-wine-*.shm");
            var swept = WineRuntime.SweepStaleBlocks();
            Console.Out.WriteLine($"wine-approve: swept {swept} stale block(s)");
            var runtime = WineRuntime.Locate();
            if (runtime is null) return Fail($"Wine bridge missing: {WineBridgeSupport.WineExecutable() ?? "wine"}");
            Console.Out.WriteLine("wine-approve: preparing Wine prefix (first run takes a while)…");
            runtime.EnsureReady();
            Console.Out.WriteLine("wine-approve: prefix ready");
            var settings = new PluginSettings();
            var trust = new PluginTrust();
            IPluginFileTrust files = new PortablePluginFileTrust();
            var infos = paths.Select(p => new VstPluginInfo(Path.GetFileNameWithoutExtension(p.TrimEnd('\\', '/')), p,
                PluginFormats.OfPlugin(p), "", "")).ToList();
            var steps = new List<PluginApprovalStep>();
            var done = await PluginApprovalPresenter.RunAsync(infos, settings, trust, files, steps.Add, CancellationToken.None);
            var lines = new List<string>();
            var failed = 0;
            foreach (var step in steps)
            {
                var line = $"wine-approve step done={step.Done}/{step.Total} path={step.Plugin.Path} role={step.Role} vendor={step.Vendor} identified={step.Identified}" +
                    (step.Refusal is { Length: > 0 } r ? $" refusal={r}" : "");
                Console.Out.WriteLine(line);
                lines.Add(line);
                if (!step.Identified) failed++;
            }
            File.WriteAllLines(Path.Combine(outDir, "wine-approve.log"), lines);
            Console.Out.WriteLine(PluginApprovalPresenter.DoneText(done, infos.Count, failed));
            if (failed > 0) return Fail($"{failed} plug-in(s) could not be identified (see refusal lines above)");
            Console.Out.WriteLine("wine-approve ok");
            return 0;
        }
        catch (Exception ex)
        {
            return Fail(ex.GetBaseException().Message);
        }
    }

    private sealed class PortablePluginFileTrust : IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => fullPath;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: TabForge --wine-approve <out-dir> <plug-in> [<plug-in> ...]");
        return 2;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"wine-approve FAILED: {message}");
        return 1;
    }
}
