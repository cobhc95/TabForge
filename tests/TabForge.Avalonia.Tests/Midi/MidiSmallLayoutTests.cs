using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Midi;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>The MIDI processing window looks right at every size and UI scale: for every processor, at the window's minimum, fifty wider,
/// 1000 DIPs and a maximised width, at UI scale 1.0, the smallest (0.7) and the largest (1.5) with the Spacious density, and with a larger
/// interface font: columns never overlap, every child stays inside its own column, no text is trimmed or cut off, the processor's preset
/// row stays on one line beside its save button, the labels of a card share one column, and the add slot sits in the chain column.
/// The same window class serves the FX window and the export dialog, and the Articulations tab follows the same clip and scroll rules.</summary>
[Collection("PluginLibraryRoot")]
public sealed class MidiSmallLayoutTests
{
    /// <summary>A look the user can pick: UI scale, density and interface font size.</summary>
    public enum Look { Default, SmallestScale, LargestScaleSpacious, LargeFont }

    private const double Maximised = 1600;

    public static IEnumerable<object[]> Cases()
    {
        var min = DialogMetrics.MidiWindowMinWidth;
        foreach (var info in MidiProcessorCatalog.All)
        {
            foreach (var width in new[] { min, min + 50, 1000d, Maximised })
                yield return new object[] { info.Type, width, Look.Default };
            foreach (var look in new[] { Look.SmallestScale, Look.LargestScaleSpacious, Look.LargeFont })
                foreach (var width in new[] { min, 1000d })
                    yield return new object[] { info.Type, width, look };
        }
    }

    private static double ScaleOf(Look look) => look switch
    {
        Look.SmallestScale => TabForge.Platform.Theme.UiDensity.MinScale,
        Look.LargestScaleSpacious => TabForge.Platform.Theme.UiDensity.MaxScale,
        _ => 1.0,
    };

    private static LayoutTransformControl? _scaleRoot;

    private static void SetScale(double scale)
    {
        WindowUiScale.CurrentAppearance.UiScale = scale;
        _scaleRoot ??= new LayoutTransformControl();
        ThemeSettingsBinding.ApplyWindow(new Window(), _scaleRoot, WindowUiScale.CurrentAppearance);
        DialogTestKit.RunQueuedUiWork();
    }

    private static void SetDensity(string density) =>
        UiDensityResources.Publish(global::Avalonia.Application.Current!.Resources, new TabForge.Services.AppearanceSettings { Density = density });

