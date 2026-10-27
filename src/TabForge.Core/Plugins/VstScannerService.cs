using System.IO;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Plugins;

// Owns: discovering VST3 bundles and VST2 DLLs by file inspection, without loading plug-in code.
// Does not own: plug-in trust decisions (PluginTrust) and hosting.
// Tests: CheckVstReparseLoop, MixerWindowTests.
/// <summary>
/// Discovers VST3 bundles and VST2 DLLs; it never loads or executes plug-in code (a DLL counts as VST2 only when
/// its export table lists the VST2 entry point, read straight from the file).
/// </summary>
public static class VstScannerService
{
    /// <summary>
    /// Session cache: the last scan's roots key + results (single entry, so memory is bounded by
    /// <see cref="InputLimits.MaxVstPlugins"/>). Avoids re-walking up to 50k entries when the Add plug-in
    /// window re-queries the same folders. TTL 5 minutes; Rescan callers pass <c>forceRefresh: true</c>.
    /// </summary>
    private static readonly object SessionGate = new();
    private static string? _sessionRootsKey;
    private static IReadOnlyList<VstPluginInfo>? _sessionResults;
    private static DateTime _sessionAtUtc;
    internal static readonly TimeSpan SessionCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>Clears the session cache (the Rescan button calls this or passes forceRefresh).</summary>
    public static void ClearSessionCache()
    {
        lock (SessionGate) { _sessionRootsKey = null; _sessionResults = null; }
    }

