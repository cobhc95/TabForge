using System.IO;
using TabForge.Services;

namespace TabForge.Plugins;

/// <param name="Role">"Instrument", "Effect", or "" when not known yet (VST2 roles are probed when first used).</param>
public sealed record VstPluginInfo(string Name, string Path, string Format = "VST3", string Vendor = "", string Role = "")
{
    public override string ToString() => $"{Name}  ({Format})";
}

// Owns: discovering VST3 bundles and VST2 DLLs by file inspection, without loading plug-in code.
// Does not own: plug-in trust decisions (PluginTrust) and hosting.
// Tests: TestVstReparseLoop, TestMixer.
/// <summary>
/// Discovers VST3 bundles and VST2 DLLs; it never loads or executes plug-in code (a DLL counts as VST2 only when
/// its export table lists the VST2 entry point, read straight from the file).
/// </summary>
public static class VstScannerService
{
    public static IReadOnlyList<VstPluginInfo> Scan() => Scan(GetDefaultRoots(), CancellationToken.None);

    public static IReadOnlyList<VstPluginInfo> Scan(CancellationToken cancellationToken) =>
        Scan(GetDefaultRoots(), cancellationToken);

    /// <summary>Bounded traversal entry point, also used by focused filesystem regression tests.</summary>
    public static IReadOnlyList<VstPluginInfo> Scan(IEnumerable<string> roots, CancellationToken cancellationToken = default) =>
        Scan(roots, cancellationToken, null);

    /// <summary>
    /// Scan with progress: (plug-ins found so far, folder being read). <paramref name="remember"/> receives the finished list as its
    /// <see cref="PluginSettings.LastScan"/>, so the arrangement view can offer VST instruments.
    /// </summary>
    public static IReadOnlyList<VstPluginInfo> Scan(IEnumerable<string> roots, CancellationToken cancellationToken, IProgress<(int Found, string Folder)>? progress,
        PluginSettings? remember = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var pending = new Stack<(string Path, int Depth)>();
        var queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            try
            {
                var fullPath = Path.GetFullPath(root);
                if (queued.Add(fullPath)) pending.Push((fullPath, 0));
            }
            catch (Exception ex) when (IsFilesystemFailure(ex)) { }
        }

        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new Dictionary<string, VstPluginInfo>(StringComparer.OrdinalIgnoreCase);
        var entriesVisited = 0;
        while (pending.Count > 0 && entriesVisited < InputLimits.MaxVstScanEntries && results.Count < InputLimits.MaxVstPlugins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            progress?.Report((results.Count, directory));
            FileAttributes directoryAttributes;
            try { directoryAttributes = File.GetAttributes(directory); }
            catch (Exception ex) when (IsFilesystemFailure(ex)) { continue; }
            if ((directoryAttributes & FileAttributes.Directory) == 0 ||
                (directoryAttributes & FileAttributes.ReparsePoint) != 0 ||
                !visitedDirectories.Add(directory)) continue;

            IEnumerable<string> children;
            try { children = Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly); }
            catch (Exception ex) when (IsFilesystemFailure(ex)) { continue; }

            try
            {
                foreach (var entry in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++entriesVisited > InputLimits.MaxVstScanEntries) break;
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(entry); }
                    catch (Exception ex) when (IsFilesystemFailure(ex)) { continue; }

                    // Ignore junctions and symlinks entirely, including links that resemble bundles.
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (string.Equals(Path.GetExtension(entry), ".vst3", StringComparison.OrdinalIgnoreCase))
                    {
                        var fullPath = Path.GetFullPath(entry);
                        var (vendor, role) = Vst3ModuleInfo(fullPath);
                        results.TryAdd(fullPath, new VstPluginInfo(Path.GetFileNameWithoutExtension(entry), fullPath, "VST3", vendor, role));
                        if (results.Count >= InputLimits.MaxVstPlugins) break;
                        continue;
                    }
                    if (!isDirectory && string.Equals(Path.GetExtension(entry), ".dll", StringComparison.OrdinalIgnoreCase)
                        && PeExports.IsVst2Plugin(entry))
                    {
                        var fullPath = Path.GetFullPath(entry);
                        results.TryAdd(fullPath, new VstPluginInfo(Path.GetFileNameWithoutExtension(entry), fullPath, "VST2", Vst2Vendor(fullPath)));
                        if (results.Count >= InputLimits.MaxVstPlugins) break;
                        continue;
                    }

