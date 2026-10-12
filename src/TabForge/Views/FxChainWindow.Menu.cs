using System.Windows;
using System.Windows.Controls;
using TabForge.Services;
using TabForge.Services.Features;
using TabForge.Views.Fx;

namespace TabForge.Views;

// Owns: the menu bar of the FX chain window: the FX menu (plug-ins, the chain's save and load), Chains and Options.
// Does not own: what the commands do (FxChainWindow.RunFxCommand), the key map (Fx/FxChainKeys).
// Tests: TestFxChainCommandScope, TestFxChainWindow.
public sealed partial class FxChainWindow
{
    private Menu BuildMenu()
    {
        var menu = new Menu();
        DockPanel.SetDock(menu, Dock.Top);
        var fx = new MenuItem { Header = "_FX" };
        fx.Items.Add(_keys.Item("Add plug-in…", FxChainIds.AddPlugin));
        fx.Items.Add(_keys.Item("Remove plug-in", FxChainIds.RemovePlugin));
        fx.Items.Add(new Separator());
        fx.Items.Add(_keys.Item("Save FX chain…", FxChainIds.Save));
        fx.Items.Add(_keys.Item("Load FX chain…", FxChainIds.Load));
        fx.Items.Add(_keys.Item("Add FX chain to the end…", FxChainIds.AddToEnd));
        fx.Items.Add(new Separator());
        fx.Items.Add(_keys.Item("Clear chain", FxChainIds.Clear));
        menu.Items.Add(fx);
        menu.Items.Add(Fx.FxPresetMenus.BuildChainsMenu(this, () => _track.Rig.Plugins.Count == 0, (replace, file) => LoadChain(replace, file), _keys));
        menu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler((_, _) => Fx.FxChainKeys.Apply(menu)));
        Fx.FxChainKeys.Apply(menu);
        var options = new MenuItem { Header = "_Options" };
        var dock = new MenuItem { Header = "Show plug-in windows in this window", IsCheckable = true, IsChecked = _host.PluginSettings.DockPluginWindows };
        dock.Click += (_, _) => { _host.PluginSettings.DockPluginWindows = dock.IsChecked; _host.SaveSettings(); _floating.Clear(); ShowSelected(); };
        var onTop = new MenuItem { Header = "Keep floating plug-in windows on top", IsCheckable = true, IsChecked = _host.PluginSettings.PluginWindowsOnTop };
        onTop.Click += (_, _) => { _host.PluginSettings.PluginWindowsOnTop = onTop.IsChecked; _host.SaveSettings(); };
        options.Items.Add(dock);
        options.Items.Add(onTop);
        options.Items.Add(new Separator());
        options.Items.Add(Item("Plug-in folders and audio settings…", _host.OpenAudioSettings));
        menu.Items.Add(options);
        return menu;

        static MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }
    }
}
