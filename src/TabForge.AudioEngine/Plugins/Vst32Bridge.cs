using System.Diagnostics;
using System.IO;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;
using TabForge.Plugins;

// The file sits under Plugins/ as planned, but the type lives in the Isolation namespace: the routing code needs the Core
// plug-in names (PluginFormats, PeExports) and AudioEngine.Plugins must not gain an edge into Core.Plugins (it would close a
// new namespace cycle through Core.Midi -> AudioEngine.Plugins). Isolation is the layer that already talks to both sides.
namespace TabForge.AudioEngine.Isolation;

// Owns: the Windows VST2/VST3 bridge decision (which file goes to a helper, and which helper), its launch record on Windows
// (VST2 x86 and 32-bit-only VST3) and Linux (VST2/VST3, x86/x64 under Wine), and the probe start info for both.
// Does not own: the helper process itself (native tfvst32/tfvst64), the 64-bit isolation launch (RemotePlugin) or scanning (VstScannerService).
// Tests: Vst32RoutingTests, Vst32WineSeamTests, Vst3WineLinuxTests, Vst3NativeTests.

/// <summary>
/// Decides whether a chain slot is a Windows VST2 or Windows-layout VST3 plug-in that must run outside the engine: a 32-bit
/// VST2 DLL on Windows or Linux through the 32-bit helper, a 32-bit-only Windows VST3 on Windows through the 32-bit helper,
/// or a 64-bit Windows VST2/VST3 on Linux through the 64-bit helper under Wine. Windows 64-bit VST2 and 64-bit (or dual-bitness)
/// Windows VST3 load natively in-process on Windows. Detection uses the saved bridged label first and the file's PE/layout when
/// the format is missing or stale, so a project saved before a bridge still routes correctly. Built-ins, native VST3 and CLAP
/// never bridge. On Linux a missing Wine bundle or helper throws a readable refusal instead of letting the engine try to load
/// a Windows PE in-process. Nothing here loads plug-in code.
/// </summary>
public static class Vst32Bridge
{
    public const string HelperName = "tfvst32.exe";
    public const string HelperName64 = "tfvst64.exe";
    public const string ProbeMode = "--probe";

    /// <summary>The helper's audio slots: two, so a timed-out block keeps its own buffers while the next one is sent.</summary>
    public const int BufferSlots = 2;

    /// <summary>The one user-facing failure when the 32-bit helper is not shipped beside TabForge.exe; never a raw BadImageFormat.</summary>
    public const string MissingText =
        "This is a 32-bit VST2 plug-in. Its 32-bit helper (tfvst32.exe) is missing from the TabForge folder, so the plug-in was not loaded.";

    /// <summary>The same failure for the 64-bit Wine helper.</summary>
    public const string Missing64Text =
        "This is a 64-bit VST2 plug-in. Its 64-bit helper (tfvst64.exe) is missing from the TabForge folder, so the plug-in was not loaded.";

    /// <summary>Where one chain slot runs: in the engine, in the existing 64-bit isolation host, or in a bitness helper.</summary>
    public enum PluginRoute { InProcess, Isolated, Bridged32, Bridged64 }

    /// <summary>The route for one slot: a bridgeable Windows VST2 plug-in always bridges, whatever "run each plug-in separately" says.</summary>
    public static PluginRoute Route(PluginSpec spec, bool separateProcess)
    {
        if (TabForge.Audio.Contracts.Builtin.BuiltinEffects.IsBuiltin(spec.Path)) return PluginRoute.InProcess;
        if (IsWineVst3Candidate(spec, sixtyFourBit: true)) return PluginRoute.Bridged64;
        if (IsWineVst3Candidate(spec, sixtyFourBit: false)) return PluginRoute.Bridged32;
        if (IsWine64Candidate(spec)) return PluginRoute.Bridged64;
        if (IsWindowsBridged(spec) || IsWine32Candidate(spec)) return PluginRoute.Bridged32;
        // A plug-in that already ended the engine once is loaded in the existing isolation host even when the global
        // "run each plug-in separately" setting is off. Bridged routes above keep their own helper (they are already isolated).
        if (spec.Isolate) return PluginRoute.Isolated;
        return separateProcess ? PluginRoute.Isolated : PluginRoute.InProcess;
    }