                    if (isDirectory && depth < InputLimits.MaxVstScanDepth)
                    {
                        var fullPath = Path.GetFullPath(entry);
                        if (queued.Add(fullPath)) pending.Push((entry, depth + 1));
                    }
                }
            }
            catch (Exception ex) when (IsFilesystemFailure(ex)) { }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var list = results.Values.OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plugin => plugin.Path, StringComparer.OrdinalIgnoreCase).ToList();
        PluginTrust.RecordScan(list, cancellationToken, progress);   // fingerprint the finds in user-writable folders: the trust baseline
        if (remember is not null) remember.LastScan = list;
        return list;
    }

    /// <summary>The folders to scan: the user's own folders, plus the standard ones when that option is on.</summary>
    public static IEnumerable<string> RootsFor(PluginSettings settings) =>
        (settings.ScanStandardFolders ? GetDefaultRoots().Concat(CommonRoots(settings)) : Array.Empty<string>())
        .Concat(settings.Folders.Where(f => !string.IsNullOrWhiteSpace(f)))
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> StandardFolders => GetDefaultRoots();

    /// <summary>The standard folders as shown to the user: with environment names, never an expanded user path (same list as <see cref="StandardFolders"/>).</summary>
    public static readonly string[] StandardFolderLabels =
    {
        @"%CommonProgramFiles%\VST3", @"%ProgramFiles%\Common Files\VST3", @"%ProgramFiles%\VstPlugins", @"%ProgramFiles%\Steinberg\VSTPlugins",
        @"%CommonProgramFiles%\VST2", @"%LocalAppData%\Programs\Common\VST3",
    };

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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var list = new List<string>();
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try
            {
                var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.Trim()).TrimEnd('\\', '/'));
                if (Directory.Exists(full) && seen.Add(full)) list.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { }
        }
        return list;
    }

    /// <summary>The existing common folders: the user's list, or the built-in one when it is empty.</summary>
    public static List<string> CommonRoots(PluginSettings settings) =>
        ExpandFolders(settings.CommonFolders.Count > 0 ? settings.CommonFolders : DefaultCommonFolders);

    private static IEnumerable<string> GetDefaultRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST3");
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files", "VST3");
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VstPlugins");
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steinberg", "VSTPlugins");
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST2");
        Add(roots, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "VST3");
        return roots;
    }

    private static void Add(HashSet<string> roots, string root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root)) return;
        roots.Add(parts.Aggregate(root, Path.Combine));
    }

    /// <summary>Vendor and role of a VST3 bundle from its moduleinfo.json (read as text; the plug-in is not loaded).</summary>
    public static (string Vendor, string Role) Vst3ModuleInfo(string bundle)
    {
        try
        {
            var info = Path.Combine(bundle, "Contents", "Resources", "moduleinfo.json");
            if (!File.Exists(info) || new FileInfo(info).Length > 1_000_000) return ("", "");
            using var doc = System.Text.Json.JsonDocument.Parse(TabForge.Services.InputLimits.ReadBoundedBytes(info, 1_000_000, "moduleinfo.json"),
                new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
            var vendor = doc.RootElement.TryGetProperty("Factory Info", out var factory) && factory.TryGetProperty("Vendor", out var v) ? v.GetString() ?? "" : "";
            var role = "";
            if (doc.RootElement.TryGetProperty("Classes", out var classes))
                foreach (var c in classes.EnumerateArray())
                {
                    if (!c.TryGetProperty("Category", out var cat) || cat.GetString() != "Audio Module Class") continue;
                    var subs = c.TryGetProperty("Sub Categories", out var s) ? s.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
                    role = subs.Any(x => x.Contains("Instrument", StringComparison.OrdinalIgnoreCase)) ? "Instrument" : "Effect";
                    if (vendor.Length == 0 && c.TryGetProperty("Vendor", out var cv)) vendor = cv.GetString() ?? "";
                    break;
                }
            return (vendor, role);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { return ("", ""); }
    }

    /// <summary>Vendor of a VST2 DLL from its version resource (read from the file, not executed).</summary>
    public static string Vst2Vendor(string dll)
    {
        try { return System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).CompanyName?.Trim() ?? ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return ""; }
    }

    private static bool IsFilesystemFailure(Exception exception) => exception is
        UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException or System.Security.SecurityException;
}
