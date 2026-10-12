using System.Windows;
using System.Windows.Controls;
using TabForge.Services.Features;
using TabForge.Plugins;
using TabForge.Views.Presets;

namespace TabForge.Views.Fx;

// Owns: the two preset menus of the FX chain window: the "+" menu beside a plug-in's preset drop-down (save, delete, rename)
//   and the Chains menu of the menu bar (the chains saved in the chains folder, save, manage), with the replace-or-add question.
// Does not own: saving a preset or loading a chain (FxChainWindow does both, through the actions it passes), or the rows' rename, duplicate,
//   delete and reorder (PresetRows over a PresetFolderStore).
// Tests: TestPresetStoresOnDisk (the stores behind both menus).
internal static class FxPresetMenus
{
    /// <summary>The "+" button: opens Save preset at once.</summary>
    internal static void ShowPresetMenu(Button button, PresetRows rows, PluginSlot? slot, string? user, Action save) => save();   // saving is the one thing "+" does; rename and delete are on the rows of the drop-down

    /// <summary>The Chains menu: one row per saved chain (loads it), then Save and Manage. <paramref name="load"/> gets replace-or-add and the chain's file.</summary>
    internal static MenuItem BuildChainsMenu(Window owner, Func<bool> chainIsEmpty, Action<bool, string> load, FxChainKeys keys)
    {
        var store = PresetFolderStore.FxChains();
        var menu = UiIds.Id(new MenuItem { Header = "_Chains", ToolTip = "Your saved FX chains: pick one to load it; rename, duplicate, delete or reorder them in the list" }, "Fx.Chains");
        PresetRows rows = null!;
        void Pick(string name)
        {
            var replace = true;
            if (!chainIsEmpty())
            {
                var answer = ThemedMessageBox.Show(owner, $"Replace this chain with \"{name}\"?\n\nYes: replace the plug-ins of this chain.\nNo: add the saved chain to the end.", "Load FX chain", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer is not (MessageBoxResult.Yes or MessageBoxResult.No)) return;
                replace = answer == MessageBoxResult.Yes;
            }
            load(replace, store.PathOf(name));
        }
        void Fill()
        {
            menu.Items.Clear();
            var names = store.Names();
            foreach (var name in names)
            {
                var item = Item("", () => Pick(name));
                item.Header = rows.Row(name, name);
                System.Windows.Automation.AutomationProperties.SetName(item, name);
                menu.Items.Add(item);
            }
            if (names.Count == 0) menu.Items.Add(new MenuItem { Header = "No saved chains yet", IsEnabled = false });
            menu.Items.Add(new Separator());
            menu.Items.Add(keys.Item("Save FX chain…", FxChainIds.Save));
            menu.Items.Add(keys.Item("Save as FX chain for other hosts (.RfxChain)…", FxChainIds.SaveForHosts));
            ((MenuItem)menu.Items[^1]!).ToolTip = "Writes this chain, with its containers and the plug-ins' current settings, as a chain file other hosts can load";
            menu.Items.Add(new Separator());
            menu.Items.Add(rows.ManageMenuItem());
        }
        rows = PresetRows.Attach(menu, PresetKind.FxChain, () => store, _ => null);
        rows.RenameElsewhere = name => PresetManagePanel.Open(menu, store, "Saved FX chains", name);   // typing inside an open menu belongs to the menu
        store.Changed += () => { if (menu.IsSubmenuOpen) Fill(); };
        menu.SubmenuOpened += (_, e) => { if (ReferenceEquals(e.OriginalSource, menu)) Fill(); };
        Fill();
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