    /// <summary>True for a Windows VST2 or Windows-layout VST3 this OS can run through a helper, from the saved label or the file.</summary>
    public static bool IsBridged(PluginSpec spec) =>
        IsWindowsBridged(spec) ||
        (IsWine32Candidate(spec) && WineBridgeSupport.Available) ||
        (IsWine64Candidate(spec) && WineBridgeSupport.Available64) ||
        (IsWineVst3Candidate(spec, sixtyFourBit: true) && WineBridgeSupport.Available64) ||
        (IsWineVst3Candidate(spec, sixtyFourBit: false) && WineBridgeSupport.Available);

    /// <summary>True for a 32-bit Windows VST2 PE on Linux, whether or not the Wine bundle is present (the launch owns the refusal).</summary>
    public static bool IsWineCandidate(PluginSpec spec) => IsWine32Candidate(spec);

    /// <summary>True for a 32-bit Windows VST2 PE on Linux, whether or not the Wine bundle is present.</summary>
    public static bool IsWine32Candidate(PluginSpec spec) => OperatingSystem.IsLinux() && Is32BitVst2(spec);

    /// <summary>True for a 64-bit Windows VST2 PE on Linux, whether or not the Wine bundle is present.</summary>
    public static bool IsWine64Candidate(PluginSpec spec) => OperatingSystem.IsLinux() && Is64BitVst2(spec);

