using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Views.Band;

// Owns: the glow of the notes sounding in one lane: a small layer over the strip that draws the score's own played-note chip
//   (ScorePlayedChip) at each sounding fret number. It repaints only when the set of sounding notes changes.
// Does not own: which notes sound (BandViewController) or where they sit (BandLane).
// Tests: TestBandNoteGlow.
internal sealed class BandNoteGlow : FrameworkElement
{
    /// <summary>One note: its centre in unzoomed strip units, the label printed there and whether it was just struck.</summary>
    internal readonly record struct Chip(double X, double Y, string Label, bool Struck);

    private IReadOnlyList<Chip> _chips = Array.Empty<Chip>();
    private double _zoom = 1, _fontSize = 11;
    private Color _colour = Color.FromRgb(0x3F, 0xB9, 0x50);
    private long _signature;

    public BandNoteGlow() => IsHitTestVisible = false;

    /// <summary>How many times the layer was repainted (a self-test counter).</summary>
    public int Repaints { get; private set; }

    /// <summary>The chips drawn now.</summary>
    internal IReadOnlyList<Chip> Chips => _chips;

    /// <summary>Shows these chips; nothing is repainted when they are the ones already shown at the same zoom, font and colour.</summary>
    public void Show(IReadOnlyList<Chip> chips, double zoom, double fontSize, Color colour)
    {
        var signature = Signature(chips, zoom, fontSize, colour);
        if (signature == _signature && chips.Count == _chips.Count) return;
        _signature = signature;
        _chips = chips; _zoom = zoom; _fontSize = fontSize; _colour = colour;
        Repaints++;
        InvalidateVisual();
    }

    private static long Signature(IReadOnlyList<Chip> chips, double zoom, double fontSize, Color colour)
    {
        var hash = new HashCode();
        hash.Add(zoom); hash.Add(fontSize); hash.Add(colour);
        foreach (var chip in chips) { hash.Add(chip.X); hash.Add(chip.Y); hash.Add(chip.Label); hash.Add(chip.Struck); }
        return hash.ToHashCode();
    }

    /// <summary>The label printed for a note: the fret, X for a dead note, the drum name on a drum track.</summary>
    internal static string LabelOf(TrackModel track, int fret, int midi, bool dead) =>
        dead ? "X" : track.Kind == TrackKind.Drums ? DrumMaps.For(track, midi > 0 ? midi : fret).Label : fret.ToString(CultureInfo.InvariantCulture);

    protected override void OnRender(DrawingContext dc)
    {
        if (_chips.Count == 0) return;
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));
        foreach (var chip in _chips)
        {
            var text = ScoreText.MakeTextIn(ScoreTextArea.Fret, chip.Label, _fontSize, ScoreText.Brush(_colour), FontWeights.Normal, "Consolas");
            ScorePlayedChip.Draw(dc, _colour, chip.Struck, chip.X, chip.Y, text.Width + 2, Math.Max(14, text.Height + 2));
        }
        dc.Pop();
    }
}
