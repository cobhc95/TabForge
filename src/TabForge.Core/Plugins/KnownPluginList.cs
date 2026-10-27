using System.IO;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>
/// Every plug-in TabForge knows: the remembered list, this run's scan, identified and approved plug-ins and the ones the open song already uses.
/// Being listed here only lets a chain entry find its file; loading stays subject to plug-in trust.
/// Owns: building that list from the settings. Does not own: matching entries to it (ReaperChainImporter), trust (PluginTrust).
/// Tests: none dedicated(full suite), TabForge.Avalonia.Tests.FxRfxChainTests.
/// </summary>
public static class KnownPluginList
{
    public static List<VstPluginInfo> Of(PluginSettings settings, IEnumerable<PluginSlot> inUse)
    {
        static string FormatOf(string path) => PluginFormats.Of(path);
        var list = settings.ScanCache.Concat(settings.Probed).Where(k => k is not null && k.Path.Length > 0)
            .Select(k => new VstPluginInfo(k.Name.Length > 0 ? k.Name : Path.GetFileNameWithoutExtension(k.Path), k.Path, k.Format.Length > 0 ? k.Format : FormatOf(k.Path), k.Vendor, k.Role, k.ClassId ?? ""))
            .Concat(settings.LastScan)
            .Concat(settings.ApprovedPluginPaths.Where(p => !string.IsNullOrEmpty(p)).Select(p => new VstPluginInfo(Path.GetFileNameWithoutExtension(p), p, FormatOf(p))))
            .Concat(inUse.Where(s => s.Path.Length > 0 && !s.Unavailable).Select(s => new VstPluginInfo(s.Name, s.Path, s.Format.Length > 0 ? s.Format : FormatOf(s.Path), s.Vendor)))
            .ToList();
        // A scan reads no vendor from most VST2 DLLs; the one a probe heard from the plug-in itself (Probed) fills the gap, so the vendor guard has something to compare.
        var vendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in list.Where(p => p.Vendor.Length > 0)) vendors.TryAdd(p.Path, p.Vendor);
        return list.Select(p => p.Vendor.Length == 0 && vendors.TryGetValue(p.Path, out var vendor) ? p with { Vendor = vendor } : p).ToList();
    }
}
