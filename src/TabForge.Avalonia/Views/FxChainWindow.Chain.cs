using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.Avalonia.Views;

// Owns: the chain commands of the FX chain window: every edit of the bound track's chain as one undo step (EditTree), the rows and the focused row, Remove,
//   Clear, the enable ticks, Add (through FxAddPlugin), the GM sound that follows the instrument, and the command ids (FxChainIds) a key or a menu item runs.
// Does not own: the host's undo and engine sync (IFxChainHost), the list gestures (FxChainList), the containers (FxChainContainers), the chain files (FxChainFiles).
// Tests: TabForge.Avalonia.Tests.FxChainWindowTests, TabForge.Avalonia.Tests.FxChainEditsTests.
public sealed partial class FxChainWindow
{
    /// <summary>The containers part of the window (tests read its menu).</summary>
    internal FxChainContainers Containers => _containers;

    private bool HasInstrument => _track.Rig.Plugins.Any(p => p.Type == PluginSlotType.Instrument);

    /// <summary>The chain's last instrument left it (removed alone or with its container): the GM sound plays again.</summary>
    private void GmBackWithoutInstrument(bool had) { if (FxChainPresenter.RestoresGmSound(had, HasInstrument)) _track.MidiSound = true; }

    private void Edit(Action change, bool refresh = false, bool show = true) => EditTree(_ => change(), refresh, show);

    /// <summary>
    /// One undo step on the chain: the change may edit the plug-in list, the tree of containers, or both (see <see cref="ChainTree.Edit"/>). The song is marked
    /// changed and the engine brought in line by the host. <paramref name="switched"/>: only bypass switches changed (every plug-in live bypass follows).
    /// </summary>
    private void EditTree(Action<List<ChainItem>> change, bool refresh = false, bool show = true, bool switched = false)
    {
        _host.Edit(_track, () =>
        {
            ChainTree.Edit(_track.Rig, change);
            if (switched && _host.Engine is { } engine && _track.Rig.Plugins.FirstOrDefault() is { } any) engine.SetPluginEnabled(_track, any);
        });
        // The app may have switched GM sound automatically (see MixerGroups.ApplyAutoGm): show what is really playing.
        ShowOptions();
        UpdateMidiButton();
        if (refresh) RefreshList(-1);
        else if (show) _ui.Post(ShowSelected, TabForge.Platform.UiPriority.Background);
    }

    /// <summary>Builds the rows again and focuses the plug-in at <paramref name="select"/> (negative: the focus stays where it is).</summary>
    private void RefreshList(int select) =>
        RefreshRows(select < 0 ? _list.Focused : _track.Rig.Plugins.ElementAtOrDefault(Math.Clamp(select, 0, Math.Max(0, _track.Rig.Plugins.Count - 1))));

    private void RefreshRows(object? focus, IReadOnlyCollection<object>? select = null)
    {
        _building = true;
        try
        {
            var keep = select ?? _list.SelectedTags;
            _list.Clear();
            FxChainRows.Fill(_list, _track.Rig, SetEnabled, _containers.SetEnabled, _containers.ToggleCollapsed);
            _list.Restore(keep, Math.Max(0, _list.IndexOf(focus)));
            _signature = RowSignature();
        }
        finally { _building = false; }
        ShowOptions();
        ShowSelected();
    }

    /// <summary>Reads the track again after it changed elsewhere (the FX power switch, an undo): the options, and the rows when the chain differs.</summary>
    public void RefreshFromTrack()
    {
        if (_closed) return;
        if (RowSignature() != _signature) RefreshRows(_list.Focused);
        else { ShowOptions(); ShowSelected(); }
    }

    private string _signature = "";

    // What the rows show: the plug-ins and containers in order with their switches and names.
    private string RowSignature() => string.Join("|", ChainTree.Tree(_track.Rig).Select(Signature));

    private static string Signature(ChainItem item) => item.Plugin is { } p
        ? $"{p.Id}:{p.Enabled}:{p.GroupBypassed}:{p.Name}"
        : $"[{item.Container!.Id}:{item.Container.Enabled}:{item.Container.Collapsed}:{item.Container.Name}:{string.Join(",", item.Children.Select(Signature))}]";

