using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Hotkey tooltips: composed from the live bindings, and no key text typed into tooltip strings.</summary>
public sealed class HotkeyTipTests
{
    [AvaloniaFact]
    public void TooltipFollowsTheCurrentBinding()
    {
        var keys = new HotkeySettings();
        var window = new Window();
        HotkeyTip.SetSource(window, () => keys);
        var button = new Button();
        window.Content = button;
        keys.Set("View.InstrumentPanel", 1, "F9");
        HotkeyTip.Set(button, "View.InstrumentPanel", "Hide fretboard / keyboard");
        Assert.Equal("Hide fretboard / keyboard (F9)", ToolTip.GetTip(button));
        keys.Set("View.InstrumentPanel", 1, "F7");
        HotkeyTip.Refresh(button);
        Assert.EndsWith(" (F7)", (string)ToolTip.GetTip(button)!);
    }

    // The tutorial search key is local to that window, not a catalogue command; the transport bar is owned by another lane.
    private static readonly string[] AllowedFiles = { "TutorialWindow.cs", "TransportBarView.cs" };

    [AvaloniaFact]
    public void NoHardWiredKeyTextInTooltips()
    {
        var root = Path.Combine(TestRepo.CheckoutRoot(), "Source", "src", "TabForge.Avalonia");
        var pattern = new Regex("(SetTip|HotkeyTip\\.Set)\\(.*\"[^\"]*\\((Space|Ctrl\\+|Shift\\+|Alt\\+|F[0-9]+\\))");
        var hits = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !AllowedFiles.Contains(Path.GetFileName(f)))
            .SelectMany(f => File.ReadLines(f).Where(l => pattern.IsMatch(l)).Select(l => Path.GetFileName(f) + ": " + l.Trim()))
            .ToList();
        Assert.True(Directory.Exists(root), root);
        Assert.Empty(hits);
    
    }

    /// <summary>Every control under <paramref name="root"/> carrying a command id: its tooltip is the composed text, and a rebinding changes it.</summary>
    private static int SweepFollowsRebinding(Control root, HotkeySettings keys)
    {
        var tipped = root.GetLogicalDescendants().OfType<Control>().Where(c => HotkeyTip.GetCommandId(c) is not null).ToList();
        foreach (var control in tipped)
        {
            var id = HotkeyTip.GetCommandId(control)!;
            var text = control.GetValue(HotkeyTip.TextProperty)!;
            HotkeyTip.Refresh(control);
            Assert.Equal(HotkeyCatalog.TooltipWithKey(keys, text, id), ToolTip.GetTip(control));
            keys.Set(id, 1, "Ctrl+Shift+F12");
            HotkeyTip.Refresh(control);
            Assert.Contains("Ctrl+Shift+F12", (string)ToolTip.GetTip(control)!);
        }
        return tipped.Count;
    }

    [AvaloniaFact]
    public void MainWindowCommandControlsShowTheLiveShortcut()
    {
        var main = new MainWindow();
        main.Show();
        var keys = new HotkeySettings();
        HotkeyTip.SetSource(main, () => keys);
        Assert.True(SweepFollowsRebinding(main, keys) > 0);
        main.Close();
    }

    [AvaloniaFact]
    public void PaletteAndTabButtonsShowTheLiveShortcut()
    {
        var keys = new HotkeySettings();
        var pane = new ToolsPaneView(new TabForge.Avalonia.Platform.ScorePreviewView());
        var tabs = new TabStripView(new TabForge.Services.TabSettings());
        var window = new Window { Content = new StackPanel { Children = { pane, tabs } } };
        HotkeyTip.SetSource(window, () => keys);
        Assert.True(SweepFollowsRebinding(pane, keys) >= PaletteToolCatalog.PaletteTools.Length / 2);
        Assert.True(SweepFollowsRebinding(tabs, keys) >= 1);
    }

    [AvaloniaFact]
    public void MenuGestureTextFollowsARebinding()
    {
        var keys = new HotkeySettings();
        var top = new MenuItem { Header = "_File" };
        var item = new MenuItem { Header = "New tab", Tag = "Tab.New" };
        top.Items.Add(item);
        keys.Set("Tab.New", 1, "Ctrl+Shift+F11");
        MainWindowMenu.RefreshGestures(top, keys);
        Assert.Equal("Ctrl+Shift+F11", item.InputGesture?.ToString());
    }
}
