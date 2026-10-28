using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge.Avalonia.Views;

// Owns: Cut, Copy, Paste and Duplicate of plug-ins in the FX chain window: the copy (the plug-in states are read from the engine first, so the copy keeps the
//   settings), the paste after the last selected row as one undo step (also into another track's window, through the main window's FxPluginClipboard), and the
//   plug-in entries of the right-click menu.
// Does not own: the text and the new plug-ins (ChainClip), the undo step and the engine sync (EditTree through the host), the clipboard value (FxPluginClipboard).
// Tests: TabForge.Avalonia.Tests.FxChainClipboardTests.
// Threads: UI thread; the state read waits asynchronously, the window never blocks.
public sealed partial class FxChainWindow
{
    private readonly FxPluginClipboard _ownClipboard = new();   // used when the window has no main window registry (a preview)

    private FxPluginClipboard PluginClipboard => FxChainWindows.For(this)?.Clipboard ?? _ownClipboard;

    /// <summary>True while the focus is in the plug-in list (a key on the list's commands only counts then).</summary>
    private bool ListFocused => _list.IsKeyboardFocusWithin;

    /// <summary>The selected rows as text with the plug-ins' current states; null when nothing is selected or the window closed meanwhile.</summary>
    private async Task<string?> SnapshotSelected()
    {
        var tags = _list.SelectedTags;
        if (tags.Count == 0) return null;
        if (_host.Engine is { } engine) await engine.CollectStatesAsync(new[] { _track }, StateCollectMs);
        return _closed ? null : ChainClip.Snapshot(_track.Rig, tags);
    }

    private async Task<bool> CopySelected()
    {
        if (await SnapshotSelected() is not { } snapshot || ChainClip.Restore(snapshot) is not { } copied) return false;
        PluginClipboard.Snapshot = snapshot;
        _status.Text = "Copied " + FxChainRows.Count(copied.Plugins.Count) + ".";
        return true;
    }

    /// <summary>Cut: copy first, then remove as the Delete key does (one undo step).</summary>
    private async Task CutSelected()
    {
        if (await CopySelected()) await RemoveSelected();
    }

    private async Task DuplicateSelected() => Insert(await SnapshotSelected());

    private void PasteClipboard() => Insert(PluginClipboard.Snapshot);

    /// <summary>Puts new copies of the snapshot's plug-ins after the last selected row (at the end without one) as one undo step, and selects them.</summary>
    private void Insert(string? snapshot)
    {
        if (snapshot is null || ChainClip.Restore(snapshot) is not { } pasted) return;
        if (_track.Rig.Plugins.Count + pasted.Plugins.Count > InputLimits.MaxPluginsPerTrack)
        {
            _status.Text = $"{FxChainRows.Count(pasted.Plugins.Count)} would make this chain longer than {InputLimits.MaxPluginsPerTrack} plug-ins.";
            return;
        }
        var after = _list.SelectedTags.LastOrDefault();
        var hadInstrument = HasInstrument;
        var instrument = pasted.Plugins.Any(p => p.Type == PluginSlotType.Instrument);
        _editors.CloseDocked();   // as Add: the old editor closes before the engine starts creating the new plug-ins
        _editors.MarkLoading(pasted.Plugins[0]);
        EditTree(tree =>
        {
            _track.Rig.Plugins.AddRange(pasted.Plugins);
            if (after is not null && ChainTree.Find(tree, after) is { } at) at.List.InsertRange(at.Index + 1, pasted.Items); else tree.AddRange(pasted.Items);
            if (instrument && !hadInstrument) _track.MidiSound = false;
            _track.SoundSource = SoundSources.Plugins;   // a pasted plug-in is heard at once, as an added one
        }, refresh: false, show: false);
        RefreshRows(pasted.Plugins[0], pasted.Plugins);
        _status.Text = "Pasted " + FxChainRows.Count(pasted.Plugins.Count) + ".";
    }

    private void SelectAllRows() => _list.SelectAll();

    /// <summary>The plug-in entries of the right-click menu for the rows as they are now; each shows the live key of its command.</summary>
    private IEnumerable<Control> ListMenuItems()
    {
        var any = _list.SelectedTags.Count > 0;
        MenuItem Item(string header, string id, bool enabled) { var item = _keys.Item(header, id); item.IsEnabled = enabled; return item; }
        yield return Item("Cut", FxChainIds.CutPlugins, any);
        yield return Item("Copy", FxChainIds.CopyPlugins, any);
        yield return Item("Paste", FxChainIds.PastePlugins, PluginClipboard.HasContent);
        yield return Item("Duplicate", FxChainIds.DuplicatePlugins, any);
        yield return Item("Delete", FxChainIds.RemovePlugin, any);
        yield return Item("Select all", FxChainIds.SelectAllPlugins, _list.Count > 0);
        yield return Item(FxGenericControls.MenuText, FxChainIds.ShowGenericControls, FxGenericControls.Supports(Selected));
    }
}
