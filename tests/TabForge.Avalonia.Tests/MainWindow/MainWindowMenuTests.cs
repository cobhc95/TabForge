using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Docking;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the checks of the main window's menu (built from MainMenuTable), its command handlers and the dock it hosts.</summary>
public class MainWindowMenuTests
{
    private static MainWindow Open()
    {
        var main = new MainWindow();
        main.Show();
        return main;
    }

    // The menu row: 30 px directly under the 44 px title row, File's header text 16 px from the left edge.
    [AvaloniaFact]
    public void MenuBarMatchesWpfRowHeightAndHeaderInset()
    {
        var main = new MainWindow { Width = 1320, Height = 900 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var menu = main.GetVisualDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu");
        Assert.Equal(44, menu.TranslatePoint(default, main)!.Value.Y, 0.5);
        Assert.Equal(30, menu.FindAncestorOfType<Border>()!.Bounds.Height, 0.5);
        var file = menu.GetVisualDescendants().OfType<MenuItem>().First();
        var text = file.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsVisible && t.Bounds.Width > 0);
        Assert.Equal(16, text.TranslatePoint(default, main)!.Value.X, 1.0);
        main.Close();
    }

    [AvaloniaFact]
    public void EveryMenuTableCommandIsHandledOrDisabled()
    {
        var main = Open();
        var items = main.GetLogicalDescendants().OfType<MenuItem>().Where(m => m.Tag is string).ToDictionary(m => (string)m.Tag!);
        var ids = MainMenuTable.CommandRows(MainMenuTable.WithFeatureRows()).Select(r => r.CommandId!).ToList();
        Assert.All(ids, id => Assert.True(items.ContainsKey(id), $"no menu item for {id}"));
        var commands = new MainWindowCommands(main, () => { });
        commands.Add("Tab.New", () => { });
        commands.Add("Tab.Close", () => { });
        commands.Add("File.Save", () => { });
        commands.Add("File.SaveAs", () => { });
        // Convention: any public type in the Avalonia assembly with a public static string list field named Commands is discovered here.
        var declared = typeof(MainWindow).Assembly.GetTypes()
            .Select(t => t.GetField("Commands", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            .Where(f => f != null && typeof(System.Collections.Generic.IEnumerable<string>).IsAssignableFrom(f.FieldType))
            .SelectMany(f => (System.Collections.Generic.IEnumerable<string>)f!.GetValue(null)!);
        var inline = new[] { "File.New", "File.Open", "File.OpenInNewTab", "File.ExportMidi", "Edit.RepeatSelection", "Note.TrillEditor", "Note.HarmonicEditor", "Note.BendEditor", "Note.TremoloBarEditor", "Note.Grace", "Note.Harmonic", "Note.Trill", "Transport.PlayPause", "File.ExportPdf", "Transport.RecordVideo", "Edit.SelectAll", "View.ZoomIn", "View.ZoomOut" };
        foreach (var id in inline.Concat(declared).Distinct()) commands.Add(id, () => { });
        Assert.All(ids, id => Assert.Equal(commands.Handles(id), items[id].IsEnabled));
        Assert.True(items["File.Exit"].IsEnabled);
        Assert.True(items["Tools.Tuner"].IsEnabled);
        Assert.Equal(MenuItemToggleType.CheckBox, items["View.InstrumentPanel"].ToggleType);
        Assert.True(items["View.LearnMode"].IsEnabled);   // Keyboard mode has its Avalonia handler (MainWindowLearnMode)
        Assert.True(items["File.Save"].IsEnabled);
        Assert.True(items["File.SaveAs"].IsEnabled);
        Assert.All(MainWindowClipboard.Commands, id => Assert.True(items[id].IsEnabled, id));
        Assert.All(MainWindowClipKeys.Commands, id => Assert.True(commands.Handles(id), id));
        Assert.All(MainWindowClipEditKeys.Commands, id => Assert.True(commands.Handles(id), id));
        Assert.All(MainWindowFileExports.Commands, id => Assert.True(commands.Handles(id), id));
        Assert.All(MainWindowSaving.Commands, id => Assert.True(commands.Handles(id), id));
        Assert.All(MainWindowTrackArmKeys.Commands, id => Assert.True(commands.Handles(id), id));
        Assert.All(MainWindowClipProperties.Commands, id => Assert.True(commands.Handles(id), id));
        main.Close();
    }

    [AvaloniaFact]
    public void InstrumentPanelRowFlipsItsCheck()
    {
        var main = Open();
        var row = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => Equals(m.Tag, "View.InstrumentPanel"));
        var before = row.IsChecked;
        row.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.NotEqual(before, row.IsChecked);
        main.Close();
    }

    /// <summary>Every main-menu row is enabled (a handler exists) or listed here as not yet ported; the listed rows are exactly the disabled ones.</summary>
    [AvaloniaFact]
    public void EveryMainMenuIdHasHandlerOrIsListedAsNotYetPorted()
    {
        var main = Open();
        var items = main.GetLogicalDescendants().OfType<MenuItem>().Where(m => m.Tag is string).ToDictionary(m => (string)m.Tag!);
        var ids = MainMenuTable.CommandRows(MainMenuTable.WithFeatureRows()).Select(r => r.CommandId!).Distinct().ToList();
        // Every menu row has its handler (PDF export is MainWindowScorePdf) except Print and Print preview: they have none, so the menu row, Ctrl+P and the palette all see them as not available.
        var notYetPorted = new[] { "File.Print", "File.PrintPreview" };
        var disabled = ids.Where(id => !items[id].IsEnabled).ToList();
        Assert.Equal(notYetPorted.OrderBy(x => x), disabled.OrderBy(x => x));
        main.Close();
    }

    [AvaloniaFact]
    public void MenuBarHasTheTopLevelMenusInOrderAndTheTableRows()
    {
        var main = Open();
        var menu = main.GetLogicalDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu");
        var tops = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(MainWindowMenu.TopLevelOrder, tops.Select(t => (string)t.Header!));
        Assert.Equal(MainMenuTable.Groups.Select(g => g.Menu).Distinct().OrderBy(x => x), MainWindowMenu.TopLevelOrder.OrderBy(x => x));
        foreach (var top in tops)
        {
            var rows = MainMenuTable.WithFeatureRows().Where(g => g.Menu == (string)top.Header!).SelectMany(g => g.Rows).ToList();
            AssertSameTree(rows, top);
        }
        main.Close();
    }

    // One menu level against its table rows: the same submenus (by caption) and the same command rows (by tag), in order; the View menu's dock items are not table rows.
    private static void AssertSameTree(IReadOnlyList<MainMenuRow> rows, MenuItem menu)
    {
        var expected = rows.Where(r => !r.IsSeparator).ToList();
        var shown = menu.Items.OfType<MenuItem>().Where(i => !((string?)i.Header is "Panels" or "Layouts")).ToList();
        Assert.Equal(expected.Select(r => r.CommandId ?? r.Header), shown.Select(i => (string?)i.Tag ?? (string?)i.Header));
        foreach (var (row, item) in expected.Zip(shown))
            if (row.IsSubmenu) AssertSameTree(row.Children!, item);
    }

    // The View menu: Panels and Layouts sit right above Reset all panels; the Panels rows are the side dock panes in DockPaneTable order.
    [AvaloniaFact]
    public void ViewMenuHasPanelsAndLayoutsAboveResetWithTheSidePaneRows()
    {
        var main = Open();
        var menu = main.GetLogicalDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu");
        var view = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "_View"));
        var items = view.Items.OfType<MenuItem>().ToList();
        var headers = items.Select(i => i.Header as string).ToList();
        var panels = headers.IndexOf("Panels");
        Assert.True(panels >= 0 && headers[panels + 1] == "Layouts" && headers[panels + 2] == "Reset all panels to default positions", string.Join(" | ", headers));
        var panelRows = items[panels].Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Equal(new[] { "Tools", "Structure", "Rhythm", "Layout", "Sections", "Lyrics", "View and tempo" }, panelRows);
        main.Close();
    }

    // Options > Stylesheet left the menu; its command still opens Settings (palette) and is a plain Settings open: Home before any open, the last page again after.
    [Fact]
    public void OptionsStylesheetKeepsTheLastPageInsteadOfPinningHome()
    {
        Assert.Equal(SettingsCatalog.Home, MainWindowOptionsMenu.ResolvePage(null));
        Assert.Equal(SettingsCatalog.Appearance, MainWindowOptionsMenu.ResolvePage(SettingsCatalog.Appearance));
    }

    [AvaloniaFact]
    public void TheReleaseMenuHasNoDevelopmentSubmenusAndExitClosesTheWindow()
    {
        var main = Open();
        var headers = main.GetLogicalDescendants().OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Assert.Contains("Band view", headers);
        Assert.DoesNotContain("Track prompts", headers);
        Assert.DoesNotContain("Preview dialogs", headers);
        new MainWindowCommands(main, () => { }).Run("File.Exit");
        Assert.False(main.IsVisible);
    }

    [AvaloniaFact]
    public void MainWindowHostsTheDockWithTheDefaultPanes()
    {
        var main = Open();
        var dock = main.GetLogicalDescendants().OfType<DockWorkspaceView>().Single();
        Assert.Single(dock.GetLogicalDescendants().OfType<ScorePreviewView>());
        Assert.Single(dock.GetLogicalDescendants().OfType<InstrumentPanelStrip>());
        Assert.Single(dock.GetLogicalDescendants().OfType<ArrangementView>());
        Assert.Single(dock.GetLogicalDescendants().OfType<ToolsPaneView>());
        Assert.True(dock.HostControl("default-timeline") is not null);
        main.Close();
    }

    [AvaloniaFact]
    public void InstrumentPaneHasNoTabAndItsXButtonHidesIt()
    {
        var main = Open();
        var tab = main.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "DockTab_instrument");
        Assert.False(tab.IsEffectivelyVisible, "the solo fretboard pane shows no title tab");
        var strip = main.GetLogicalDescendants().OfType<InstrumentPanelStrip>().Single();
        strip.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "InstrumentHideButton").RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(main.GetLogicalDescendants().OfType<InstrumentPanelStrip>());
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "dark")]
    [InlineData(true, "light")]
    public void MainWindowRendersTheDefaultLayoutAndTheFretboardShowsEveryString(bool light, string themeName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var main = new MainWindow();
        DialogTestKit.SavePng(main, nameof(MainWindowMenuTests), $"main-window-{themeName}.png");
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();   // the default layout's first size change sets the fretboard pane's height; this pass lays that out
        Assert.Equal((1500d, 960d, 1080d, 720d), (main.Width, main.Height, main.MinWidth, main.MinHeight));   // the first-start size and minimum
        double Top(Control c) => c.TranslatePoint(default, main)!.Value.Y;
        var editor = main.GetLogicalDescendants().OfType<ScorePreviewView>().Single();
        var strip = main.GetLogicalDescendants().OfType<InstrumentPanelStrip>().Single();
        var timeline = main.GetLogicalDescendants().OfType<ArrangementView>().Single();
        var editorPane = editor.FindAncestorOfType<ScrollViewer>()!;   // the score view is scrolled inside its pane, so its own top is not the pane's
        // Compose: the fretboard above the score (the default, FretboardAtBottom off), the arrangement along the bottom.
        Assert.True(Top(strip) < Top(editorPane) && Top(editorPane) < Top(timeline), "pane order top to bottom: instrument, editor, timeline");
        Assert.True(timeline.Bounds.Height > 40, "the arrangement is visible");
        var surface = strip.FindAncestorOfType<DockTabSurface>()!;
        Assert.True(Top(strip.Panel) + strip.Panel.Bounds.Height <= Top(surface) + surface.Bounds.Height + 0.5, $"the whole fretboard is inside its pane: panel {Top(strip.Panel)}+{strip.Panel.Bounds.Height}, pane {Top(surface)}+{surface.Bounds.Height}");
        Assert.Equal(TabForge.Avalonia.Platform.InstrumentPanelView.PanelHeight, strip.Panel.Bounds.Height, 1);
        Assert.True(strip.Panel.Bounds.Width <= surface.Bounds.Width + 0.5, $"the whole fretboard is inside its pane width: panel {strip.Panel.Bounds.Width}, pane {surface.Bounds.Width}");
        main.Close();
    }
}