using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the checks of the quick action icon buttons (the left strip's file icons) and the menu row having no Open / Save group.</summary>
public class QuickActionsGroupTests
{
    private const string OpenIcon = "M3,6 H9 L11,8 H21 V19 H3 Z";

    private static MainWindow Open()
    {
        var main = new MainWindow { Width = 1320, Height = 900 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        return main;
    }

    [AvaloniaFact]
    public void TheMenuRowHasNoQuickActionButtons()
    {
        var main = Open();
        var row = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(main).OfType<Border>().Single(b => b.Name == "MainMenuRow");
        Assert.DoesNotContain(global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(row).OfType<Border>(), b => b.Name == "QuickActionsGroup");
        main.Close();
    }

    [AvaloniaFact]
    public void TheStripToggleSitsBeforeFileWithoutCoveringIt()
    {
        var main = Open();
        var all = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(main).ToList();
        var toggle = all.OfType<global::Avalonia.Controls.Primitives.ToggleButton>().First(b => (b.Tag as string) == "View.ToggleLeftStrip");
        var file = all.OfType<MenuItem>().First(m => (m.Header as string) == "_File");
        var toggleRight = toggle.TranslatePoint(new global::Avalonia.Point(toggle.Bounds.Width, 0), main)!.Value.X;
        var fileLeft = file.TranslatePoint(default, main)!.Value.X;
        Assert.True(toggleRight <= fileLeft + 0.01, $"toggle right {toggleRight}, File left {fileLeft}");
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
        var state = TabForge.Docking.DockWorkspaceRules.Restore(saved, true, tree.ToList(), _ => false, _ => ("", ""), new System.Collections.Generic.List<string>());
        Assert.DoesNotContain("quickactions", state.ClosedPanels);
        Assert.DoesNotContain("quickactions", TabForge.Docking.DockLayoutTree.EnumerateAllPanels(state));
        main.Close();
    }

    [AvaloniaFact]
    public void AButtonRunsItsCommandAndItsTipUsesTheCatalogKey()
    {
        var main = Open();
        var commands = new MainWindowCommands(main, () => { });
        var ran = 0;
        commands.Add("File.Open", () => ran++);
        var button = QuickActionsGroup.Button(() => commands, "File.Open", "Open", OpenIcon);
        var host = new Window { Content = button };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsEnabled);
        Assert.True(button.Focusable);
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, ran);
        var hotkeys = new HotkeySettings();
        TabForge.Avalonia.Platform.Controls.HotkeyTip.SetSource(button, () => hotkeys);
        Assert.Equal("Open (" + HotkeyCatalog.DisplayAll(hotkeys, "File.Open") + ")", TabForge.Avalonia.Platform.Controls.HotkeyTip.Compose(button));
        host.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void AButtonIsDisabledWithoutAHandler()
    {
        var main = Open();
        var button = QuickActionsGroup.Button(() => new MainWindowCommands(main, () => { }), "File.Open", "Open", OpenIcon);
        var host = new Window { Content = button };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEnabled);
        host.Close();
        main.Close();
    }
}
