using System.IO;
using System.Windows;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Views;

// Owns: the FX chain window's "load an .RfxChain file" step: which plug-ins count as known, the offer to scan when some
//       are missing, and the one summary message.
// Does not own: parsing and matching (ReaperChainImporter), plug-in trust (PluginTrust decides at load, as for any chain),
//       putting the chain on the track (FxChainWindow).
// Tests: TestReaperChainImport (parser, matching, summary).
public static class RfxChainImportFlow
{
    /// <summary>
    /// Every plug-in TabForge knows: the remembered list, this run's scan, identified and approved plug-ins and the ones the
    /// open song already uses. Being listed here only lets a chain entry find its file; loading stays subject to plug-in trust.
    /// </summary>
    public static List<VstPluginInfo> KnownPlugins(PluginSettings settings, IEnumerable<PluginSlot> inUse)
    {
        static string FormatOf(string path) => path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? "VST3" : "VST2";
        return settings.ScanCache.Concat(settings.Probed).Where(k => k is not null && k.Path.Length > 0)
            .Select(k => new VstPluginInfo(k.Name.Length > 0 ? k.Name : Path.GetFileNameWithoutExtension(k.Path), k.Path, k.Format.Length > 0 ? k.Format : FormatOf(k.Path), k.Vendor, k.Role))
            .Concat(settings.LastScan)
            .Concat(settings.ApprovedPluginPaths.Where(p => !string.IsNullOrEmpty(p)).Select(p => new VstPluginInfo(Path.GetFileNameWithoutExtension(p), p, FormatOf(p))))
            .Concat(inUse.Where(s => s.Path.Length > 0 && !s.Unavailable).Select(s => new VstPluginInfo(s.Name, s.Path, s.Format.Length > 0 ? s.Format : FormatOf(s.Path), s.Vendor)))
            .ToList();
    }

    /// <summary>
    /// Loads the file; null when it is unreadable or no chain file. Shows the summary when something was left out. A scan runs
    /// off the UI thread and reports through <paramref name="status"/>; the caller keeps a second import from starting meanwhile.
    /// </summary>
    public static async Task<List<PluginSlot>?> LoadAsync(Window owner, string path, IFxChainHost host, Action<string> status)
    {
        var settings = host.PluginSettings;
        var inUse = host.Tracks.SelectMany(t => t.Rig.Plugins).ToList();
        if (ReaperChainImporter.Read(path) is not { } parsed) return null;
        var imported = ReaperChainImporter.Build(parsed, KnownPlugins(settings, inUse));
        // Some are unknown: scan the user's plug-in folders unless this run already did, then offer the standard folders once.
        if (imported.Missing > 0 && settings.LastScan.Count == 0 && VstScannerService.RootsFor(settings).Any()) imported = await Rescan();
        if (imported.Missing > 0 && !settings.ScanStandardFolders
            && ThemedMessageBox.Show(owner, $"{imported.Missing} plug-in{(imported.Missing == 1 ? " in this chain is" : "s in this chain are")} not in your plug-in list.\n\n"
                + "Scan the standard VST folders for them now? This turns on Settings > Audio & Plug-ins > Also scan the standard VST folders.",
                "FX chain import", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            VstScannerService.EnableStandardFolders(settings);
            host.SaveSettings();
            imported = await Rescan();
        }
        if (imported.Report.Length > 0 && owner.IsLoaded) ThemedMessageBox.Show(owner, imported.Report, "FX chain import", MessageBoxButton.OK, MessageBoxImage.Information);
        return imported.Chain;

        async Task<ReaperChainImporter.Imported> Rescan()
        {
            var roots = VstScannerService.RootsFor(settings).ToList();
            var report = new Progress<(int Found, string Folder)>(p => status($"Scanning… {p.Found} found · {p.Folder}"));
            try
            {
                var found = await Task.Run(() => VstScannerService.Scan(roots, CancellationToken.None, report, settings));
                if (settings.RememberScan)
                {
                    settings.ScanCache = found.Select(f => new KnownPlugin { Name = f.Name, Path = f.Path, Format = f.Format, Vendor = f.Vendor, Role = f.Role }).ToList();
                    host.SaveSettings();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { Services.Trace.Error(Services.Trace.Engine, "FX chain import: scan folders: " + ex.Message); }
            status("");
            return ReaperChainImporter.Build(parsed, KnownPlugins(settings, inUse));
        }
    }
}
