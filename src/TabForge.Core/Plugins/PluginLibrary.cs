using System.IO;
using System.Text.Json;
using TabForge.Audio.Contracts;
using TabForge.Services;

namespace TabForge.Plugins;

// Owns: user presets and saved FX chains in app data (small bounded JSON).
// Does not own: chain states (ChainStateStore) or the plug-in loader.
// Tests: ProjectPluginAndClipLimitTests.

/// <summary>
/// User presets (one plug-in's saved state, by name) and saved FX chains, kept in %APPDATA%\TabForge.
/// Files are small JSON, read with bounds and validated like song data.
/// </summary>
public static class PluginLibrary
{
    private const long MaxFileBytes = 32L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, MaxDepth = 32 };

    /// <summary>Tests only: a scratch folder instead of the user's. Key: none (one folder); ceiling: one value; thread: set on the test thread before any call.</summary>
#pragma warning disable CS0649 // Assigned only by the self-test seam.
    internal static string? RootOverride;
#pragma warning restore CS0649

    public static string Root => RootOverride ?? TabForge.Services.UserPaths.Roaming;
    public static string ChainsFolder => Path.Combine(Root, "FxChains");

    internal static string PresetFolder(PluginSlot slot) => Path.Combine(Root, PresetFolderStore.PluginFolderName, SafeFileNames.SafeFileName(slot.Name.Length > 0 ? slot.Name : Path.GetFileNameWithoutExtension(slot.Path), "Plug-in", 80));

    /// <summary>A user preset: the plug-in's state and its Volume knob (OutputDb; null in presets saved before it was stored).</summary>
    public sealed record Preset(string Name, string State, double? OutputDb = null);

    /// <summary>The plug-in's saved presets in the user's order (alphabetical until they are reordered).</summary>
    public static IReadOnlyList<string> PresetNames(PluginSlot slot) => PresetFolderStore.Plugin(slot).Names();

    public static void SavePreset(PluginSlot slot, string name, string state)
    {
        var folder = PresetFolder(slot);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, SafeFileNames.SafeFileName(name, "Plug-in", 80) + ".tfpreset");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Preset(name, state, slot.OutputDb), Json);
        FilePathPolicy.WriteAtomically(path, s => s.Write(bytes));   // a failed write leaves the previous preset intact
    }

    /// <summary>A saved preset's state and Volume (null when the preset has none, or it is out of range); null when unreadable.</summary>
    public static (string State, double? OutputDb)? LoadPreset(PluginSlot slot, string name)
    {
        var path = Path.Combine(PresetFolder(slot), SafeFileNames.SafeFileName(name, "Plug-in", 80) + ".tfpreset");
        if (!File.Exists(path)) return TabForge.Audio.Contracts.Builtin.BuiltinPresets.State(slot.Path, name) is { } factory ? (Convert.ToBase64String(factory), null) : null;   // a built-in effect's factory preset
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
        var path = Path.Combine(PresetFolder(slot), SafeFileNames.SafeFileName(name, "Plug-in", 80) + ".tfpreset");
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Saves a whole chain (plug-ins, order, roles, wiring, wet and states).</summary>
    public static void SaveChain(string path, IReadOnlyList<PluginSlot> chain, IReadOnlyList<ChainContainer>? containers = null)
    {
        // The file stays a list of plug-ins; containers ride on the first one, so a reader that does not know them loads the flat chain.
        var node = JsonSerializer.SerializeToNode(chain, Json)!.AsArray();
        if (ChainTree.Valid(containers, chain.Count) is { Count: > 0 } valid && node.Count > 0) node[0]!["ChainContainers"] = JsonSerializer.SerializeToNode(valid, Json);
        var bytes = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString(Json));
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
            ChainTree.Pack(chain, probe.Tracks[0].Rig.Containers);   // validated; they stay with the list until it lands on a track
            return chain;
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException) { Services.Trace.Error(Services.Trace.Engine, "plug-in chain: read: " + ex.Message); return null; }
    }
}
