// Owns: the Add plug-in list's logic without UI types: the vendor and role the probed list adds to a plug-in, the filter by name or
//       vendor, the sort by column and direction, the row text (role "approve to identify" for a plug-in not yet approved), the status
//       line, and the merge of a common-folder scan into the list.
// Does not own: the window, search box, list and buttons (Views/PluginBrowserDialog.cs), scanning (VstScannerService), approval
//       (PluginTrust) or the settings store.
// Tests: no dedicated test class.
using TabForge.Plugins;

namespace TabForge.Presenters;

/// <summary>One list row: the text the columns show, and the plug-in it adds.</summary>
public sealed record PluginBrowserRow(string Name, string Vendor, string Format, string Role, VstPluginInfo Info);

/// <summary>
/// Stateless list logic: the view passes its current list, probed roles, not-approved set, search text and sort column each time.
/// Thread: UI thread only (the window calls it).
/// </summary>
public static class PluginBrowserListPresenter
{
    private const string ApproveToIdentify = "approve to identify";
    private const string Identifying = "…";

    /// <summary>The plug-in with the vendor and role the probed list knows, where its own file does not tell them.</summary>
    public static VstPluginInfo WithKnown(VstPluginInfo plugin, IEnumerable<(string Path, string Vendor, string Role)> probed)
    {
        var known = probed.FirstOrDefault(k => string.Equals(k.Path, plugin.Path, StringComparison.OrdinalIgnoreCase));
        return known.Path is null ? plugin : plugin with { Role = plugin.Role.Length > 0 ? plugin.Role : known.Role, Vendor = plugin.Vendor.Length > 0 ? plugin.Vendor : known.Vendor };
    }

    /// <summary>The rows shown: those whose name or vendor contains the trimmed query (ignoring case), sorted by column 1 vendor, 2 format, 3 role or else name, then by name.</summary>
    public static List<PluginBrowserRow> Rows(IReadOnlyList<VstPluginInfo> all, IEnumerable<(string Path, string Vendor, string Role)> probed,
        IReadOnlySet<string> notIdentified, string query, int sortColumn, bool ascending)
    {
        var text = query.Trim();
        var rows = all.Select(p => WithKnown(p, probed))
            .Where(p => text.Length == 0 || p.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || p.Vendor.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(p => new PluginBrowserRow(p.Name, p.Vendor, p.Format, RoleText(p, notIdentified), p));
        Func<PluginBrowserRow, string> key = sortColumn switch { 1 => r => r.Vendor, 2 => r => r.Format, 3 => r => r.Role, _ => r => r.Name };
        var ordered = ascending ? rows.OrderBy(key, StringComparer.CurrentCultureIgnoreCase) : rows.OrderByDescending(key, StringComparer.CurrentCultureIgnoreCase);
        return ordered.ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The status line: the notice to add folders or the count of plug-ins (and whether it is the remembered list).</summary>
    public static string StatusText(int count, bool foldersChosen, bool remembered) =>
        count == 0
            ? (foldersChosen ? "No plug-ins found in your folders." : "No plug-in folders yet: click Scan the standard VST folders, or Add folder…, or browse to a plug-in file.")
            : $"{count} plug-ins{(remembered ? " (remembered list)" : "")}. Double-click one, or select it and press Enter, to add it.";

    /// <summary>The list after a common-folder scan: the found plug-ins merged in, one per path, sorted by name.</summary>
    public static List<VstPluginInfo> MergeCommonScan(IEnumerable<VstPluginInfo> existing, IEnumerable<VstPluginInfo> found) =>
        existing.Concat(found).GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static string RoleText(VstPluginInfo plugin, IReadOnlySet<string> notIdentified) =>
        plugin.Role.Length > 0 ? plugin.Role : notIdentified.Contains(plugin.Path) ? ApproveToIdentify : Identifying;
}
