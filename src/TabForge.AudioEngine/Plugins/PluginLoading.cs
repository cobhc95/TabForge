using System.Diagnostics;

namespace TabForge.AudioEngine.Plugins;

/// <summary>
/// The one place that turns a plug-in file into an in-process instance (the engine, the isolated plug-in host and the scan probe
/// all use it), so format detection and construction cannot drift from <see cref="IPluginInstance.Reconfigure"/>.
/// </summary>
public static class PluginFactory
{
    /// <summary>VST3 by file extension (a .vst3 file or bundle) or by the catalog's format; everything else is loaded as VST2.</summary>
    public static bool IsVst3(string path, string format) =>
        path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "VST3", StringComparison.OrdinalIgnoreCase);

    /// <param name="isInstrument">VST3: which class of the bundle to pick (the first instrument or the first effect); VST2 reports its own role.</param>
    public static IPluginInstance Create(string path, string format, bool isInstrument, double sampleRate, int maxBlock) =>
        IsVst3(path, format) ? new Vst3Plugin(path, isInstrument, sampleRate, maxBlock) : new Vst2Plugin(path, sampleRate, maxBlock);

    /// <summary>
    /// Create for a process that has no engine spec (the isolated plug-in host, the identify probe): the approved SHA-256 is
    /// checked through <see cref="TabForge.Audio.Contracts.PluginIdentity.Hold(string, string?, bool)"/> and the file stays held until the instance exists.
    /// <paramref name="create"/> is a test seam (a counter, to prove an unapproved file is never executed).
    /// </summary>
    public static IPluginInstance CreateVerified(string path, string format, bool isInstrument, double sampleRate, int maxBlock,
        string? expectedSha256, bool required, Func<string, IPluginInstance>? create = null)
    {
        using var hold = TabForge.Audio.Contracts.PluginIdentity.Hold(path, expectedSha256, required);
        return create is not null ? create(path) : Create(path, format, isInstrument, sampleRate, maxBlock);
    }
}

/// <summary>Creating a plug-in instance and loading its saved state without leaking the instance when the state is rejected.</summary>
public static class PluginLoading
{
    /// <summary>
    /// Creates an instance and applies <paramref name="state"/> (when given). If applying it throws, the new instance
    /// (its native module, editor, or its own process) is disposed before the exception propagates.
    /// </summary>
    public static IPluginInstance CreateWithState(Func<IPluginInstance> create, byte[]? state, out long createMs)
    {
        var timer = Stopwatch.StartNew();
        var plugin = create();
        createMs = timer.ElapsedMilliseconds;
        try
        {
            if (state is { Length: > 0 }) plugin.SetState(state);
            return plugin;
        }
        catch
        {
            try { plugin.Dispose(); }
            catch (Exception ex) { EngineLog.Write($"dispose failed: {ex.Message}"); }
            throw;
        }
    }
}
