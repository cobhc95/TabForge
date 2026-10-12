using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TabForge.Views.Band;

// Owns: the track colour frame of one Band row: a rounded 1 px line in the track's colour around the row's content and a dim halo
//   outside it. The line and the halo's two rings are plain Borders that lie beside the content (siblings, never ancestors), with frozen
//   brushes and no Effect, so nothing here is drawn again while the score inside moves and the score keeps its pixel-sharp place.
// Does not own: the content, the row's own outline with the frame off, or the setting (BandSettings.ColourFrame).
// Tests: TestBandPillsAndRows.
internal sealed class BandRowFrame : Grid
{
    /// <summary>The room the halo takes on every side of the line.</summary>
    internal const double HaloWidth = 4;
    /// <summary>How far the content lies inside the row with the frame on: the halo, the line and the room inside the line. 8 is a whole
    /// number of device pixels at every quarter step of the display scale, so the score inside stands on whole pixels as it does without the frame.</summary>
    internal const double Inset = 8;
    private readonly Border _outer = new() { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(HaloWidth), IsHitTestVisible = false };
    private readonly Border _inner = new() { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2), Margin = new Thickness(2), IsHitTestVisible = false };
    private readonly Border _line = new() { IsHitTestVisible = false };
    private readonly Border _holder = new();
    private readonly Brush _colour;
    private bool _on, _selected, _applied;

    public BandRowFrame(UIElement content, Color colour)
    {
        Colour = colour;
        _colour = Frozen(colour, 0.55);
        _outer.BorderBrush = Frozen(colour, 0.07);
        _inner.BorderBrush = Frozen(colour, 0.10);
        _holder.Child = content;
        Children.Add(_outer);
        Children.Add(_inner);
        Children.Add(_holder);
        Children.Add(_line);
        Set(false, false);
    }

    /// <summary>The track's colour, as the track list and the pill dot show it.</summary>
    internal Color Colour { get; }
    /// <summary>The line around the content (thickness 0 with the frame off): drawn over the content's edge room, not around it as a parent.</summary>
    internal Border Line => _line;
    internal bool HaloVisible => _outer.Visibility == Visibility.Visible;

    /// <summary>Shows or hides the frame. The selected row's line takes the accent colour, as its outline does with the frame off.</summary>
    public void Set(bool on, bool selected)
    {
        if (_applied && on == _on && selected == _selected) return;
        _applied = true; _on = on; _selected = selected;
        _outer.Visibility = _inner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _line.Margin = new Thickness(on ? HaloWidth : 0);
        _line.BorderThickness = new Thickness(on ? 1 : 0);
        // The room inside the line keeps the square corners of the content inside the rounded line, so the content needs no clip.
        _holder.Margin = new Thickness(on ? Inset : 0);
        _line.CornerRadius = new CornerRadius(on ? 6 : 0);
        if (on && selected) _line.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); else _line.BorderBrush = on ? _colour : null;
    }

    private static Brush Frozen(Color colour, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), colour.R, colour.G, colour.B));
        brush.Freeze();
        return brush;
    }
}
