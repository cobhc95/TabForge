using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views.Presets;

namespace TabForge.Views.Fx;

// Owns: container presets: the preset drop-down and Save preset… in the container pane, writing a container (its bypass, mix,
//   routing text, nested containers and its plug-ins with their states) as a preset file, and loading one into the shown container:
//   its contents are replaced in place as one undo step, and a plug-in that is not trusted here is left out and listed.
// Does not own: the file format and its bounds (a packed plug-in list read by PluginLibrary.LoadChain, as an FX chain), trust
//   (PluginTrust, through the check it is given; the engine decides again at load), the undo step (the edit action it is given),
//   the organisable rows (PresetRows).
// Tests: TestFxContainerPresets.
internal sealed class FxContainerPresets
{
    private const string Hint = "Load preset…";
    private readonly Window _owner;
    private readonly Func<ChainContainer?> _shown;
    private readonly Func<RigPreset> _rig;
    private readonly Action<Action<List<ChainItem>>> _edit;
    private readonly Action<IReadOnlyList<PluginSlot>> _leaving;
    private readonly Func<Task> _collectStates;
    private readonly PresetFolderStore _store = PresetFolderStore.Containers();
    private readonly ComboBox _presets = new() { Width = 220 };
    private readonly Button _save = new() { Content = "Save preset…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
    private readonly PresetRows _rows;
    private bool _filling;

    /// <summary>True when a plug-in of a preset may be loaded here (PluginTrust). Tests replace it.</summary>
    internal Func<PluginSlot, bool> Trusted;
    /// <summary>Shows a message (a themed box). Tests collect it.</summary>
    internal Action<string> Say;

    public FxContainerPresets(Window owner, StackPanel bar, Func<ChainContainer?> shown, Func<RigPreset> rig, Action<Action<List<ChainItem>>> edit,
        Action<IReadOnlyList<PluginSlot>> leaving, Func<PluginSlot, bool> trusted, Func<Task> collectStates)
    {
        (_owner, _shown, _rig, _edit, _leaving, Trusted, _collectStates) = (owner, shown, rig, edit, leaving, trusted, collectStates);
        Say = text => ThemedMessageBox.Show(_owner, text, "Container preset", MessageBoxButton.OK, MessageBoxImage.Information);
        var label = new TextBlock { Text = "Preset", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        UiIds.Id(_presets, "Fx.ContainerPresets", "Container presets");
        TooltipShortcuts.Bind(_presets, "Your saved container presets: picking one replaces what is in this container (one undo step). Rename, duplicate, delete or drag them in the open list", null);
        TooltipShortcuts.Bind(_save, "Save this container with its plug-ins and their settings as a preset", null);
        _rows = PresetRows.Attach(_presets, PresetKind.Container, () => _store, o => o is ComboBoxItem { Tag: string name } ? name : null);
        _presets.SelectionChanged += (_, _) => { if (!_filling && _presets.SelectedItem is ComboBoxItem { Tag: string name }) Load(name); };
        _save.Click += async (_, _) => await SaveAsked();
        _store.Changed += Fill;
        bar.Children.Add(label); bar.Children.Add(_presets); bar.Children.Add(_save);
    }

    /// <summary>The closed box's text.</summary>
    internal string Shown => (_presets.SelectedItem as ComboBoxItem)?.Content as string ?? "";

    /// <summary>Lists the presets for the shown container. A preset name an imported chain gave it shows as plain text when no file has it.</summary>
    public void Fill()
    {
        _filling = true;
        try
        {
            var current = _shown()?.Preset;
            _presets.Items.Clear();
            _presets.Items.Add(new ComboBoxItem { Content = Hint });
            ComboBoxItem? match = null;
            foreach (var name in _store.Names())
            {
                var item = _rows.Item(name, name);
                _presets.Items.Add(item);
                if (string.Equals(name, current, StringComparison.OrdinalIgnoreCase)) match = item;
            }
            if (match is null && current is { Length: > 0 })
                _presets.Items.Insert(1, match = new ComboBoxItem { Content = current, ToolTip = "The preset name this container came with (not a saved preset here)" });
            _rows.AddManage(_presets.Items);
            _presets.SelectedItem = match ?? _presets.Items[0];
        }
        finally { _filling = false; }
    }

    private async Task SaveAsked()
    {
        if (_shown() is not { } container || AskName(container.Name) is not { } name) return;
        if (PresetRules.Error(name) is { } error) { Say(error); return; }
        if (_store.Names().Contains(name, StringComparer.OrdinalIgnoreCase) && ThemedMessageBox.Show(_owner, $"Replace the preset \"{name}\"?", "Container preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _collectStates();   // the plug-ins' settings as they are now
        if (_owner.IsLoaded && ReferenceEquals(_shown(), container)) Save(name);
    }

    /// <summary>Writes the shown container as a preset (a failed write leaves an older preset intact). False when nothing was written.</summary>
    internal bool Save(string name)
    {
        if (_shown() is not { } container) return false;
        var packed = ChainTree.PackCopy(_rig(), container);
        if (packed.Count == 0) { Say("This container has no plug-ins to save."); return false; }
        try
        {
            var path = _store.PathOf(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            PluginLibrary.SaveChain(path, packed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.Trace.Error(Services.Trace.Ui, "container preset: save: " + ex.Message);
            Say("The preset could not be saved: " + ex.Message);
            return false;
        }
        Fill();
        return true;
    }

    /// <summary>Replaces what is in the shown container with a preset's contents, as one undo step. Plug-ins that are not trusted here are
    /// left out and listed in one message; the file is read with the bounds of an FX chain file. False when nothing changed.</summary>
    internal bool Load(string name)
    {
        if (_shown() is not { } target) return false;
        List<PluginSlot>? chain = null;
        try { chain = PluginLibrary.LoadChain(_store.PathOf(name)); }
        catch (InvalidDataException ex) { Services.Trace.Error(Services.Trace.Ui, "container preset: name: " + ex.Message); }
        if (chain is null || chain.Count == 0) { Say("That preset could not be read."); Fill(); return false; }
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
            Say($"The preset does not fit: a track holds at most {InputLimits.MaxPluginsPerTrack} plug-ins.");
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
            Say("These plug-ins of the preset are not installed or not approved here and were left out:\n\n" + string.Join("\n", skipped.Take(20)) + (skipped.Count > 20 ? $"\n… and {skipped.Count - 20} more" : ""));
        return true;
    }

    private string? AskName(string suggestion)
    {
        var box = new TextBox { Text = suggestion, Margin = new Thickness(0, 0, 0, 10), MaxLength = PresetRules.MaxNameLength };
        System.Windows.Automation.AutomationProperties.SetName(box, "Preset name");
        var ok = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(14, 3, 14, 3) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = "Preset name", Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(box); stack.Children.Add(buttons);
        var dialog = new Window { Title = "Save container preset", Content = stack, Width = 320, SizeToContent = SizeToContent.Height, Owner = _owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        dialog.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        dialog.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        ok.Click += (_, _) => { if (box.Text.Trim().Length > 0) dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
