using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Plugins.Clap;
using TabForge.Plugins;

namespace TabForge.AudioEngine.Isolation;

// Owns: the --plugin-info probe loading one approved plug-in in a throwaway process (fail-closed hash check), and the
// bridge hand-off when that file is a bridged VST2 plug-in or a 32-bit-only VST3 on Windows.

/// <summary>
/// `TabForge.exe --plugin-info &lt;path&gt; &lt;approved sha256&gt;`: loads one plug-in in a throwaway process and prints
/// "role|vendor|name" (role = Instrument or Effect). A plug-in that crashes here only ends this process.
/// Like the plug-in scan, this tells instruments from effects without trusting file names. This executes the plug-in, so it
/// only ever runs for an approved file: the hash is required (fail closed) and is checked through the shared
/// <see cref="TabForge.Audio.Contracts.PluginIdentity"/> rule before the file is loaded. Exit 5 = refused (missing or changed).
/// A 32-bit VST2 file is identified by the native <c>tfvst32.exe --probe</c> instead, and a 64-bit Windows VST2 on Linux by
/// <c>tfvst64.exe --probe</c>, started from this throwaway wrapper in a kill-on-close job so the app's timeout cannot leave
/// an orphaned helper.
/// </summary>
public static class PluginInfoProbe
{
    /// <summary>The wrapper's own bound on the helper on Windows; on Linux the bridged ceiling applies (first Wine setup takes minutes).</summary>
    internal const int BridgeProbeTimeoutMs = 15000;

    /// <summary>The wrapper's bound for the current OS (Linux bridged probes get the long ceiling).</summary>
    internal static int BridgeTimeoutForCurrentOs => OperatingSystem.IsLinux() ? PluginCatalog.BridgedProbeTimeoutMs : BridgeProbeTimeoutMs;

