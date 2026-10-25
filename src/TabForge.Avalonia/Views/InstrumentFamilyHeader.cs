using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

// Owns: one family header row of the Choose instrument window: the chevron (turned when the family is open), the family name, the sound count in muted
//   text on the right, and the whole row as the click target: hover and press shades from the theme (HoverShade), a hand cursor, Enter and Space from the keyboard.
//   A header without a toggle (the picker that keeps every family open) is the plain name.
// Does not own: which families are open (InstrumentPickerPresenter) or the tiles under the header (InstrumentPickerWindow).
// Tests: no dedicated test class.
// Threads: UI thread only.
internal sealed class InstrumentFamilyHeader : Border
{
    private const string Chevron = "▸";

    private readonly TextBlock _chevron = new TextBlock { Text = Chevron, VerticalAlignment = VerticalAlignment.Center, RenderTransform = new RotateTransform(DialogMetrics.PickerChevronClosed) }.WithMargin(DialogMetrics.PickerChevronGap);
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };

    public InstrumentFamilyHeader(string family, Action? toggle)
    {
        Family = family;
        var name = new TextBlock { Text = family, FontSize = DialogMetrics.FontSize, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary");
        _chevron[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary");
        _count[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_count, Dock.Right);
        row.Children.Add(_count);
        if (toggle is not null)
        {
            DockPanel.SetDock(_chevron, Dock.Left);
            row.Children.Add(_chevron);
        }
        row.Children.Add(name);
        Child = row;
        DialogMetrics.StylePickerHeader(this);
        Background = AvaloniaPalette.TransparentBrush;   // transparent at rest, so the whole row takes the pointer
        if (toggle is null) return;
        Focusable = true;
        HoverShade.Wire(this, fill => Background = fill, () => AvaloniaPalette.TransparentBrush);
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus();
            toggle();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            toggle();
            e.Handled = true;
        };
    }

    /// <summary>The family this header names.</summary>
    public string Family { get; }

    /// <summary>True while the family's tiles are shown (the chevron is turned down).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The count text on the right.</summary>
    public string CountText => _count.Text ?? "";

    /// <summary>Shows the open or closed chevron and the number of sounds in the family.</summary>
    public void Show(bool open, int count)
    {
        IsOpen = open;
        _chevron.RenderTransform = new RotateTransform(open ? DialogMetrics.PickerChevronOpen : DialogMetrics.PickerChevronClosed);
        _count.Text = count.ToString();
    }
}
