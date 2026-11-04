using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TabForge.AudioEngine.Isolation;
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
// Tests: TabForge.Avalonia.Tests.PluginApprovalWindowTests, PluginBrowserWindowTests.
// Threads: UI thread only; the scan and each probe run on the thread pool and report back through the awaiting UI context.
public sealed class PluginBrowserSession
{
    public const string NoCommonFoldersText = "None of the common plug-in folders exist on this computer.";
    public const string NotAPluginText = "That file is not a VST plug-in.";
    private const int SaveEvery = 8, MinParallel = 2, MaxParallel = 8, CoresPerProbe = 3;

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
            ? settings.ScanCache.Select(k => new VstPluginInfo(k.Name, k.Path, PluginFormats.Normalize(k.Format), k.Vendor, k.Role, k.ClassId)).ToList()
            : settings.LastScan;
    }

    public PluginSettings Settings => _settings;
    public IReadOnlyList<VstPluginInfo> All => _all;
    public bool Scanning { get; private set; }
    public bool Identifying { get; private set; }
    /// <summary>Not approved: its role is not read by running it.</summary>
    public HashSet<string> NotIdentified { get; } = new(PathComparer.Default);
    /// <summary>Approved, but the probe could not read them (crash, hang, timeout): their role says "could not identify".</summary>
    public HashSet<string> CouldNotIdentify { get; } = new(PathComparer.Default);
    /// <summary>Readable refusal per path that could not be identified (category text for the status line and rows).</summary>
    public ConcurrentDictionary<string, string> Refusals { get; } = new(PathComparer.Default);
    /// <summary>True while "Approve all / selected" approves and identifies its plug-ins.</summary>
    public bool Approving { get; private set; }
    private CancellationTokenSource? _approveCancel;
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

    /// <summary>The user's choice: lists CLAP plug-ins (.clap) in the scan, or stops listing them. The saved scan is dropped so the next scan decides.</summary>
    public void SetScanClap(bool on)
    {
        _settings.ScanClap = on;
        VstScannerService.ClearSessionCache();
        _save();
    }

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
            if (!_settings.Folders.Any(f => string.Equals(f, folder, PathComparer.Comparison))) _settings.Folders.Add(folder);
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

    /// <summary>The file dialog filter: a Windows VST2 .dll is offered only where a bridge helper can load it.</summary>
    public static string PluginFileFilter =>
        OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && WineBridgeSupport.AnyAvailable)
            ? "Plug-ins (*.vst3;*.dll;*.clap)|*.vst3;*.dll;*.clap"
            : "Plug-ins (*.vst3;*.clap)|*.vst3;*.clap";

    /// <summary>The info of one plug-in file chosen by hand, or null with the reason in <paramref name="problem"/>.</summary>
    public static VstPluginInfo? FromFile(string path, out string? problem) =>
        FromFile(path, out problem, HostSystem.Current, OperatingSystem.IsLinux() && WineBridgeSupport.Available,
            OperatingSystem.IsLinux() && WineBridgeSupport.Available64);

    /// <summary>The same rule with the OS and Wine helper availability passed in, so every family can be checked on any machine.</summary>
    internal static VstPluginInfo? FromFile(string path, out string? problem, HostOs os, bool wineBridge, bool wineBridge64 = false)
    {
        problem = null;
        var trimmed = path.TrimEnd('\\', '/');
        if (PluginFormats.IsClap(trimmed)) return new VstPluginInfo(Path.GetFileNameWithoutExtension(trimmed), path, PluginFormats.Clap);   // chosen by hand: the user's explicit choice
        var vst3 = trimmed.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase);
        var bridged32 = false;
        var bridged64 = false;
        if (vst3 && os == HostOs.Linux && Vst3Windows.IsWindowsOnly(trimmed))
        {
            // A Windows-layout bundle with no Linux binary: route through the helper, like a Windows VST2.
            if (Vst3Windows.Has(trimmed, sixtyFourBit: true))
            {
                if (!wineBridge64) { problem = wineBridge ? WineBridgeSupport.MissingVst3_64Text : WineBridgeSupport.NoWineVst3_64Text; return null; }
                bridged64 = true;
            }
            else if (Vst3Windows.Has(trimmed, sixtyFourBit: false))
            {
                if (!wineBridge) { problem = wineBridge64 ? WineBridgeSupport.MissingVst3Text : WineBridgeSupport.NoWineVst3Text; return null; }
                bridged32 = true;
            }
        }
        if (vst3 && os == HostOs.Windows && Vst3Windows.Is32BitOnly(trimmed))
        {
            // A 32-bit-only Windows VST3 runs through the 32-bit helper, like a 32-bit VST2; 64-bit stays native.
            bridged32 = true;
        }
        if (!vst3)
        {
            var pe32 = PeExports.IsVst2Plugin32(trimmed);
            var pe64 = !pe32 && PeExports.IsVst2Plugin64(trimmed);
            if (!pe32 && !pe64) { problem = NotAPluginText; return null; }
            if (pe32)
            {
                // A 32-bit Windows VST2 is loadable only through a bridge: the helper on Windows, Wine on Linux with the bundle.
                if (os == HostOs.Windows) bridged32 = true;
                else if (os == HostOs.Linux)
                {
                    if (!wineBridge) { problem = WineBridgeSupport.RefusalFor(os); return null; }
                    bridged32 = true;
                }
                else { problem = WineBridgeSupport.RefusalFor(os); return null; }
            }
            else if (os == HostOs.Windows) { /* native in-process: no bridged label */ }
            else if (os == HostOs.Linux)
            {
                // The 64-bit Windows VST2 route is the x64 helper under Wine; only a missing helper is a refusal.
                if (!wineBridge64) { problem = wineBridge ? WineBridgeSupport.Missing64Text : WineBridgeSupport.RefusalFor(os, sixtyFourBit: true); return null; }
                bridged64 = true;
            }
            else { problem = WineBridgeSupport.MacText; return null; }
        }
        var (vendor, role, classId) = vst3 ? VstScannerService.Vst3ModuleInfo(trimmed) : (VstScannerService.Vst2Vendor(trimmed), "", "");
        return new VstPluginInfo(Path.GetFileNameWithoutExtension(trimmed), path,
            bridged32 ? PluginFormats.Bridged32 : bridged64 ? PluginFormats.Bridged64 : vst3 ? PluginFormats.Vst3 : PluginFormats.Vst2,
            vendor, role, classId);
    }

    /// <summary>Scans the user's folders (the standard folders when chosen), or just <paramref name="commonRoots"/> and merges what it finds into the list.</summary>
    public async Task ScanAsync(List<string>? commonRoots = null)
    {
        if (Scanning || Approving) return;
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
    /// is open. Results are remembered, so each is checked only once. Bridged rows on Linux use the bridged probe ceiling (first Wine setup takes minutes);
    /// the Wine prefix is prepared once off the UI thread with a preparing status, so a slow first run is never misclassified as "could not identify".
    /// </summary>
    public async Task IdentifyAsync(Func<bool> windowOpen)
    {
        if (Identifying || Approving) return;
        Identifying = true;
        _stopRequested = false;
        _resume = null;
        SetRun(IdentifyRun.Running);
        try
        {
            var known = KnownRoles();
            var unknown = _all.Where(p => PluginBrowserListPresenter.WithKnown(p, known).Role.Length == 0).ToList();
            var before = _all.Count - unknown.Count;   // identified in an earlier pass (kept in the saved list), so a reopened window continues from there
            var done = before;
            var total = _all.Count;
            (_doneNow, _totalNow) = (done, total);
            // First-run Wine setup once, off the UI thread, before any bridged probe (no misclassification).
            var wineFailure = await PrepareWineForIdentifyAsync(unknown, windowOpen);
            if (wineFailure is not null)
            {
                // Broken prefix: bridged rows are refusals, not timeouts; only native rows are probed.
                foreach (var plugin in unknown.Where(p => PluginCatalog.NeedsBridgedProbe(p.Path, p.Format)))
                {
                    CouldNotIdentify.Add(plugin.Path);
                    Refusals[plugin.Path] = wineFailure;
                    NotIdentified.Remove(plugin.Path);
                }
                unknown = unknown.Where(p => !PluginCatalog.NeedsBridgedProbe(p.Path, p.Format)).ToList();
                Status?.Invoke($"Wine prefix failed: {wineFailure}");
                Changed?.Invoke();
            }
            using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / CoresPerProbe, MinParallel, MaxParallel));
            await Task.WhenAll(unknown.Select(async plugin =>
            {
                await gate.WaitAsync();
                try
                {
                    if (_resume is { } paused) await paused.Task;   // Pause waits here, between plug-ins; a probe already running finishes
                    if (_stopRequested || !windowOpen()) return;
                    var detailed = await Task.Run(() => PluginCatalog.DescribeDetailed(plugin.Path, _settings, _trust, _files, PluginCatalog.DefaultProbeTimeoutMs, plugin.Format));
                    if (detailed.Role.Length == 0 && !PluginCatalog.IsIdentified(plugin.Path, _settings))
                    {
                        if (!_trust.IsTrusted(plugin.Path, _settings, _files)) NotIdentified.Add(plugin.Path);   // unapproved or changed: never executed to find out
                        else
                        {
                            CouldNotIdentify.Add(plugin.Path);
                            if (detailed.Refusal is { Length: > 0 } refusal) Refusals[plugin.Path] = refusal;
                        }
                    }
                    else Refusals.TryRemove(plugin.Path, out _);
                }
                finally { gate.Release(); }
                var now = Interlocked.Increment(ref done);
                if (!windowOpen()) return;
                _doneNow = now;
                if (!Scanning && Run == IdentifyRun.Running) Status?.Invoke(PluginBrowserListPresenter.IdentifyRunningText(now, total));
                if ((now - before) % SaveEvery == 0 || now == total) { _save(); Changed?.Invoke(); }
            }));
            _totalNow = total;
            if (_stopRequested) { _save(); SetRun(IdentifyRun.Stopped); Status?.Invoke(PluginBrowserListPresenter.IdentifyStoppedText(_doneNow, total)); Changed?.Invoke(); }
        }
        finally { Identifying = false; if (Run != IdentifyRun.Stopped) SetRun(IdentifyRun.Idle); }
    }

    /// <summary>Prepares the Wine prefix once when bridged rows need it (off the UI thread). Null on success / not needed; else the refusal text.</summary>
    private async Task<string?> PrepareWineForIdentifyAsync(IReadOnlyList<VstPluginInfo> unknown, Func<bool> windowOpen)
    {
        if (!OperatingSystem.IsLinux()) return null;
        if (!unknown.Any(p => PluginCatalog.NeedsBridgedProbe(p.Path, p.Format))) return null;
        var runtime = WineRuntime.Locate();
        if (runtime is null) return null;   // missing Wine/helper: each row keeps its hand-pick refusal, no prefix work
        Status?.Invoke(PluginBrowserListPresenter.WinePreparingText);
        try
        {
            await Task.Run(() => { WineRuntime.SweepStaleBlocks(); runtime.EnsureReady(); });
            if (!windowOpen()) return null;
            Status?.Invoke(PluginBrowserListPresenter.IdentifyRunningText(_doneNow, _totalNow));
            return null;
        }
        catch (Exception ex)
        {
            return $"{ProbeRefusal.BrokenPrefixText} ({ex.GetBaseException().Message})";
        }
    }

    /// <summary>The plug-ins among <paramref name="rows"/> a batch may approve; the trust check can read files, so it runs off the UI thread.</summary>
    public Task<IReadOnlyList<VstPluginInfo>> UnapprovedAsync(IReadOnlyList<PluginBrowserRow> rows) =>
        Task.Run(() => PluginApprovalPresenter.Unapproved(rows, path => _trust.Check(path, _settings, _files)));

    /// <summary>
    /// The user confirmed: approves these plug-ins (same trust record as approving one) and identifies each in turn off the UI thread, updating the rows and the status line
    /// as it goes. A failing probe leaves that plug-in "could not identify" and the batch goes on; <see cref="Stop"/> ends it after the plug-in being probed.
    /// The Wine prefix is prepared once off the UI thread with a preparing status when bridged rows are present.
    /// </summary>
    public async Task ApproveAsync(IReadOnlyList<VstPluginInfo> plugins)
    {
        if (Approving || Identifying || Scanning || plugins.Count == 0) return;
        Approving = true;
        _approveCancel = new CancellationTokenSource();
        (_doneNow, _totalNow) = (0, plugins.Count);
        RunChanged?.Invoke();
        Status?.Invoke(PluginBrowserListPresenter.WinePreparingTextFor(plugins));
        var wineFailure = await PrepareWineForApproveAsync(plugins);
        if (wineFailure is not null)
        {
            foreach (var plugin in plugins.Where(p => PluginCatalog.NeedsBridgedProbe(p.Path, p.Format)))
            {
                CouldNotIdentify.Add(plugin.Path);
                Refusals[plugin.Path] = wineFailure;
            }
            // Approve the native rows only; bridged rows stay refused (no misclassification as timeouts).
            plugins = plugins.Where(p => !PluginCatalog.NeedsBridgedProbe(p.Path, p.Format)).ToList();
            if (plugins.Count == 0)
            {
                Approving = false;
                _approveCancel.Dispose();
                _approveCancel = null;
                _save();
                RunChanged?.Invoke();
                Changed?.Invoke();
                Status?.Invoke($"Wine prefix failed: {wineFailure}");
                return;
            }
        }
        Status?.Invoke(PluginApprovalPresenter.RunningText(0, plugins.Count));
        var failed = 0;
        var done = 0;
        string? firstRefusal = null;
        try
        {
            done = await PluginApprovalPresenter.RunAsync(plugins, _settings, _trust, _files, step =>
            {
                NotIdentified.Remove(step.Plugin.Path);
                if (!step.Identified)
                {
                    CouldNotIdentify.Add(step.Plugin.Path); failed++;
                    if (step.Refusal is { Length: > 0 })
                    {
                        Refusals[step.Plugin.Path] = step.Refusal;
                        firstRefusal ??= step.Refusal;
                    }
                }
                else { CouldNotIdentify.Remove(step.Plugin.Path); Refusals.TryRemove(step.Plugin.Path, out _); }
                _doneNow = step.Done;
                Status?.Invoke(PluginApprovalPresenter.RunningText(step.Done, step.Total));
                Changed?.Invoke();
            }, _approveCancel.Token);
        }
        finally
        {
            Approving = false;
            _approveCancel.Dispose();
            _approveCancel = null;
            _save();
            RunChanged?.Invoke();
            Changed?.Invoke();
        }
        var doneText = PluginApprovalPresenter.DoneText(done, plugins.Count, failed);
        Status?.Invoke(firstRefusal is null ? doneText : $"{doneText} First refusal: {firstRefusal}");
    }

    private async Task<string?> PrepareWineForApproveAsync(IReadOnlyList<VstPluginInfo> plugins)
    {
        if (!OperatingSystem.IsLinux()) return null;
        if (!plugins.Any(p => PluginCatalog.NeedsBridgedProbe(p.Path, p.Format))) return null;
        var runtime = WineRuntime.Locate();
        if (runtime is null) return null;
        Status?.Invoke(PluginBrowserListPresenter.WinePreparingText);
        try
        {
            await Task.Run(() => { WineRuntime.SweepStaleBlocks(); runtime.EnsureReady(); });
            return null;
        }
        catch (Exception ex)
        {
            return $"{ProbeRefusal.BrokenPrefixText} ({ex.GetBaseException().Message})";
        }
    }

    private bool _stopRequested;
    private TaskCompletionSource? _resume;
    private int _doneNow, _totalNow;
    /// <summary>Plug-ins identified so far (earlier passes included) and the plug-ins in the list: the progress bar and the status text.</summary>
    public (int Done, int Total) IdentifyProgress => (_doneNow, _totalNow);

    /// <summary>Where the identify pass is: Running (Pause, Stop), Paused (Resume, Stop), Stopped (until Rescan or reopening), else Idle.</summary>
    public IdentifyRun Run { get; private set; }
    /// <summary>Raised when <see cref="Run"/> changes (the window shows or hides its buttons).</summary>
    public event Action? RunChanged;

    private void SetRun(IdentifyRun run) { Run = run; RunChanged?.Invoke(); }

    /// <summary>Pauses between plug-ins: the probes already running finish, no new one starts. Does nothing unless running.</summary>
    public void Pause()
    {
        if (Run != IdentifyRun.Running) return;
        _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetRun(IdentifyRun.Paused);
        Status?.Invoke(PluginBrowserListPresenter.IdentifyPausedText(_doneNow, _totalNow));
        Changed?.Invoke();
    }

    /// <summary>Continues a paused pass.</summary>
    public void Resume()
    {
        if (Run != IdentifyRun.Paused) return;
        var resume = _resume;
        _resume = null;
        SetRun(IdentifyRun.Running);
        resume?.TrySetResult();
    }

    /// <summary>Stops after the probes already running; the rest stay listed with "…" and are checked by Rescan or when the window reopens.</summary>
    public void Stop()
    {
        _approveCancel?.Cancel();
        if (Run is not (IdentifyRun.Running or IdentifyRun.Paused)) return;
        _stopRequested = true;
        var resume = _resume;
        _resume = null;
        resume?.TrySetResult();   // a paused pass wakes up to finish
    }
}
