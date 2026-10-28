using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the two .RfxChain steps of the FX chain window: "Save as FX chain for other hosts" (the plug-ins' current settings and their own description
//   from the engine, the file dialog, the atomic write, one message with what was left out) and "load an .RfxChain file" (which plug-ins count as known, the
//   offer to scan when some are missing, the summary message).
// Does not own: the file layout and parsing (RfxChainExporter, ReaperChainImporter), plug-in trust (PluginTrust decides at load), putting the chain on the
//   track (FxChainFiles), the dialogs (FxChainDialogs).
// Tests: no dedicated test class.
public sealed class FxRfxChainFlows
{
    private const string ExportCaption = "Export FX chain", ImportCaption = "FX chain import";
    private const int StateCollectMs = 2000;
    private readonly Window _owner;
    private readonly IFxChainHost _host;
    private readonly FxChainDialogs _dialogs;
    private readonly Action<string> _status;

    public FxRfxChainFlows(Window owner, IFxChainHost host, FxChainDialogs dialogs, Action<string> status)
    {
        (_owner, _host, _dialogs, _status) = (owner, host, dialogs, status);
    }

    /// <summary>True when a file was written. Shows one message when something was left out or the file could not be written.</summary>
    public async Task<bool> ExportAsync(TrackModel track)
    {
        var engine = _host.Engine;
        if (engine is not null) await engine.CollectStatesAsync(new[] { track }, StateCollectMs);
        // One snapshot of the chain: the info is asked for, and attached to, exactly these plug-ins even if the chain changes meanwhile.
        var slots = track.Rig.Plugins.ToList();
        var info = engine is null ? new LoadedPluginInfo?[slots.Count] : await engine.GetPluginInfoAsync(track, slots);
        if (!_owner.IsVisible) return false;   // the window closed while the engine was asked
        var builtInSound = engine is not null && MixerGroups.GmSounds(track, engine.Mixer);
        var result = RfxChainExporter.Export(RfxChainExporter.FromTree(ChainTree.Tree(slots, track.Rig.Containers), slots, info, WhyNotLoaded), builtInSound);
        var notes = string.Join("\n\n", result.Notes);
        if (result.Written == 0)
        {
            await _dialogs.Ask(_owner, "No plug-in of this chain can be exported.\n\n" + notes, ExportCaption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        var chosen = _dialogs.Files.SaveFile(new FileDialogOptions
        {
            Key = TabForge.Services.DialogKeys.FxChainExportRfx, Title = ExportCaption, Filter = FxChainFiles.ExportFilter, DefaultExt = ".RfxChain", AddExtension = true,
            FileName = SafeFileNames.SafeFileName(track.Name, "FX chain"),
        }, _owner);
        if (chosen is null) return false;
        try
        {
            var path = FilePathPolicy.OutputFile(chosen, "FX chain file", FxChainFiles.RfxExtension);
            var bytes = new UTF8Encoding(false).GetBytes(result.Text);
            FilePathPolicy.WriteAtomically(path, stream => stream.Write(bytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Trace.Error(Trace.Ui, "FX chain export: " + ex.Message);
            await _dialogs.Ask(_owner, "The FX chain could not be exported: " + ex.Message, ExportCaption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _status($"Saved {Path.GetFileName(chosen)}");
        if (notes.Length > 0) await _dialogs.Tell(_owner, notes, ExportCaption);
        return true;
    }

    // A plug-in the engine did not load: its file is gone, or it waits for the user's approval, or it failed to load.
    private RfxExportGap WhyNotLoaded(PluginSlot slot) =>
        !File.Exists(slot.Path) && !Directory.Exists(slot.Path) ? RfxExportGap.NotInstalled
        : !_host.Trust.IsTrusted(slot.Path, _host.PluginSettings, _host.PluginFiles) ? RfxExportGap.NotTrusted
        : RfxExportGap.Unsupported;

    /// <summary>
    /// Loads the file; null when it is unreadable or no chain file. A fresh profile has scanned nothing, so the standard, configured and Reaper folders are scanned
    /// before the first match. Shows the summary when something was left out. A scan runs off the UI thread and reports through the status line; the caller keeps a
    /// second import from starting meanwhile.
    /// </summary>
    public async Task<ReaperChainImporter.Imported?> ImportAsync(string path, CancellationToken cancel = default)
    {
        var settings = _host.PluginSettings;
        var inUse = _host.Tracks.SelectMany(t => t.Rig.Plugins).ToList();
        if (ReaperChainImporter.Read(path) is not { } parsed) return null;
        var standard = false;   // the standard folders count even though the user's option is off: only for a first match with nothing known
        // The user's folders, and the ones Reaper itself is set up with.
        List<string> Roots() => (standard ? VstScannerService.StandardFolders.Concat(VstScannerService.CommonRoots(settings)) : Enumerable.Empty<string>())
            .Concat(VstScannerService.RootsFor(settings)).Concat(ReaperPluginFolders.Roots()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var nothingKnown = KnownPluginList.Of(settings, inUse).Count == 0;
        if (nothingKnown) { standard = true; await Scan(); }
        var imported = await Match();
        // Some are unknown: scan the user's plug-in folders unless this run already did, then offer the standard folders once.
        if (imported.Missing > 0 && !nothingKnown && settings.LastScan.Count == 0 && Roots().Count > 0) { await Scan(); imported = await Match(); }
        if (imported.Missing > 0 && !settings.ScanStandardFolders
            && await _dialogs.Ask(_owner, $"{imported.Missing} plug-in{(imported.Missing == 1 ? " in this chain is" : "s in this chain are")} not in your plug-in list.\n\n"
                + "Scan the standard VST folders for them now? This turns on Settings > Audio & Plug-ins > Also scan the standard VST folders.",
                ImportCaption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == MessageBoxAnswer.Yes)
        {
            VstScannerService.EnableStandardFolders(settings);
            _host.SaveSettings();
            if (!standard) { await Scan(); imported = await Match(); }   // a fresh profile's first match already looked there: the answer only keeps the option on
        }
        if (imported.Report.Length > 0 && _owner.IsVisible) await _dialogs.Tell(_owner, imported.Report, ImportCaption);
        return imported;

        async Task Scan()
        {
            var roots = Roots();
            var report = new Progress<(int Found, string Folder)>(p => _status($"Scanning… {p.Found} found · {p.Folder}"));
            try
            {
                var found = await Task.Run(() => VstScannerService.Scan(roots, cancel, report, _host.Trust, _host.PluginFiles, settings));
                if (settings.RememberScan)
                {
                    settings.ScanCache = found.Select(f => new KnownPlugin { Name = f.Name, Path = f.Path, Format = f.Format, Vendor = f.Vendor, Role = f.Role, ClassId = f.ClassId }).ToList();
                    _host.SaveSettings();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.Error(Trace.Engine, "FX chain import: scan folders: " + ex.Message);
            }
            _status("");
        }

        // The vendors of the matched plug-ins are asked for first (off the UI thread), so a file name that matches another maker's plug-in is caught.
        async Task<ReaperChainImporter.Imported> Match()
        {
            var catalog = KnownPluginList.Of(settings, inUse);
            await Task.Run(() => ChainVendorLookup.Fill(parsed, catalog, settings, _host.Trust, _host.PluginFiles, cancel), cancel);
            return ReaperChainImporter.Build(parsed, KnownPluginList.Of(settings, inUse), Roots());
        }
    }
}
