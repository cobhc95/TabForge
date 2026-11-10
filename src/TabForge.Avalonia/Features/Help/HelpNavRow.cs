using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Features.Help;

// Owns: one chapter row of the Help window's list: its title, the accent tint with an accent bar while its chapter is open, a hover shade, and the click, Enter,
//   Space, Up and Down handling (a focusable row, so the list works from the keyboard).
// Does not own: which chapter is open (HelpWindow), the colours (palette keys, bound live).
// Tests: TabForge.Avalonia.Tests.HelpWindowTests.
internal sealed class HelpNavRow : Border
{
    private readonly TextBlock _label;
    private readonly Border _bar;
    private bool _selected, _hover;

    internal HelpNavRow(int chapter, string title, Action<int, int?> open, Action<int, int> step)
    {
        Chapter = chapter;
        Focusable = true;
        MinHeight = ChromeMetrics.HelpNavRowHeight;
        Background = AvaloniaPalette.TransparentBrush;
        CornerRadius = new CornerRadius(ChromeMetrics.RadiusMedium);
        DialogMetrics.Spaced(this, ChromeMetrics.HelpNavRowInset, ChromeMetrics.HelpRowGap);
        _label = DialogMetrics.Spaced(new TextBlock { Text = title, FontSize = ChromeMetrics.HelpNavSize, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, ChromeMetrics.HelpNavIndent, 0, ChromeMetrics.HelpBlockGap, 0);
        _bar = new Border { Width = ChromeMetrics.HelpNavBar, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(ChromeMetrics.RadiusTiny), IsVisible = false };
        HelpRichText.Bind(_bar, BackgroundProperty, "Accent");
        var grid = new Grid();
        grid.Children.Add(_bar);
        grid.Children.Add(_label);
        Child = grid;
        Paint();
        PointerEntered += (_, _) => { _hover = true; Paint(); };
        PointerExited += (_, _) => { _hover = false; Paint(); };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Focus();
            open(chapter, null);
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None) return;
            switch (e.Key)
            {
                case Key.Enter or Key.Space: open(chapter, null); break;
                case Key.Up: step(chapter, -1); break;
                case Key.Down: step(chapter, 1); break;
                default: return;
            }
            e.Handled = true;
        };
    }

    /// <summary>The chapter this row opens.</summary>
    internal int Chapter { get; }

    /// <summary>True while this row's chapter is open: the accent tint, the accent bar and the selected ink.</summary>
    internal bool Selected
    {
        get => _selected;
        set { _selected = value; Paint(); }
    }

    private void Paint()
    {
        _bar.IsVisible = _selected;
        if (_selected) HelpRichText.Bind(this, BackgroundProperty, "SelectedTint");
        else if (_hover) HelpRichText.Bind(this, BackgroundProperty, "HoverBrush");
        else Background = AvaloniaPalette.TransparentBrush;
        HelpRichText.Bind(_label, TextBlock.ForegroundProperty, _selected ? "SelectedInk" : "TextPrimary");
        _label.FontWeight = _selected ? FontWeight.SemiBold : FontWeight.Normal;
    }
}
