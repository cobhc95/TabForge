// Owns: the adaptive layout of the MIDI processing window's editor column, measured from the real text so it holds at every UI scale,
//   density, font size and window size: a parameter row (label beside its control, or above it when the two do not fit), the card that
//   aligns its rows on one shared label column and puts two knob rows side by side when both fit, and the header line that keeps the
//   processor's name and its preset row on one line when they fit and moves the preset row under the name when they do not.
// Does not own: the parameter controls (MidiParamEditors), the card frames and headings (the window), the metrics (DialogMetrics).
// Tests: MidiSmallLayoutTests (every processor at several widths and UI scales: nothing leaves its column, no text is trimmed).
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views.Midi;

/// <summary>One labelled parameter row: the label sits in the card's shared label column beside the control, or above the control when
/// the column and the control's minimum do not fit the row. The label wraps; it is never trimmed.</summary>
internal sealed class MidiFieldRow : Panel
{
    private double _labelWidth = double.NaN;
    private bool _stacked;

    public MidiFieldRow(string labelText, string tip, Control editor)
    {
        Label = new TextBlock { Text = labelText, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush");
        MidiSharedRows.Tip(Label, tip);
        Editor = editor;
        Children.Add(Label);
        Children.Add(editor);
    }

    public TextBlock Label { get; }
    public Control Editor { get; }

    /// <summary>The narrowest the control may be beside its label (set by the editor that built it; the control's MinWidth also counts).</summary>
    public double EditorMinWidth { get; set; }

    /// <summary>The width of the label column (set by the card); NaN uses the label's own width.</summary>
    public double LabelWidth
    {
        get => _labelWidth;
        set { if (_labelWidth.Equals(value)) return; _labelWidth = value; InvalidateMeasure(); }
    }

    /// <summary>True when the label is above the control (set by the card for a narrow row).</summary>
    public bool Stacked
    {
        get => _stacked;
        set { if (_stacked == value) return; _stacked = value; InvalidateMeasure(); }
    }

    /// <summary>The label's width on one line in its current font, measured on a detached copy so the live label keeps its layout.</summary>
    public double NaturalLabelWidth()
    {
        var probe = new TextBlock { Text = Label.Text, FontFamily = Label.FontFamily, FontSize = Label.FontSize, FontWeight = Label.FontWeight, FontStyle = Label.FontStyle, FontStretch = Label.FontStretch };
        probe.Measure(Size.Infinity);
        return Math.Ceiling(probe.DesiredSize.Width);
    }

    /// <summary>The room the row needs to keep its label beside the control: the label column, the gap and the control's minimum.</summary>
    public double SideBySideWidth(double labelColumn) => labelColumn + DialogMetrics.MidiFieldGap + Math.Max(EditorMinWidth, Editor.MinWidth);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        if (Stacked)
        {
            Label.Measure(new Size(width, double.PositiveInfinity));
            Editor.Measure(new Size(width, double.PositiveInfinity));
            return new Size(Math.Max(Label.DesiredSize.Width, Editor.DesiredSize.Width), Label.DesiredSize.Height + DialogMetrics.MidiStackGap + Editor.DesiredSize.Height);
        }
        var label = LabelColumn();
        Label.Measure(new Size(label, double.PositiveInfinity));
        var rest = double.IsInfinity(width) ? double.PositiveInfinity : Math.Max(0, width - label - DialogMetrics.MidiFieldGap);
        Editor.Measure(new Size(rest, double.PositiveInfinity));
        return new Size(label + DialogMetrics.MidiFieldGap + Editor.DesiredSize.Width, Math.Max(Label.DesiredSize.Height, Editor.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Stacked)
        {
            var labelHeight = Label.DesiredSize.Height;
            Label.Arrange(new Rect(0, 0, finalSize.Width, labelHeight));
            var top = labelHeight + DialogMetrics.MidiStackGap;
            Editor.Arrange(new Rect(0, top, finalSize.Width, Math.Max(0, finalSize.Height - top)));
            return finalSize;
        }
        var label = LabelColumn();
        var left = label + DialogMetrics.MidiFieldGap;
        var rowHeight = finalSize.Height;
        Label.Arrange(new Rect(0, Math.Max(0, (rowHeight - Label.DesiredSize.Height) / 2), label, Label.DesiredSize.Height));
        Editor.Arrange(new Rect(left, 0, Math.Max(0, finalSize.Width - left), rowHeight));
        return finalSize;
    }

    private double LabelColumn() => double.IsNaN(LabelWidth) ? NaturalLabelWidth() : LabelWidth;
}

/// <summary>The rows of one parameter card. Every labelled row shares one label column (the widest label on one line); rows marked
/// <see cref="HalfProperty"/> (knobs, ticks) sit two per line when two fit side by side, one per line otherwise; the others span the card.
/// A row whose label column and control do not fit its width puts the label above the control instead.</summary>
internal sealed class MidiCardPanel : Panel
{
    /// <summary>A row that may share its line with the next such row (a knob or a tick).</summary>
    public static readonly AttachedProperty<bool> HalfProperty = AvaloniaProperty.RegisterAttached<MidiCardPanel, Control, bool>("Half");

