using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Services.Features;

namespace TabForge.Avalonia.Views;

// Owns: the menu bar of the FX chain window: the FX menu (plug-ins, the chain's save and load), Chains and Options, with the gesture text of each command.
// Does not own: what the commands do (FxChainWindow.Chain.cs RunFxCommand), the key map (Fx/FxChainKeys), the saved chains rows (Fx/FxChainFiles).
// Tests: TabForge.Avalonia.Tests.FxChainKeysTests, TabForge.Avalonia.Tests.FxChainWindowTests.
public sealed partial class FxChainWindow
{
    public const string DockOptionText = "Show plug-in windows in this window", OnTopOptionText = "Keep floating plug-in windows on top", SettingsText = "Plug-in folders and audio settings…";

    private Menu BuildMenu()
    {
        var menu = MenuStability.CreateMenu();
        menu.Name = "FxMenu";
        DockPanel.SetDock(menu, Dock.Top);
        var fx = new MenuItem { Header = "_FX", Name = "FxItem" };
        fx.Items.Add(_keys.Item("Add plug-in…", FxChainIds.AddPlugin));
        fx.Items.Add(_keys.Item("Remove plug-in", FxChainIds.RemovePlugin));
        fx.Items.Add(new Separator());
        fx.Items.Add(_keys.Item("Save FX chain…", FxChainIds.Save));
        fx.Items.Add(_keys.Item("Load FX chain…", FxChainIds.Load));
        fx.Items.Add(_keys.Item("Add FX chain to the end…", FxChainIds.AddToEnd));
        fx.Items.Add(new Separator());
        fx.Items.Add(_keys.Item("Clear chain", FxChainIds.Clear));
        menu.Items.Add(fx);
        var chains = new MenuItem { Header = "_Chains", Name = "ChainsItem" };
        ToolTip.SetTip(chains, "Your saved FX chains: pick one to load it; rename, duplicate, delete or reorder them with Manage presets…");
        _files.FillSavedChains(chains, _keys);
        chains.SubmenuOpened += (_, _) => _files.FillSavedChains(chains, _keys);
        menu.Items.Add(chains);
        var options = new MenuItem { Header = "_Options", Name = "OptionsItem" };
        var dock = CheckItem(DockOptionText, _host.PluginSettings.DockPluginWindows, on =>
        {
            if (on == _host.PluginSettings.DockPluginWindows) return;
            _host.PluginSettings.DockPluginWindows = on;
            _host.SaveSettings();
            _editors.ForgetFloating();
            ShowSelected();
        });
        var onTop = CheckItem(OnTopOptionText, _host.PluginSettings.PluginWindowsOnTop, on => { if (on == _host.PluginSettings.PluginWindowsOnTop) return; _host.PluginSettings.PluginWindowsOnTop = on; _host.SaveSettings(); });
        options.Items.Add(dock);
        options.Items.Add(onTop);
        options.Items.Add(new Separator());
        var settings = new MenuItem { Header = SettingsText };
        settings.Click += (_, _) => _host.OpenAudioSettings(this);
        options.Items.Add(settings);
        options.SubmenuOpened += (_, _) =>
        {
            dock.IsChecked = _host.PluginSettings.DockPluginWindows;
            onTop.IsChecked = _host.PluginSettings.PluginWindowsOnTop;
        };
        menu.Items.Add(options);
        // The gesture text is read from the live bindings each time a menu opens, so a changed shortcut shows at once.
        fx.SubmenuOpened += (_, _) => _keys.Apply(menu);
        _keys.Apply(menu);
        return menu;
    }

    private static MenuItem CheckItem(string header, bool isChecked, System.Action<bool> changed)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = isChecked };
        item.PropertyChanged += (_, e) => { if (e.Property == MenuItem.IsCheckedProperty) changed(item.IsChecked); };
        return item;
    }
}