    private static MidiProcessingWindow Open(string type, double width, Look look = Look.Default)
    {
        var scale = ScaleOf(look);
        SetScale(scale);
        if (look == Look.LargestScaleSpacious) SetDensity("Spacious");
        var info = MidiProcessorCatalog.Find(type)!;
        var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument, MidiProcessors = { MidiProcessorCatalog.Create(info) } };
        var track = FxTestKit.Track(new[] { slot });
        var engine = new FxTestKit.FakeEngine();
        var window = new MidiProcessingWindow(FxTestKit.Host(track, engine), track, slot, apply => apply());
        if (look == Look.LargeFont) window.FontSize = 15;
        window.Width = width * WindowUiScale.CurrentScale(window);
        window.Height = 700 * WindowUiScale.CurrentScale(window);
        window.Show();
        for (var pass = 0; pass < 8; pass++)
        {
            DialogTestKit.RunQueuedUiWork();
            window.UpdateLayout();
        }
        return window;
    }

    private static void Reset()
    {
        SetScale(1.0);
        SetDensity("Comfortable");
    }

    private static Rect InWindow(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window) ?? new Point(0, 0);
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window) ?? origin;
        return new Rect(origin, end);
    }

    private static bool Shown(Control child) =>
        child.IsVisible && child.IsEffectivelyVisible && child.Bounds.Width >= 0.5
        && child is not Popup && child is not ToolTip
        && !child.GetVisualAncestors().Any(a => a is Popup or ToolTip);

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void EveryProcessorFitsItsColumnsAtEverySizeAndScale(string type, double width, Look look)
    {
        var window = Open(type, width, look);
        var what = $"{type} at {width} ({look})";
        try
        {
            Assert.True(window.MinWidth / WindowUiScale.CurrentScale(window) >= DialogMetrics.MidiCatalogMinWidth + DialogMetrics.MidiListMinWidth + DialogMetrics.MidiEditorMinWidth - 0.5);
            Assert.True(window.Width + 0.5 >= window.MinWidth);
            var catalog = DialogTestKit.Named<DockPanel>(window, "CatalogPane");
            var list = DialogTestKit.Named<DockPanel>(window, "ListPane");
            var detail = DialogTestKit.Named<DockPanel>(window, "DetailPane");
            var catalogBox = InWindow(catalog, window);
            var listBox = InWindow(list, window);
            var detailBox = InWindow(detail, window);
            Assert.True(catalogBox.Right <= listBox.Left + 1.5, $"{what}: catalog overlaps chain");
            Assert.True(listBox.Right <= detailBox.Left + 1.5, $"{what}: chain overlaps editor");
            foreach (var (pane, box, name) in new[] { (catalog, catalogBox, "catalog"), (list, listBox, "chain"), (detail, detailBox, "editor") })
                foreach (var child in pane.GetVisualDescendants().OfType<Control>().Where(Shown))
                {
                    var at = InWindow(child, window);
                    Assert.True(at.Left >= box.Left - 1.5, $"{what}: {name} child {child.GetType().Name} {child.Name} starts left of its column");
                    Assert.True(at.Right <= box.Right + 1.5, $"{what}: {name} child {child.GetType().Name} {child.Name} spills past its column ({at.Right:0.#} > {box.Right:0.#})");
                }

            NoTextIsTrimmed(window, what);

            var presetBox = DialogTestKit.Named<ComboBox>(window, "ProcessorPresetBox");
            var save = DialogTestKit.Named<Button>(window, "SaveProcessorPresetButton");
            var boxAt = InWindow(presetBox, window);
            var saveAt = InWindow(save, window);
            Assert.True(saveAt.Left >= boxAt.Right - 1.5, $"{what}: Save preset is not beside its box");
            Assert.True(saveAt.Top < boxAt.Bottom && saveAt.Bottom > boxAt.Top, $"{what}: Save preset wrapped under its box");
            Assert.True(saveAt.Right <= detailBox.Right + 1.5, $"{what}: Save preset leaves the editor column");

            foreach (var card in window.GetVisualDescendants().OfType<MidiCardPanel>().Where(Shown))
            {
                var offsets = card.Children.Where(c => c.IsVisible).Select(c => c is Border { Child: MidiFieldRow row } ? row : null).OfType<MidiFieldRow>()
                    .Where(r => !r.Stacked).Select(r => r.Editor.TranslatePoint(new Point(0, 0), r)!.Value.X - r.Editor.Margin.Left).ToList();
                var debug = string.Join(" ", card.Children.Where(c => c.IsVisible).Select(c => c is Border { Child: MidiFieldRow row } ? row : null).OfType<MidiFieldRow>().Select(r => $"{r.Editor.GetType().Name}:{r.Editor.Bounds.X:0.00}/{r.Label.Bounds.Width:0.00}/{r.LabelWidth:0.00}/{r.Bounds.X:0.00}"));
                Assert.True(offsets.Count == 0 || offsets.Max() - offsets.Min() < 1, $"{what}: the labels of a card do not share one column ({debug})");
            }

            var slot = InWindow(DialogTestKit.Named<Button>(window, "AddSlotButton"), window);
            Assert.True(slot.Left >= listBox.Left - 1.5 && slot.Right <= listBox.Right + 1.5 && slot.Bottom <= listBox.Bottom + 1.5, $"{what}: the add slot leaves the chain column");
        }
        finally
        {
            window.Close();
            Reset();
        }
    }

    /// <summary>No visible text of the processors tab is trimmed with an ellipsis or cut off by a box narrower than its one line.</summary>
    private static void NoTextIsTrimmed(Window window, string what)
    {
        var cut = new List<string>();
        foreach (var text in window.GetVisualDescendants().OfType<TextBlock>().Where(Shown))
        {
            if (string.IsNullOrEmpty(text.Text)) continue;
            if (text.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(v => v.Name == "DetailViewer") is { } viewer && !IsInView(text, viewer)) continue;
            var layout = text.TextLayout;
            if (layout.TextLines.Any(l => l.HasCollapsed)) cut.Add($"\"{text.Text}\" trimmed");
            else if (text.TextWrapping == TextWrapping.NoWrap && layout.WidthIncludingTrailingWhitespace > text.Bounds.Width - text.Padding.Left - text.Padding.Right + 1)
                cut.Add($"\"{text.Text}\" cut ({layout.WidthIncludingTrailingWhitespace:0.#} > {text.Bounds.Width:0.#})");
        }
        Assert.True(cut.Count == 0, $"{what}: {string.Join("; ", cut.Take(6))}");
    }

    private static bool IsInView(Control control, ScrollViewer viewer)
    {
        var top = control.TranslatePoint(new Point(0, 0), viewer);
        return top is { } at && at.Y < viewer.Viewport.Height && at.Y + control.Bounds.Height > 0;
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void KnobsSitTwoPerLineWhenWideAndOnePerLineWhenNarrow(double scale)
    {
        var look = scale > 1 ? Look.LargestScaleSpacious : Look.Default;
        var wide = Open("humanize", Maximised, look);
        try
        {
            Assert.Contains(wide.GetVisualDescendants().OfType<MidiCardPanel>(), c => c.TwoColumns);
        }
        finally { wide.Close(); Reset(); }
        var narrow = Open("humanize", DialogMetrics.MidiWindowMinWidth, look);
        try
        {
            Assert.DoesNotContain(narrow.GetVisualDescendants().OfType<MidiCardPanel>(), c => c.TwoColumns);
        }
        finally { narrow.Close(); Reset(); }
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
        finally { window.Close(); Reset(); }
    }
}
