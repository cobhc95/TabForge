using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The row pieces of the Mix Table: a toggle, its label, the control and a fixed-width value readout; a ticked row gets an accent bar and a tint,
/// an unticked row shows its control disabled and its readout muted. Colours are palette keys (DynamicResource), never literal brushes.
/// Owns: the row layout, the on/off look and the readout wording (units, L/C/R pan). Does not own: the values or the result (MixTablePresenter).
/// Tests: TabForge.Avalonia.Tests.MixTableColourChooserEffectValuesTests.
/// </summary>
internal static class MixTableRows
{
    public const string Hint = "Choose what changes from this beat and when it takes effect.";

    /// <summary>Pan -8..8 as "L3", "C" or "R5"; every other scale as its number.</summary>
    public static string PanText(double value) => (int)value switch { < 0 => $"L{-(int)value}", > 0 => $"R{(int)value}", _ => "C" };

    public static string TempoText(string text) => int.TryParse(text?.Trim(), out var bpm) ? $"{bpm} BPM" : "BPM";

    public static TextBlock Readout() => new TextBlock()
    {
        Width = DialogMetrics.MixReadoutWidth, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
    }.WithMargin(DialogMetrics.MixReadoutMargin);

    /// <summary>The slider of a scale row; <paramref name="show"/> turns its value into the readout text and runs on every change, so a drag updates it live.</summary>
    public static Slider Scale(double min, double max, double value, TextBlock readout, System.Func<double, string> show)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        readout.Text = show(value);
        slider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) readout.Text = show(slider.Value); };
        return slider;
    }

    /// <summary>One row: toggle | control | readout. The toggle enables the control and switches the row's look.</summary>
    public static Control Row(CheckBox check, Control editor, TextBlock readout, bool on)
    {
        check.Width = DialogMetrics.MixToggleWidth; check.VerticalAlignment = VerticalAlignment.Center;
        editor.WithMargin(DialogMetrics.MixEditorMargin);
        var tint = new Border { IsHitTestVisible = false, Opacity = 0.12, CornerRadius = new CornerRadius(4), [!Border.BackgroundProperty] = new DynamicResourceExtension("AccentBrush") };
        var bar = new Border { Width = DialogMetrics.MixBarWidth, CornerRadius = new CornerRadius(2), IsHitTestVisible = false, [!Border.BackgroundProperty] = new DynamicResourceExtension("AccentBrush") }.WithMargin(DialogMetrics.MixBarMargin);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3,Auto,*,Auto"), MinHeight = DialogMetrics.MixRowMinHeight }.WithMargin(DialogMetrics.MixRowGap);
        Grid.SetColumnSpan(tint, 4);
        Grid.SetColumn(check, 1); Grid.SetColumn(editor, 2); Grid.SetColumn(readout, 3);
        grid.Children.Add(tint); grid.Children.Add(bar); grid.Children.Add(check); grid.Children.Add(editor); grid.Children.Add(readout);

        void Apply(bool isOn)
        {
            editor.IsEnabled = isOn;
            tint.IsVisible = bar.IsVisible = isOn;
            readout[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(isOn ? "TextPrimary" : "TextMuted");
        }
        check.IsChecked = on;
        Apply(on);
        check.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) Apply(check.IsChecked == true); };
        return grid;
    }

    /// <summary>A titled section holding the rows.</summary>
    public static Border Section(string header, params Control[] rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows) stack.Children.Add(row);
        return ToolWindowParts.Group(header, stack);
    }
}
