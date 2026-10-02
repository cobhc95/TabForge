using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TabForge.Docking;

namespace TabForge.Services;

// Owns: reading and writing settings.json with bounded, atomic file access.
// Does not own: the in-memory settings object (AppSettingsStore) and migration.
// Tests: TestSettingsWithInlinePluginStates, TestSecurityInputBoundaries.
/// <summary>Bounded settings import and same-directory atomic persistence.</summary>
public static class SettingsFileService
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = InputLimits.MaxJsonDepth
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        MaxDepth = InputLimits.MaxJsonDepth,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HashSet<string> DockPanelIds = new(StringComparer.Ordinal)
    {
        "instrument", "timeline", "tools", "structure", "rhythm", "layout", "sections", "practice", "playback"
    };

    public static AppSettings Load(string path)
    {
        path = FilePathPolicy.ExistingFile(path, "settings file", ".json");
        var json = InputLimits.ReadBoundedText(path, InputLimits.MaxSettingsJsonBytes, "settings file");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = InputLimits.MaxJsonDepth });
            ValidateJsonShape(document.RootElement);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions)
                           ?? throw new InvalidDataException("The settings file is empty or invalid.");
            return SettingsMigration.Normalize(json, settings);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The settings file is malformed or exceeds the supported JSON depth.", ex);
        }
    }

    public static void SaveAtomic(string path, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        path = FilePathPolicy.OutputFile(path, "settings file", ".json");

        // Complete serialization and the size check before touching the existing file.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, WriteOptions);
        if (bytes.LongLength > InputLimits.MaxSettingsJsonBytes)
            throw new InvalidDataException("The settings data exceeds the 2 MiB size limit.");

        FilePathPolicy.WriteAtomically(path, stream => stream.Write(bytes), createDirectory: true);
    }

    private static void ValidateJsonShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The settings document must be a JSON object.");
        ValidateTextLengths(root, null, 0);

        var appearance = Property(root, "Appearance");
        var recentColours = Property(appearance, "RecentColours");
        if (recentColours.ValueKind == JsonValueKind.Array && recentColours.GetArrayLength() > InputLimits.MaxRecentColours)
            throw new InvalidDataException("The settings file contains too many recent colours.");
        if (recentColours.ValueKind == JsonValueKind.Array && recentColours.EnumerateArray().Any(colour =>
                colour.ValueKind != JsonValueKind.String || colour.GetString()!.Length > InputLimits.MaxRecentColourLength))
            throw new InvalidDataException("The settings file contains an invalid or overlong recent colour.");

        var hotkeys = Property(root, "Hotkeys");
        foreach (var (bindingsName, disabledName) in new[] { ("Bindings", "DisabledActions"), ("Bindings2", "DisabledActions2") })
        {
            var bindings = Property(hotkeys, bindingsName);
            if (bindings.ValueKind == JsonValueKind.Object)
            {
                var count = 0;
                foreach (var pair in bindings.EnumerateObject())
                {
                    if (++count > InputLimits.MaxHotkeyBindings)
                        throw new InvalidDataException("The settings file contains too many hotkey bindings.");
                    if (pair.Name.Length > InputLimits.MaxHotkeyActionIdLength || pair.Value.ValueKind != JsonValueKind.String ||
                        pair.Value.GetString()!.Length > InputLimits.MaxHotkeyGestureLength)
                        throw new InvalidDataException("The settings file contains an invalid hotkey binding.");
                }
            }
            var disabled = Property(hotkeys, disabledName);
            if (disabled.ValueKind == JsonValueKind.Array && disabled.GetArrayLength() > InputLimits.MaxHotkeyBindings)
                throw new InvalidDataException("The settings file contains too many disabled hotkeys.");
            if (disabled.ValueKind == JsonValueKind.Array && disabled.EnumerateArray().Any(action =>
                    action.ValueKind != JsonValueKind.String || action.GetString()!.Length > InputLimits.MaxHotkeyActionIdLength))
                throw new InvalidDataException("The settings file contains an invalid disabled hotkey.");
        }

        var workspace = Property(root, "Workspace");
        if (workspace.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        if (workspace.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The workspace layout is invalid.");

        var floating = Property(workspace, "Floating");
        if (floating.ValueKind == JsonValueKind.Array && floating.GetArrayLength() > InputLimits.MaxWorkspaceFloatingWindows)
            throw new InvalidDataException("The workspace contains too many floating panels.");
        var closed = Property(workspace, "ClosedPanels");
        if (closed.ValueKind == JsonValueKind.Array && closed.GetArrayLength() > InputLimits.MaxWorkspaceClosedPanels)
            throw new InvalidDataException("The workspace contains too many closed panels.");

        var state = JsonSerializer.Deserialize<DockWorkspaceState>(workspace.GetRawText(), ReadOptions);
        if (state is null || !NormalizeWorkspace(state))
            throw new InvalidDataException("The workspace layout contains invalid panel data.");
    }

    internal static bool NormalizeWorkspace(DockWorkspaceState workspace)
    {
        if (workspace.Version != 1 || workspace.Floating is null || workspace.ClosedPanels is null ||
            workspace.Floating.Count > InputLimits.MaxWorkspaceFloatingWindows ||
            workspace.ClosedPanels.Count > InputLimits.MaxWorkspaceClosedPanels ||
            workspace.ClosedPanels.Any(id => !DockPanelIds.Contains(id))) return false;

        var nodes = 0;
        var hosts = new HashSet<string>(StringComparer.Ordinal);
        var panels = new HashSet<string>(StringComparer.Ordinal);
        var floatingIds = new HashSet<string>(StringComparer.Ordinal);
        if (!ValidateNode(workspace.Root, 1, ref nodes, hosts, panels)) return false;
        foreach (var floating in workspace.Floating)
        {
            if (floating is null || !InputLimits.IsSafeText(floating.Id, 64, allowLineBreaks: false) ||
                string.IsNullOrWhiteSpace(floating.Id) || !floatingIds.Add(floating.Id) ||
                !ValidateNode(floating.Root, 1, ref nodes, hosts, panels)) return false;
            floating.Left = Clamp(floating.Left, -32_768, 32_768, 180);
            floating.Top = Clamp(floating.Top, -32_768, 32_768, 140);
            floating.Width = Clamp(floating.Width, 80, 4_096, 360);
            floating.Height = Clamp(floating.Height, 80, 4_096, 260);
        }
        return true;
    }

    private static bool ValidateNode(DockNodeState? node, int depth, ref int count,
        HashSet<string> hosts, HashSet<string> panels)
    {
        if (node is null) return true;
        if (++count > InputLimits.MaxWorkspaceNodes || depth > InputLimits.MaxWorkspaceDepth ||
            !InputLimits.IsSafeText(node.Kind, 16, allowLineBreaks: false) ||
            !InputLimits.IsSafeText(node.HostId, 64, allowLineBreaks: false) || string.IsNullOrWhiteSpace(node.HostId) ||
            !hosts.Add(node.HostId) || node.Panels is null || node.Panels.Count > InputLimits.MaxWorkspacePanelsPerNode ||
            !InputLimits.IsSafeText(node.Orientation, 16, allowLineBreaks: false)) return false;
        node.Ratio = Clamp(node.Ratio, 0.05, 0.95, 0.5);

        if (node.Kind == "split")
        {
            if (node.Orientation is not ("Horizontal" or "Vertical") || node.First is null || node.Second is null || node.Panels.Count != 0)
                return false;
        }
        else if (node.Kind == "tabs")
        {
            if (node.First is not null || node.Second is not null ||
                node.Panels.Any(id => !DockPanelIds.Contains(id) || !panels.Add(id))) return false;
            if (node.SelectedPanel is not null && !node.Panels.Contains(node.SelectedPanel, StringComparer.Ordinal)) return false;
        }
        else if (node.Kind == "editor")
        {
            if (node.First is not null || node.Second is not null || node.Panels.Count != 0) return false;
        }
        else return false;

        return ValidateNode(node.First, depth + 1, ref count, hosts, panels) &&
               ValidateNode(node.Second, depth + 1, ref count, hosts, panels);
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return default;
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }

    private static void ValidateTextLengths(JsonElement element, string? arrayProperty, int depth)
    {
        if (depth > InputLimits.MaxJsonDepth) throw new InvalidDataException("The settings JSON nesting is too deep.");
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Length > 256)
                        throw new InvalidDataException("The settings file contains an overlong property name.");
                    ValidateTextLengths(property.Value, property.Name, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ValidateTextLengths(item, arrayProperty, depth + 1);
                break;
            case JsonValueKind.String:
                // Plug-in states of auto-load / startup chains: older builds stored them inline (base64); they are moved
                // to ChainStates files on load, so accept them up to the plug-in state limit rather than rejecting the file.
                var maximumLength = string.Equals(arrayProperty, "RecentColours", StringComparison.OrdinalIgnoreCase)
                    ? InputLimits.MaxRecentColourLength
                    : string.Equals(arrayProperty, "State", StringComparison.Ordinal)
                        ? TabForge.Audio.Contracts.PluginStateLimits.MaxBase64Chars
                        : InputLimits.MaxSettingsTextLength;
                if (element.GetString()!.Length > maximumLength)
                    throw new InvalidDataException("The settings file contains overlong text.");
                break;
        }
    }

    private static double Clamp(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
