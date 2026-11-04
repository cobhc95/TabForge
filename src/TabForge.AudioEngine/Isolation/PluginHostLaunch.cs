using System.Globalization;
using TabForge.Audio.Contracts;

// The record sits in the plug-in namespace (its file lives beside RemotePlugin) so the dependency stays Isolation -> Plugins:
// the 32-bit routing code chooses a launch, the isolation proxy starts and supervises it.
namespace TabForge.AudioEngine.Plugins;

// Owns: the immutable launch record of a plug-in's own process (which executable, which mode, which transport, how many audio
// slots, and any Wine environment) and its argument list.
// Does not own: starting or supervising the process (RemotePlugin) or choosing the 32-bit helper (Vst32Bridge).
// Tests: Vst32RoutingTests, Vst32WineSeamTests.

/// <summary>How the helper's control frames move: the Windows named pipe, or loopback TCP under Wine.</summary>
public enum PluginHostTransport { NamedPipe, Tcp }

/// <summary>
/// How one plug-in's own process is started. The default is TabForge itself in <c>--plugin-host</c> mode (the existing 64-bit
/// isolation); a 32-bit VST2 plug-in uses <c>tfvst32.exe --vst32-host</c> on Windows, or <c>wine tfvst32.exe --vst32-host-tcp</c>
/// on Linux. The record is immutable, so a running session cannot have its executable or protocol changed underneath it.
/// </summary>
/// <param name="ExecutablePath">Absolute executable path (the helper on Windows, the bundled wine loader on Linux).</param>
/// <param name="Mode">The first helper argument: <c>--plugin-host</c>, <c>--vst32-host</c> or <c>--vst32-host-tcp</c>.</param>
/// <param name="BufferSlots">Audio buffer slots in the shared block (1 for the managed host; 2 for the native helper).</param>
/// <param name="Transport">Named pipe/events (Windows default) or loopback TCP/file flags (Wine).</param>
/// <param name="HelperPath">The Windows helper exe passed to wine before the mode; null when the executable is the helper.</param>
/// <param name="Environment">Extra environment variables for the child (the private Wine prefix and cleaned library paths).</param>
/// <param name="BridgeBitness">32 or 64: the helper architecture the TCP hello must report (the child itself on Windows, the Wine helper on Linux).</param>
public sealed record PluginHostLaunch(string ExecutablePath, string Mode, int BufferSlots = 1,
    PluginHostTransport Transport = PluginHostTransport.NamedPipe, string? HelperPath = null,
    IReadOnlyDictionary<string, string>? Environment = null, int BridgeBitness = 32, string Format = "VST2")
{
    public const string PluginHostMode = "--plugin-host";
    public const string Vst32HostMode = "--vst32-host";
    public const string Vst32TcpHostMode = "--vst32-host-tcp";

    /// <summary>The existing isolation launch: this executable, <c>--plugin-host</c>, one audio slot.</summary>
    public static PluginHostLaunch ForCurrentProcess() => new(System.Environment.ProcessPath ?? "", PluginHostMode);

    /// <summary>True when the native helper is launched: it receives the buffer slot count as its last argument.</summary>
    public bool IsVst32Bridge => Mode is Vst32HostMode or Vst32TcpHostMode;

    /// <summary>True for the Wine transport: TCP control and a file-backed audio block.</summary>
    public bool IsTcp => Transport == PluginHostTransport.Tcp;

    /// <summary>
    /// The host's arguments in their one order: (wine only: the helper exe) mode, id, engine pid, sample rate, block size,
    /// instrument, format, path, approved SHA-256 (or "none"), log path (or "none"), trust kind, then the buffer slot count
    /// for the native helper, then the approved local-DLL closure hash (or "none"; the managed host reads it, the native helper
    /// ignores the extra argument because the engine has already verified and held the closure). The TCP mode replaces
    /// id/engine-pid with the loopback port and the explicit block path.
    /// </summary>
    internal List<string> Arguments(string id, int enginePid, int sampleRate, int maxBlock, PluginSpec spec, string? logPath,
        int tcpPort = 0, string? blockPath = null)
    {
        var arguments = new List<string>();
        if (!string.IsNullOrEmpty(HelperPath)) arguments.Add(HelperPath);
        if (IsTcp)
        {
            if (tcpPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(tcpPort));
            if (string.IsNullOrWhiteSpace(blockPath)) throw new ArgumentException("The TCP transport needs an explicit block path.", nameof(blockPath));
            arguments.AddRange(new[]
            {
                Mode,
                tcpPort.ToString(CultureInfo.InvariantCulture),
                blockPath,
                sampleRate.ToString(CultureInfo.InvariantCulture),
                maxBlock.ToString(CultureInfo.InvariantCulture),
                spec.IsInstrument ? "1" : "0",
                Format,
                spec.Path,
                string.IsNullOrEmpty(spec.ExpectedSha256) ? "none" : spec.ExpectedSha256,
                string.IsNullOrWhiteSpace(logPath) ? "none" : logPath,
                spec.LocationTrusted ? "location" : "checked",
                Math.Clamp(BufferSlots, 1, 2).ToString(CultureInfo.InvariantCulture),
                string.IsNullOrEmpty(spec.ExpectedDependencies) ? "none" : spec.ExpectedDependencies,
            });
            return arguments;
        }
        arguments.AddRange(new[]
        {
            Mode,
            id,
            enginePid.ToString(CultureInfo.InvariantCulture),
            sampleRate.ToString(CultureInfo.InvariantCulture),
            maxBlock.ToString(CultureInfo.InvariantCulture),
            spec.IsInstrument ? "1" : "0",
            IsVst32Bridge ? Format : spec.Format,
            spec.Path,
            string.IsNullOrEmpty(spec.ExpectedSha256) ? "none" : spec.ExpectedSha256,
            string.IsNullOrWhiteSpace(logPath) ? "none" : logPath,
            spec.LocationTrusted ? "location" : "checked",
        });
        if (IsVst32Bridge) arguments.Add(Math.Clamp(BufferSlots, 1, 2).ToString(CultureInfo.InvariantCulture));
        // The managed isolation host reads this at args[11]; the native helper ignores the extra argument (it already runs
        // only after the engine's PluginIdentity.Hold has verified and held the closure).
        arguments.Add(string.IsNullOrEmpty(spec.ExpectedDependencies) ? "none" : spec.ExpectedDependencies);
        return arguments;
    }
}
