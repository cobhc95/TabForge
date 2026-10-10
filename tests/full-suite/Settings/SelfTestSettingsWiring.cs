using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TabForge.Services;

namespace TabForge;

// Owns: the data-driven wiring audit of the settings catalogue: which stored property each row changes, that real code reads it, and that no two rows share one.
// Does not own: the per-row round trip (TestEverySettingIsWired) or the entry-point sync checks (TestSettingsEntryPointsInSync).
/// <summary>Walks the catalogue: each row's changed JSON property must be read by code outside the settings plumbing, and be bound by one row only.</summary>
public static partial class SelfTest
{
    /// <summary>Properties the audit knows are read through a name the scan cannot see (reflection, a copy of the object, a serializer) or are applied on restart.</summary>
    private static readonly HashSet<string> WiringScanExemptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Tabs.WidthMode",                           // read through TabSettings.ShrinkToFit
        "Appearance.GroupColours.Other instruments", // a dictionary key, read by group name in TrackColouring
    };

    /// <summary>Rows meant to share one stored property (two views of one value).</summary>
    private static readonly HashSet<string> WiringSharedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Timeline.ShowIndividualNotes",   // two views of one choice: the notes view and the continuous line exclude each other
    };

    /// <summary>The leaf paths of a settings object as "Path.To.Leaf" -> JSON text.</summary>
    private static Dictionary<string, string> SettingsLeaves(AppSettings s)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        void Walk(JsonNode? n, string path)
        {
            switch (n)
            {
                case JsonObject o: foreach (var p in o) Walk(p.Value, path.Length == 0 ? p.Key : path + "." + p.Key); break;
                case JsonArray a: map[path] = a.ToJsonString(); break;
                default: map[path] = n?.ToJsonString() ?? "null"; break;
            }
        }
        Walk(JsonSerializer.SerializeToNode(s), "");
        return map;
    }

    /// <summary>The source files whose references count as "code reads this property": everything but the settings plumbing and the tests.</summary>
    private static List<string> WiringReaderSources(string root)
    {
        var src = Path.Combine(root, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\") && !f.Contains(@"\SelfTests\"))
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                if (name.StartsWith("MainWindow.", StringComparison.Ordinal)) return true;
                return !(name.StartsWith("SettingsCatalog", StringComparison.Ordinal) || name.StartsWith("Settings", StringComparison.Ordinal) ||
                         name.EndsWith("Settings.cs", StringComparison.Ordinal) || name.StartsWith("AppSettings", StringComparison.Ordinal));
            }).ToList();
    }

    private static void TestSettingsWiringAudit()
    {
        var root = FindRepositoryRoot();
        if (root is null) { Skip("settings wiring audit (each row's property is read by code)", "no source checkout found", "source-hygiene"); return; }
        var machine = new Func<string, bool>(k => k == "vst.device" || k == "vst.input" || k.StartsWith("vst.asio.", StringComparison.Ordinal));
        var settings = new AppSettings();
        var catalog = SettingsCatalog.Build(settings).Where(d => d.Kind != SettingKind.Button && !machine(d.Key)).ToList();
        var text = string.Join("\n", WiringReaderSources(root).Select(File.ReadAllText));
        var bound = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var dead = new List<string>();
        var noProperty = new List<string>();
        foreach (var row in catalog)
        {
            var before = SettingsLeaves(settings);
            var value = AlternativeValue(row);
            if (value is null) continue;
            row.Set(value);
            var after = SettingsLeaves(settings);
            var changed = after.Where(p => !before.TryGetValue(p.Key, out var old) || old != p.Value).Select(p => p.Key).ToList();
            if (changed.Count == 0) { noProperty.Add(row.Key); continue; }
            foreach (var path in changed)
            {
                if (!bound.TryGetValue(path, out var rows)) bound[path] = rows = new();
                rows.Add(row.Key);
                var leaf = path[(path.LastIndexOf('.') + 1)..];
                if (!WiringScanExemptions.Contains(path) && !Regex.IsMatch(text, @"\." + Regex.Escape(leaf) + @"\b") && !Regex.IsMatch(text, @"\b" + Regex.Escape(leaf) + @"\s*=")) dead.Add($"{row.Key} -> {path}");
            }
            row.Set(row.Get()); // keep going with the new value; later rows start from a changed object, like the real audit
        }
        var shared = bound.Where(p => p.Value.Count > 1 && !WiringSharedProperties.Contains(p.Key)).Select(p => $"{p.Key}: {string.Join("+", p.Value)}").ToList();
        Check("every settings row changes a stored property", noProperty.Count == 0, string.Join(", ", noProperty));
        Check("every stored property a row changes is read by code", dead.Count == 0, string.Join("; ", dead));
        Check("no two rows write the same stored property", shared.Count == 0, string.Join("; ", shared));
    }

    private static bool WiringMachineRow(string key) => key == "vst.device" || key == "vst.input" || key.StartsWith("vst.asio.", StringComparison.Ordinal);

    /// <summary>A settings file with one stored value forced out of range (or to a bad string) is repaired by the load, so no row ever shows an impossible value.</summary>
    private static void TestSettingsLoadBoundsEveryRow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tabforge-bounds-{Environment.ProcessId}.json");
        var unbounded = new List<string>();
        var defaultsLost = new List<string>();
        try
        {
            var fresh = new AppSettings();
            SettingsFileService.SaveAtomic(path, fresh);
            var loadedDefaults = SettingsFileService.Load(path);
            var wanted = SettingsCatalog.Build(fresh).Where(d => d.Kind != SettingKind.Button && !WiringMachineRow(d.Key)).ToDictionary(d => d.Key, d => Format(d.Get()));
            foreach (var row in SettingsCatalog.Build(loadedDefaults).Where(d => wanted.ContainsKey(d.Key)))
                if (wanted[row.Key] != Format(row.Get())) defaultsLost.Add($"{row.Key} ({wanted[row.Key]} -> {Format(row.Get())})");

            foreach (var row in SettingsCatalog.Build(new AppSettings()).Where(d => d.Kind is SettingKind.Number or SettingKind.Choice or SettingKind.Colour && !WiringMachineRow(d.Key)))
            {
                var settings = new AppSettings();
                var rowNow = SettingsCatalog.Build(settings).First(d => d.Key == row.Key);
                var before = SettingsLeaves(settings);
                var value = AlternativeValue(rowNow);
                if (value is null) continue;
                rowNow.Set(value);
                var after = SettingsLeaves(settings);
                var leaf = after.Where(p => !before.TryGetValue(p.Key, out var old) || old != p.Value).Select(p => p.Key).FirstOrDefault();
                if (leaf is null) continue;
                var variants = row.Kind == SettingKind.Number
                    ? new[] { (JsonNode?)JsonValue.Create(row.Max * 1000 + 1000), JsonValue.Create(-(row.Max * 1000 + 1000)) }
                    : new[] { (JsonNode?)JsonValue.Create("zz-not-valid") };
                foreach (var bad in variants)
                {
                    var root = JsonSerializer.SerializeToNode(settings)!.AsObject();
                    var parts = leaf.Split('.');
                    var node = root;
                    foreach (var part in parts[..^1]) node = node[part]!.AsObject();
                    // A string row stored as a number (or the reverse) is a different shape: only vary the kind the property already has.
                    var isNumber = node[parts[^1]] is JsonValue v && v.TryGetValue<double>(out _); var isString = node[parts[^1]] is JsonValue s && s.TryGetValue<string>(out _);
                    if (row.Kind == SettingKind.Number ? !isNumber : !isString) continue;
                    node[parts[^1]] = bad?.DeepClone();
                    File.WriteAllText(path, root.ToJsonString());
                    object? got;
                    try { got = SettingsCatalog.Build(SettingsFileService.Load(path)).First(d => d.Key == row.Key).Get(); }
                    catch (InvalidDataException ex) { unbounded.Add($"{row.Key} ({bad?.ToJsonString()} rejects the whole file: {ex.GetBaseException().Message})"); continue; } // Not logged: the failure is the check result
                    var ok = row.Kind switch
                    {
                        SettingKind.Number => Convert.ToDouble(got, CultureInfo.InvariantCulture) >= row.Min - 1e-6 && Convert.ToDouble(got, CultureInfo.InvariantCulture) <= row.Max + 1e-6,
                        SettingKind.Choice => row.Choices.Any(c => string.Equals(c, got?.ToString(), StringComparison.OrdinalIgnoreCase)),
                        _ => SettingsColor.IsValid(got?.ToString()),
                    };
                    if (!ok) unbounded.Add($"{row.Key} ({bad?.ToJsonString()} -> {Format(got)})");
                }
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } } // Not logged: test cleanup of a temporary file.
        Check("every row keeps its default through save and load", defaultsLost.Count == 0, string.Join("; ", defaultsLost));
        Check("a settings file with an out-of-range or invalid value is repaired on load, for every number, choice and colour row", unbounded.Count == 0, string.Join("; ", unbounded));
    }

    /// <summary>Reset all and Import fill the staged settings from another settings object: every row must end up with that object's value, including the plug-in, video and keyboard rows.</summary>
    private static void TestSettingsResetAndImportCoverEveryRow()
    {
        var source = new AppSettings();
        var rows = SettingsCatalog.Build(source).Where(d => d.Kind != SettingKind.Button && !WiringMachineRow(d.Key)).ToList();
        foreach (var row in rows)
            if (AlternativeValue(row) is { } v) row.Set(v);
        var wanted = SettingsCatalog.Build(source).Where(d => d.Kind != SettingKind.Button && !WiringMachineRow(d.Key)).ToDictionary(d => d.Key, d => d.Get());
        var target = new AppSettings();
        TabForge.Views.PreferencesWindow.CopySettings(source, target);
        var missed = SettingsCatalog.Build(target).Where(d => wanted.TryGetValue(d.Key, out var w) && !SameValue(d.Get(), w)).Select(d => d.Key).ToList();
        Check("Reset all and Import copy every settings row", missed.Count == 0, string.Join(", ", missed));

        // Approvals, trust records and the quarantine list are not rows: an imported file never brings them.
        var withApprovals = new AppSettings();
        withApprovals.Plugins.ApprovedPluginPaths.Add(@"C:\Plugins\Approved.vst3");
        withApprovals.Plugins.Quarantined.Add("Crashy");
        var staged = new AppSettings();
        TabForge.Views.PreferencesWindow.CopySettings(withApprovals, staged);
        Check("Import and Reset all never copy plug-in approvals or the quarantine list", staged.Plugins.ApprovedPluginPaths.Count == 0 && staged.Plugins.Quarantined.Count == 0);
    }

    /// <summary>The default of every row is a value the row accepts (inside its range, one of its choices), and setting a row back to its default restores it exactly (what "Reset" does).</summary>
    private static void TestSettingsRowsResetToDefault()
    {
        var defaults = SettingsCatalog.Build(new AppSettings()).Where(d => d.Kind != SettingKind.Button && !WiringMachineRow(d.Key)).ToDictionary(d => d.Key, d => d.Get());
        var settings = new AppSettings();
        var rows = SettingsCatalog.Build(settings).Where(d => defaults.ContainsKey(d.Key)).ToList();
        var outside = new List<string>();
        var notRestored = new List<string>();
        foreach (var row in rows)
        {
            var want = defaults[row.Key];
            if (row.Kind == SettingKind.Number && Convert.ToDouble(want, CultureInfo.InvariantCulture) is var n && (n < row.Min - 1e-6 || n > row.Max + 1e-6)) outside.Add($"{row.Key} default {Format(want)} not in {row.Min}..{row.Max}");
            if (row.Kind == SettingKind.Choice && !row.Choices.Any(c => string.Equals(c, want?.ToString(), StringComparison.OrdinalIgnoreCase))) outside.Add($"{row.Key} default '{want}' is not a choice");
            if (row.Kind == SettingKind.Colour && !SettingsColor.IsValid(want?.ToString())) outside.Add($"{row.Key} default '{want}' is not a colour");
            if (AlternativeValue(row) is { } alt) row.Set(alt);
        }
        // Reset in page order, as the Settings window does, then compare with the defaults.
        foreach (var row in rows.OrderBy(r => r.Order)) row.Set(defaults[row.Key]);
        foreach (var row in SettingsCatalog.Build(settings).Where(d => defaults.ContainsKey(d.Key)))
            if (!SameValue(row.Get(), defaults[row.Key])) notRestored.Add($"{row.Key} (default {Format(defaults[row.Key])}, after reset {Format(row.Get())})");
        Check("every row's default is inside the row's range and choices", outside.Count == 0, string.Join("; ", outside));
        Check("setting every row back to its default restores the default exactly", notRestored.Count == 0, string.Join("; ", notRestored));
    }
}