    private readonly record struct Slot(Control Child, double X, double Y, double Width, double Height);

    private List<Slot> _slots = new();
    private double _plannedWidth = double.NaN;

    public static void SetHalf(Control control, bool half) => control.SetValue(HalfProperty, half);
    public static bool GetHalf(Control control) => control.GetValue(HalfProperty);

    /// <summary>True when the last layout put two rows on one line (tests).</summary>
    internal bool TwoColumns { get; private set; }

    /// <summary>The row inside a parameter's frame (the window wraps each row in a highlight border).</summary>
    private static MidiFieldRow? FieldOf(Control child) => child switch
    {
        MidiFieldRow row => row,
        Border { Child: MidiFieldRow row } => row,
        _ => null,
    };

    // The horizontal room a row's frame and margins take around the field.
    private static double Chrome(Control child, MidiFieldRow field)
    {
        var outer = child.Margin.Left + child.Margin.Right;
        if (child is Border border && !ReferenceEquals(child, field))
            outer += border.BorderThickness.Left + border.BorderThickness.Right + border.Padding.Left + border.Padding.Right + field.Margin.Left + field.Margin.Right;
        return outer;
    }

    private List<Slot> Plan(double width)
    {
        var fields = Children.Where(c => c.IsVisible).Select(c => (Child: c, Field: FieldOf(c))).ToList();
        var labelColumn = fields.Where(f => f.Field is not null).Select(f => f.Field!.NaturalLabelWidth()).DefaultIfEmpty(0).Max();
        double Need(Control child, MidiFieldRow? field)
        {
            if (field is not null) return field.SideBySideWidth(labelColumn) + Chrome(child, field);
            child.Measure(Size.Infinity);
            return child.DesiredSize.Width;
        }
        var needs = fields.Select(f => Need(f.Child, f.Field)).ToList();
        var halfNeed = fields.Select((f, i) => GetHalf(f.Child) ? needs[i] : 0).DefaultIfEmpty(0).Max();
        if (double.IsInfinity(width)) width = Math.Max(halfNeed * 2 + DialogMetrics.MidiCardColumnGap, needs.DefaultIfEmpty(0).Max());
        var twoColumns = halfNeed > 0 && halfNeed * 2 + DialogMetrics.MidiCardColumnGap <= width;
        TwoColumns = false;
        var column = twoColumns ? (width - DialogMetrics.MidiCardColumnGap) / 2 : width;
        var slots = new List<Slot>();
        double y = 0;
        var i = 0;
        while (i < fields.Count)
        {
            var half = twoColumns && GetHalf(fields[i].Child);
            var pair = half && i + 1 < fields.Count && GetHalf(fields[i + 1].Child) ? 2 : 1;
            if (pair == 2) TwoColumns = true;
            var w = half ? column : width;
            double lineHeight = 0;
            for (var k = 0; k < pair; k++)
            {
                var (child, field) = fields[i + k];
                if (field is not null)
                {
                    var stacked = field.SideBySideWidth(labelColumn) + Chrome(child, field) > w + 0.5;
                    if (stacked != field.Stacked || !field.LabelWidth.Equals(labelColumn)) child.InvalidateMeasure();
                    field.LabelWidth = labelColumn;
                    field.Stacked = stacked;
                }
                child.Measure(new Size(w, double.PositiveInfinity));
                lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
            }
            for (var k = 0; k < pair; k++)
                slots.Add(new Slot(fields[i + k].Child, k * (column + DialogMetrics.MidiCardColumnGap), y, w, lineHeight));
            y += lineHeight;
            i += pair;
        }
        return slots;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _slots = Plan(availableSize.Width);
        _plannedWidth = availableSize.Width;
        var width = _slots.Select(s => s.X + s.Width).DefaultIfEmpty(0).Max();
        var height = _slots.Select(s => s.Y + s.Height).DefaultIfEmpty(0).Max();
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!(Math.Abs(finalSize.Width - _plannedWidth) < 0.5)) { _slots = Plan(finalSize.Width); _plannedWidth = finalSize.Width; }
        foreach (var slot in _slots) slot.Child.Arrange(new Rect(slot.X, slot.Y, slot.Width, slot.Height));
        return finalSize;
    }
}