    /// <summary>Session-cache key of <paramref name="roots"/> (sorted, case-insensitive; capped).</summary>
    internal static string SessionKey(IEnumerable<string> roots)
    {
        var list = roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r =>
        {
            try { return Path.GetFullPath(r.Trim()); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or System.Security.SecurityException) { return r.Trim(); }
        }).Distinct(PathComparer.Default).OrderBy(r => r, PathComparer.Default).Take(1024).ToList();
        return string.Join('\n', list);
    }

    /// <summary>Bounded entry point with an explicit refresh flag (additive; existing overloads cache transparently).</summary>
    public static IReadOnlyList<VstPluginInfo> Scan(IEnumerable<string> roots, CancellationToken cancellationToken, PluginTrust trust, IPluginFileTrust files, bool forceRefresh) =>
        Scan(roots, cancellationToken, null, trust, files, null, forceRefresh);

    /// <summary>Bounded entry point with progress and an explicit refresh flag (additive).</summary>
    public static IReadOnlyList<VstPluginInfo> Scan(IEnumerable<string> roots, CancellationToken cancellationToken, IProgress<(int Found, string Folder)>? progress, PluginTrust trust, IPluginFileTrust files,
        PluginSettings? remember, bool forceRefresh)
    {
        ArgumentNullException.ThrowIfNull(roots);
        // RememberScan on with a populated persisted cache: serve it without touching the disk
        // (default off stays safe by always scanning). Bounded by MaxVstPlugins; Rescan bypasses via forceRefresh.
        if (!forceRefresh && remember is { RememberScan: true } && remember.ScanCache is { Count: > 0 } persisted)
        {
            var cached = persisted.Where(k => k is not null && !string.IsNullOrWhiteSpace(k.Path))
                .Take(InputLimits.MaxVstPlugins)
                .Select(k => new VstPluginInfo(
                    string.IsNullOrWhiteSpace(k.Name) ? Path.GetFileNameWithoutExtension(k.Path) : k.Name,
                    k.Path, string.IsNullOrWhiteSpace(k.Format) ? "VST3" : k.Format, k.Vendor ?? "", k.Role ?? "", k.ClassId ?? ""))
                .ToList();
            remember.LastScan = cached;
            lock (SessionGate) { _sessionRootsKey = SessionKey(roots); _sessionResults = cached; _sessionAtUtc = DateTime.UtcNow; }
            return cached;
        }
        if (!forceRefresh)
        {
            var key = SessionKey(roots) + (remember is { ScanClap: true } ? "|clap" : "");
            lock (SessionGate)
            {
                if (_sessionResults is not null && string.Equals(_sessionRootsKey, key, StringComparison.Ordinal) &&
                    DateTime.UtcNow - _sessionAtUtc < SessionCacheTtl)
                {
                    if (remember is not null) remember.LastScan = _sessionResults;
                    return _sessionResults;
                }
            }
        }
        var fresh = ScanUncached(roots, cancellationToken, progress, trust, files, remember);
        lock (SessionGate) { _sessionRootsKey = SessionKey(roots) + (remember is { ScanClap: true } ? "|clap" : ""); _sessionResults = fresh; _sessionAtUtc = DateTime.UtcNow; }
        return fresh;
    }

    /// <summary>Bounded traversal entry point, also used by focused filesystem regression tests.</summary>
    public static IReadOnlyList<VstPluginInfo> Scan(IEnumerable<string> roots, CancellationToken cancellationToken, PluginTrust trust, IPluginFileTrust files) =>
        Scan(roots, cancellationToken, null, trust, files);

    /// <summary>
    /// Scan with progress: (plug-ins found so far, folder being read). <paramref name="remember"/> receives the finished list as its
    /// <see cref="PluginSettings.LastScan"/>, so the arrangement view can offer VST instruments.
    /// Session-cached (5-minute TTL, same roots); Rescan passes <c>forceRefresh: true</c> via the overload above.
    /// </summary>
    public static IReadOnlyList<VstPluginInfo> Scan(IEnumerable<string> roots, CancellationToken cancellationToken, IProgress<(int Found, string Folder)>? progress, PluginTrust trust, IPluginFileTrust files,
        PluginSettings? remember = null) =>
        Scan(roots, cancellationToken, progress, trust, files, remember, forceRefresh: false);

    /// <summary>Uncached filesystem walk (session cache + RememberScan handled by the <c>forceRefresh</c> overload).</summary>
    private static IReadOnlyList<VstPluginInfo> ScanUncached(IEnumerable<string> roots, CancellationToken cancellationToken, IProgress<(int Found, string Folder)>? progress, PluginTrust trust, IPluginFileTrust files,
        PluginSettings? remember)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var scan = new ScanState { IncludeClap = remember is { ScanClap: true } };
        QueueRoots(roots, scan);
        while (scan.Pending.Count > 0 && scan.EntriesVisited < InputLimits.MaxVstScanEntries && scan.Results.Count < InputLimits.MaxVstPlugins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = scan.Pending.Pop();
            progress?.Report((scan.Results.Count, directory));
            ScanDirectory(directory, depth, scan, cancellationToken);
        }
        if (scan.UnreadableCount > 0) Services.Trace.Error(Services.Trace.Engine, $"plug-in scan: {scan.UnreadableCount} folders or entries could not be read; first: {scan.FirstUnreadable}");

        cancellationToken.ThrowIfCancellationRequested();
        var list = scan.Results.Values.OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plugin => plugin.Path, StringComparer.OrdinalIgnoreCase).ToList();
        trust.RecordScan(list.Where(plugin => !plugin.IsUnsupported32).ToList(), cancellationToken, progress, files);   // fingerprint the finds in user-writable folders: the trust baseline
        RememberScanResult(remember, list);
        return list;
    }

    /// <summary>What a folder walk accumulates: the folders still to read, those already seen, the plug-ins found and the unreadable entries.</summary>
    private sealed class ScanState
    {
        public readonly Stack<(string Path, int Depth)> Pending = new();
        public readonly HashSet<string> Queued = new(PathComparer.Default);
        public readonly HashSet<string> Visited = new(PathComparer.Default);
        public readonly Dictionary<string, VstPluginInfo> Results = new(PathComparer.Default);
        public bool IncludeClap;   // CLAP is opt-in: the .clap files are listed only when the user turned it on
        public int EntriesVisited;
        public int UnreadableCount;   // unreadable folders and entries are counted, and one summary line is logged after the scan
        public string? FirstUnreadable;
        public void Unreadable(string what, string path, Exception ex) { UnreadableCount++; FirstUnreadable ??= $"{what} {path}: {ex.Message}"; }
    }

    private static void QueueRoots(IEnumerable<string> roots, ScanState scan)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            try
            {
                var fullPath = Path.GetFullPath(root);
                if (scan.Queued.Add(fullPath)) scan.Pending.Push((fullPath, 0));
            }
            catch (Exception ex) when (IsFilesystemFailure(ex)) { Services.Trace.Error(Services.Trace.Engine, "VST scan: resolve root: " + ex.Message); }
        }
    }

    /// <summary>Reads one folder: queues its sub-folders and records the plug-ins in it.</summary>
    private static void ScanDirectory(string directory, int depth, ScanState scan, CancellationToken cancellationToken)
    {
        FileAttributes directoryAttributes;
        try { directoryAttributes = File.GetAttributes(directory); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { Services.Trace.Write(Services.Trace.Engine, "plug-in scan: folder not present, skipped: " + directory); return; }   // an optional default folder that does not exist is not an error
        catch (Exception ex) when (IsFilesystemFailure(ex)) { scan.Unreadable("folder attributes", directory, ex); return; }
        if ((directoryAttributes & FileAttributes.Directory) == 0 ||
            (directoryAttributes & FileAttributes.ReparsePoint) != 0 ||
            !scan.Visited.Add(directory)) return;

        IEnumerable<string> children;
        try { children = Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly); }
        catch (Exception ex) when (IsFilesystemFailure(ex)) { scan.Unreadable("list folder", directory, ex); return; }

        try
        {
            foreach (var entry in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scan.EntriesVisited > InputLimits.MaxVstScanEntries) break;
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception ex) when (IsFilesystemFailure(ex)) { scan.Unreadable("entry attributes", entry, ex); continue; }

                // Ignore junctions and symlinks entirely, including links that resemble bundles.
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (ScanEntry(entry, (attributes & FileAttributes.Directory) != 0, depth, scan)) break;
            }
        }
        catch (Exception ex) when (IsFilesystemFailure(ex)) { Services.Trace.Error(Services.Trace.Engine, "VST scan: read folder: " + ex.Message); }
    }

    /// <summary>Records a VST3 bundle or a VST2 library, or queues a sub-folder. True when the plug-in limit is reached and the scan of the folder ends.</summary>
    private static bool ScanEntry(string entry, bool isDirectory, int depth, ScanState scan)
    {
        if (string.Equals(Path.GetExtension(entry), ".vst3", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(entry);
            var (vendor, role, classId) = Vst3ModuleInfo(fullPath);
            scan.Results.TryAdd(fullPath, new VstPluginInfo(Path.GetFileNameWithoutExtension(entry), fullPath, "VST3", vendor, role, classId));
            return scan.Results.Count >= InputLimits.MaxVstPlugins;
        }
        if (scan.IncludeClap && string.Equals(Path.GetExtension(entry), ".clap", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(entry);
            scan.Results.TryAdd(fullPath, new VstPluginInfo(Path.GetFileNameWithoutExtension(entry), fullPath, PluginFormats.Clap, ""));
            return scan.Results.Count >= InputLimits.MaxVstPlugins;
        }
        var vst2Here = PluginScanFolders.Vst2Supported(HostSystem.Current);   // VST2 hosting is Windows-only: a .dll elsewhere is never a plug-in
        if (vst2Here && !isDirectory && string.Equals(Path.GetExtension(entry), ".dll", StringComparison.OrdinalIgnoreCase)
            && PeExports.IsVst2Plugin(entry))
        {
            var fullPath = Path.GetFullPath(entry);
            scan.Results.TryAdd(fullPath, new VstPluginInfo(Path.GetFileNameWithoutExtension(entry), fullPath, "VST2", Vst2Vendor(fullPath)));
            return scan.Results.Count >= InputLimits.MaxVstPlugins;
        }

        if (vst2Here && !isDirectory && string.Equals(Path.GetExtension(entry), ".dll", StringComparison.OrdinalIgnoreCase) && PeExports.IsVst2Plugin32(entry))
        {
            var fullPath = Path.GetFullPath(entry);
            scan.Results.TryAdd(fullPath, new VstPluginInfo(Path.GetFileNameWithoutExtension(entry), fullPath, VstPluginInfo.Format32, Vst2Vendor(fullPath)));
            return scan.Results.Count >= InputLimits.MaxVstPlugins;
        }

        if (isDirectory && depth < InputLimits.MaxVstScanDepth)
        {
            var fullPath = Path.GetFullPath(entry);
            if (scan.Queued.Add(fullPath)) scan.Pending.Push((entry, depth + 1));
        }
        return false;
    }

    /// <summary>RememberScan on: persists a bounded copy of the list for next time (off: nothing is written). The list itself is always kept as the last scan.</summary>
    private static void RememberScanResult(PluginSettings? remember, List<VstPluginInfo> list)
    {
        if (remember is null) return;
        remember.LastScan = list;
        if (!remember.RememberScan) return;
        try
        {
            remember.ScanCache = list.Take(InputLimits.MaxVstPlugins)
                .Select(p => new KnownPlugin { Name = p.Name ?? "", Path = p.Path ?? "", Format = p.Format ?? "", Vendor = p.Vendor ?? "", Role = p.Role ?? "", ClassId = p.ClassId ?? "" })
                .ToList();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Services.Trace.Error(Services.Trace.Engine, "VST scan: remember list: " + ex.Message); }
    }

    /// <summary>The folders to scan: the user's own folders, plus the standard ones when that option is on.</summary>
    public static IEnumerable<string> RootsFor(PluginSettings settings) =>
        (settings.ScanStandardFolders ? GetDefaultRoots().Concat(CommonRoots(settings)) : Array.Empty<string>())
        .Concat(settings.ScanClap ? ClapRoots() : Array.Empty<string>())
        .Concat(FolderListText.Clean(settings.Folders).Where(IsLocalScanFolder))
        .Distinct(PathComparer.Default);

    /// <summary>
    /// False for a UNC (\\server\share) or device form: a scan does not read network shares or device paths. Such a folder is dropped
    /// from the settings and from the scan; the plug-in itself can still be approved by its path.
    /// </summary>
    public static bool IsLocalScanFolder(string? folder)
    {
        var text = (folder ?? "").Trim().Replace('/', '\\');
        return !text.StartsWith(@"\\", StringComparison.Ordinal) && !MediaPathPolicy.IsDeviceForm(text);
    }

    public static IEnumerable<string> StandardFolders => GetDefaultRoots();

    /// <summary>The standard folders as shown to the user: with environment names, never an expanded user path (same list as <see cref="StandardFolders"/>).</summary>
    public static readonly string[] StandardFolderLabels = PluginScanFolders.Labels(HostSystem.Current).ToArray();

    /// <summary>The standard CLAP folders that exist on this machine (scanned only when the user turned CLAP on).</summary>
    public static IEnumerable<string> ClapRoots() => ExpandFolders(PluginScanFolders.Clap(PluginScanInputs.Current()));

    /// <summary>The standard CLAP folders as shown to the user.</summary>
    public static IReadOnlyList<string> ClapFolderLabels => PluginScanFolders.ClapLabels(HostSystem.Current);

    /// <summary>The Add plug-in window's "Scan the standard VST folders" button: turns the option on (the user's choice, off by default).</summary>
    public static void EnableStandardFolders(PluginSettings settings) => settings.ScanStandardFolders = true;

    /// <summary>The built-in list of usual VST folders (environment variables are expanded when scanning).</summary>
    public static readonly string[] DefaultCommonFolders =
    {
        @"%PROGRAMFILES(X86)%\Steinberg\VstPlugins", @"%PROGRAMFILES%\Steinberg\VstPlugins", @"%COMMONPROGRAMFILES%\VST3", @"%COMMONPROGRAMFILES(X86)%\VST3",
        @"%LOCALAPPDATA%\Programs\Common\VST3", @"C:\Program Files\VSTPlugins", @"C:\Program Files\Steinberg", @"C:\Program Files (x86)\VSTPlugIns",
        @"C:\Program Files (x86)\Steinberg", @"C:\Program Files\Common Files\VST2", @"C:\Program Files\VstPlugins",
    };

    /// <summary>Expands %VARS%, drops blanks, missing folders and duplicates.</summary>
    public static List<string> ExpandFolders(IEnumerable<string> patterns)
    {
        var seen = new HashSet<string>(PathComparer.Default); var list = new List<string>();
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try
            {
                var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.Trim()).TrimEnd('\\', '/'));
                if (Directory.Exists(full) && seen.Add(full)) list.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { Services.Trace.Error(Services.Trace.Engine, "VST scan: parse scan roots: " + ex.Message); }
        }
        return list;
    }

    /// <summary>The existing common folders: the user's list, or the built-in one when it is empty.</summary>
    public static List<string> CommonRoots(PluginSettings settings)
    {
        IEnumerable<string> folders = settings.CommonFolders.Count > 0 ? settings.CommonFolders : DefaultCommonFolders;
        return ExpandFolders(FolderListText.Clean(folders).Where(IsLocalScanFolder));
    }

    private static IEnumerable<string> GetDefaultRoots() => PluginScanFolders.Vst3(PluginScanInputs.Current());

    /// <summary>Vendor, role and class id (32 hex digits, upper case) of a VST3 bundle from its moduleinfo.json (read as text; the plug-in is not loaded).</summary>
    public static (string Vendor, string Role, string ClassId) Vst3ModuleInfo(string bundle)
    {
        try
        {
            var info = Path.Combine(bundle, "Contents", "Resources", "moduleinfo.json");
            if (!File.Exists(info) || new FileInfo(info).Length > 1_000_000) return ("", "", "");
            using var doc = System.Text.Json.JsonDocument.Parse(TabForge.Services.InputLimits.ReadBoundedBytes(info, 1_000_000, "moduleinfo.json"),
                new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
            var vendor = doc.RootElement.TryGetProperty("Factory Info", out var factory) && factory.TryGetProperty("Vendor", out var v) ? v.GetString() ?? "" : "";
            var role = ""; var classId = "";
            if (doc.RootElement.TryGetProperty("Classes", out var classes))
                foreach (var c in classes.EnumerateArray())
                {
                    if (!c.TryGetProperty("Category", out var cat) || cat.GetString() != "Audio Module Class") continue;
                    var subs = c.TryGetProperty("Sub Categories", out var s) ? s.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
                    role = subs.Any(x => x.Contains("Instrument", StringComparison.OrdinalIgnoreCase)) ? "Instrument" : "Effect";
                    if (vendor.Length == 0 && c.TryGetProperty("Vendor", out var cv)) vendor = cv.GetString() ?? "";
                    if (c.TryGetProperty("CID", out var cid)) classId = Vst3ClassId.Normalize(cid.GetString());
                    break;
                }
            return (vendor, role, classId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { return ("", "", ""); } // Not logged: vendor is optional metadata: left blank
    }

    /// <summary>Vendor of a VST2 DLL from its version resource (read from the file, not executed).</summary>
    public static string Vst2Vendor(string dll)
    {
        try { return System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).CompanyName?.Trim() ?? ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return ""; } // Not logged: vendor is optional metadata: left blank
    }

    private static bool IsFilesystemFailure(Exception exception) => exception is
        UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException or System.Security.SecurityException;
}
