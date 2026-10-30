// Owns: the building blocks every MIDI editor in the app reuses so narrow windows wrap instead of clipping:
//   the Learn toggle (one look, one tooltip, bound to the learning state), the wrapping note-chip list, the wrap-friendly
//   entry-plus-buttons row, the adaptive label-plus-control grid, and the wrapped tooltip helper. One place, used by the
//   MIDI processing window, the note-map rows, the preset bars and the Articulations pane.
// Does not own: what a processor does (MidiProcessorCatalog), the learning state itself (MidiLearn), or the window layout.
// Tests: MidiClipLayoutTests (wrap and toggle look), MidiLearnGuardTests (no raw Learn button outside this file).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views.Midi;

/// <summary>The shared building blocks of every MIDI editor: the Learn toggle, the wrapping chip list,
/// the wrap-friendly entry row, the adaptive label/control grid and the wrapped tooltip.</summary>
internal static class MidiSharedRows
{
    /// <summary>The Learn toggle text, shared so every MIDI editor names it the same way.</summary>
    public const string LearnText = "Learn";

    /// <summary>The Learn toggle tooltip: listening stays on while toggled.</summary>
    public const string LearnTip = "Learn: play notes on your MIDI keyboard to add them (on while listening)";

    /// <summary>Gives the control a tooltip that wraps instead of running off the window edge.</summary>
    public static void Tip(Control target, string text)
    {
        ToolTip.SetTip(target, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = DialogMetrics.MidiTipMaxWidth });
    }

    /// <summary>A Learn toggle: lit with the toolbar selected look while on, plain while off, following hover and press.
/// Flips the learning state through <paramref name="flipped"/>; the caller starts or stops its own sink there.</summary>
    public static ToggleButton LearnToggle(string name, bool listening, Action<bool> flipped)
    {
        var button = new ToggleButton { Content = LearnText, Name = name, Focusable = false, HorizontalAlignment = HorizontalAlignment.Left };
        DialogMetrics.WithPadding(button, DialogMetrics.MidiButtonPad);
        button.Spaced(DialogMetrics.MidiLearnGap);
        PianoRollToolLook.Apply(button);
        Tip(button, LearnTip);
        button.IsChecked = listening;
        button.IsCheckedChanged += (_, _) => flipped(button.IsChecked == true);
        return button;
    }

    /// <summary>A wrap-friendly entry row: the text box first, then the buttons, flowing onto a second line when narrow.</summary>
    public static WrapPanel EntryRow(TextBox input, params Control[] buttons)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(input);
        foreach (var part in buttons) row.Children.Add(part);
        return row;
    }

    /// <summary>The note input box of an entry row: stretches, shrinks to its minimum, grows no wider than the frame.</summary>
    public static TextBox NoteInput(string name, string tip)
    {
        var input = new TextBox { Name = name, MinWidth = DialogMetrics.MidiNoteInputMinWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
        Tip(input, tip);
        return input;
    }

    /// <summary>A small row button (Add, Clear, preset Save) sharing one look and spacing.</summary>
    public static Button RowButton(string text, string name, string tip)
    {
        var button = new Button { Content = text, Name = name, HorizontalAlignment = HorizontalAlignment.Left };
        DialogMetrics.WithPadding(button, DialogMetrics.MidiButtonPad);
        button.Spaced(DialogMetrics.MidiLearnGap);
        Tip(button, tip);
        return button;
    }

    /// <summary>One note chip (number plus name) with the accent edge; the caller removes the note on click.</summary>
    public static Border Chip(string text, int tag, string tip)
    {
        var chip = new Border
        {
            Tag = tag,
            CornerRadius = new CornerRadius(DialogMetrics.MidiChipCorner),
            BorderThickness = DialogMetrics.MidiFrameBorder,
            Child = new TextBlock { Text = text, FontSize = DialogMetrics.MidiSmallText },
        };
        chip.Spaced(DialogMetrics.MidiChipGap);
        chip.Padded(DialogMetrics.MidiChipInset);
        chip[!Border.BorderBrushProperty] = new DynamicResourceExtension("AccentBrush");
        Tip(chip, tip);
        return chip;
    }

    /// <summary>An adaptive parameter row: the label shrinks to its minimum while the editor takes the rest.
    /// The row itself stretches, so dropdowns and boxes fill the editor column at any width.</summary>
    public static Grid ParamRow(string labelText, string tip, Control editor, string labelName = "")
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var label = new TextBlock
        {
            Text = labelText,
            MinWidth = DialogMetrics.MidiLabelMinWidth,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        return FinishParamRow(grid, label, labelName, tip, editor, null);
    }

    /// <summary>An adaptive parameter row whose label also stays inside <paramref name="viewer"/>: a long label trims
    /// with an ellipsis instead of pushing the editor past the viewport (the editor keeps its full minimum width).</summary>
    public static Grid ParamRow(string labelText, string tip, Control editor, ScrollViewer? viewer)
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var label = new TextBlock
        {
            Text = labelText,
            MinWidth = DialogMetrics.MidiLabelMinWidth,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        return FinishParamRow(grid, label, "", tip, editor, viewer);
    }

    private static Grid FinishParamRow(Grid grid, TextBlock label, string labelName, string tip, Control editor, ScrollViewer? viewer)
    {
        if (labelName.Length > 0) label.Name = labelName;
        label.Spaced(DialogMetrics.MidiInputGap);
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        Tip(label, tip);
        if (viewer is not null)
            label.Bind(TextBlock.MaxWidthProperty, new Binding("Viewport.Width") { Source = viewer, Converter = new ReserveWidthConverter(), ConverterParameter = DialogMetrics.MidiLabelReserve });
        Grid.SetColumn(label, 0);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(label);
        grid.Children.Add(editor);
        return grid;
    }

    private sealed class ReserveWidthConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is double width && parameter is double reserve ? Math.Max(0, width - reserve) : value;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>A dropdown that stretches and stays wide enough for its longest item
    /// (for example the Applies-to All-except row).</summary>
    public static void StretchDrop(ComboBox box, string tip)
    {
        box.MinWidth = DialogMetrics.MidiChoiceMinWidth;
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        Tip(box, tip);
    }

    /// <summary>Holds a wrapping panel to the viewport of <paramref name="viewer"/>, so it flows onto more lines
    /// instead of laying out one endless row (a vertical scroller measures its content with no width limit).
    /// A single child wider than the viewport still engages the horizontal scroll fallback.</summary>
    public static void ConstrainToViewport(Layoutable target, ScrollViewer viewer) =>
        target.Bind(Layoutable.MaxWidthProperty, new Binding("Viewport.Width") { Source = viewer });

    /// <summary>As <see cref="ConstrainToViewport(Layoutable, ScrollViewer)"/>, but leaves <paramref name="reserve"/> DIPs free for the
    /// insets and frames the target sits in, so a wrapped row never runs past the viewer's right edge.</summary>
    public static void ConstrainToViewport(Layoutable target, ScrollViewer viewer, double reserve) =>
        target.Bind(Layoutable.MaxWidthProperty, new Binding("Viewport.Width") { Source = viewer, Converter = new ReserveWidthConverter(), ConverterParameter = reserve });
}
