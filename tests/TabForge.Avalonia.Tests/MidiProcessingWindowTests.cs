using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using TabForge.Audio;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Avalonia.Views.Midi;
using TabForge.Avalonia.Views.Presets;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the MIDI processing window checks: the structure in WPF order, the window title, the presenter texts, the list rules (Up, Down and Remove enabling,
/// move, remove, add after the selection, the cap), the searchable catalog of processors and parameters, the parameter controls of every kind and their edits,
/// the log and Learn watch, the presets, dark and light PNGs, contrast and the preview's menu item.
/// Does not own: the window, the presenter, the catalog or the palette.
/// Tests: this class.
/// </summary>
public class MidiProcessingWindowTests
{
    private sealed record Made(MidiProcessingWindow Window, PluginSlot Slot, FxTestKit.FakeEngine Engine, List<string> Edits, TrackModel Track);

    private static Made Open(PluginSlot? slot = null, bool show = true)
    {
        slot ??= MidiProcessingWindow.SampleSlot();
        var track = FxTestKit.Track(new[] { slot });
        var engine = new FxTestKit.FakeEngine();
        var edits = new List<string>();
        var window = new MidiProcessingWindow(FxTestKit.Host(track, engine), track, slot, apply => { edits.Add("edit"); apply(); });
        window.Dialogs.Ask = (_, _, _, _, _) => System.Threading.Tasks.Task.FromResult(MessageBoxAnswer.Yes);
        window.Dialogs.Prompt = (_, _, _, _) => System.Threading.Tasks.Task.FromResult<string?>("Mine");
        if (show) window.Show();
        return new Made(window, slot, engine, edits, track);
    }

    private static ListBox Rows(Window window) => DialogTestKit.Named<ListBox>(window, "ProcessorList");

    private static ListBox Catalog(Window window) => DialogTestKit.Named<ListBox>(window, "CatalogList");

    /// <summary>The buttons and text of the window's controls in tree order, outside the two lists.</summary>
    private static (string Role, string Text)[] Layout(Window window) => DialogTestKit.LayoutControls(window)
        .Where(c => c is Button or TextBlock or TextBox or ComboBox && !c.GetLogicalAncestors().Any(a => a is ListBox))
        .Select(c => (Role: c.GetType().Name, Text: c switch
        {
            Button b => b.Content as string ?? "",
            TextBlock t => t.Text ?? "",
            TextBox t => t.Text ?? "",
            _ => "",
        }))
        .ToArray();

    [AvaloniaFact]
    public void ControlsMatchWpfWindowOrderAndTexts()
    {
        var window = Open().Window;
        // The window's controls in tree order: the preset bar, the catalog, the list with its buttons, then the detail pane with the log panel.
        var expected = new[]
        {
            ("TextBlock", "Presets"), ("ComboBox", ""), ("Button", "Save preset…"), ("Button", "Delete"),
            ("TextBlock", "Add a processor"), ("TextBox", ""), ("Button", "Add ›"),
            ("TextBlock", "In order (top runs first)"), ("Button", "▲"), ("Button", "▼"), ("Button", "Remove"),
            ("TextBlock", "MIDI log (this window watches while it is open)"), ("TextBox", ""), ("Button", "Clear log"),
        };
        Assert.Equal(expected, Layout(window).Take(expected.Length));
        Assert.Equal("MIDI processing: Sample synth", window.Title);
        Assert.Equal((1020d, 620d, 760d, 420d), (window.Width, window.Height, window.MinWidth, window.MinHeight));
        window.Close();
    }

    [AvaloniaFact]
    public void ShowsTheSelectedProcessorAndTheUnknownTypeFromThePresenter()
    {
        var window = Open().Window;
        Assert.Equal(3, Rows(window).ItemCount);
        var transpose = MidiProcessorCatalog.Find("transpose")!;
        Assert.Equal(transpose.Name, DialogTestKit.Named<TextBlock>(window, "TitleText").Text);
        Assert.Equal(transpose.Description, DialogTestKit.Named<TextBlock>(window, "DescriptionText").Text);
        Assert.Equal("legacyFilter (unknown)", MidiProcessingListPresenter.RowLabel("legacyFilter", null));

        Rows(window).SelectedIndex = 1;
        Assert.Equal("legacyFilter", DialogTestKit.Named<TextBlock>(window, "TitleText").Text);
        Assert.Equal(MidiProcessingListPresenter.UnknownTypeDescription, DialogTestKit.Named<TextBlock>(window, "DescriptionText").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void ListButtonsFollowThePresenterRules()
    {
        var window = Open().Window;
        var up = DialogTestKit.Named<Button>(window, "UpButton");
        var down = DialogTestKit.Named<Button>(window, "DownButton");
        var remove = DialogTestKit.Named<Button>(window, "RemoveButton");

        Assert.Equal((false, true, true), (up.IsEnabled, down.IsEnabled, remove.IsEnabled));
        Rows(window).SelectedIndex = 2;
        Assert.Equal((true, false, true), (up.IsEnabled, down.IsEnabled, remove.IsEnabled));
        Rows(window).SelectedIndex = -1;
        Assert.Equal((false, false, false), (up.IsEnabled, down.IsEnabled, remove.IsEnabled));
        window.Close();
    }

    [AvaloniaFact]
    public void MoveAndRemoveChangeTheSlotInOrderThroughTheEditCallback()
    {
        var made = Open();
        Rows(made.Window).SelectedIndex = 1;
        FxTestKit.PressButton(made.Window, "UpButton");
        Assert.Equal(new[] { "legacyFilter", "transpose", "log" }, made.Slot.MidiProcessors.Select(p => p.Type));
        Assert.Equal(0, Rows(made.Window).SelectedIndex);
        FxTestKit.PressButton(made.Window, "RemoveButton");
        Assert.Equal(new[] { "transpose", "log" }, made.Slot.MidiProcessors.Select(p => p.Type));
        Assert.Equal(2, made.Edits.Count);
        made.Window.Close();
    }

    [AvaloniaFact]
    public void AddPutsTheCatalogEntryAfterTheSelectedProcessor()
    {
        var made = Open();
        Rows(made.Window).SelectedIndex = 0;
        DialogTestKit.Named<TextBox>(made.Window, "SearchBox").Text = "velocity";
        Assert.Equal("Velocity", ((MidiProcessorCatalog.SearchHit)Catalog(made.Window).SelectedItem!).Display);
        FxTestKit.PressButton(made.Window, "AddButton");
        Assert.Equal(new[] { "transpose", "velocity", "legacyFilter", "log" }, made.Slot.MidiProcessors.Select(p => p.Type));
        made.Window.Close();
    }

    [AvaloniaFact]
    public void AddDoesNothingAtTheProcessorCap()
    {
        var slot = MidiProcessingWindow.SampleSlot();
        slot.MidiProcessors.Clear();
        for (var i = 0; i < MidiProcessingListPresenter.MaxProcessors; i++) slot.MidiProcessors.Add(new PluginMidiProcessor { Type = "transpose" });
        var made = Open(slot);
        FxTestKit.PressButton(made.Window, "AddButton");
        Assert.Equal(MidiProcessingListPresenter.MaxProcessors, slot.MidiProcessors.Count);
        made.Window.Close();
    }

    [AvaloniaFact]
    public void SearchListsProcessorsFirstThenTheirParameters()
    {
        var window = Open().Window;
        var all = MidiProcessorCatalog.All.Count;
        Assert.Equal(all, Catalog(window).ItemCount);
        DialogTestKit.Named<TextBox>(window, "SearchBox").Text = "velocity";
        var hits = Catalog(window).Items.Cast<MidiProcessorCatalog.SearchHit>().ToList();
        Assert.Equal(0, Catalog(window).SelectedIndex);
        Assert.Null(hits[0].Param);
        Assert.Contains(hits, h => h.Param is not null && h.Display.Contains('›'));
        window.Close();
    }

    [AvaloniaFact]
    public void ParameterMatchJumpsToTheParameterOfTheExistingProcessor()
    {
        var made = Open();
        DialogTestKit.Named<TextBox>(made.Window, "SearchBox").Text = "transpose semitones";
        var hit = Catalog(made.Window).Items.Cast<MidiProcessorCatalog.SearchHit>().FirstOrDefault(h => h.Param is not null && h.Info.Type == "transpose");
        if (hit is null) return;
        Rows(made.Window).SelectedIndex = 2;
        Catalog(made.Window).SelectedItem = hit;
        FxTestKit.PressButton(made.Window, "AddButton");
        Assert.Equal(0, Rows(made.Window).SelectedIndex);   // the transpose already in the list is reused, not added again
        Assert.Equal(3, made.Slot.MidiProcessors.Count);
        made.Window.Close();
    }

    [AvaloniaFact]
    public void EveryParameterKindGetsItsControl()
    {
        foreach (var info in MidiProcessorCatalog.All)
        {
            var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument, MidiProcessors = { MidiProcessorCatalog.Create(info) } };
            var made = Open(slot);
            foreach (var p in info.Params.Where(p => p.Kind is not (MidiParamKind.MapPreset or MidiParamKind.NoteSet)))
            {
                var name = "Param_" + p.Key;
                var control = made.Window.GetLogicalDescendants().OfType<Control>().Distinct().FirstOrDefault(c => c.Name == name);
                Assert.True(control is not null, $"{info.Type}: no control for {p.Key}");
                Assert.True(p.Kind switch
                {
                    MidiParamKind.Number => control is KnobControl,
                    MidiParamKind.Choice => control is ComboBox,
                    MidiParamKind.Check => control is CheckBox,
                    MidiParamKind.Text => control is TextBox,
                    _ => true,
                }, $"{info.Type}: wrong control for {p.Key}");
            }
            made.Window.Close();
        }
    }

    [AvaloniaFact]
    public void ParameterEditsAreStoredThroughTheEditCallback()
    {
        var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument, MidiProcessors = { MidiProcessorCatalog.Create(MidiProcessorCatalog.Find("velocity")!) } };
        var made = Open(slot);
        var info = MidiProcessorCatalog.Find("velocity")!;
        var number = info.Params.First(p => p.Kind == MidiParamKind.Number && p.Group == "Main");
        var knob = made.Window.GetLogicalDescendants().OfType<KnobControl>().Distinct().First(k => k.Name == "Param_" + number.Key);
        var scale = Math.Max(1, number.Scale);
        var target = Math.Clamp(number.Default + 1, number.Min, number.Max);
        knob.Value = target * scale;
        var stored = MidiProcessorCatalog.ParseParams(slot.MidiProcessors[0], info)[number.Key]!.GetValue<double>();
        Assert.Equal(target, stored, 3);
        Assert.NotEmpty(made.Edits);
        var choice = info.Params.FirstOrDefault(p => p.Kind == MidiParamKind.Choice && p.Group == "Main");
        if (choice is not null)
        {
            var combo = made.Window.GetLogicalDescendants().OfType<ComboBox>().Distinct().First(c => c.Name == "Param_" + choice.Key);
            var other = combo.Items.OfType<ComboBoxItem>().First(i => !ReferenceEquals(i, combo.SelectedItem));
            combo.SelectedItem = other;
            Assert.Equal((int)other.Tag!, (int)MidiProcessorCatalog.ParseParams(slot.MidiProcessors[0], info)[choice.Key]!.GetValue<double>());
        }
        made.Window.Close();
    }

    [AvaloniaFact]
    public void NoteSetEditorAddsAndRemovesChips()
    {
        var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument, MidiProcessors = { MidiProcessorCatalog.Create(MidiProcessorCatalog.Find("velocity")!) } };
        var made = Open(slot);
        var info = MidiProcessorCatalog.Find("velocity")!;
        var mode = made.Window.GetLogicalDescendants().OfType<ComboBox>().Distinct().First(c => c.Name == "Param_NoteMode");
        var editor = made.Window.GetLogicalDescendants().OfType<Border>().Distinct().First(b => b.Name == "NoteEditor");
        Assert.False(editor.IsVisible);   // Applies to: All notes
        mode.SelectedIndex = 1;
        Assert.True(editor.IsVisible);
        var input = made.Window.GetLogicalDescendants().OfType<TextBox>().Distinct().First(t => t.Name == "NoteInput");
        input.Text = "36, 38, 40-41";
        FxTestKit.Press(made.Window, made.Window.GetLogicalDescendants().OfType<Button>().Distinct().First(b => b.Name == "NoteAdd"));
        Assert.Equal("36,38,40,41", MidiProcessorCatalog.ParseParams(slot.MidiProcessors[0], info)["NoteSet"]!.GetValue<string>().Replace(" ", ""));
        FxTestKit.Press(made.Window, made.Window.GetLogicalDescendants().OfType<Button>().Distinct().First(b => b.Name == "NoteClear"));
        Assert.Equal("", MidiProcessorCatalog.ParseParams(slot.MidiProcessors[0], info)["NoteSet"]!.GetValue<string>());
        made.Window.Close();
    }

    [AvaloniaFact]
    public void LogProcessorStartsTheEngineWatchAndLinesAreDescribed()
    {
        var made = Open();   // the sample list holds an enabled log processor
        Assert.Contains("watch:True", made.Engine.Calls);
        Assert.True(DialogTestKit.Named<StackPanel>(made.Window, "LogPanel").IsVisible);
        made.Engine.RaiseMidiLog(0, new MidiLogLine(1.5f, 0x90, 60, 100, 0));
        var text = DialogTestKit.Named<TextBox>(made.Window, "LogBox").Text;
        Assert.Contains("Note on", text);
        Assert.Contains("vel 100", text);
        made.Window.Close();
        Assert.Contains("watch:False", made.Engine.Calls);
    }

    [AvaloniaFact]
    public void WholeListPresetsSaveAndReplaceTheList()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var made = Open();
        FxTestKit.PressButton(made.Window, "SavePresetButton");
        DialogTestKit.RunQueuedUiWork();
        var box = DialogTestKit.Named<ComboBox>(made.Window, "PresetBox");
        Assert.Contains(box.Items.OfType<object>(), i => i.ToString() == "★ Mine");
        var builtIn = box.Items.OfType<MidiPresetBars.Entry>().First(e => e.Build is not null);
        box.SelectedItem = builtIn;
        DialogTestKit.RunQueuedUiWork();
        Assert.Equal(builtIn.Build!().Select(p => p.Type), made.Slot.MidiProcessors.Select(p => p.Type));
        made.Window.Close();
    }

    [AvaloniaFact]
    public void PresetRowsRenderTheirTextInsteadOfTheRecordDump()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var made = Open();
        FxTestKit.PressButton(made.Window, "SavePresetButton");
        DialogTestKit.RunQueuedUiWork();

        var list = DialogTestKit.Named<ComboBox>(made.Window, "PresetBox");
        var processors = DialogTestKit.Named<ComboBox>(made.Window, "ProcessorPresetBox");
        foreach (var box in new[] { list, processors })
        {
            var rows = box.Items.OfType<MidiPresetBars.Entry>().ToList();
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.Equal(row.Text, row.ToString()));
            Assert.DoesNotContain(rows, row => row.ToString().Contains("Entry {"));
        }

        var listRows = list.Items.OfType<MidiPresetBars.Entry>().ToList();
        Assert.Contains(listRows, row => row.Text == MidiPresetBars.Hint && row.ToString() == "Load preset…");
        Assert.Contains(listRows, row => row.Text == "★ Mine" && row.ToString() == "★ Mine");
        Assert.Contains(listRows, row => row.Text == PresetRowText.Manage && row.ToString() == "Manage presets…");
        made.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "midi-processing-dark.png")]
    [InlineData(true, "midi-processing-light.png")]
    public void RendersPngInEachTheme(bool light, string fileName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = Open(show: false).Window;
        Assert.True(new System.IO.FileInfo(DialogTestKit.SavePng(window, nameof(MidiProcessingWindowTests), fileName)).Length > 0);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnTheWindow(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = Open().Window;
        foreach (var text in new IBrush?[]
        {
            DialogTestKit.Named<TextBlock>(window, "TitleText").Foreground, DialogTestKit.Named<TextBlock>(window, "DescriptionText").Foreground,
            DialogTestKit.Named<TextBlock>(window, "ParameterNote").Foreground, DialogTestKit.Named<Button>(window, "RemoveButton").Foreground,
        })
            Assert.True(DialogTestKit.Contrast(text, window.Background, "MIDI processing") >= 4.5);
        window.Close();
    }

    [AvaloniaFact]
    public void PreviewToolsMenuOpensMidiProcessing()
    {
        var main = new MainWindow();
        main.Show();
        var item = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "MIDI processing…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var opened = main.OwnedWindows.OfType<MidiProcessingWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }
}
