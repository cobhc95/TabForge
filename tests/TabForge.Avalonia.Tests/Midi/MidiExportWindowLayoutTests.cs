// Owns: the File > Export MIDI window's layout with seven tracks at 560, 700 and 830 DIPs: nothing runs past the scrolling body, the built-in
//   status text wraps instead of clipping, each plug-in track's map row keeps its selector, count and link, Cancel and Export sit docked
//   outside the body inside the window at its smallest height, and the minimum size is set.
// Does not own: the range math, the export itself, the articulation maps or the token guards.
// Tests: this class. Headless.
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
using TabForge.Avalonia.Views.Articulations;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

[Collection("PluginLibraryRoot")]
public sealed class MidiExportWindowLayoutTests
{
    private const string BuiltInPrefix = "Built-in sound";

    /// <summary>Seven tracks: six with a plug-in instrument (map rows), one built-in, one with a long name.</summary>
    private static SongProject BuildSong()
    {
        var song = new SongProject { Title = "Layout", Tempo = 120, TimeSignatureNumerator = 4, TimeSignatureDenominator = 4 };
        var names = new[] { "Guitar I", "Guitar II", "Bass", "Drums", "Keys", "Built-in lead", "A track with a very long name that cannot fit the name column" };
        for (var i = 0; i < names.Length; i++)
        {
            var track = new TrackModel
            {
                Name = names[i], Kind = TrackKind.Guitar, MidiChannel = i,
                StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1),
            };
            if (i != 5)
            {
                track.SoundSource = SoundSources.Plugins;
                track.MidiSound = false;
                track.Rig.Plugins.Add(new PluginSlot { Name = "Plug-in", Type = PluginSlotType.Instrument });
            }
            song.Tracks.Add(track);
        }
        return song;
    }

    /// <summary>Opens the window in the articulation-map mode, so every ticked track has its map row.</summary>
    private static MidiExportWindow Open(double width, double height)
    {
        var settings = new TabForge.Rendering.MidiExportSettings { UseArticulationMap = true };
        var window = new MidiExportWindow(BuildSong(), settings, hasSelection: false, snip: null, selection: null, saveFile: null,
            quietSave: () => { }, report: _ => { }, openArticulations: _ => { });
        window.Width = width;
        window.Height = height;
        window.Show();
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
        return window;
    }

    private static ScrollViewer Body(Window window) => DialogTestKit.Named<ScrollViewer>(window, "MidiExportScroll");

    /// <summary>The visible controls of the body's content whose right edge lies past the body's viewport (scroll bars excluded).</summary>
    private static List<string> Overflows(ScrollViewer body)
    {
        var over = new List<string>();
        var content = (Control)body.Content!;
        foreach (var control in content.GetVisualDescendants().OfType<Control>().Prepend(content))
        {
            if (control is ScrollViewer || control.GetVisualAncestors().TakeWhile(a => !ReferenceEquals(a, body)).OfType<ScrollBar>().Any()) continue;
            if (!control.IsEffectivelyVisible || control.Bounds.Width < 0.5) continue;
            var end = control.TranslatePoint(new Point(control.Bounds.Width, 0), body);
            if (end is { } point && point.X > body.Viewport.Width + 1.5) over.Add($"{control.GetType().Name} {control.Name} ends at {point.X:0}");
        }
        return over;
    }

    private static TextBlock Status(Window window, string prefix) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text is { } text && text.StartsWith(prefix, System.StringComparison.Ordinal));

    [AvaloniaFact]
    public void NothingRunsPastTheBodyAtAnyWidth()
    {
        foreach (var width in new[] { DialogMetrics.MidiExportMinWidth, 560d, 700d, 830d })
        {
            var window = Open(width, DialogMetrics.MidiExportHeight);
            try
            {
                var body = Body(window);
                var over = Overflows(body);
                Assert.True(over.Count == 0, $"at {width} DIPs: " + string.Join("; ", over.Take(8)));
                Assert.True(body.Extent.Width <= body.Viewport.Width + 1.5, $"the body scrolls sideways at {width} DIPs");
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void BuiltInStatusTextWrapsInsteadOfClippingAndRowsAreCompact()
    {
        foreach (var width in new[] { 560d, 830d })
        {
            var window = Open(width, DialogMetrics.MidiExportHeight);
            try
            {
                var body = Body(window);
                var built = Status(window, BuiltInPrefix);
                Assert.Equal(TextWrapping.Wrap, built.TextWrapping);
                var end = built.TranslatePoint(new Point(built.Bounds.Width, 0), body);
                Assert.True(end is { } point && point.X <= body.Viewport.Width + 1.5, $"the status runs past the edge at {width}");
                var selector = window.GetVisualDescendants().OfType<ArticulationPresetSelector>().First();
                var row = selector.GetVisualAncestors().OfType<Grid>().First(g => g.Name == "TechniqueRow");
                var link = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "TechniqueEdit");
                                Assert.True(row.Bounds.Height < (width < 700 ? 2 : 1) * link.Bounds.Height + 2 * (DialogMetrics.MidiExportPartGap.Top + DialogMetrics.MixRowGap.Top) + 12, $"row {row.Bounds.Height:0} vs link {link.Bounds.Height:0} is not compact at {width}");
                Assert.True(selector.Bounds.Width > 40, "the selector has no width");
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void EveryPluginTrackKeepsItsSelectorCountAndLinkInView()
    {
        foreach (var width in new[] { 560d, 700d, 830d })
        {
            var window = Open(width, DialogMetrics.MidiExportHeight);
            try
            {
                var body = Body(window);
                var counts = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.Text is { } text && text.StartsWith("mapped ", System.StringComparison.Ordinal)).ToList();
                var links = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "TechniqueEdit").ToList();
                var selectors = window.GetVisualDescendants().OfType<ArticulationPresetSelector>().ToList();
                // Seven tracks: six plug-in rows show "mapped X of Y"; the built-in row shows its note instead but keeps its selector and link.
                Assert.Equal(6, counts.Count);
                Assert.Equal(7, links.Count);
                Assert.Equal(7, selectors.Count);
                foreach (var part in counts.Cast<Control>().Concat(links).Concat(selectors))
                {
                    Assert.True(part.IsEffectivelyVisible && part.Bounds.Width > 0.5, $"{part.GetType().Name} is not shown at {width} DIPs");
                    var end = part.TranslatePoint(new Point(part.Bounds.Width, 0), body);
                    Assert.True(end is { } point && point.X <= body.Viewport.Width + 1.5, $"{part.GetType().Name} runs past the edge at {width} DIPs");
                }
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void CancelAndExportStayDockedInsideTheWindowAtMinimumHeight()
    {
        foreach (var width in new[] { 560d, 700d, 830d })
        {
            var window = Open(width, DialogMetrics.MidiExportMinHeight);
            try
            {
                var body = Body(window);
                var export = window.GetVisualDescendants().OfType<Button>().First(b => b.Content is string text && text == "Export");
                var cancel = DialogTestKit.Named<Button>(window, "Cancel");
                foreach (var button in new[] { export, cancel })
                {
                    Assert.DoesNotContain(button.GetVisualAncestors(), a => ReferenceEquals(a, body));
                    var bottom = button.TranslatePoint(new Point(0, button.Bounds.Height), window);
                    Assert.True(bottom is { } point && point.Y > 0 && point.Y <= window.ClientSize.Height + 1.5,
                        $"{button.Name} is cut off at the bottom of a {width} DIP window");
                }
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void SetsItsMinimumSize()
    {
        var window = new MidiExportWindow(BuildSong(), new TabForge.Rendering.MidiExportSettings(), hasSelection: false, snip: null, selection: null,
            saveFile: null, quietSave: () => { }, report: _ => { });
        try
        {
            Assert.Equal(DialogMetrics.MidiExportMinWidth, window.MinWidth);
            Assert.Equal(DialogMetrics.MidiExportMinHeight, window.MinHeight);
            Assert.True(window.MinWidth > 0 && window.MinHeight > 0);
        }
        finally { window.Close(); }
    }
}
