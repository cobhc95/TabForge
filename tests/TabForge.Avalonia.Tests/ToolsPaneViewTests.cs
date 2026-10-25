using Avalonia;
using Avalonia.Headless;
using Avalonia.VisualTree;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// The Tools pane: one button per Tools-tab row in catalogue order; a button runs its command through the window's command table and is disabled when that table has no handler.
public sealed class ToolsPaneViewTests
{
    [Fact]
    public void PaletteTabsHoldTheWpfRowCountsInStripOrder()
    {
        Assert.Equal(new[] { "tools", "structure", "rhythm", "layout" }, PaletteToolCatalog.PaletteTabs.Select(tab => tab.Id));
        Assert.Equal(22, PaletteToolCatalog.StructureTab.Length);
        Assert.Equal(8, PaletteToolCatalog.RhythmTab.Length);
        Assert.Equal(11, PaletteToolCatalog.LayoutTab.Length);
    }

    [AvaloniaFact]
    public void ToolsPaneHasOneButtonPerToolsTabRowInCatalogueOrder()
    {
        var pane = new ToolsPaneView(new ScorePreviewView());

        Assert.Equal(PaletteToolCatalog.ToolsTab.Select(tool => tool.Label), pane.Entries.Select(entry => entry.Chrome.GetValue(TabForge.Avalonia.Platform.Controls.HotkeyTip.TextProperty)));
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IconButtonOfEveryPaletteHitTestsHoversAndDarkensOnPress(int tab)
    {
        var rows = new[] { PaletteToolCatalog.ToolsTab, PaletteToolCatalog.StructureTab, PaletteToolCatalog.RhythmTab, PaletteToolCatalog.LayoutTab }[tab];
        var pane = new ToolsPaneView(new ScorePreviewView(), rows);
        var window = new Window { Width = 400, Height = 600, Content = pane };
        window.Show();
        var entry = pane.Entries[0];
        entry.Enabled = true;
        var centre = entry.Chrome.TranslatePoint(new global::Avalonia.Point(entry.Chrome.Bounds.Width / 2, entry.Chrome.Bounds.Height / 2), window)!.Value;
        var rest = entry.Chrome.Background;
        Assert.NotNull(rest);   // a transparent fill, not none: the whole rectangle takes the pointer
        window.MouseMove(centre);
        var hover = entry.Chrome.Background;
        Assert.NotEqual(rest, hover);
        window.MouseDown(centre, global::Avalonia.Input.MouseButton.Left);
        Assert.NotEqual(hover, entry.Chrome.Background);
        window.MouseUp(centre, global::Avalonia.Input.MouseButton.Left);
    }

    [AvaloniaFact]
    public void EveryToolsTabButtonHasACommandAndHoversWhenEnabled()
    {
        var score = new ScorePreviewView();
        var owner = new Window { Content = score };
        var commands = new MainWindowCommands(owner, () => { });
        MainWindowEdit.Register(commands, score, owner, new ContextMenuHost(), () => false, _ => { });
        var pane = new ToolsPaneView(score);
        pane.Attach(commands);
        var window = new Window { Width = 400, Height = 3000, Content = pane };
        window.Show();
        foreach (var entry in pane.Entries.Where(entry => entry.Tool.Supported))
        {
            Assert.NotNull(PaletteToolCatalog.CommandIdFor(entry.Tool.Id));
            Assert.True(entry.Enabled, entry.Tool.Id);
            var centre = entry.Chrome.TranslatePoint(new global::Avalonia.Point(entry.Chrome.Bounds.Width / 2, entry.Chrome.Bounds.Height / 2), window)!.Value;
            var rest = entry.Chrome.Background;
            window.MouseMove(centre);
            Assert.NotEqual(rest, entry.Chrome.Background);
            window.MouseMove(new global::Avalonia.Point(0, 0));
        }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NarrowPaneWrapsEveryButtonInsideItsWidthAndScrollsDown(int tab)
    {
        var rows = new[] { PaletteToolCatalog.ToolsTab, PaletteToolCatalog.StructureTab, PaletteToolCatalog.RhythmTab, PaletteToolCatalog.LayoutTab }[tab];
        var pane = new ToolsPaneView(new ScorePreviewView(), rows);
        var window = new Window { Width = 220, Height = 120, Content = pane };
        window.Show();
        window.UpdateLayout();
        foreach (var entry in pane.Entries)
        {
            var left = entry.Chrome.TranslatePoint(default, pane)!.Value.X;
            Assert.True(left >= 0 && left + entry.Chrome.Bounds.Width <= pane.Bounds.Width, entry.Tool.Id);
        }
        var scroll = pane.GetVisualDescendants().OfType<ScrollViewer>().Single();
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
    }

    [AvaloniaFact]
    public void ToolsPanesInTheDefaultLayoutKeepEveryButtonInsideTheirWidth()
    {
        var main = new MainWindow();
        main.Show();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        var panes = main.GetLogicalDescendants().OfType<ToolsPaneView>().Where(pane => pane.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(panes);
        foreach (var pane in panes)
        {
            foreach (var entry in pane.Entries)
            {
                var left = entry.Chrome.TranslatePoint(default, pane)!.Value.X;
                Assert.True(left >= 0 && left + entry.Chrome.Bounds.Width <= pane.Bounds.Width + 0.5, $"{entry.Tool.Id} at {left} in a pane {pane.Bounds.Width} wide");
            }
        }
        main.Close();
    }

    [AvaloniaFact]
    public void ToolsPaneOverflowShowsAVisibleScrollBarThatReachesTheLastBeatButton()
    {
        var main = new MainWindow();
        main.MinHeight = 300;
        main.Show();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        var pane = main.GetLogicalDescendants().OfType<ToolsPaneView>().Single(candidate => candidate.IsEffectivelyVisible && candidate.Entries.Any(entry => entry.Tool.Group == "Beat"));
        var scroll = pane.GetVisualDescendants().OfType<ScrollViewer>().Single();
        AssertOverflowScrollsToLastBeatButton(main, pane, scroll);

        main.Height = 420;
        main.UpdateLayout();
        AssertOverflowScrollsToLastBeatButton(main, pane, scroll);
        main.Close();
    }

    private static void AssertOverflowScrollsToLastBeatButton(Window main, ToolsPaneView pane, ScrollViewer scroll)
    {
        main.UpdateLayout();
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, $"extent {scroll.Extent.Height} viewport {scroll.Viewport.Height}");
        Assert.False(scroll.AllowAutoHide);
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(candidate => candidate.Orientation == Orientation.Vertical);
        Assert.True(bar.IsVisible);
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        main.UpdateLayout();
        var last = pane.Entries.Last(entry => entry.Tool.Group == "Beat").Chrome;
        var top = last.TranslatePoint(default, scroll)!.Value.Y;
        Assert.True(top >= 0 && top + last.Bounds.Height <= scroll.Viewport.Height + 0.5, $"last Beat button at {top} in a viewport {scroll.Viewport.Height} tall");
    }

    [AvaloniaFact]
    public void StructurePaneHasOneButtonPerStructureRowInCatalogueOrder()
    {
        var pane = new ToolsPaneView(new ScorePreviewView(), PaletteToolCatalog.StructureTab);

        Assert.Equal(PaletteToolCatalog.StructureTab.Select(tool => tool.Label), pane.Entries.Select(entry => entry.Chrome.GetValue(TabForge.Avalonia.Platform.Controls.HotkeyTip.TextProperty)));
    }

    [AvaloniaFact]
    public void ToolButtonRunsItsCommandWhenTheTableHandlesIt()
    {
        var ran = 0;
        var commands = new MainWindowCommands(new Window(), () => { });
        commands.Add("Note.Quarter", () => ran++);
        var pane = new ToolsPaneView(new ScorePreviewView());
        pane.Attach(commands);

        Assert.True(pane.Entries.Single(entry => entry.Tool.Id == "duration:quarter").Enabled);
        Assert.False(pane.Entries.Single(entry => entry.Tool.Id == "dynamic:f").Enabled);
        pane.RunTool("duration:quarter");
        pane.RunTool("dynamic:f");
        Assert.Equal(1, ran);
    }
}
