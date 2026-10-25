using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: what the Add plug-in window does besides drawing: the list of plug-ins (the remembered list, or the last scan), scanning the folders (off the UI
//   thread, with progress), identifying each plug-in's role and vendor in a throwaway process, the remembered list, the folder choices and the common
//   folders, and a single plug-in file chosen by hand.
// Does not own: the window (PluginBrowserWindow), the rows and their filter (PluginBrowserListPresenter), scanning and identifying themselves
//   (VstScannerService, PluginCatalog), approval (PluginTrust; the caller approves what the user picked).
// Tests: no dedicated test class.
// Threads: UI thread only; the scan and each probe run on the thread pool and report back through the awaiting UI context.
public sealed class PluginBrowserSession
{
    public const string NoCommonFoldersText = "None of the common plug-in folders exist on this computer.";
    public const string NotAPluginText = "That file is not a 64-bit VST plug-in.";
    private const int IdentifyTimeoutMs = 10000, SaveEvery = 8, MinParallel = 2, MaxParallel = 8, CoresPerProbe = 3;

    private readonly PluginSettings _settings;
    private readonly TabForge.Plugins.PluginTrust _trust;
    private readonly IPluginFileTrust _files;
    private readonly Action _save;
    private IReadOnlyList<VstPluginInfo> _all;

    /// <param name="settings">The plug-in settings (folders, remembered list, identified plug-ins).</param>
    /// <param name="trust">The app's plug-in trust state.</param>
    /// <param name="files">The file checks plug-in trust uses.</param>
    /// <param name="save">Writes the settings after a change.</param>
    public PluginBrowserSession(PluginSettings settings, TabForge.Plugins.PluginTrust trust, IPluginFileTrust files, Action save)
    {
        (_settings, _trust, _files, _save) = (settings, trust, files, save);
        _all = settings.RememberScan && settings.ScanCache.Count > 0
            ? settings.ScanCache.Select(k => new VstPluginInfo(k.Name, k.Path, k.Format, k.Vendor, k.Role, k.ClassId)).ToList()
            : settings.LastScan;
    }

    public PluginSettings Settings => _settings;
    public IReadOnlyList<VstPluginInfo> All => _all;
    public bool Scanning { get; private set; }
    public bool Identifying { get; private set; }
    /// <summary>Not approved: its role is not read by running it.</summary>
    public HashSet<string> NotIdentified { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The list or a status sentence changed (the window filters again).</summary>
    public event Action? Changed;
    /// <summary>A sentence for the status line (progress, results).</summary>
    public event Action<string>? Status;

    /// <summary>Some folders or the standard folders are chosen (the window scans them when it opens).</summary>
    public bool FoldersChosen => _settings.Folders.Count > 0 || _settings.ScanStandardFolders;

    /// <summary>True when the window should scan on opening: folders are chosen and no remembered list stands in for the scan.</summary>
    public bool ScansOnOpen => FoldersChosen && !(_settings.RememberScan && _settings.ScanCache.Count > 0);

    public List<(string Path, string Vendor, string Role)> KnownRoles() => _settings.Probed.Select(k => (k.Path, k.Vendor, k.Role)).ToList();

    public void SetRemember(bool on)
    {
        _settings.RememberScan = on;
        _settings.ScanCache = on ? CacheOfAll() : new List<KnownPlugin>();
        _save();
        Changed?.Invoke();
    }

    private List<KnownPlugin> CacheOfAll() =>
        _all.Select(p => new KnownPlugin { Name = p.Name, Path = p.Path, Format = p.Format, Vendor = p.Vendor, Role = p.Role, ClassId = p.ClassId }).ToList();

    /// <summary>The user's choice: turns on "Also scan the standard VST folders".</summary>
    public void EnableStandardFolders()
    {
        VstScannerService.EnableStandardFolders(_settings);
        _save();
    }

    /// <summary>Adds folders to the scan list (kept in the settings).</summary>
    public void AddFolders(IEnumerable<string> folders)
    {
        foreach (var folder in folders)
            if (!_settings.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) _settings.Folders.Add(folder);
        _save();
    }

    /// <summary>The common folders as lines for the editor: the user's list, else the defaults.</summary>
    public IReadOnlyList<string> CommonFolderLines => _settings.CommonFolders.Count > 0 ? _settings.CommonFolders : VstScannerService.DefaultCommonFolders;

    /// <summary>Stores the common folder list; the default list is stored as empty (the defaults then follow the app).</summary>
    public void SetCommonFolders(IReadOnlyList<string> lines)
    {
        _settings.CommonFolders = lines.SequenceEqual(VstScannerService.DefaultCommonFolders) ? new List<string>() : lines.ToList();
        _save();
    }

    /// <summary>The info of one plug-in file chosen by hand, or null with the reason in <paramref name="problem"/>.</summary>
    public static VstPluginInfo? FromFile(string path, out string? problem)
    {
        problem = null;
        var vst3 = path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase);
        if (!vst3 && !PeExports.IsVst2Plugin(path)) { problem = NotAPluginText; return null; }
        var (vendor, role, classId) = vst3 ? VstScannerService.Vst3ModuleInfo(path) : (VstScannerService.Vst2Vendor(path), "", "");
        return new VstPluginInfo(Path.GetFileNameWithoutExtension(path), path, vst3 ? "VST3" : "VST2", vendor, role, classId);
    }

    /// <summary>Scans the user's folders (the standard folders when chosen), or just <paramref name="commonRoots"/> and merges what it finds into the list.</summary>
    public async Task ScanAsync(List<string>? commonRoots = null)
    {
        if (Scanning) return;
        Scanning = true;
        var roots = commonRoots ?? VstScannerService.RootsFor(_settings).ToList();
        var report = new Progress<(int Found, string Folder)>(p => Status?.Invoke($"Scanning… {p.Found} found · {p.Folder}"));
        var before = _all.Count;
        try
        {
            var found = await Task.Run(() => VstScannerService.Scan(roots, CancellationToken.None, report, _trust, _files, _settings));
            // Common-folder scans merge into the remembered list instead of replacing it.
            _all = commonRoots is null ? found : PluginBrowserListPresenter.MergeCommonScan(_all, found);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Trace.Error(Trace.Engine, "plug-in browser: scan folders: " + ex.Message);
        }
        Scanning = false;
        if (_settings.RememberScan)
        {
            _settings.ScanCache = CacheOfAll();
            _save();
        }
        Changed?.Invoke();
        if (commonRoots is not null) Status?.Invoke($"Scanned {roots.Count} common folder(s): {_all.Count} plug-ins in the list ({_all.Count - before} new).");
    }

    /// <summary>The roots "Scan common folders" scans; empty when none of them exist.</summary>
    public List<string> CommonRoots() => VstScannerService.CommonRoots(_settings);

    /// <summary>
    /// Plug-ins whose role / vendor the files do not tell are asked when they are approved (several at a time, each in a throwaway process), while the window
    /// is open. Results are remembered, so each is checked only once.
    /// </summary>
    public async Task IdentifyAsync(Func<bool> windowOpen)
    {
        if (Identifying) return;
        Identifying = true;
        try
        {
            var known = KnownRoles();
            var unknown = _all.Where(p => PluginBrowserListPresenter.WithKnown(p, known).Role.Length == 0).ToList();
            var done = 0;
            using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / CoresPerProbe, MinParallel, MaxParallel));
            await Task.WhenAll(unknown.Select(async plugin =>
            {
                await gate.WaitAsync();
                try
                {
                    if (!windowOpen()) return;
                    var described = await Task.Run(() => PluginCatalog.Describe(plugin.Path, _settings, _trust, _files, IdentifyTimeoutMs));
                    if (described.Role.Length == 0) NotIdentified.Add(plugin.Path);   // unapproved or changed: never executed to find out
                }
                finally { gate.Release(); }
                done++;   // back on the UI thread here
                if (!windowOpen()) return;
                if (!Scanning) Status?.Invoke($"Identifying plug-ins… {done} of {unknown.Count} (instrument or effect, vendor)");
                if (done % SaveEvery == 0 || done == unknown.Count) { _save(); Changed?.Invoke(); }
            }));
        }
        finally { Identifying = false; }
    }
}
