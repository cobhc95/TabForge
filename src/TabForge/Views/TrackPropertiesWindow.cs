using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using TabForge.Controllers;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Track properties: an instrument picture, identity (name, performer, colour, notes), instrument and
/// MIDI sound, mixer (volume/pan knobs) and a visual string-tuning editor with presets.
/// Edits apply to the track only when OK is pressed.
/// </summary>
public static class TrackPropertiesWindow
{
    internal sealed record TuningPreset(string Name, int[] HighToLow);

    private static readonly TuningPreset[] SixString =
    {
        new("Standard E", new[] { 64, 59, 55, 50, 45, 40 }),
        new("Eb standard (half step down)", new[] { 63, 58, 54, 49, 44, 39 }),
        new("D standard", new[] { 62, 57, 53, 48, 43, 38 }),
        new("C# standard", new[] { 61, 56, 52, 47, 42, 37 }),
        new("C standard", new[] { 60, 55, 51, 46, 41, 36 }),
        new("Drop D", new[] { 64, 59, 55, 50, 45, 38 }),
        new("Drop C#", new[] { 63, 58, 54, 49, 44, 37 }),
        new("Drop C", new[] { 62, 57, 53, 48, 43, 36 }),
        new("Drop B", new[] { 61, 56, 52, 47, 42, 35 }),
        new("Drop A", new[] { 59, 54, 50, 45, 40, 33 }),
        new("Open G (D G D G B D)", new[] { 62, 59, 55, 50, 43, 38 }),
        new("Open D (D A D F# A D)", new[] { 62, 57, 54, 50, 45, 38 }),
        new("DADGAD", new[] { 62, 57, 55, 50, 45, 38 }),
    };
    private static readonly TuningPreset[] SevenString =
    {
        new("Standard B", new[] { 64, 59, 55, 50, 45, 40, 35 }),
        new("Drop A", new[] { 64, 59, 55, 50, 45, 40, 33 }),
        new("A standard", new[] { 62, 57, 53, 48, 43, 38, 33 }),
    };
    private static readonly TuningPreset[] EightString =
    {
        new("Standard F#", new[] { 64, 59, 55, 50, 45, 40, 35, 30 }),
        new("Drop E", new[] { 64, 59, 55, 50, 45, 40, 35, 28 }),
    };
    private static readonly TuningPreset[] FourStringBass =
    {
        new("Standard E", new[] { 43, 38, 33, 28 }),
        new("Eb standard", new[] { 42, 37, 32, 27 }),
        new("D standard", new[] { 41, 36, 31, 26 }),
        new("Drop D", new[] { 43, 38, 33, 26 }),
        new("Drop C", new[] { 41, 36, 31, 24 }),
    };
    private static readonly TuningPreset[] FiveStringBass =
    {
        new("Standard B", new[] { 43, 38, 33, 28, 23 }),
        new("Standard E, high C", new[] { 48, 43, 38, 33, 28 }),
        new("Drop A", new[] { 43, 38, 33, 28, 21 }),
        new("A standard", new[] { 41, 36, 31, 26, 21 }),
    };
    private static readonly TuningPreset[] SixStringBass =
    {
        new("Standard B (high C)", new[] { 48, 43, 38, 33, 28, 23 }),
        new("Drop A", new[] { 48, 43, 38, 33, 28, 21 }),
    };
    private static readonly TuningPreset[] SevenStringBass =
    {
        new("Standard F# (high F)", new[] { 53, 48, 43, 38, 33, 28, 23 }),
    };
    private static readonly TuningPreset[] NineString =
    {
        new("Standard C#", new[] { 64, 59, 55, 50, 45, 40, 35, 30, 25 }),
    };

    public static IEnumerable<(string Name, int[] HighToLow)> SixStringPresets => SixString.Select(p => (p.Name, p.HighToLow));

    // Presets depend on the instrument as well as the count: a 6-string bass is not a guitar.
    internal static IEnumerable<TuningPreset> PresetsFor(int strings, bool bass) => (strings, bass) switch
    {
        (4, _) => FourStringBass,
        (5, _) => FiveStringBass,
        (6, true) => SixStringBass,
        (6, false) => SixString,
        (7, true) => SevenStringBass,
        (7, false) => SevenString,
        (8, _) => EightString,
        (9, _) => NineString,
        _ => Array.Empty<TuningPreset>()
    };

