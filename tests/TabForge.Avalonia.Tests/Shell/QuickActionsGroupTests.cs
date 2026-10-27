using System;
using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the checks of the Open / Save quick actions group in the menu row.</summary>
public class QuickActionsGroupTests
{
    private static MainWindow Open()
    {
        var main = new MainWindow { Width = 1320, Height = 900 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        return main;
    }

    private static Border Row(MainWindow main) => main.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MainMenuRow");

    [AvaloniaFact]
    public void GroupSitsBeforeTheViewIconsWithOpenAndSave()
    {
        var main = Open();
        var panel = (DockPanel)Row(main).Child!;
        var group = QuickActionsGroup.In(Row(main))!;
        var toggles = panel.Children.OfType<StackPanel>().Single(p => p.Name == MenuRowToggles.PanelName);
        Assert.True(panel.Children.IndexOf(group) > panel.Children.IndexOf(toggles));   // docked right after the icons: sits left of them
        Assert.True(group.TranslatePoint(default, main)!.Value.X < toggles.TranslatePoint(default, main)!.Value.X);
        var buttons = group.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains(QuickActionsGroup.ButtonClass)).ToList();
        Assert.Equal(new object?[] { "File.Open", "File.Save" }, buttons.Select(b => b.Tag).ToArray());
        Assert.Equal(new[] { "Open", "Save" }, buttons.Select(b => global::Avalonia.Automation.AutomationProperties.GetName(b)).ToArray());
        Assert.All(buttons, b => Assert.True(b.Focusable));
        main.Close();
    }

    [AvaloniaFact]
    public void GroupHasNoGripAndIsNotAPane()
    {
        var group = QuickActionsGroup.Build(() => null);
        var host = new Window { Width = 400, Height = 200, Content = new StackPanel { Children = { group } } };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(group.GetVisualDescendants().OfType<Border>(), b => b.Opacity == 0.6);
        Assert.Equal(2, group.GetVisualDescendants().OfType<Button>().Count());
        Assert.DoesNotContain(TabForge.Docking.DockPaneTable.Ids, id => id == "quickactions");
        host.Close();
    }

    [AvaloniaFact]
    public void SeparatorSitsBetweenTheGroupAndTheViewIcons()
    {
        var main = Open();
        var panel = (DockPanel)Row(main).Child!;
        var group = QuickActionsGroup.In(Row(main))!;
        var toggles = panel.Children.OfType<StackPanel>().Single(p => p.Name == MenuRowToggles.PanelName);
        var i = panel.Children.IndexOf(group);
        Assert.Equal(i - 1, panel.Children.IndexOf(panel.Children.OfType<Border>().First(b => b.Width == 1 && b.Height == 15)));
        Assert.True(panel.Children.IndexOf(toggles) < i - 1);
        main.Close();
    }

    [AvaloniaFact]
    public void AStaleQuickActionsPaneInASavedLayoutIsDropped()
    {
        var main = Open();
        var saved = TabForge.Docking.DockWorkspaceRules.Capture(main.DockWorkspace.State);
        var tree = TabForge.Docking.DockLayoutTree.EnumerateAllPanels(saved).ToList();
        Assert.DoesNotContain("quickactions", tree);
        saved.ClosedPanels.Add("quickactions");
        Assert.True(TabForge.Docking.DockPaneTable.IsSavedId("quickactions"));   // an old layout still validates, then the pane is dropped
        var registered = tree.ToList();
        var state = TabForge.Docking.DockWorkspaceRules.Restore(saved, true, registered, _ => false, _ => ("", ""), new System.Collections.Generic.List<string>());
        Assert.DoesNotContain("quickactions", state.ClosedPanels);
        Assert.DoesNotContain("quickactions", TabForge.Docking.DockLayoutTree.EnumerateAllPanels(state));
        main.Close();
    }

    [AvaloniaFact]
    public void ButtonsRunTheirCommandsAndTipsUseTheCatalogKeys()
    {
        var main = Open();
        var commands = new MainWindowCommands(main, () => { });
        var ran = new System.Collections.Generic.List<string>();
        commands.Add("File.Open", () => ran.Add("open"));
        commands.Add("File.Save", () => ran.Add("save"));
        var group = QuickActionsGroup.Build(() => commands);
        var host = new Window { Content = group };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        var buttons = group.GetVisualDescendants().OfType<Button>().ToList();
        Assert.All(buttons, b => Assert.True(b.IsEnabled));
        buttons[0].RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        buttons[1].RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { "open", "save" }, ran);
        var hotkeys = new HotkeySettings();
        Assert.Equal("Open (" + TabForge.Services.HotkeyCatalog.DisplayAll(hotkeys, "File.Open") + ")", HotkeyTipText(buttons[0], hotkeys));
        Assert.Equal("Save (" + TabForge.Services.HotkeyCatalog.DisplayAll(hotkeys, "File.Save") + ")", HotkeyTipText(buttons[1], hotkeys));
        host.Close();
        main.Close();
    }

    private static string HotkeyTipText(Control button, HotkeySettings keys)
    {
        TabForge.Avalonia.Platform.Controls.HotkeyTip.SetSource(button, () => keys);
        return TabForge.Avalonia.Platform.Controls.HotkeyTip.Compose(button);
    }

    [AvaloniaFact]
    public void ButtonsAreDisabledWithoutAHandler()
    {
        var main = Open();
        var group = QuickActionsGroup.Build(() => new MainWindowCommands(main, () => { }));
        var host = new Window { Content = group };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.All(group.GetVisualDescendants().OfType<Button>(), b => Assert.False(b.IsEnabled));
        host.Close();
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupFillIsDistinctFromTheRowInBothThemes(bool light)
    {
        var fill = AvaloniaPalette.Get(QuickActionsGroup.FillToken, light);
        var panel = AvaloniaPalette.Get("PanelBackground", light);
        Assert.NotEqual(panel, fill);
        Assert.True(Math.Abs(AvaloniaPalette.ColorOf(fill).R - AvaloniaPalette.ColorOf(panel).R) >= 8);
        var c = AvaloniaPalette.ColorOf(fill);
        Assert.True(Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) <= 8);   // neutral, not accent-coloured
        var main = Open();
        main.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.TryFindResource(QuickActionsGroup.FillToken, main.ActualThemeVariant, out var brush) && brush is not null);
        Assert.NotNull(QuickActionsGroup.In(Row(main))!.Background);
        main.Close();
    }
}
