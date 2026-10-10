using System.IO;
using System.Text.Json;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>
/// User presets (one plug-in's saved state, by name) and saved FX chains, kept in %APPDATA%\TabForge.
/// Files are small JSON, read with bounds and validated like song data.
/// </summary>
public static class PluginLibrary
{
    private const long MaxFileBytes = 32L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, MaxDepth = 32 };

    /// <summary>Tests only: a scratch folder instead of the user's.</summary>
    internal static string? RootOverride;

    public static string Root => RootOverride ?? TabForge.Services.UserPaths.Roaming;
    public static string ChainsFolder => Path.Combine(Root, "FxChains");

    private static string PresetFolder(PluginSlot slot) => Path.Combine(Root, "Presets", Safe(slot.Name.Length > 0 ? slot.Name : Path.GetFileNameWithoutExtension(slot.Path)));

    private static string Safe(string name)
    {
        return TabForge.Audio.Contracts.SafeFileNames.SafeFileName(name, "Plug-in", 80);
    }

    /// <summary>A user preset: the plug-in's state and its Volume knob (OutputDb; null in presets saved before it was stored).</summary>
    public sealed record Preset(string Name, string State, double? OutputDb = null);

    public static IReadOnlyList<string> PresetNames(PluginSlot slot)
    {
        var folder = PresetFolder(slot);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.tfpreset").Select(Path.GetFileNameWithoutExtension).OfType<string>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(1000).ToList()
            : Array.Empty<string>();
    }

    public static void SavePreset(PluginSlot slot, string name, string state)
    {
        var folder = PresetFolder(slot);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Safe(name) + ".tfpreset");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Preset(name, state, slot.OutputDb), Json);
        FilePathPolicy.WriteAtomically(path, s => s.Write(bytes));   // a failed write leaves the previous preset intact
    }

    /// <summary>A saved preset's state and Volume (null when the preset has none, or it is out of range); null when unreadable.</summary>
    public static (string State, double? OutputDb)? LoadPreset(PluginSlot slot, string name)
    {
        var path = Path.Combine(PresetFolder(slot), Safe(name) + ".tfpreset");
        if (!File.Exists(path)) return null;
        try
        {
            var preset = JsonSerializer.Deserialize<Preset>(InputLimits.ReadBoundedBytes(path, MaxFileBytes, "preset"), Json);
            if (preset?.State is not { Length: > 0 and <= InputLimits.MaxPluginStateChars } state) return null;
            var db = preset.OutputDb is { } v && double.IsFinite(v) && v is >= -60 and <= 12 ? v : (double?)null;
            return (state, db);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException) { Services.Trace.Error(Services.Trace.Engine, "plug-in preset: read: " + ex.Message); return null; }
    }

    public static void DeletePreset(PluginSlot slot, string name)
    {
        var path = Path.Combine(PresetFolder(slot), Safe(name) + ".tfpreset");
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Saves a whole chain (plug-ins, order, roles, wiring, wet and states).</summary>
    public static void SaveChain(string path, IReadOnlyList<PluginSlot> chain)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(chain, Json);
        FilePathPolicy.WriteAtomically(path, s => s.Write(bytes));   // a failed write leaves the previous chain file intact
    }

    /// <summary>Reads a saved chain; null when the file is not a valid chain.</summary>
    public static List<PluginSlot>? LoadChain(string path)
    {
        try
        {
            var chain = JsonSerializer.Deserialize<List<PluginSlot>>(InputLimits.ReadBoundedBytes(path, MaxFileBytes, "FX chain"), Json);
            if (chain is null || chain.Count > InputLimits.MaxPluginsPerTrack) return null;
            // Validate exactly like plug-ins inside a song.
            var probe = Presets.TemplateFactory.Blank();
            probe.Tracks[0].Rig.Plugins = chain;
            ProjectValidator.Validate(probe);
            return chain;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException) { Services.Trace.Error(Services.Trace.Engine, "plug-in chain: read: " + ex.Message); return null; }
    }
}