    /// <summary>Parses "C4", "D#2", "Eb3", "f#" (octave optional: nearest to middle of the guitar range) to MIDI.</summary>
    public static bool TryParseNote(string text, out int midi)
    {
        midi = 0;
        var m = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^([A-Ga-g])([#b♯♭]?)(-?\d)?$");
        if (!m.Success) return false;
        var pc = char.ToUpperInvariant(m.Groups[1].Value[0]) switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, _ => 11 };
        var acc = m.Groups[2].Value;
        if (acc is "#" or "♯") pc++;
        else if (acc is "b" or "♭") pc--;
        var octave = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 3;
        midi = (octave + 1) * 12 + pc;
        return midi is >= 12 and <= 108;
    }

    public static string NoteName(int midi) => MusicTheoryService.NoteName(midi);

    internal static TrackKind KindOf(string instrument, TrackModel track) => TrackSetup.KindOf(instrument, track.MidiChannel, track.Kind);

    /// <summary>Where "Add track" puts the new track; <see cref="InsertIndex"/> is set when the user confirms.</summary>
    public sealed class AddTrackPlacement
    {
        public AddTrackPlacement(int trackCount, int selectedIndex) { TrackCount = trackCount; SelectedIndex = selectedIndex; InsertIndex = trackCount; }
        public int TrackCount { get; }
        public int SelectedIndex { get; }
        public int InsertIndex { get; set; }
    }

    public static bool Show(Window owner, TrackModel track) => ShowCore(owner, track, null);

    /// <summary>
    /// The same window as Track properties, for a new (not yet added) track: pick the instrument from the
    /// catalogue, tuning, mixer and details, then where it goes in the track list. Returns false on Cancel.
    /// </summary>
    public static bool ShowAdd(Window owner, TrackModel track, AddTrackPlacement placement) => ShowCore(owner, track, placement);

    private static bool ShowCore(Window owner, TrackModel track, AddTrackPlacement? add) => new TrackPropertiesDialog(owner, track, add).Run();

    /// <summary>
    /// Sets new string tunings. With <paramref name="keepFrets"/> each note keeps its fret and moves in
    /// pitch by its string's change; otherwise pitches stay and frets are recomputed where possible.
    /// </summary>
    internal static void ApplyTuning(TrackModel track, List<int> tunings, bool keepFrets)
    {
        var old = track.StringTunings;
        if (old.SequenceEqual(tunings)) return;
        foreach (var measure in track.Measures)
            foreach (var cell in measure.Cells.Concat(measure.Voice2Cells))
                foreach (var note in cell.Notes)
                {
                    if (note.StringIndex < 0 || note.StringIndex >= tunings.Count) continue;
                    if (keepFrets)
                    {
                        var delta = note.StringIndex < old.Count ? tunings[note.StringIndex] - old[note.StringIndex] : 0;
                        note.MidiValue = Math.Clamp(note.MidiValue + delta, 0, 127);
                        if (note.SlideTargetMidi > 0) note.SlideTargetMidi = Math.Clamp(note.SlideTargetMidi + delta, 0, 127);
                        if (note.TrillTargetMidi > 0) note.TrillTargetMidi = Math.Clamp(note.TrillTargetMidi + delta, 0, 127);
                    }
                    else
                    {
                        var fret = note.MidiValue - tunings[note.StringIndex] - track.Capo;
                        if (fret >= 0 && fret <= track.NumberOfFrets) note.Fret = fret;
                    }
                }
        track.StringTunings = tunings.ToList();
    }
}

/// <summary>Vector instrument pictures for the track properties header, tinted with the track colour.</summary>
internal static class InstrumentArt
{
    public static UIElement Build(TrackKind kind, Color tint, int strings)
    {
        var canvas = new Canvas { Width = 250, Height = 190 };
        var wood = new LinearGradientBrush(Color.FromRgb(0x8A, 0x5A, 0x2B), Color.FromRgb(0x4E, 0x30, 0x16), 90);
        var body = new LinearGradientBrush(Lighten(tint, 0.25), Darken(tint, 0.35), 60);
        var metal = new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE2));
        var edge = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0));
        void Add(Shape s, double x, double y) { Canvas.SetLeft(s, x); Canvas.SetTop(s, y); canvas.Children.Add(s); }

        switch (kind)
        {
            case TrackKind.Drums:
            {
                Add(new Ellipse { Width = 150, Height = 40, Fill = body, Stroke = edge, StrokeThickness = 2 }, 50, 80);
                Add(new Rectangle { Width = 150, Height = 50, Fill = body, Stroke = edge, StrokeThickness = 2 }, 50, 100);
                Add(new Ellipse { Width = 150, Height = 40, Fill = new SolidColorBrush(Color.FromRgb(0xEE, 0xEA, 0xE0)), Stroke = edge, StrokeThickness = 2 }, 50, 60 + 20);
                Add(new Ellipse { Width = 150, Height = 40, Fill = new SolidColorBrush(Color.FromRgb(0xF4, 0xF1, 0xEA)), Stroke = edge, StrokeThickness = 2 }, 50, 78);
                Add(new Ellipse { Width = 120, Height = 16, Fill = new SolidColorBrush(Color.FromRgb(0xD9, 0xB3, 0x4A)), Stroke = edge, StrokeThickness = 1.5 }, 10, 30);
                Add(new Ellipse { Width = 100, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(0xD9, 0xB3, 0x4A)), Stroke = edge, StrokeThickness = 1.5 }, 140, 22);
                Add(new Rectangle { Width = 3, Height = 120, Fill = metal }, 69, 38);
                Add(new Rectangle { Width = 3, Height = 130, Fill = metal }, 189, 30);
                Add(new Line { X1 = 0, Y1 = 0, X2 = 60, Y2 = 40, Stroke = wood, StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }, 100, 40);
                Add(new Line { X1 = 60, Y1 = 0, X2 = 0, Y2 = 40, Stroke = wood, StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }, 95, 42);
                break;
            }
            case TrackKind.Keys:
            {
                Add(new Rectangle { Width = 220, Height = 90, RadiusX = 8, RadiusY = 8, Fill = body, Stroke = edge, StrokeThickness = 2 }, 15, 55);
                for (var i = 0; i < 14; i++)
                    Add(new Rectangle { Width = 14, Height = 56, Fill = Brushes.White, Stroke = edge, StrokeThickness = 1 }, 26 + i * 14.3, 80);
                foreach (var i in new[] { 0, 1, 3, 4, 5, 7, 8, 10, 11, 12 })
                    Add(new Rectangle { Width = 9, Height = 34, Fill = Brushes.Black }, 36 + i * 14.3, 80);
                Add(new Rectangle { Width = 190, Height = 12, RadiusX = 3, RadiusY = 3, Fill = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)) }, 30, 62);
                break;
            }
            default:
            {
                var bass = kind == TrackKind.Bass;
                // Neck and headstock run diagonally across the card.
                var group = new Canvas { Width = 250, Height = 190, RenderTransform = new RotateTransform(-28, 125, 95) };
                void G(Shape s, double x, double y) { Canvas.SetLeft(s, x); Canvas.SetTop(s, y); group.Children.Add(s); }
                var neckLength = bass ? 150 : 130;
                G(new Rectangle { Width = neckLength, Height = 16, Fill = wood, Stroke = edge, StrokeThickness = 1 }, 118, 87);
                G(new Rectangle { Width = 34, Height = 26, RadiusX = 5, RadiusY = 5, Fill = wood, Stroke = edge, StrokeThickness = 1 }, 118 + neckLength - 4, 82);
                for (var f = 1; f < 9; f++) G(new Rectangle { Width = 1.5, Height = 16, Fill = metal }, 118 + f * (neckLength / 9.0), 87);
                var pegs = Math.Clamp(strings, 4, 8);
                for (var p = 0; p < pegs; p++)
                    G(new Ellipse { Width = 6, Height = 6, Fill = metal }, 122 + neckLength + (p % 2) * 12, 78 + (p / 2) * 10 + (p % 2) * 3);
                // Body: two overlapping bouts plus a waist.
                G(new Ellipse { Width = bass ? 92 : 100, Height = bass ? 78 : 88, Fill = body, Stroke = edge, StrokeThickness = 2 }, 20, 51);
                G(new Ellipse { Width = bass ? 70 : 76, Height = bass ? 64 : 70, Fill = body, Stroke = edge, StrokeThickness = 2 }, 84, 60);
                G(new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x10)) }, 92, 84);
                G(new Rectangle { Width = 8, Height = 34, RadiusX = 2, RadiusY = 2, Fill = Brushes.Black }, 46, 78);
                for (var s = 0; s < Math.Min(pegs, 6); s++)
                    G(new Rectangle { Width = neckLength + 70, Height = 0.9, Fill = metal }, 50, 89 + s * (12.0 / Math.Max(1, Math.Min(pegs, 6) - 1)));
                canvas.Children.Add(group);
                break;
            }
        }
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    }

    private static Color Lighten(Color c, double t) => Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
    private static Color Darken(Color c, double t) => Color.FromRgb((byte)(c.R * (1 - t)), (byte)(c.G * (1 - t)), (byte)(c.B * (1 - t)));
}
