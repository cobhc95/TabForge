using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Plugins;

// Owns: finding out the maker of the VST2 plug-ins a chain file matches whose scan gave no vendor, so the import's vendor guard can compare it.
// Does not own: the identify rules and the throwaway probe process (PluginCatalog), matching (ReaperChainImporter), trust (PluginTrust decides who may be probed).
// Tests: no dedicated test class.
public static class ChainVendorLookup
{
    /// <summary>
    /// Asks each trusted plug-in that carries saved settings in the file and has no vendor in <paramref name="catalog"/> for its vendor (one probe each, remembered in
    /// <paramref name="settings"/>), up to <see cref="MaxParallelProbes"/> at once. An unapproved file is never run. Blocking: call it off the UI thread; <paramref name="cancel"/> stops
    /// the probes not yet started (OperationCanceledException).
    /// </summary>
    public static void Fill(ReaperChainImporter.Parsed parsed, IReadOnlyList<VstPluginInfo> catalog, PluginSettings settings, PluginTrust trust, IPluginFileTrust files, CancellationToken cancel = default)
    {
        var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toProbe = new List<string>();
        foreach (var e in parsed.Entries)
        {
            if (e.State is not { Length: > 0 } || e.Vendor.Length == 0 || ReaperChainImporter.Match(e, catalog) is not { Vendor.Length: 0, Format: "VST2" } known) continue;
            if (asked.Add(known.Path) && trust.IsTrusted(known.Path, settings, files)) toProbe.Add(known.Path);
        }
        Parallel.ForEach(toProbe, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelProbes, CancellationToken = cancel },
            path => PluginCatalog.Describe(path, settings, trust, files, timeoutMs: ProbeMs));
    }

    private const int ProbeMs = 5000, MaxParallelProbes = 4;
}
