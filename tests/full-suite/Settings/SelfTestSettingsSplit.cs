using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TabForge.Services;

namespace TabForge;

/// <summary>Guards the file split of SettingsCatalog and AppSettings: catalogue ids, order and pages, and the settings JSON, are unchanged.</summary>
public static partial class SelfTest
{
    // Re-recorded when the Band view lane/playhead rows, track lines and the Basic/Advanced placement of the Timeline rows changed.
    private const string CatalogSnapshotHash = "85F5A11014ADD318";
    private const int CatalogSnapshotCount = 216;
    private const string DefaultJsonSnapshotHash = "7D57A7EFE5724FB5";
    private const string PopulatedJsonSnapshotHash = "32178CA59C72E506";

    private static string ShortHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    /// <summary>Changes every simple property reachable in the settings graph (bool/int/double/string) by a fixed rule, so the JSON of a populated object is deterministic.</summary>
    private static void PopulateSettings(object node, int depth)
    {
        if (depth > 3) return;
        foreach (var p in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (!p.CanWrite || p.GetIndexParameters().Length > 0) continue;
            var t = p.PropertyType;
            try
            {
                var v = p.GetValue(node);
                if (t == typeof(bool)) p.SetValue(node, !(bool)v!);
                else if (t == typeof(int)) p.SetValue(node, (int)v! + 1);
                else if (t == typeof(double)) p.SetValue(node, (double)v! + 0.5);
                else if (t == typeof(string)) p.SetValue(node, (v as string ?? "") + "x");
                else if (v is not null && t.IsClass && t.Namespace is { } ns && ns.StartsWith("TabForge", StringComparison.Ordinal)) PopulateSettings(v, depth + 1);
            }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException or InvalidCastException) { /* validated property: leave as is */ }
        }
    }

    private static void TestSettingsFileSplitSnapshots()
    {
        var catalog = SettingsCatalog.Build(new AppSettings());
        var rows = string.Join('\n', catalog.Select(d => $"{d.Key}|{d.Category}|{d.Group}|{d.More}|{d.Order}|{d.Kind}|{d.Title}"));
        var defaults = JsonSerializer.Serialize(new AppSettings());
        var populated = new AppSettings();
        PopulateSettings(populated, 0);
        var populatedJson = JsonSerializer.Serialize(populated);
        var h1 = ShortHash(rows); var h2 = ShortHash(defaults); var h3 = ShortHash(populatedJson);
        Check("settings split: catalogue rows (id, page, group, order) unchanged", h1 == CatalogSnapshotHash && catalog.Count == CatalogSnapshotCount, $"{h1} count {catalog.Count}");
        Check("settings split: default settings JSON unchanged", h2 == DefaultJsonSnapshotHash, h2);
        Check("settings split: populated settings JSON unchanged", h3 == PopulatedJsonSnapshotHash, h3);
    }
}