    /// <summary>
    /// True for a Windows-layout VST3 plug-in (bundle or flat .vst3) on Linux that has no native Linux binary, of the
    /// requested bitness. The saved bridged label wins when it names one bitness, so a stale format cannot silently flip it.
    /// </summary>
    public static bool IsWineVst3Candidate(PluginSpec spec, bool sixtyFourBit)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(spec.Path)) return false;
        if (TabForge.Audio.Contracts.Builtin.BuiltinEffects.IsBuiltin(spec.Path)) return false;
        var path = spec.Path.TrimEnd('\\', '/');
        if (!PluginFactory.IsVst3(path, spec.Format)) return false;
        var format = (spec.Format ?? "").Trim();
        if (format.Equals(PluginFormats.Bridged64, StringComparison.OrdinalIgnoreCase)) return sixtyFourBit;
        if (format.Equals(PluginFormats.Bridged32, StringComparison.OrdinalIgnoreCase)) return !sixtyFourBit;
        if (!Vst3Windows.IsWindowsOnly(path)) return false;
        return Vst3Windows.Has(path, sixtyFourBit);
    }

    /// <summary>Windows keeps its exact rule: the saved 32-bit label or the PE header/layout; 64-bit stays native.</summary>
    private static bool IsWindowsBridged(PluginSpec spec)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(spec.Path)) return false;
        if (TabForge.Audio.Contracts.Builtin.BuiltinEffects.IsBuiltin(spec.Path)) return false;
        var path = spec.Path.TrimEnd('\\', '/');
        if (PluginFactory.IsClap(path, spec.Format)) return false;
        var format = (spec.Format ?? "").Trim();
        if (format.Equals(PluginFormats.Bridged32, StringComparison.OrdinalIgnoreCase) ||
            format.Equals(PluginFormats.LegacyUnsupported32, StringComparison.OrdinalIgnoreCase)) return true;
        if (PluginFactory.IsVst3(path, format))
        {
            // A 32-bit-only Windows VST3 bridges like a 32-bit VST2; a 64-bit (or dual-bitness) VST3 stays native.
            // A stale saved VST3 label cannot hide the layout; a saved 64-bit-bridged label (from Linux) never bridges here.
            // (format is the trimmed saved label; IsVst3 also matches the .vst3 extension, so stale labels still route by layout.)
            if (format.Length > 0 && !format.Equals(PluginFormats.Vst3, StringComparison.OrdinalIgnoreCase) &&
                !format.Equals(PluginFormats.Vst2, StringComparison.OrdinalIgnoreCase)) return false;
            return Vst3Windows.Is32BitOnly(path);
        }
        if (format.Length > 0 && !format.Equals(PluginFormats.Vst2, StringComparison.OrdinalIgnoreCase)) return false;
        return PeExports.IsVst2Plugin32(path);
    }

    private static bool Is32BitVst2(PluginSpec spec) => IsVst2Bitness(spec, x86: true);

    private static bool Is64BitVst2(PluginSpec spec) => IsVst2Bitness(spec, x86: false);

    /// <summary>The PE machine decides; any saved VST2 label is tolerated so a stale label cannot hide the real bitness.</summary>
    private static bool IsVst2Bitness(PluginSpec spec, bool x86)
    {
        if (string.IsNullOrWhiteSpace(spec.Path)) return false;
        if (TabForge.Audio.Contracts.Builtin.BuiltinEffects.IsBuiltin(spec.Path)) return false;
        var path = spec.Path.TrimEnd('\\', '/');
        if (PluginFactory.IsVst3(path, spec.Format) || PluginFactory.IsClap(path, spec.Format)) return false;
        var format = (spec.Format ?? "").Trim();
        if (format.Length > 0 &&
            !format.Equals(PluginFormats.Vst2, StringComparison.OrdinalIgnoreCase) &&
            !format.Equals(PluginFormats.Bridged32, StringComparison.OrdinalIgnoreCase) &&
            !format.Equals(PluginFormats.Bridged64, StringComparison.OrdinalIgnoreCase) &&
            !format.Equals(PluginFormats.LegacyUnsupported32, StringComparison.OrdinalIgnoreCase)) return false;
        return x86 ? PeExports.IsVst2Plugin32(path) : PeExports.IsVst2Plugin64(path);
    }

    /// <summary>The helper file name for one bitness.</summary>
    public static string HelperNameFor(bool sixtyFourBit) => sixtyFourBit ? HelperName64 : HelperName;

    /// <summary>The 32-bit helper path beside the app (tests may pass their own folder); never PATH, never the working directory.</summary>
    public static string HelperPath(string? folder = null) => HelperPathFor(sixtyFourBit: false, folder);

    /// <summary>The 64-bit helper path beside the app.</summary>
    public static string HelperPath64(string? folder = null) => HelperPathFor(sixtyFourBit: true, folder);

    public static string HelperPathFor(bool sixtyFourBit, string? folder = null) =>
        Path.Combine(string.IsNullOrWhiteSpace(folder) ? AppContext.BaseDirectory : folder, HelperNameFor(sixtyFourBit));

    /// <summary>The immutable launch record for a bridged slot; throws a readable failure when the platform runtime is missing.</summary>
    public static PluginHostLaunch Launch(PluginSpec spec, string? helperFolder = null, WineRuntime? wine = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (OperatingSystem.IsLinux())
        {
            var vst3 = IsWineVst3Candidate(spec, sixtyFourBit: true) || IsWineVst3Candidate(spec, sixtyFourBit: false);
            var sixtyFour = vst3 ? IsWineVst3Candidate(spec, sixtyFourBit: true) : Is64BitVst2(spec);
            var runtime = wine ?? WineRuntime.Locate(helperFolder: helperFolder)
                ?? throw new InvalidOperationException(
                    $"{(vst3 ? (sixtyFour ? WineBridgeSupport.NoWineVst3_64Text : WineBridgeSupport.NoWineVst3Text) : (sixtyFour ? WineBridgeSupport.NoWine64Text : WineBridgeSupport.MissingText))} Expected: {WineBridgeSupport.WineExecutable() ?? "wine"} and {(sixtyFour ? HelperPath64(helperFolder) : HelperPath(helperFolder))}");
            var helper = sixtyFour ? runtime.HelperPath64 : runtime.HelperPath;
            if (!File.Exists(helper)) throw new InvalidOperationException($"{(vst3 ? (sixtyFour ? WineBridgeSupport.MissingVst3_64Text : WineBridgeSupport.MissingVst3Text) : (sixtyFour ? Missing64Text : MissingText))} Expected: {helper}");
            if (wine is null) runtime.EnsureReady();   // lazy first run; the engine main thread is the only caller
            return LaunchWine(spec, runtime, sixtyFour, vst3 ? "VST3" : "VST2");
        }
        if (Is64BitVst2(spec))
            throw new InvalidOperationException("A 64-bit Windows VST2 plug-in loads natively on Windows and is not sent to a helper.");
        if (PluginFactory.IsVst3(spec.Path.TrimEnd('\\', '/'), spec.Format))
        {
            // Only a 32-bit-only VST3 reaches the helper on Windows; anything else stays native.
            if (!Vst3Windows.Is32BitOnly(spec.Path.TrimEnd('\\', '/')))
                throw new InvalidOperationException("A 64-bit VST3 plug-in loads natively on Windows and is not sent to a helper.");
            var exeVst3 = HelperPath(helperFolder);
            if (!File.Exists(exeVst3)) throw new InvalidOperationException($"{WineBridgeSupport.MissingVst3Text} Expected: {exeVst3}");
            return new PluginHostLaunch(exeVst3, PluginHostLaunch.Vst32HostMode, BufferSlots, BridgeBitness: 32, Format: "VST3");
        }
        var exe = HelperPath(helperFolder);
        if (!File.Exists(exe)) throw new InvalidOperationException($"{MissingText} Expected: {exe}");
        return new PluginHostLaunch(exe, PluginHostLaunch.Vst32HostMode, BufferSlots);
    }

    /// <summary>The Wine launch record without first-run setup (the engine's <see cref="Launch"/> prepares the prefix; tests use this).</summary>
    internal static PluginHostLaunch LaunchWine(PluginSpec spec, WineRuntime runtime) =>
        LaunchWine(spec, runtime, Is64BitVst2(spec), "VST2");

    internal static PluginHostLaunch LaunchWine(PluginSpec spec, WineRuntime runtime, bool sixtyFour, string format)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(runtime);
        var helper = sixtyFour ? runtime.HelperPath64 : runtime.HelperPath;
        if (!File.Exists(helper)) throw new InvalidOperationException($"{(sixtyFour ? Missing64Text : MissingText)} Expected: {helper}");
        return new PluginHostLaunch(runtime.WineExecutable, PluginHostLaunch.Vst32TcpHostMode, BufferSlots,
            PluginHostTransport.Tcp, HelperPath: helper, Environment: runtime.EnvironmentFor(),
            BridgeBitness: sixtyFour ? 64 : 32, Format: format);
    }

    /// <summary>
    /// The identify probe's process start: the helper directly on Windows, or the bundled wine loader with the helper as
    /// its first argument on Linux. The environment carries the private prefix and the cleaned library paths.
    /// </summary>
    internal static ProcessStartInfo ProbeStartInfo(string helperPath, string path, string approvedSha256, WineRuntime? wine = null,
        string approvedDependencies = "")
    {
        var executable = wine is null ? helperPath : wine.WineExecutable;
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (wine is not null)
        {
            start.ArgumentList.Add(helperPath);
            foreach (var (key, value) in wine.EnvironmentFor()) start.Environment[key] = value;
        }
        start.ArgumentList.Add(ProbeMode);
        // Wine's helper is a Windows process: the plug-in path must be the Z: view, never the host's /home/... path.
        start.ArgumentList.Add(wine is null ? path : WineRuntime.ToWindowsPath(path));
        start.ArgumentList.Add(approvedSha256);
        start.ArgumentList.Add(approvedDependencies.Length == 0 ? "none" : approvedDependencies);   // native --probe ignores it; the wrapper holds the closure
        return start;
    }
}
