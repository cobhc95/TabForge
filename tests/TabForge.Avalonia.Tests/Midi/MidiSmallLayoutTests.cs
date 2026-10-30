using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>The MIDI processing window stays inside its columns when small: at its minimum, fifty wider and at
/// full width every processor keeps each child inside its own column, columns never overlap, the minimum is
/// honoured and the list buttons stay in the middle column. The same window class serves the FX window and the
/// export dialog, and the Articulations tab follows the same clip and scroll rules.</summary>
[Collection("PluginLibraryRoot")]
public sealed class MidiSmallLayoutTests
{
    public static IEnumerable<object[]> Cases()
    {
        var min = DialogMetrics.MidiWindowMinWidth;
        foreach (var info in MidiProcessorCatalog.All)
            foreach (var width in new[] { min, min + 50, 1000d })
                yield return new object[] { info.Type, width };
    }

    private static MidiProcessingWindow Open(string type, double width)
    {
        var info = MidiProcessorCatalog.Find(type)!;
        var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument, MidiProcessors = { MidiProcessorCatalog.Create(info) } };
        var track = FxTestKit.Track(new[] { slot });
        var engine = new FxTestKit.FakeEngine();
        var window = new MidiProcessingWindow(FxTestKit.Host(track, engine), track, slot, apply => apply());
        window.Width = width;
        window.Height = 700;
        window.Show();
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
        return window;
    }

    private static Rect InWindow(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window) ?? new Point(0, 0);
        return new Rect(origin, control.Bounds.Size);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void SmallWindowKeepsEveryChildInsideItsColumn(string type, double width)
    {
        var window = Open(type, width);
        try
        {
            Assert.True(window.MinWidth >= DialogMetrics.MidiCatalogMinWidth + DialogMetrics.MidiListMinWidth + DialogMetrics.MidiEditorMinWidth - 0.5);
            Assert.True(window.Width + 0.5 >= window.MinWidth);
            var catalog = DialogTestKit.Named<DockPanel>(window, "CatalogPane");
            var list = DialogTestKit.Named<DockPanel>(window, "ListPane");
            var detail = DialogTestKit.Named<DockPanel>(window, "DetailPane");
            var catalogBox = InWindow(catalog, window);
            var listBox = InWindow(list, window);
            var detailBox = InWindow(detail, window);
            Assert.True(catalogBox.Right <= listBox.Left + 1.5, $"{type} at {width}: catalog overlaps list");
            Assert.True(listBox.Right <= detailBox.Left + 1.5, $"{type} at {width}: list overlaps editor");
            foreach (var (pane, box, name) in new[] { (catalog, catalogBox, "catalog"), (list, listBox, "list"), (detail, detailBox, "editor") })
            {
                foreach (var child in pane.GetVisualDescendants().OfType<Control>())
                {
                    if (!child.IsVisible || !child.IsEffectivelyVisible || child.Bounds.Width < 0.5) continue;
                    if (child is global::Avalonia.Controls.Primitives.Popup) continue;
                    if (child.GetVisualAncestors().OfType<global::Avalonia.Controls.Primitives.Popup>().Any()) continue;
                    if (child.GetVisualAncestors().OfType<ToolTip>().Any() || child is ToolTip) continue;
                    var end = child.TranslatePoint(new Point(child.Bounds.Width, 0), window);
                    var start = child.TranslatePoint(new Point(0, 0), window);
                    if (end is null || start is null) continue;
                    Assert.True(start.Value.X >= box.Left - 1.5, $"{type} at {width}: {name} child {child.GetType().Name} {child.Name} starts left of its column");
                    Assert.True(end.Value.X <= box.Right + 1.5, $"{type} at {width}: {name} child {child.GetType().Name} {child.Name} spills past its column");
                }
            }
            var buttons = DialogTestKit.Named<WrapPanel>(window, "ListButtonRow");
            var buttonsBox = InWindow(buttons, window);
            Assert.True(buttonsBox.Left >= listBox.Left - 1.5 && buttonsBox.Right <= listBox.Right + 1.5, $"{type} at {width}: button row leaves the middle column");
            Assert.True(buttonsBox.Top >= listBox.Top - 1.5 && buttonsBox.Bottom <= listBox.Bottom + 1.5, $"{type} at {width}: button row leaves the middle column vertically");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ArticulationsTabFollowsTheSameSmallRules()
    {
        var window = Open(MidiProcessorCatalog.All.First().Type, DialogMetrics.MidiWindowMinWidth);
        try
        {
            window.OpenArticulationsTab();
            DialogTestKit.RunQueuedUiWork();
            window.UpdateLayout();
            DialogTestKit.RunQueuedUiWork();
            var pane = window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "ArticulationsPane");
            Assert.NotNull(pane);
            Assert.True(pane!.ClipToBounds);
            var viewer = window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "ArticulationViewer");
            Assert.NotNull(viewer);
            Assert.Equal(global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, viewer!.HorizontalScrollBarVisibility);
        }
        finally { window.Close(); }
    }
}
