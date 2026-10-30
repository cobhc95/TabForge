using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the 'Move notes' popup (T with notes selected), a light-dismiss Popup centred over the piano roll on the app's dark card (theme brushes; its logical parent is the
//   anchor, as the other popups do, so it gets the theme and control templates): the title and 'n notes selected', 'How should they move?' with the Half steps / Stay in the
//   key choice, 'How far?' with the round - and + buttons around the big value in words (click it to type a signed number; Up/Down step 1, Shift+Up/Down step 12, the wheel
//   steps 1), the One octave down / up pills, the snap-to-key tick, the Before / After note chips, the amber range warning and Cancel / Move notes.
//   Enter confirms (also while typing), Esc and a click away cancel.
// Does not own: the maths and wording (PianoRollTransposeDialog), the edit or its undo step (the caller runs TransposeAsync once), the Move by setting (the caller stores it).
// Tests: TabForge.Avalonia.Tests.PianoRollTransposePopupTests, PianoRollTransposeDialogTests, PianoRollKeyboardTests.
public sealed partial class PianoRollTransposePopup
{
    private readonly PianoRollTransposeDialog _model;
    private readonly Action<bool>? _modeChanged;
    private readonly Popup _popup;
    private readonly Control _anchor;
    private readonly TaskCompletionSource<MoveNotesAnswer?> _done = new();
    private readonly TextBox _value;
    private readonly TextBlock _unit, _warning, _countText, _snapCaption;
    private readonly ToggleButton _half, _stay;
    private readonly CheckBox _snap;
    private readonly StackPanel _before = new() { Name = "MoveBefore", Orientation = Orientation.Horizontal, Spacing = DialogMetrics.TransposeRowSpacing / 2 };
    private readonly StackPanel _after = new() { Name = "MoveAfter", Orientation = Orientation.Horizontal, Spacing = DialogMetrics.TransposeRowSpacing / 2 };
    private Button _octaveDown = null!, _octaveUp = null!;
    private MoveNotesAnswer? _answer;
    private bool _showing, _closed;

    private PianoRollTransposePopup(PianoRollTransposeDialog model, Control target, Action<bool>? modeChanged)
    {
        (_model, _anchor, _modeChanged) = (model, target, modeChanged);
        _value = new TextBox
        {
            Name = "TransposeValue",
            Width = DialogMetrics.TransposeNumberWidth,
            FontSize = DialogMetrics.TransposeNumberSize,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = default,
            Background = AvaloniaPalette.TransparentBrush,
        };
        AutomationProperties.SetName(_value, "Amount to move");
        ToolTip.SetTip(_value, "Click to type a number (a negative number goes down). Up and Down change it by 1, Shift+Up and Shift+Down by 12");
        _value.TextChanged += (_, _) => PreviewTyped();
        _value.GotFocus += (_, _) => ShowValue();
        _value.LostFocus += (_, _) => { if (_closed) return; if (Parse(_value.Text) is { } typed) _model.SetDelta(typed); ShowAll(); };
        _unit = PromptChrome.Muted("MoveUnit", "");
        _unit.HorizontalAlignment = HorizontalAlignment.Center;
        _unit.TextAlignment = TextAlignment.Center;
        _warning = PromptChrome.Muted("TransposeWarning", "");
        _warning.Foreground = AvaloniaPalette.PaletteBrush("Warning", false);
        _warning.FollowTheme(() => _warning.Foreground = AvaloniaPalette.PaletteBrush("Warning", _warning.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light));
        _countText = PromptChrome.Muted("TransposeCount", model.CountText);
        _snapCaption = Small("MoveSnapCaption");
        _snapCaption.Text = PianoRollTransposeDialog.SnapCaption;
        _half = Segment("MoveHalfSteps", PianoRollTransposeDialog.HalfStepsText, "Move by half steps: every key on the piano, in or out of the key", out _);
        _stay = Segment("MoveStayInKey", PianoRollTransposeDialog.StayText, "Move by steps of the scale, so notes stay in the key", out _);
        _half.Click += (_, _) => Pick(false);
        _stay.Click += (_, _) => Pick(true);
        _snap = new CheckBox { Name = "MoveSnap", IsChecked = model.SnapToKey };
        ToolTip.SetTip(_snap, "Notes that land outside the key move to the nearest key note (this move only; the Lock to scale setting does not change)");
        AutomationProperties.SetName(_snap, "Snap the result to the key");
        _snap.IsCheckedChanged += (_, _) => { _model.SnapToKey = _snap.IsChecked == true; ShowAll(); };

        var cancel = PromptChrome.Action("TransposeCancel", "Cancel", primary: false, isCancel: true);
        cancel.Click += (_, _) => Close();
        var ok = PromptChrome.Action("TransposeOk", "Move notes", primary: true);
        ok.Click += (_, _) => Confirm();
        AutomationProperties.SetName(ok, "Move notes");
        AutomationProperties.SetName(cancel, "Cancel");

        var frame = BuildFrame(cancel, ok);
        frame.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        frame.PointerWheelChanged += (_, e) => { if (e.Delta.Y != 0) { Nudge(e.Delta.Y > 0 ? 1 : -1); e.Handled = true; } };

        _popup = DialogMetrics.CenteredPopup(target, frame);
        ((ISetLogicalParent)_popup).SetParent(target);   // takes the anchor's theme, resources and control templates (without it the popup is an unthemed white box)
        _popup.Opened += (_, _) => frame.Focus();   // the value box shows words until it is clicked; keys work from the panel
        _popup.Closed += (_, _) => { _closed = true; ((ISetLogicalParent)_popup).SetParent(null); _done.TrySetResult(_answer); };
        ShowAll();
    }

