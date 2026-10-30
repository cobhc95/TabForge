// Owns: the MIDI clipping layout checks: every processor's editor at 640, 800 and 1000 DIPs fits its viewport
//   (or engages the intentional horizontal scroll fallback), button rows wrap when narrow, long texts wrap, the Learn
//   toggle shows the selected look while on, and the Articulations pane follows the same rules. Also the Learn guard:
//   no raw Learn button outside the shared helper.
// Does not own: the editors (MidiSharedRows, MidiParamEditors), the window, or the token guards.
// Tests: this class.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Articulations;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Midi;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>The MIDI editors keep every field visible when the window is narrow: wrapping instead of clipping.</summary>
[Collection("PluginLibraryRoot")]
public sealed class MidiClipLayoutTests
{
    private sealed record Made(MidiProcessingWindow Window, PluginSlot Slot, TrackModel Track);

    private static Made Open(PluginSlot slot, double width)
    {
        var track = FxTestKit.Track(new[] { slot });
        var engine = new FxTestKit.FakeEngine();
        var window = new MidiProcessingWindow(FxTestKit.Host(track, engine), track, slot, apply => apply());
        window.Dialogs.Ask = (_, _, _, _, _) => System.Threading.Tasks.Task.FromResult(MessageBoxAnswer.Yes);
        window.Width = width;
        window.Height = 700;
        window.Show();
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
        return new Made(window, slot, track);
    }

