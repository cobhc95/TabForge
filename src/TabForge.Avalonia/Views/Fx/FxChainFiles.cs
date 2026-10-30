using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Presets;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the FX chain window's chain files: Load FX chain and Add FX chain to the end (the picker, then the load into the bound track as one undo step,
//   containers unpacked), Save FX chain (the plug-in states are read first, containers saved), the .RfxChain import and export, and the Chains menu: one
//   row per saved chain (with the replace-or-add question), Save, Save for other hosts, and Manage. Each outcome goes to the window's status line.
// Does not own: reading and checking a chain file (ChainFileApply), the undo step (the window's edit callback), the dialogs (FxChainDialogs), the
//   .RfxChain parsing and writing (ReaperChainImporter, RfxChainExporter), plug-in trust (PluginTrust decides when the engine loads the chain).
// Tests: TabForge.Avalonia.Tests.FxChainWindowTests.
public sealed class FxChainFiles
{
    public const string LoadCaption = "Load FX chain", SaveCaption = "Save FX chain", ExportCaption = "Export FX chain";
    public const string ChainFilter = "TabForge FX chain (*.tfchain)|*.tfchain";
    public const string LoadFilter = "All FX chains (*.tfchain, *.RfxChain)|*.tfchain;*.RfxChain|" + ChainFilter + "|Imported FX chain (*.RfxChain)|*.RfxChain";
    public const string ExportFilter = "FX chain for other hosts (*.RfxChain)|*.RfxChain";
    public const string RfxExtension = ".rfxchain";
    public const string NoSavedChainsText = "No saved chains yet", SaveMenuText = "Save FX chain…", SaveForHostsText = "Save as FX chain for other hosts (.RfxChain)…";
    public const string SaveForHostsTip = "Writes this chain, with its containers and the plug-ins' current settings, as a chain file other hosts can load";
    private const int StateCollectMs = 2000;

    private readonly Window _owner;
    private readonly Func<FxChainDialogs> _dialogs;
    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly Action<int> _applied;
    private readonly Action<string> _status;
    private readonly Action<Action<List<ChainItem>>> _edit;
    private readonly Action<IEnumerable<PluginSlot>> _leaving;
    private bool _loading;   // an import is scanning: a second load waits
    private readonly CancellationTokenSource _windowClosed = new();   // an import's scan and probes stop when the window closes

    /// <param name="owner">The window the dialogs and the replace question belong to.</param>
    /// <param name="applied">Called after a load changes the chain, with the index of the first plug-in it added; the window shows that row.</param>
    /// <param name="status">Receives each outcome sentence.</param>
    /// <param name="edit">Runs a change of the chain tree as one undo step and refreshes the rows.</param>
    /// <param name="leaving">The plug-ins about to leave the chain (their editors close first).</param>
    public FxChainFiles(Window owner, Func<FxChainDialogs> dialogs, IFxChainHost host, TrackModel track, Action<int> applied, Action<string> status,
        Action<Action<List<ChainItem>>> edit, Action<IEnumerable<PluginSlot>>? leaving = null)
    {
        (_owner, _dialogs, _host, _track, _applied, _status, _edit) = (owner, dialogs, host, track, applied, status, edit);
        _leaving = leaving ?? (_ => { });
        owner.Closed += (_, _) => _windowClosed.Cancel();
    }

    private RigPreset Rig => _track.Rig;

    /// <summary>Asks for a chain file and loads it: it replaces the chain, or is added to its end.</summary>
    public async Task Load(bool replace)
    {
        if (_loading) return;
        Directory.CreateDirectory(PluginLibrary.ChainsFolder);
        var reaper = Path.Combine(TabForge.Platform.AppFolders.Current.ReaperResources, "FXChains");
        var useReaper = Directory.Exists(reaper) && !Directory.EnumerateFiles(PluginLibrary.ChainsFolder, "*" + ChainFileApply.FileExtension).Any();
        var file = _dialogs().Files.OpenFile(new FileDialogOptions
        {
            Key = TabForge.Services.DialogKeys.FxChainLoad, Title = LoadCaption, Filter = LoadFilter, InitialDirectory = useReaper ? reaper : PluginLibrary.ChainsFolder,
        }, _owner);
        if (file is not null) await LoadFile(file, replace, preferred: replace ? FxPresetLoadMode.Replace : FxPresetLoadMode.Add);
    }