    private Border BuildFrame(Button cancel, Button ok)
    {
        var title = PromptChrome.Heading(PianoRollTransposeDialog.TitleText);
        title.Name = "MoveTitle";
        var column = new StackPanel { Width = DialogMetrics.TransposePopupWidth };
        column.Children.Add(title);
        column.Children.Add(_countText);
        column.Children.Add(SectionLabel("MoveHowLabel", "How should they move?"));
        column.Children.Add(SegmentRow());
        column.Children.Add(SectionLabel("MoveFarLabel", "How far?"));
        column.Children.Add(StepperRow());
        column.Children.Add(_unit);
        column.Children.Add(OctaveRow());
        column.Children.Add(SnapRow());
        column.Children.Add(PreviewRows());
        column.Children.Add(_warning);
        column.Children.Add(PromptChrome.Footer(null, null, cancel, ok));
        var frame = DialogMetrics.TransposeFrame(column);
        frame.Name = "MoveNotesPanel";
        frame.Focusable = true;
        frame.CornerRadius = AvaloniaFormControlStyles.ControlCornerRadius;
        frame[!Border.BackgroundProperty] = new DynamicResourceExtension("Panel2Brush");
        frame[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        frame[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        AutomationProperties.SetName(frame, PianoRollTransposeDialog.TitleText);
        return frame;
    }

    /// <summary>The popup's panel (its theme brushes are asserted by tests).</summary>
    internal static Border PanelOf(Popup popup) => (Border)popup.Child!;

    private static TextBlock SectionLabel(string name, string text)
    {
        var label = PromptChrome.Muted(name, text);
        return label.WithMargin(DialogMetrics.MoveSectionGap);
    }

    private static TextBlock Small(string name)
    {
        var block = new TextBlock { Name = name, FontSize = DialogMetrics.MoveSmallSize, TextWrapping = TextWrapping.Wrap };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return block;
    }

    // One segment of the Half steps / Stay in the key choice: a title with a small caption under it.
    private static ToggleButton Segment(string name, string title, string tip, out TextBlock caption)
    {
        caption = Small(name + "Caption");
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.TextAlignment = TextAlignment.Center;
        var head = new TextBlock { Text = title, FontSize = DialogMetrics.MoveSegmentTitleSize, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var button = new ToggleButton
        {
            Name = name, Focusable = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, MinHeight = DialogMetrics.MoveSegmentHeight,
            Content = new StackPanel { Children = { head, caption } },
        };
        button.WithPadding(DialogMetrics.MoveSegmentInset);
        PianoRollToolLook.Apply(button, () => false);
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, title);
        return button;
    }

    private Control SegmentRow()
    {
        ((TextBlock)((StackPanel)_half.Content!).Children[1]).Text = PianoRollTransposeDialog.HalfStepsCaption;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = DialogMetrics.TransposeRowSpacing };
        Grid.SetColumn(_stay, 1);
        grid.Children.Add(_half);
        grid.Children.Add(_stay);
        return grid;
    }

    // The round - and + buttons around the big value.
    private Control StepperRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = DialogMetrics.TransposeRowSpacing };
        row.Children.Add(Flat("TransposeMinus", "Move one step lower", "−", -1, DialogMetrics.TransposeStepSize, DialogMetrics.TransposeStepSize, DialogMetrics.TransposeStepCorner));
        row.Children.Add(_value);
        row.Children.Add(Flat("TransposePlus", "Move one step higher", "+", 1, DialogMetrics.TransposeStepSize, DialogMetrics.TransposeStepSize, DialogMetrics.TransposeStepCorner));
        return row;
    }

    // The 'One octave down' and 'One octave up' pills (12 half steps, or 7 steps in Stay in the key).
    private Control OctaveRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = DialogMetrics.TransposeRowSpacing };
        _octaveDown = Flat("TransposeOctaveDown", "Move one octave lower", "One octave down", 0, DialogMetrics.TransposePillWidth, DialogMetrics.TransposePillHeight, DialogMetrics.TransposePillCorner, -1);
        _octaveUp = Flat("TransposeOctaveUp", "Move one octave higher", "One octave up", 0, DialogMetrics.TransposePillWidth, DialogMetrics.TransposePillHeight, DialogMetrics.TransposePillCorner, 1);
        row.Children.Add(_octaveDown);
        row.Children.Add(_octaveUp);
        return row.WithMargin(DialogMetrics.MoveSectionGap);
    }

    private Control SnapRow()
    {
        var box = new StackPanel { Name = "MoveSnapRow", Orientation = Orientation.Horizontal, Spacing = DialogMetrics.TransposeRowSpacing };
        var text = new StackPanel { Children = { new TextBlock { Name = "MoveSnapText", TextWrapping = TextWrapping.Wrap, MaxWidth = DialogMetrics.TransposePopupWidth - DialogMetrics.TransposeStepSize }, _snapCaption } };
        box.Children.Add(_snap);
        box.Children.Add(text);
        return box.WithMargin(DialogMetrics.MoveCaptionGap);
    }

    // The BEFORE / AFTER rows: the lowest three distinct pitches and where they land.
    private Control PreviewRows()
    {
        Control Line(string name, string label, StackPanel chips)
        {
            var text = new TextBlock { Name = name, Text = label, Width = DialogMetrics.MoveRowLabelWidth, FontSize = DialogMetrics.MoveSmallSize, VerticalAlignment = VerticalAlignment.Center };
            text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
            chips.VerticalAlignment = VerticalAlignment.Center;
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { text, chips } }.WithMargin(DialogMetrics.MoveChipRowGap);
        }
        return new StackPanel { Children = { Line("MoveBeforeLabel", "Before", _before), Line("MoveAfterLabel", "After", _after) } }.WithMargin(DialogMetrics.MoveSectionGap);
    }

    private static Border Chip(string text, bool accent)
    {
        var label = new TextBlock { Text = text, FontSize = DialogMetrics.MoveSmallSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border
        {
            Child = label, MinWidth = DialogMetrics.MoveChipMinWidth, Height = DialogMetrics.MoveChipHeight, CornerRadius = DialogMetrics.MoveChipCorner,
            BorderThickness = DialogMetrics.PianoRollFrameOutline,
        };
        chip.WithPadding(DialogMetrics.MoveChipInsetX, 0);
        chip[!Border.BorderBrushProperty] = new DynamicResourceExtension(accent ? "AccentBrush" : "BorderBrush");
        return chip;
    }

    private Button Flat(string name, string tip, string text, int step, double width, double height, global::Avalonia.CornerRadius corner, int octaves = 0)
    {
        var button = new Button
        {
            Name = name, Content = text, Width = width, Height = height, Focusable = false,
            VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        PianoRollToolLook.Apply(button, () => false);
        button.CornerRadius = corner;
        AutomationProperties.SetName(button, tip);
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => Nudge(octaves != 0 ? octaves * _model.OctaveAmount : step);
        return button;
    }

    private void Pick(bool stay)
    {
        if (stay && !_model.HasScale) { ShowAll(); return; }
        if (Parse(_value.Text) is { } typed && _value.IsFocused) _model.SetDelta(typed);
        _model.ScaleSteps = stay;
        _modeChanged?.Invoke(stay);
        ShowAll();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? PianoRollTransposeDialog.Octave : 1;
        if (e.Key is Key.Enter) Confirm();
        else if (e.Key is Key.Escape) Close();
        else if (e.Key is Key.Up) Nudge(step);
        else if (e.Key is Key.Down) Nudge(-step);
        else return;
        e.Handled = true;
    }

    private void Nudge(int step)
    {
        if (_value.IsFocused && Parse(_value.Text) is { } typed) _model.SetDelta(typed);
        _model.Add(step);
        ShowAll();
    }

    // A typed number previews at once (nothing when it does not read); the box keeps what was typed until a button steps.
    private void PreviewTyped()
    {
        if (_showing) return;
        if (Parse(_value.Text) is { } typed) { _model.SetDelta(typed); ShowRest(); }
    }

    // The value box: the signed number while it is being typed in, else the words ('up 1').
    private void ShowValue()
    {
        _showing = true;
        try
        {
            _value.Text = _value.IsFocused ? PianoRollTransposeDialog.Signed(_model.Delta) : _model.DeltaWords;
            if (_value.IsFocused) _value.SelectAll();
        }
        finally { _showing = false; }
    }

    private void ShowAll()
    {
        ShowValue();
        ShowRest();
    }

    private void ShowRest()
    {
        _unit.Text = _model.UnitCaption;
        _warning.Text = _model.WarningText;
        _warning.IsVisible = _model.WarningText.Length > 0;
        _half.IsChecked = !_model.ScaleSteps;
        _stay.IsChecked = _model.ScaleSteps;
        _stay.IsEnabled = _model.HasScale;
        ((TextBlock)((StackPanel)_stay.Content!).Children[1]).Text = _model.StayCaption;
        _snap.IsChecked = _model.SnapToKey;
        var snapRow = (Panel)_snap.Parent!;
        snapRow.IsVisible = _model.SnapVisible;
        ((TextBlock)((StackPanel)snapRow.Children[1]).Children[0]).Text = _model.SnapText;
        Fill(_before, _model.BeforeChips, false);
        Fill(_after, _model.AfterChips, true);
        ToolTip.SetTip(_octaveDown, _model.ScaleSteps ? "Move one octave lower (7 steps of the scale)" : "Move one octave lower (12 half steps)");
        ToolTip.SetTip(_octaveUp, _model.ScaleSteps ? "Move one octave higher (7 steps of the scale)" : "Move one octave higher (12 half steps)");
    }

    private static void Fill(StackPanel row, System.Collections.Generic.IReadOnlyList<string> names, bool accent)
    {
        row.Children.Clear();
        foreach (var name in names) row.Children.Add(Chip(name, accent));
    }

    private void Confirm()
    {
        if (_value.IsFocused && Parse(_value.Text) is { } typed) _model.SetDelta(typed);
        _answer = _model.Answer;
        Close();
    }

    private void Close() => _popup.IsOpen = false;

    private static int? Parse(string? text)
    {
        var trimmed = (text ?? "").Trim().TrimStart('+');
        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>Opens the popup over <paramref name="target"/>; the answer is the number, mode and snap, or null when cancelled or clicked away. <paramref name="modeChanged"/> hears each Half steps / Stay in the key switch (the caller stores the Move by setting).</summary>
    public static Task<MoveNotesAnswer?> AskAsync(Control target, PianoRollTransposeDialog model, Action<bool>? modeChanged = null)
    {
        Open(target, model, out var answer, modeChanged);
        return answer;
    }

    /// <summary>Opens the popup and returns it (tests drive its controls); <paramref name="answer"/> completes when it closes.</summary>
    internal static Popup Open(Control target, PianoRollTransposeDialog model, out Task<MoveNotesAnswer?> answer, Action<bool>? modeChanged = null)
    {
        var popup = new PianoRollTransposePopup(model, target, modeChanged);
        popup._popup.IsOpen = true;
        answer = popup._done.Task;
        return popup._popup;
    }
}
