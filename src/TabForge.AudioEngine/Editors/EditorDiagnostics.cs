using System.Diagnostics;
using System.Runtime.CompilerServices;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Plugins.Clap;

namespace TabForge.AudioEngine.Editors;

// Owns: the plug-in editor diagnostics (what an editor was opened on, then its idle and process rate every ~2 s), for finding frozen meters.
// Does not own: the engine log (EngineLogWriter), the counters' owners (TrackChain.Effect.ProcessCalls, the plug-in host's audio loop).
// Tests: PluginEditorHostTests.
// Threads: main thread only; the audio thread only increments its own Interlocked counters, which are read here.

/// <summary>Describes a plug-in instance for the engine log: kind, managed instance id and native handle.</summary>
internal static class EditorDiagnostics
{
    /// <summary>Wall ms between two rate lines of one open editor.</summary>
    public const double ReportMs = 2000;

    public static string Kind(IPluginInstance plugin) => plugin switch
    {
        Vst2Plugin => "VST2",
        Vst3Plugin => "VST3",
        ClapPlugin => "CLAP",
        Isolation.RemotePlugin => "remote",
        _ => plugin.GetType().Name,
    };

    public static string Describe(IPluginInstance plugin)
    {
        var native = plugin switch { Vst2Plugin v2 => v2.NativeHandle, Vst3Plugin v3 => v3.NativeHandle, _ => IntPtr.Zero };
        return $"{Kind(plugin)} instance={RuntimeHelpers.GetHashCode(plugin)} native=0x{native:X}";
    }
}

/// <summary>One open editor's idle ticks and the plug-in's process calls, reported as the change since the last line.</summary>
internal sealed class EditorRate
{
    private readonly string _name;
    private readonly string _kind;
    private readonly int _id;
    private long _ticks, _lastTicks, _lastProcess = -1, _stamp = Stopwatch.GetTimestamp();

    public EditorRate(string name, IPluginInstance plugin) { _name = name; _kind = EditorDiagnostics.Kind(plugin); _id = RuntimeHelpers.GetHashCode(plugin); }

    /// <summary>One editor idle tick (main thread). Returns the rate line when it is due, else null; <paramref name="processCalls"/> is read only then (-1: not counted here).</summary>
    public string? Tick(Func<long> processCalls)
    {
        _ticks++;
        var elapsed = Stopwatch.GetElapsedTime(_stamp).TotalMilliseconds;
        if (elapsed < EditorDiagnostics.ReportMs) return null;
        _stamp = Stopwatch.GetTimestamp();
        var process = processCalls();
        var line = $"editor rate '{_name}' {_kind} instance={_id}: idle ticks +{_ticks - _lastTicks}, process calls {(process < 0 ? "n/a" : _lastProcess >= 0 ? $"+{process - _lastProcess}" : $"{process} total")} in {elapsed:0} ms";
        _lastTicks = _ticks;
        _lastProcess = process;
        return line;
    }
}