    private static PluginSlot SlotFor(MidiProcessorInfo info)
    {
        var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument, MidiProcessors = { MidiProcessorCatalog.Create(info) } };
        return slot;
    }

    private static void ShowNotes(Made made)
    {
        var window = made.Window;
        var mode = window.GetLogicalDescendants().OfType<ComboBox>().Distinct().FirstOrDefault(c => c.Name == "Param_NoteMode");
        if (mode is null) return;
        mode.SelectedIndex = 1;   // Only these notes: the note editor (chips plus entry row) appears
        DialogTestKit.RunQueuedUiWork();
        var input = window.GetLogicalDescendants().OfType<TextBox>().Distinct().First(t => t.Name == "NoteInput");
        input.Text = "36,38,40-41";
        FxTestKit.Press(window, window.GetLogicalDescendants().OfType<Button>().Distinct().First(b => b.Name == "NoteAdd"));
        DialogTestKit.RunQueuedUiWork();
        window.UpdateLayout();
    }

    private static ScrollViewer EditorViewer(Control scope) =>
        scope.GetVisualAncestors().OfType<ScrollViewer>().First();

    /// <summary>Every visible control of the scope ends at or before the viewport's right edge, unless the
    /// intentional horizontal scroll fallback is engaged (the extent runs past the viewport).</summary>
    private static void FitsOrScrolls(Control scope, string what)
    {
        var viewer = EditorViewer(scope);
        var overflow = new List<string>();
        foreach (var control in scope.GetVisualDescendants().OfType<Control>().Prepend(scope))
        {
            if (control is ScrollViewer) continue;
            if (!control.IsVisible || !control.IsEffectivelyVisible || control.Bounds.Width < 0.5) continue;
            var end = control.TranslatePoint(new Point(control.Bounds.Width, 0), viewer);
            if (end is null) continue;
            if (end.Value.X > viewer.Viewport.Width + 1.5) overflow.Add($"{control.GetType().Name} {control.Name} ends at {end.Value.X:0}");
        }
        var fallback = viewer.Extent.Width > viewer.Viewport.Width + 1.5
            && viewer.HorizontalScrollBarVisibility != global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        Assert.True(overflow.Count == 0 || fallback, what + " clips: " + string.Join("; ", overflow.Take(8)));
    }

    [AvaloniaFact]
    public void EveryProcessorEditorFitsOrScrollsAtNarrowWidths()
    {
        foreach (var width in new[] { 640d, 800d, 1000d })
            foreach (var info in MidiProcessorCatalog.All)
            {
                var made = Open(SlotFor(info), width);
                try
                {
                    ShowNotes(made);
                    var panel = DialogTestKit.Named<StackPanel>(made.Window, "ParamPanel");
                    FitsOrScrolls(panel, $"{info.Type} at {width}");
                }
                finally { made.Window.Close(); }
            }
    }

    [AvaloniaFact]
    public void NoProcessorNeedsTheScrollFallbackAtOneThousand()
    {
        foreach (var info in MidiProcessorCatalog.All)
        {
            var made = Open(SlotFor(info), 1000);
            try
            {
                ShowNotes(made);
                var panel = DialogTestKit.Named<StackPanel>(made.Window, "ParamPanel");
                var viewer = EditorViewer(panel);
                Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 1.5, $"{info.Type} still scrolls sideways at 1000 DIPs");
            }
            finally { made.Window.Close(); }
        }
    }

    [AvaloniaFact]
    public void NoteEntryRowWrapsBelowTheThreshold()
    {
        var made = Open(SlotFor(MidiProcessorCatalog.Find("velocity")!), 640);
        try
        {
            ShowNotes(made);
            var input = made.Window.GetLogicalDescendants().OfType<TextBox>().Distinct().First(t => t.Name == "NoteInput");
            var row = (WrapPanel)input.GetVisualParent()!;
            var tops = row.Children.Select(c => c.TranslatePoint(new Point(0, 0), row)?.Y ?? 0).ToList();
            Assert.True(tops.Max() - tops.Min() > 1, "the note entry row should flow onto a second line at 640 DIPs");
        }
        finally { made.Window.Close(); }
    }

    [AvaloniaFact]
    public void LongTextsWrapInsteadOfTrimming()
    {
        var made = Open(MidiProcessingWindow.SampleSlot(), 640);
        try
        {
            Assert.Equal(TextWrapping.Wrap, DialogTestKit.Named<TextBlock>(made.Window, "DescriptionText").TextWrapping);
            Assert.Equal(TextWrapping.Wrap, DialogTestKit.Named<TextBlock>(made.Window, "TitleText").TextWrapping);
            ShowNotes(made);
            var chips = made.Window.GetLogicalDescendants().OfType<WrapPanel>().Distinct().First(p => p.Name == "NoteChips");
            Assert.IsType<WrapPanel>(chips);
        }
        finally { made.Window.Close(); }
    }

    [AvaloniaFact]
    public void AppliesToDropdownFitsItsLongestItem()
    {
        var made = Open(SlotFor(MidiProcessorCatalog.Find("velocity")!), 800);
        try
        {
            var combo = made.Window.GetLogicalDescendants().OfType<ComboBox>().Distinct().First(c => c.Name == "Param_NoteMode");
            Assert.True(combo.MinWidth >= DialogMetrics.MidiChoiceMinWidth);
            Assert.Equal(HorizontalAlignment.Stretch, combo.HorizontalAlignment);
        }
        finally { made.Window.Close(); }
    }

    [AvaloniaFact]
    public void KnobAndValueNeverOverlap()
    {
        foreach (var width in new[] { 640d, 1000d })
        {
            var made = Open(SlotFor(MidiProcessorCatalog.Find("velocity")!), width);
            try
            {
                ShowNotes(made);
                var knob = made.Window.GetLogicalDescendants().OfType<KnobControl>().Distinct().First();
                var value = DialogTestKit.Named<TextBlock>(made.Window, "ParamValue_" + knob.Name!.Substring("Param_".Length));
                var screen = made.Window;
                var knobBox = new Rect(knob.TranslatePoint(new Point(0, 0), screen)!.Value, knob.Bounds.Size);
                var valueBox = new Rect(value.TranslatePoint(new Point(0, 0), screen)!.Value, value.Bounds.Size);
                Assert.False(knobBox.Intersects(valueBox), $"knob and value overlap at {width}");
            }
            finally { made.Window.Close(); }
        }
    }

    [AvaloniaFact]
    public void LearnToggleShowsTheSelectedLookWhileOn()
    {
        var made = Open(SlotFor(MidiProcessorCatalog.Find("velocity")!), 800);
        try
        {
            ShowNotes(made);
            var learn = made.Window.GetLogicalDescendants().OfType<ToggleButton>().Distinct().First(b => b.Name == "NoteLearn");
            learn.IsChecked = false;
            DialogTestKit.RunQueuedUiWork();
            var off = learn.Background;
            learn.IsChecked = true;
            DialogTestKit.RunQueuedUiWork();
            var on = learn.Background;
            Assert.NotEqual(off?.ToString(), on?.ToString());
            var tip = ToolTip.GetTip(learn) as TextBlock;
            Assert.NotNull(tip);
            Assert.Contains("listening", tip!.Text);
            Assert.Equal(TextWrapping.Wrap, tip.TextWrapping);
        }
        finally { made.Window.Close(); }
    }

    [AvaloniaFact]
    public void LearnToggleFlipsTheLearningState()
    {
        var seen = new List<bool>();
        var toggle = MidiSharedRows.LearnToggle("NoteLearn", false, on => seen.Add(on));
        toggle.IsChecked = true;
        toggle.IsChecked = false;
        Assert.Equal(new[] { true, false }, seen);
    }

    [AvaloniaFact]
    public void ArticulationsPaneFitsOrScrollsAtNarrowWidths()
    {
        foreach (var width in new[] { 640d, 800d, 1000d })
        {
            var track = FxTestKit.Track();
            var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument };
            var owner = new Window();
            var pane = new ArticulationsPane(FxTestKit.Host(track), track, slot, _ => { }, owner, () => new Views.Fx.FxChainDialogs());
            var window = new Window { Width = width, Height = 700, Content = pane };
            window.Show();
            DialogTestKit.RunQueuedUiWork();
            window.UpdateLayout();
            try
            {
                var table = pane.GetVisualDescendants().OfType<Grid>().First(g => g.ColumnDefinitions.Count == 6);
                FitsOrScrolls(table, $"Articulations at {width}");
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void NoRawLearnButtonOutsideTheSharedHelper()
    {
        var repo = ArchitectureScan.RepoRoot();
        var views = Path.Combine(repo, "Source", "src", "TabForge.Avalonia", "Views");
        var raw = new Regex(@"new\s+(ToggleButton|Button)\s*\{[^}]*""Learn""|Content\s*=\s*""Learn""", RegexOptions.Compiled);
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(views, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file).Equals("MidiSharedRows.cs", StringComparison.OrdinalIgnoreCase)) continue;
            var text = File.ReadAllText(file);
            if (raw.IsMatch(text)) hits.Add(Path.GetRelativePath(repo, file));
        }
        Assert.True(hits.Count == 0, "raw Learn button outside MidiSharedRows: " + string.Join("; ", hits));
    }
}