    /// <summary>Loads one chain file (the picker answer, or a saved chain from the Chains menu) and reports the outcome.</summary>
    /// <param name="replace">Kept for the callers that name the menu command; the question (or Settings) decides replace or add.</param>
    /// <param name="displayName">The preset name the question shows; null takes the file name.</param>
    /// <param name="preferred">The choice the question starts on; null follows the menu command (Replace, or Add for "Add FX chain to the end…").</param>
    public async Task LoadFile(string path, bool replace, string? displayName = null, FxPresetLoadMode? preferred = null)
    {
        if (_loading) return;
        ChainFileApply.ChainFileRead read;
        string? noneFound = null;
        _loading = true;
        try
        {
            if (path.EndsWith(RfxExtension, StringComparison.OrdinalIgnoreCase))
            {
                var imported = await new FxRfxChainFlows(_owner, _host, _dialogs(), _status).ImportAsync(path, _windowClosed.Token);
                var fileName = Path.GetFileName(path);
                noneFound = imported?.NoneFoundNotice(fileName);
                read = ChainFileApply.Check(fileName, imported?.Chain, Rig.Plugins.Count, replace: true, noneFound);
            }
            else read = ChainFileApply.Read(path, 0, replace: true);
        }
        catch (OperationCanceledException)
        {
            return;   // the window closed during the scan: nothing to report
        }
        catch (FileNotFoundException)
        {
            // The file went missing after the picker or the menu listed it: the status says so, and the chain stays as it was.
            _status.Error($"{Path.GetFileName(path)} could not be found.");
            return;
        }
        catch (ArgumentException)
        {
            _status.Error($"{Path.GetFileName(path)} is not an FX chain file.");
            return;
        }
        finally { _loading = false; }
        if (read.Plugins is not { } loaded) { _status(read.Outcome.Notice); return; }

        // The question comes before any change: Cancel leaves the chain, its settings and its undo history alone.
        var name = displayName ?? FxPresetLoadPresenter.NameOf(path);
        var start = preferred ?? (replace ? FxPresetLoadMode.Replace : FxPresetLoadMode.Add);
        var answer = await FxPresetLoadQuiz.AskAsync(_owner, _dialogs(), _host.PluginSettings, _host.SaveSettings, name, Rig.Plugins.Count,
            FxPresetLoadPresenter.ShowsTwoInstruments(Rig, loaded), start);
        if (answer is not { } choice) return;
        var mode = choice.Mode;
        var place = ChainFileApply.Check(Path.GetFileName(path), loaded, Rig.Plugins.Count, mode == FxPresetLoadMode.Replace, noneFound);
        _status(place.Outcome.Notice);
        if (place.Plugins is not { } toLoad) return;

        if (mode == FxPresetLoadMode.Replace) _leaving(Rig.Plugins.ToList());
        var hadInstrument = FxChainPresenter.HasInstrument(Rig.Plugins);
        _edit(tree =>
        {
            var merged = FxChainMerge.Merge(Rig, toLoad, mode);
            Rig.Plugins.Clear();
            Rig.Plugins.AddRange(merged.Plugins);
            tree.Clear();   // a replace also drops the old containers; an add keeps them in the merged tree
            tree.AddRange(ChainTree.Tree(merged.Plugins, merged.Containers));
            FxChainPresenter.FollowsGmSound(_track, hadInstrument, _host.PluginSettings.AutoGmSound);
            _track.SoundSource = SoundSources.Plugins;
        });
        _applied(place.Outcome.FirstAdded);
    }

    /// <summary>Reads the plug-in states, asks for a file name and writes the chain (with its containers) to it as a chain file.</summary>
    public async Task Save()
    {
        if (_host.Engine is { } engine) await engine.CollectStatesAsync(new[] { _track }, StateCollectMs);
        Directory.CreateDirectory(PluginLibrary.ChainsFolder);
        var chosen = _dialogs().Files.SaveFile(new FileDialogOptions
        {
            Key = TabForge.Services.DialogKeys.FxChainSave, Title = SaveCaption, Filter = ChainFilter, DefaultExt = ChainFileApply.FileExtension,
            InitialDirectory = PluginLibrary.ChainsFolder, FileName = _track.Name,
        }, _owner);
        if (chosen is null) return;
        try { PluginLibrary.SaveChain(chosen, Rig.Plugins, Rig.Containers); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.Error(Trace.Ui, "FX chain: save: " + ex.Message);
            _status.Error("The FX chain could not be saved: " + ex.Message);
            await _dialogs().Ask(_owner, "The FX chain could not be saved: " + ex.Message, SaveCaption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _status($"Saved {Path.GetFileName(chosen)}");
    }

    /// <summary>Save as FX chain for other hosts: the .RfxChain export.</summary>
    public Task SaveForHosts() => new FxRfxChainFlows(_owner, _host, _dialogs(), _status).ExportAsync(_track);

    /// <summary>Fills the Chains menu with one row per saved chain (each loads it), then Save, Save for other hosts and Manage. Called when the menu opens.</summary>
    public void FillSavedChains(MenuItem chains, FxChainKeys keys)
    {
        chains.Items.Clear();
        var store = PresetFolderStore.FxChains();
        var names = store.Names();
        foreach (var name in names)
        {
            var row = new MenuItem { Header = name };
            var path = store.PathOf(name);
            row.Click += (_, _) => _ = LoadSavedAsync(path, name);
            chains.Items.Add(row);
        }
        if (names.Count == 0) chains.Items.Add(new MenuItem { Header = NoSavedChainsText, IsEnabled = false });
        chains.Items.Add(new Separator());
        chains.Items.Add(keys.Item(SaveMenuText, FxChainIds.Save));
        var hosts = keys.Item(SaveForHostsText, FxChainIds.SaveForHosts);
        ToolTip.SetTip(hosts, SaveForHostsTip);
        chains.Items.Add(hosts);
        chains.Items.Add(new Separator());
        var manage = new MenuItem { Header = PresetRowText.Manage };
        manage.Click += (_, _) => PresetManager.Open(_owner, PresetCatalogKind.FxChain, null);
        chains.Items.Add(manage);
        keys.Apply(chains);
    }

    // A saved chain from the menu: the same question as every other load decides replace, add to the end or nothing (Cancel).
    private Task LoadSavedAsync(string path, string name) => LoadFile(path, replace: true, name);
}
