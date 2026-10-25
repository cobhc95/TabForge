using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using EM = TabForge.AudioEngine.Midi;

namespace TabForge.Avalonia.Views.Midi;

/// <summary>The Learn state of the note-set editor: while on, the next note played into the track is handed to <see cref="Sink"/>.</summary>
public sealed class MidiLearn
{
    public bool Active { get; private set; }
    public Action<int>? Sink { get; private set; }
    /// <summary>Learn was switched on or off (the window starts or stops watching the engine's input).</summary>
    public event Action? Changed;
    public void Start(Action<int> sink) { Active = true; Sink = sink; Changed?.Invoke(); }
    public void Stop() { if (!Active) return; Active = false; Sink = null; Changed?.Invoke(); }
}

// Owns: the control of each kind of MIDI processor parameter (a knob with its value text, a drop-down, a tick, a text box, the note chips with an add box and
//   Learn), each storing its value into the processor through the edit callback; a knob drag is one undo step with a light engine message while it turns.
// Does not own: which parameters a processor has (MidiProcessorCatalog), the window layout (MidiProcessingWindow), the drum map rows (MidiNoteMapRows).
// Tests: TabForge.Avalonia.Tests.MidiProcessingWindowTests.
public sealed class MidiParamEditors
{
    public const double ItemWidth = 310, ItemWidthText = 460, ItemWidthNotes = 600, LabelWidth = 140, ChoiceWidth = 150, KnobSize = 28, ValueWidth = 90, TextBoxWidth = 300;
    public const int NoteSetMax = 128, TextMaxLength = 512;

    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly PluginSlot _slot;
    private readonly Action<Action> _edit;
    private readonly MidiLearn _learn;
    private bool _dragging;
    private UndoSnapshot? _since;

    /// <param name="edit">Runs a change of the slot as one undo step with the engine update.</param>
    public MidiParamEditors(IFxChainHost host, TrackModel track, PluginSlot slot, Action<Action> edit, MidiLearn learn)
    {
        (_host, _track, _slot, _edit, _learn) = (host, track, slot, edit, learn);
    }

    /// <summary>The note-set editor of the shown processor (hidden while Applies to is All notes); null when it has none.</summary>
    public Border? NoteEditor { get; private set; }

    /// <summary>Forgets the editor of the processor that was shown.</summary>
    public void Reset() => NoteEditor = null;

    private static void Store(PluginMidiProcessor proc, JsonObject values) => proc.Params = values.ToJsonString();

