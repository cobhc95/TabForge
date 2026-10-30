// Owns: the control of each kind of MIDI processor parameter (a knob with its value text, a drop-down, a tick, a text box, the note chips with an add box and
//   Learn), each storing its value into the processor through the edit callback; a knob drag is one undo step with a light engine message while it turns.
// Does not own: which parameters a processor has (MidiProcessorCatalog), the window layout (MidiProcessingWindow), the drum map rows (MidiNoteMapRows).
// Tests: TabForge.Avalonia.Tests.MidiProcessingWindowTests, MidiClipLayoutTests.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using EM = TabForge.Core.Midi;

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

// Owns: the control of each kind of MIDI processor parameter; every row stretches and wraps through MidiSharedRows,
//   so all processors share the adaptive layout with no per-processor special cases.
// Does not own: which parameters a processor has (MidiProcessorCatalog), the window layout (MidiProcessingWindow), the drum map rows (MidiNoteMapRows).
// Tests: TabForge.Avalonia.Tests.MidiProcessingWindowTests.
public sealed class MidiParamEditors
{
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

    /// <summary>The editor column's scroller: while set, labels stay inside its viewport instead of pushing editors out.</summary>
    public ScrollViewer? Viewer { get; set; }

    /// <summary>Forgets the editor of the processor that was shown.</summary>
    public void Reset() => NoteEditor = null;

    private static void Store(PluginMidiProcessor proc, JsonObject values) => proc.Params = values.ToJsonString();

    /// <summary>The control of one parameter of the processor <paramref name="proc"/> whose values are <paramref name="values"/>.</summary>
    public Control Build(PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        if (p.Kind == MidiParamKind.Check) return Wrap(Check(proc, values, p));
        if (p.Kind == MidiParamKind.NoteSet) return NoteSet(proc, values, p);
        var tip = p.Tip.Length > 0 ? p.Tip : p.Label;
        Control editor = p.Kind switch
        {
            MidiParamKind.Number => Number(proc, values, p),
            MidiParamKind.Choice => Choice(proc, values, p),
            MidiParamKind.Text => Text(proc, values, p),
            _ => new TextBlock { Text = "" },
        };
        return Wrap(Viewer is null ? MidiSharedRows.ParamRow(p.Label, tip, editor) : MidiSharedRows.ParamRow(p.Label, tip, editor, Viewer));
    }

    private static Control Wrap(Control content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.Spaced(DialogMetrics.MidiRowGap);
        return content;
    }

    private CheckBox Check(PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        var box = new CheckBox { Content = p.Label, IsChecked = values[p.Key]!.GetValue<double>() >= 0.5, VerticalAlignment = VerticalAlignment.Center, Name = "Param_" + p.Key, HorizontalAlignment = HorizontalAlignment.Left };
        box.Spaced(DialogMetrics.MidiCheckGap);
        if (p.Tip.Length > 0) MidiSharedRows.Tip(box, p.Tip);
        box.IsCheckedChanged += (_, _) =>
        {
            var on = box.IsChecked == true ? 1 : 0;
            if (values[p.Key]!.GetValue<double>() == on) return;
            _edit(() => { values[p.Key] = on; Store(proc, values); });
        };
        return box;
    }

    private Control Number(PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        var scale = Math.Max(1, p.Scale);
        var knob = new KnobControl
        {
            Minimum = p.Min * scale, Maximum = p.Max * scale, DefaultValue = p.Default * scale, Origin = Math.Clamp(0, p.Min, p.Max) * scale,
            Width = DialogMetrics.MidiKnobSize, Height = DialogMetrics.MidiKnobSize,
            Label = p.Label, Format = v => p.Format(v / scale), Name = "Param_" + p.Key, Step = 1,
            Parse = text => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var typed) ? typed * scale : null,
            FromDisplay = displayed => displayed * scale,
        };
        MidiSharedRows.Tip(knob, p.Tip.Length > 0 ? p.Tip : p.Label);
        var text = new TextBlock { MinWidth = DialogMetrics.MidiValueMinWidth, VerticalAlignment = VerticalAlignment.Center, Name = "ParamValue_" + p.Key, TextWrapping = TextWrapping.Wrap };
        text.Spaced(DialogMetrics.MidiValueGap);
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
        var row = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.Children.Add(knob);
        row.Children.Add(text);
        return row;
    }

    private ComboBox Choice(PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        var combo = new ComboBox { Name = "Param_" + p.Key, HorizontalAlignment = HorizontalAlignment.Stretch };
        MidiSharedRows.StretchDrop(combo, p.Tip.Length > 0 ? p.Tip : p.Label);
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
        var box = new TextBox { Text = values[p.Key]!.GetValue<string>(), MaxLength = TextMaxLength, Name = "Param_" + p.Key, MinWidth = DialogMetrics.MidiTextMinWidth, HorizontalAlignment = HorizontalAlignment.Stretch, TextWrapping = TextWrapping.Wrap };
        if (p.Tip.Length > 0) MidiSharedRows.Tip(box, p.Tip);
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

    // Chips (number + name) that wrap, an add box ("36, 38, 40-45, C2, D#3"), and Learn (adds the next note played into this track).
    private Border NoteSet(PluginMidiProcessor proc, JsonObject values, MidiParamInfo p)
    {
        var frame = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = DialogMetrics.MidiNoteFrameMinWidth,
            BorderThickness = DialogMetrics.MidiFrameBorder, CornerRadius = new CornerRadius(DialogMetrics.MidiFrameCorner), Name = "NoteEditor",
            IsVisible = values["NoteMode"]!.GetValue<double>() >= 0.5,
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush"), [!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBrush"),
        };
        frame.Padded(DialogMetrics.MidiFrameInset);
        frame.Spaced(DialogMetrics.MidiRowGap);
        NoteEditor = frame;
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, Name = "NoteChips", HorizontalAlignment = HorizontalAlignment.Stretch };
        chips.Spaced(DialogMetrics.MidiNoteGap);
        var empty = new TextBlock { Text = "No notes yet: type some below or press Learn and play them.", FontSize = DialogMetrics.MidiSmallText, TextWrapping = TextWrapping.Wrap, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush") };
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
                var chip = MidiSharedRows.Chip($"{n} {NoteLabel(n)}  ×", n, "Click to remove");
                var captured = n;
                chip.PointerReleased += (_, _) => Commit(Current().Where(x => x != captured));
                chips.Children.Add(chip);
            }
        }
        var input = MidiSharedRows.NoteInput("NoteInput", "Notes to add: 36, 38, 40-45, C2, D#3 (Enter adds)");
        void AddText()
        {
            var set = EM.NoteSetText.Parse(input.Text);
            var added = Enumerable.Range(0, NoteSetMax).Where(n => set[n]).ToList();
            if (added.Count == 0) return;
            input.Text = "";
            Commit(Current().Concat(added));
        }
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddText(); e.Handled = true; } };
        var add = MidiSharedRows.RowButton("Add", "NoteAdd", "Adds the typed notes to the list");
        add.Click += (_, _) => AddText();
        var learn = MidiSharedRows.LearnToggle("NoteLearn", false, on => { if (on) _learn.Start(n => Commit(Current().Append(n))); else _learn.Stop(); });
        var clear = MidiSharedRows.RowButton("Clear", "NoteClear", "Removes every note from the list");
        clear.Click += (_, _) => Commit(Array.Empty<int>());
        frame.Child = new StackPanel { Children = { chips, MidiSharedRows.EntryRow(input, add, learn, clear) } };
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