    /// <param name="create">Test seam only: replaces the real load (a counter proves an unapproved file is never executed).</param>
    public static int Run(string[] args, Func<string, IPluginInstance>? create = null)
    {
        if (args.Length < 2 || !File.Exists(args[1]) && !Directory.Exists(args[1])) return 2;
        var path = args[1];
        var approvedSha256 = args.Length > 2 ? args[2] : "";
        var approvedDependencies = args.Length > 3 && args[3] != "none" ? args[3] : "";
        if (UsesBridgeHelper(path)) return RunBridged(Path.GetFullPath(path), approvedSha256, approvedDependencies);
        try
        {
            // VST3: prefer the bundle's instrument class (a bundle with one reports it); VST2 reports its own role.
            using var plugin = PluginFactory.CreateVerified(path, "", isInstrument: true, 48000, 256, approvedSha256, locationTrusted: false,
                new EngineThreads(new EngineLogWriter()), create, approvedDependencies);
            var role = plugin.IsInstrument ? "Instrument" : "Effect";
            Console.Out.WriteLine(plugin switch
            {
                Vst2Plugin vst2 => $"{role}|{Clean(vst2.Vendor)}|{Clean(vst2.ProductName)}",
                ClapPlugin clap => $"{role}|{Clean(clap.Vendor)}|{Clean(clap.ProductName)}",   // CLAP names itself in its descriptor
                _ => $"{role}||",
            });
            Console.Out.Flush();
            return 0;
        }
        catch (TabForge.Audio.Contracts.PluginChangedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 5;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetBaseException().Message);
            return 1;
        }
    }

    /// <summary>True when the identify probe must hand this file to a bridge helper rather than load it here.
    /// On Linux every Windows-only VST3 is a Wine job, exactly like the engine's chain route; without this the
    /// approval probe tried to load a Windows PE in-process and refused a plug-in the engine can run.</summary>
    internal static bool UsesBridgeHelper(string path) =>
        (OperatingSystem.IsLinux() && path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) &&
         Vst3Windows.IsWindowsOnly(path) && (WineBridgeSupport.Available64 || WineBridgeSupport.Available)) ||
        UsesVst32Helper(path) || UsesVst64Helper(path);

    /// <summary>True for the x86 bridge: 32-bit VST2 (Windows, or Linux with the x86 helper shipped) or a 32-bit-only VST3.</summary>
    internal static bool UsesVst32Helper(string path) =>
        UsesVst32Helper(path, OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), WineBridgeSupport.Available);

    /// <summary>Testable form of <see cref="UsesVst32Helper(string)"/>: the OS and helper availability are explicit.</summary>
    internal static bool UsesVst32Helper(string path, bool windows, bool linux, bool x86Helper) =>
        (PeExports.IsVst2Plugin32(path) ||
         ((windows || linux) && Vst3Windows.IsWindowsOnly(path) && Vst3Windows.Is32BitOnly(path))) &&
        (windows || (linux && x86Helper));

    /// <summary>True for the x64 Wine bridge: a 64-bit Windows VST2 or a Windows-layout VST3 on Linux with the x64 helper shipped.</summary>
    internal static bool UsesVst64Helper(string path) =>
        UsesVst64Helper(path, OperatingSystem.IsLinux(), WineBridgeSupport.Available64);

    /// <summary>
    /// Testable form of <see cref="UsesVst64Helper(string)"/>: Linux and the x64 helper are explicit. A VST3 bundle that also
    /// has a Linux binary stays native, exactly like <see cref="Vst32Bridge.Route"/>, so the probe cannot send the engine a
    /// file the engine would load in-process (D4).
    /// </summary>
    internal static bool UsesVst64Helper(string path, bool linux, bool x64Helper) =>
        linux && x64Helper && (PeExports.IsVst2Plugin64(path) ||
                               (Vst3Windows.IsWindowsOnly(path) && Vst3Windows.Has(path, sixtyFourBit: true)));

    /// <summary>
    /// The helper bitness for a bridged probe: VST2 by its PE header, VST3 by the Windows binary in the bundle.
    /// A 32-bit-only VST3 goes to the x86 helper; a bundle with both bitnesses uses x64, the same choice as the engine route.
    /// </summary>
    internal static bool BridgeIsSixtyFourBit(string path) =>
        PeExports.IsVst2Plugin64(path) || Vst3Windows.Has(path, sixtyFourBit: true);

    /// <summary>True when the probe path is a VST3 bundle or flat .vst3 file (for the user-facing failure texts).</summary>
    private static bool IsVst3(string path) => path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) || Directory.Exists(path);

    /// <summary>The helper command line: `&lt;helper&gt; --probe &lt;path&gt; &lt;approved sha256&gt;` (absolute paths only).</summary>
    internal static ProcessStartInfo BridgeStartInfo(string helperPath, string path, string approvedSha256) =>
        Vst32Bridge.ProbeStartInfo(helperPath, path, approvedSha256);

    private static int RunBridged(string path, string approvedSha256, string approvedDependencies = "") =>
        RunBridged(path, approvedSha256, helperFolder: null, BridgeTimeoutForCurrentOs, startProcess: null, approvedDependencies);

    /// <summary>Runs the helper, relays its line and bounds the wait; a timeout kills the whole helper tree (test seam for the process start).</summary>
    internal static int RunBridged(string path, string approvedSha256, string? helperFolder, int timeoutMs, Func<ProcessStartInfo, Process>? startProcess,
        string approvedDependencies = "")
    {
        path = Path.GetFullPath(path);   // the helper may change its working directory to the plug-in folder
        var vst3 = IsVst3(path);
        var sixtyFour = BridgeIsSixtyFourBit(path);
        WineRuntime? wine = null;
        string exe;
        if (OperatingSystem.IsLinux())
        {
            wine = WineRuntime.Locate(helperFolder: helperFolder);
            if (wine is null)
            {
                Console.Error.WriteLine($"{(vst3 ? (sixtyFour ? WineBridgeSupport.NoWineVst3_64Text : WineBridgeSupport.NoWineVst3Text) : (sixtyFour ? WineBridgeSupport.NoWine64Text : WineBridgeSupport.MissingText))} Expected: {WineBridgeSupport.WineExecutable() ?? "wine"}");
                return 1;
            }
            exe = sixtyFour ? wine.HelperPath64 : wine.HelperPath;
        }
        else exe = Vst32Bridge.HelperPath(helperFolder);
        if (!File.Exists(exe))
        {
            Console.Error.WriteLine($"{(vst3 ? (sixtyFour ? WineBridgeSupport.MissingVst3_64Text : WineBridgeSupport.MissingVst3Text) : (sixtyFour ? Vst32Bridge.Missing64Text : Vst32Bridge.MissingText))} Expected: {exe}");
            return 1;
        }
        try
        {
            // The probe executes the plug-in, so it checks the same main hash and local DLL closure the engine will, and holds
            // those files open while the helper loads (the helper itself only re-checks the main binary).
            using var identityHold = TabForge.Audio.Contracts.PluginIdentity.Hold(path, approvedSha256, locationTrusted: false, approvedDependencies);
            var start = Vst32Bridge.ProbeStartInfo(exe, path, approvedSha256, wine, approvedDependencies);
            using var process = (startProcess is null ? Process.Start(start) : startProcess(start))
                ?? throw new InvalidOperationException("The plug-in helper did not start.");
#if WINDOWS
            using var job = new TabForge.Audio.Contracts.ChildProcessJob();
            job.Add(process);   // the wrapper's death closes the job and ends the helper too
#endif
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { /* swallow: the helper already exited. */ }
                Console.Error.WriteLine($"The plug-in helper did not answer within {timeoutMs / 1000} s.");
                return 1;
            }
            Console.Out.Write(output.GetAwaiter().GetResult());
            Console.Error.Write(error.GetAwaiter().GetResult());
            Console.Out.Flush();
            Console.Error.Flush();
            return process.ExitCode;
        }
        catch (TabForge.Audio.Contracts.PluginChangedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 5;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            Console.Error.WriteLine($"{(sixtyFour ? Vst32Bridge.Missing64Text : Vst32Bridge.MissingText)} {ex.Message}");
            return 1;
        }
    }

    private static string Clean(string text) => text.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ').Trim();
}