    /// <summary>The control of one parameter of the processor <paramref name="proc"/> whose values are <paramref name="values"/>.</summary>
    public Control Build(PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Width = ItemWidth, Margin = new Thickness(0, 3, 12, 3) };
        if (p.Kind == MidiParamKind.Check) { item.Children.Add(Check(proc, values, p)); return item; }
        if (p.Kind == MidiParamKind.NoteSet) return NoteSet(proc, values, p);
        var label = new TextBlock
        {
            Text = p.Label, Width = LabelWidth, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush"),
        };
        ToolTip.SetTip(label, p.Tip.Length > 0 ? p.Tip : p.Label);
        item.Children.Add(label);
        switch (p.Kind)
        {
            case MidiParamKind.Number: Number(item, proc, values, p); break;
            case MidiParamKind.Choice: item.Children.Add(Choice(proc, values, p)); break;
            case MidiParamKind.Text: item.Width = ItemWidthText; item.Children.Add(Text(proc, values, p)); break;
        }
        return item;
    }

    private CheckBox Check(PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        var box = new CheckBox { Content = p.Label, IsChecked = values[p.Key]!.GetValue<double>() >= 0.5, VerticalAlignment = VerticalAlignment.Center, Name = "Param_" + p.Key };
        if (p.Tip.Length > 0) ToolTip.SetTip(box, p.Tip);
        box.IsCheckedChanged += (_, _) =>
        {
            var on = box.IsChecked == true ? 1 : 0;
            if (values[p.Key]!.GetValue<double>() == on) return;
            _edit(() => { values[p.Key] = on; Store(proc, values); });
        };
        return box;
    }

    private void Number(StackPanel item, PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        var scale = Math.Max(1, p.Scale);
        var knob = new KnobControl
        {
            Minimum = p.Min * scale, Maximum = p.Max * scale, DefaultValue = p.Default * scale, Origin = Math.Clamp(0, p.Min, p.Max) * scale, Width = KnobSize, Height = KnobSize,
            Label = p.Label, Format = v => p.Format(v / scale), Name = "Param_" + p.Key, Step = 1,
            Parse = text => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var typed) ? typed * scale : null,
            FromDisplay = displayed => displayed * scale,
        };
        ToolTip.SetTip(knob, p.Tip.Length > 0 ? p.Tip : p.Label);
        var text = new TextBlock { Width = ValueWidth, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Name = "ParamValue_" + p.Key };
        knob.Value = Math.Round(values[p.Key]!.GetValue<double>() * scale);
        text.Text = p.Format(knob.Value / scale);
        knob.EditStarted += (_, _) => { _dragging = true; _since = _host.Checkpoint(); };
        knob.ValueChanged += (_, raw) =>
        {
            text.Text = p.Format(raw / scale);
            var v = raw / scale;
            if (values[p.Key]!.GetValue<double>() == v) return;
            if (_dragging)
            {
                values[p.Key] = v;
                Store(proc, values);
                _host.Engine?.SetMidiProcessors(_track, _slot);   // a light message while turning; the full update follows on release
            }
            else _edit(() => { values[p.Key] = v; Store(proc, values); });   // e.g. a reset by double-click: its own undo step
        };
        knob.EditEnded += (_, _) =>
        {
            _dragging = false;
            _host.Edit(_track, () => { }, _since);
            _since = null;
        };
        item.Children.Add(knob);
        item.Children.Add(text);
    }

    private ComboBox Choice(PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        var combo = new ComboBox { Width = ChoiceWidth, Name = "Param_" + p.Key };
        if (p.Tip.Length > 0) ToolTip.SetTip(combo, p.Tip);
        var current = (int)Math.Round(values[p.Key]!.GetValue<double>());
        var items = (p.Choices ?? Array.Empty<(int, string)>()).Select(c => new ComboBoxItem { Content = c.Item2, Tag = c.Item1 }).ToList();
        combo.ItemsSource = items;
        combo.SelectedItem = items.FirstOrDefault(i => (int)i.Tag! == current);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not ComboBoxItem { Tag: int value } || (int)Math.Round(values[p.Key]!.GetValue<double>()) == value) return;
            if (p.Key == "NoteMode" && NoteEditor is not null) NoteEditor.IsVisible = value != 0;
            _edit(() => { values[p.Key] = value; Store(proc, values); });
        };
        return combo;
    }

    private TextBox Text(PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        var box = new TextBox { Width = TextBoxWidth, Text = values[p.Key]!.GetValue<string>(), MaxLength = TextMaxLength, Name = "Param_" + p.Key };
        if (p.Tip.Length > 0) ToolTip.SetTip(box, p.Tip);
        void Commit()
        {
            var text = box.Text ?? "";
            if (text == values[p.Key]!.GetValue<string>()) return;
            _edit(() => { values[p.Key] = text; Store(proc, values); });
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return box;
    }

    // Chips (number + name), an add box ("36, 38, 40-45, C2, D#3"), and Learn (adds the next note played into this track).
    private Border NoteSet(PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        var frame = new Border
        {
            Width = ItemWidthNotes, Padding = new Thickness(6), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Name = "NoteEditor",
            IsVisible = values["NoteMode"]!.GetValue<double>() >= 0.5, Margin = new Thickness(0, 3, 12, 3),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush"), [!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBrush"),
        };
        NoteEditor = frame;
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6), Name = "NoteChips" };
        var empty = new TextBlock { Text = "No notes yet: type some below or press Learn and play them.", FontSize = 11, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush") };
        List<int> Current() { var set = EM.NoteSetText.Parse(values[p.Key]!.GetValue<string>()); return Enumerable.Range(0, NoteSetMax).Where(n => set[n]).ToList(); }
        void Commit(IEnumerable<int> notes)
        {
            var text = EM.NoteSetText.Format(notes);
            _edit(() => { values[p.Key] = text; Store(proc, values); });
            Rebuild();
        }
        void Rebuild()
        {
            chips.Children.Clear();
            var notes = Current();
            if (notes.Count == 0) chips.Children.Add(empty);
            foreach (var n in notes)
            {
                var chip = new Border
                {
                    Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 1), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Tag = n,
                    [!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush"),
                    Child = new TextBlock { Text = $"{n} {NoteLabel(n)}  ×", FontSize = 11, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextBrush") },
                };
                ToolTip.SetTip(chip, "Click to remove");
                var captured = n;
                chip.PointerReleased += (_, _) => Commit(Current().Where(x => x != captured));
                chips.Children.Add(chip);
            }
        }
        var input = new TextBox { Width = 200, Name = "NoteInput" };
        ToolTip.SetTip(input, "Notes to add: 36, 38, 40-45, C2, D#3 (Enter adds)");
        void AddText()
        {
            var set = EM.NoteSetText.Parse(input.Text);
            var added = Enumerable.Range(0, NoteSetMax).Where(n => set[n]).ToList();
            if (added.Count == 0) return;
            input.Text = "";
            Commit(Current().Concat(added));
        }
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddText(); e.Handled = true; } };
        var add = new Button { Content = "Add", Padding = new Thickness(10, 2), Margin = new Thickness(6, 0, 0, 0), Name = "NoteAdd" };
        add.Click += (_, _) => AddText();
        var learn = new ToggleButton { Content = "Learn", Padding = new Thickness(10, 2), Margin = new Thickness(6, 0, 0, 0), Name = "NoteLearn" };
        ToolTip.SetTip(learn, "Play a note (keyboard or song): it is added to the list. Needs the track to be sounding through this chain.");
        learn.IsCheckedChanged += (_, _) => { if (learn.IsChecked == true) _learn.Start(n => Commit(Current().Append(n))); else _learn.Stop(); };
        var clear = new Button { Content = "Clear", Padding = new Thickness(10, 2), Margin = new Thickness(6, 0, 0, 0), Name = "NoteClear" };
        clear.Click += (_, _) => Commit(Array.Empty<int>());
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { input, add, learn, clear } };
        frame.Child = new StackPanel { Children = { chips, row } };
        Rebuild();
        return frame;
    }

    // The note's name; on a drum track the piece the General MIDI map gives it.
    private string NoteLabel(int n)
    {
        var drums = _track.IsDrumTrack;
        if (drums && DrumMapLibrary.Find("General MIDI") is { } gm && gm.Notes.TryGetValue(n, out var piece)) return piece;
        return MusicTheoryService.NoteName(n);
    }
}