    /// <summary>Removes every selected plug-in as one step (or the selected containers with their plug-ins, after a question).</summary>
    private async Task RemoveSelected()
    {
        var slots = _list.SelectedTags.OfType<PluginSlot>().ToList();
        var target = FxChainPresenter.ChooseRemove(_list.SelectedTags.Any(t => t is ChainContainer), slots.Count);
        if (target == RemoveTarget.Containers) { await _containers.DeleteSelected(); return; }
        if (target == RemoveTarget.Nothing) return;
        var index = _track.Rig.Plugins.IndexOf(slots[0]);
        _editors.Leaving(slots);
        Edit(() => { var had = HasInstrument; _track.Rig.Plugins.RemoveAll(slots.Contains); GmBackWithoutInstrument(had); }, refresh: false, show: false);
        RefreshList(index);
    }

    /// <summary>Clear chain: every plug-in and container goes (one undo step, the editors close first).</summary>
    private void ClearChain()
    {
        _editors.Leaving(_track.Rig.Plugins);
        EditTree(tree => { var had = HasInstrument; tree.Clear(); _track.Rig.Plugins.Clear(); GmBackWithoutInstrument(had); }, refresh: true);
    }

    /// <summary>A row's tick: switches that plug-in, or every selected plug-in when its row is one of several selected, as one step.</summary>
    private void SetEnabled(PluginSlot slot, bool on)
    {
        var slots = _list.TargetsOf(slot).OfType<PluginSlot>().ToList();
        EditTree(_ => { foreach (var s in slots) { s.Enabled = on; _host.Engine?.SetPluginEnabled(_track, s); } }, refresh: slots.Count > 1, switched: true);
    }

    // ---------- Add ----------

    private bool _adding;   // the chooser or the identify probe is running: a second Add waits

    private async Task AddPlugin()
    {
        if (_adding || _track.Rig.Plugins.Count >= InputLimits.MaxPluginsPerTrack) return;
        _adding = true;
        try
        {
            var added = await new FxAddPlugin(_host, Dialogs, text => _status.Text = text).ChooseAsync(this);
            if (added is null || _closed) return;
            var hadInstrument = HasInstrument;
            var isInstrument = added.Type == PluginSlotType.Instrument;
            _editors.CloseDocked();   // close the old editor before the engine starts creating the new plug-in
            _editors.MarkLoading(added);
            Edit(() =>
            {
                // An instrument goes first (it makes the sound the effects process); effects go to the end.
                if (isInstrument && !hadInstrument) _track.Rig.Plugins.Insert(0, added); else _track.Rig.Plugins.Add(added);
                if (isInstrument && !hadInstrument) _track.MidiSound = false;
                // A plug-in you add is loaded and heard at once: adding one switches the chain on.
                _track.SoundSource = SoundSources.Plugins;
            }, refresh: false, show: false);
            RefreshList(_track.Rig.Plugins.IndexOf(added));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _status.Text = "";
            Trace.Error(Trace.Ui, "FX chain: add plug-in: " + ex.GetBaseException().Message);
            await Dialogs.Ask(this, "The plug-in could not be added: " + ex.GetBaseException().Message, "Add plug-in", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _adding = false; }
    }

    // ---------- commands ----------

    /// <summary>Runs a command of this window by its id. True when it was used (a rename with no container focused is not).</summary>
    private bool RunFxCommand(string id)
    {
        switch (id)
        {
            case FxChainIds.AddPlugin: _ = AddPlugin(); return true;
            case FxChainIds.RemovePlugin: _ = RemoveSelected(); return true;
            case FxChainIds.CutPlugins: _ = CutSelected(); return true;
            case FxChainIds.CopyPlugins: _ = CopySelected(); return true;
            case FxChainIds.PastePlugins: PasteClipboard(); return true;
            case FxChainIds.DuplicatePlugins: _ = DuplicateSelected(); return true;
            case FxChainIds.SelectAllPlugins: SelectAllRows(); return true;
            case FxChainIds.Save: _ = _files.Save(); return true;
            case FxChainIds.Load: _ = _files.Load(replace: true); return true;
            case FxChainIds.AddToEnd: _ = _files.Load(replace: false); return true;
            case FxChainIds.SaveForHosts: _ = _files.SaveForHosts(); return true;
            case FxChainIds.Clear: ClearChain(); return true;
            default: return _containers.Run(id);
        }
    }

    /// <summary>Runs a command by id (tests and the menu use it).</summary>
    public bool Run(string id) => RunFxCommand(id);
}
