using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Views.Presets;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: container presets: the preset drop-down and Save preset… in the container pane, writing a container (its bypass, mix, routing text, nested
//   containers and its plug-ins with their states) as a preset file, and loading one into the shown container: its contents are replaced in place as one
//   undo step, and a plug-in that is not trusted here is left out and listed.
// Does not own: the file format and its bounds (a packed plug-in list read by PluginLibrary.LoadChain, as an FX chain), trust (PluginTrust, through the check it
//   is given; the engine decides again at load), the undo step (the edit action it is given), organising the files (the preset manager).
// Tests: no dedicated test class.
public sealed class FxContainerPresets
{
    public const string Hint = "Load preset…", Caption = "Container preset";
    private readonly Window _owner;
    private readonly Func<ChainContainer?> _shown;
    private readonly Func<RigPreset> _rig;
    private readonly Action<Action<List<ChainItem>>> _edit;
    private readonly Action<IReadOnlyList<PluginSlot>> _leaving;
    private readonly Func<Task> _collectStates;
    private readonly Func<FxChainDialogs> _dialogs;
    private readonly PresetFolderStore _store = PresetFolderStore.Containers();
    private readonly ComboBox _presets = new() { Width = 220, Name = "ContainerPresetBox" };
    private readonly Button _save = new() { Content = "Save preset…", Padding = new Thickness(10, 3), Margin = new Thickness(6, 0, 0, 0), Name = "ContainerSavePreset" };
    private bool _filling;

    /// <summary>True when a plug-in of a preset may be loaded here (PluginTrust). Tests replace it.</summary>
    internal Func<PluginSlot, bool> Trusted { get; set; }

    public FxContainerPresets(Window owner, StackPanel bar, Func<ChainContainer?> shown, Func<RigPreset> rig, Action<Action<List<ChainItem>>> edit,
        Action<IReadOnlyList<PluginSlot>> leaving, Func<PluginSlot, bool> trusted, Func<Task> collectStates, Func<FxChainDialogs> dialogs)
    {
        (_owner, _shown, _rig, _edit, _leaving, Trusted, _collectStates, _dialogs) = (owner, shown, rig, edit, leaving, trusted, collectStates, dialogs);
        var label = new TextBlock { Text = "Preset", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush") };
        ToolTip.SetTip(_presets, "Your saved container presets: picking one replaces what is in this container (one undo step). Rename, duplicate, delete or drag them with Manage presets…");
        ToolTip.SetTip(_save, "Save this container with its plug-ins and their settings as a preset");
        _ = PresetRows.Attach(_presets, () => _store,
            item => item is string name && _store.Names().Contains(name, StringComparer.OrdinalIgnoreCase) ? name : null,
            () => PresetCatalogKind.Container, () => null, Fill);
        _presets.SelectionChanged += async (_, _) =>
        {
            if (_filling || _presets.SelectedItem is not string name) return;
            if (PresetRowText.IsManage(_presets.SelectedItem)) return;   // PresetRows reverts the row and opens the manager
            if (name != Hint) await Load(name);
        };
        _save.Click += async (_, _) => await SaveAsked();
        _store.Changed += Fill;
        bar.Children.Add(label);
        bar.Children.Add(_presets);
        bar.Children.Add(_save);
    }

    /// <summary>The closed box's text.</summary>
    public string Shown => _presets.SelectedItem as string ?? "";

    /// <summary>Lists the presets for the shown container. A preset name an imported chain gave it shows as plain text when no file has it.</summary>
    public void Fill()
    {
        _filling = true;
        try
        {
            var current = _shown()?.Preset;
            var items = new List<object> { Hint };
            var names = _store.Names();
            items.AddRange(names);
            string? match = names.FirstOrDefault(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
            if (match is null && current is { Length: > 0 }) { items.Insert(1, current); match = current; }
            PresetRowText.AppendManage(items);
            _presets.ItemsSource = items;
            _presets.SelectedItem = match ?? Hint;
        }
        finally { _filling = false; }
    }

    private async Task SaveAsked()
    {
        if (_shown() is not { } container) return;
        var name = (await _dialogs().Prompt(_owner, "Save container preset", "Preset name", container.Name))?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (PresetRules.Error(name) is { } error) { await _dialogs().Tell(_owner, error, Caption); return; }
        if (_store.Names().Contains(name, StringComparer.OrdinalIgnoreCase)
            && await _dialogs().Ask(_owner, $"Replace the preset \"{name}\"?", Caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != MessageBoxAnswer.Yes) return;
        await _collectStates();   // the plug-ins' settings as they are now
        if (_owner.IsVisible && ReferenceEquals(_shown(), container)) await Save(name);
    }

    /// <summary>Writes the shown container as a preset (a failed write leaves an older preset intact). False when nothing was written.</summary>
    public async Task<bool> Save(string name)
    {
        if (_shown() is not { } container) return false;
        var packed = ChainTree.PackCopy(_rig(), container);
        if (packed.Count == 0) { await _dialogs().Tell(_owner, "This container has no plug-ins to save.", Caption); return false; }
        try
        {
            var path = _store.PathOf(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            PluginLibrary.SaveChain(path, packed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.Error(Trace.Ui, "container preset: save: " + ex.Message);
            await _dialogs().Tell(_owner, "The preset could not be saved: " + ex.Message, Caption);
            return false;
        }
        Fill();
        return true;
    }

    /// <summary>Replaces what is in the shown container with a preset's contents, as one undo step. Plug-ins that are not trusted here are left out and
    /// listed in one message; the file is read with the bounds of an FX chain file. False when nothing changed.</summary>
    public async Task<bool> Load(string name)
    {
        if (_shown() is not { } target) return false;
        List<PluginSlot>? chain = null;
        try { chain = PluginLibrary.LoadChain(_store.PathOf(name)); }
        catch (InvalidDataException ex) { Trace.Error(Trace.Ui, "container preset: name: " + ex.Message); }
        if (chain is null || chain.Count == 0) { await _dialogs().Tell(_owner, "That preset could not be read.", Caption); Fill(); return false; }
        var tree = ChainTree.Tree(chain, chain[0].ChainContainers);
        var skipped = new List<string>();
        void Prepare(List<ChainItem> items)
        {
            // A path is never loaded because a file names it: only a plug-in this app already trusts stays.
            items.RemoveAll(i => { if (i.Plugin is not { } p || Trusted(p)) return false; skipped.Add($"{p.Name} ({(p.Path.Length > 0 ? Path.GetFileName(p.Path) : "no file")})"); return true; });
            foreach (var item in items)
            {
                if (item.Plugin is { } p) { p.Id = Guid.NewGuid().ToString("N"); p.ChainContainers = null; }
                else { item.Container!.Id = Guid.NewGuid().ToString("N"); Prepare(item.Children); }
            }
        }
        Prepare(tree);
        var saved = tree.Count == 1 ? tree[0].Container : null;
        var contents = saved is null ? tree : tree[0].Children;
        var rig = _rig();
        var inside = ChainTree.Find(ChainTree.Tree(rig), target) is { } now ? ChainTree.Plugins(now.List[now.Index].Children).ToList() : new List<PluginSlot>();
        if (rig.Plugins.Count - inside.Count + ChainTree.Plugins(contents).Count() > InputLimits.MaxPluginsPerTrack)
        {
            await _dialogs().Tell(_owner, $"The preset does not fit: a track holds at most {InputLimits.MaxPluginsPerTrack} plug-ins.", Caption);
            Fill();
            return false;
        }
        _leaving(inside);
        _edit(live =>
        {
            if (ChainTree.Find(live, target) is not { } at) return;
            var item = at.List[at.Index];
            item.Children.Clear();
            item.Children.AddRange(contents);
            if (saved is not null) { target.Enabled = saved.Enabled; target.Mix = saved.Mix; target.Routing = saved.Routing; }
            target.Preset = name;
            ChainTree.Write(rig, live);   // the plug-ins that left the container leave the chain, the new ones join it
        });
        if (skipped.Count > 0)
            await _dialogs().Tell(_owner, "These plug-ins of the preset are not installed or not approved here and were left out:\n\n" + string.Join("\n", skipped.Take(20)) + (skipped.Count > 20 ? $"\n… and {skipped.Count - 20} more" : ""), Caption);
        return true;
    }
}