/// <summary>The editor header: the processor's icon and name first, its preset row after. Both stay on one line when the name fits
/// beside the preset row on one line; otherwise the preset row moves under the name (still one line itself) and the name wraps.</summary>
internal sealed class MidiHeaderRow : Panel
{
    private bool _oneLine;

    public MidiHeaderRow(Control lead, Control trail)
    {
        Lead = lead;
        Trail = trail;
        Children.Add(lead);
        Children.Add(trail);
    }

    public Control Lead { get; }
    public Control Trail { get; }

    /// <summary>True while the preset row sits on the name's line (tests).</summary>
    internal bool IsOneLine => _oneLine;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        Lead.Measure(Size.Infinity);
        var leadNatural = Lead.DesiredSize.Width;
        double trailNatural = 0;
        if (Trail.IsVisible) { Trail.Measure(Size.Infinity); trailNatural = Trail.DesiredSize.Width; }
        _oneLine = Trail.IsVisible && (double.IsInfinity(width) || leadNatural + DialogMetrics.MidiHeaderGap + trailNatural <= width);
        if (_oneLine)
        {
            var leadWidth = double.IsInfinity(width) ? leadNatural : width - trailNatural - DialogMetrics.MidiHeaderGap;
            Lead.Measure(new Size(leadWidth, double.PositiveInfinity));
            return new Size(Lead.DesiredSize.Width + DialogMetrics.MidiHeaderGap + trailNatural, Math.Max(Lead.DesiredSize.Height, Trail.DesiredSize.Height));
        }
        Lead.Measure(new Size(width, double.PositiveInfinity));
        var height = Lead.DesiredSize.Height;
        var used = Lead.DesiredSize.Width;
        if (Trail.IsVisible)
        {
            Trail.Measure(new Size(width, double.PositiveInfinity));
            height += DialogMetrics.MidiStackGap + Trail.DesiredSize.Height;
            used = Math.Max(used, Trail.DesiredSize.Width);
        }
        return new Size(double.IsInfinity(width) ? used : Math.Min(used, width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = finalSize.Width;
        if (_oneLine)
        {
            var height = finalSize.Height;
            var trail = Trail.DesiredSize.Width;
            var lead = Math.Max(0, width - trail - DialogMetrics.MidiHeaderGap);
            Lead.Arrange(new Rect(0, (height - Lead.DesiredSize.Height) / 2, lead, Lead.DesiredSize.Height));
            Trail.Arrange(new Rect(width - trail, (height - Trail.DesiredSize.Height) / 2, trail, Trail.DesiredSize.Height));
            return finalSize;
        }
        Lead.Arrange(new Rect(0, 0, width, Lead.DesiredSize.Height));
        if (Trail.IsVisible) Trail.Arrange(new Rect(0, Lead.DesiredSize.Height + DialogMetrics.MidiStackGap, Math.Min(width, Trail.DesiredSize.Width), Trail.DesiredSize.Height));
        return finalSize;
    }
}
