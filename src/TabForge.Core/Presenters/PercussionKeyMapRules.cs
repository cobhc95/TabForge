using System.Collections.ObjectModel;
using TabForge.Platform;

namespace TabForge.Presenters;

// Owns: the drum key map's maths: the GM percussion names 27-87 (the labels the key map and the custom drum map window show), the column grid,
//   each cell's rectangle, the point-to-note hit test, the label text, the label size, the hit-glow fade and the pane height the map needs.
// Does not own: drawing (the Avalonia InstrumentPanelView), colours, or writing the note.
//   The drum map presets' piece names (Resources/DrumMaps/*.json) are a separate vocabulary used to match notes between presets.
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
public static class PercussionKeyMapRules
{
    public const int FirstNote = 27;
    public const double MinRowHeight = 14;
    /// <summary>A hit glows for this long after its onset.</summary>
    public const double GlowWindowMs = 260;
    /// <summary>The map's left edge: the cells start this far in from the pane's left side.</summary>
    public const double CellLeft = 8;
    /// <summary>The map's top edge: the cells start this far down from the pane's top.</summary>
    public const double CellTop = 5;
    /// <summary>The pane size the hit test assumes before the panel has been laid out.</summary>
    public const double FallbackWidth = 900;
    public const double FallbackHeight = 168;
    private const double ScalesButtonStrip = 8 + 24 + 4;

    /// <summary>The note names, index 0 is <see cref="FirstNote"/>. Read-only: callers index it, they never change it.</summary>
    public static readonly IReadOnlyList<string> Names = Array.AsReadOnly(new[]
    {
        "High Q", "Slap", "Scratch Push", "Scratch Pull", "Sticks", "Square Click", "Metronome Click", "Metronome Bell",
        "Acoustic Bass Drum", "Bass Drum 1", "Side Stick", "Acoustic Snare", "Hand Clap", "Electric Snare", "Low Floor Tom",
        "Closed Hi-Hat", "High Floor Tom", "Pedal Hi-Hat", "Low Tom", "Open Hi-Hat", "Low-Mid Tom", "Hi-Mid Tom",
        "Crash Cymbal 1", "High Tom", "Ride Cymbal 1", "Chinese Cymbal", "Ride Bell", "Tambourine", "Splash Cymbal",
        "Cowbell", "Crash Cymbal 2", "Vibraslap", "Ride Cymbal 2", "High Bongo", "Low Bongo", "Mute Hi Conga",
        "Open Hi Conga", "Low Conga", "High Timbale", "Low Timbale", "High Agogo", "Low Agogo", "Cabasa", "Maracas",
        "Short Whistle", "Long Whistle", "Short Guiro", "Long Guiro", "Claves", "Hi Wood Block", "Low Wood Block",
        "Mute Cuica", "Open Cuica", "Mute Triangle", "Open Triangle", "Shaker", "Jingle Bell", "Bell Tree",
        "Castanets", "Mute Surdo", "Open Surdo",
    });

    public readonly record struct Grid(int Columns, int Rows, double CellWidth, double CellHeight);

    private static int ColumnsFor(double width) => Math.Clamp((int)(width / 190), 4, 13);

    public static Grid GridFor(double width, double height)
    {
        var columns = ColumnsFor(width);
        var rows = (int)Math.Ceiling(Names.Count / (double)columns);
        return new Grid(columns, rows, (width - 16) / columns, (height - 10) / rows);
    }

    /// <summary>The rectangle a cell's highlight covers; notes run down a column, then on to the next.</summary>
    public static UiRect CellRect(Grid grid, int index)
        => new(CellLeft + index / grid.Rows * grid.CellWidth, CellTop + index % grid.Rows * grid.CellHeight, grid.CellWidth - 4, grid.CellHeight - 1);

    /// <summary>The GM note under a point of the map, or null in the left and top margins, between or outside the cells.</summary>
    public static int? NoteAt(double width, double height, UiPoint point)
    {
        if (point.X < CellLeft || point.Y < CellTop) return null;
        var grid = GridFor(width, height);
        var col = (int)Math.Floor((point.X - CellLeft) / grid.CellWidth);
        var row = (int)Math.Floor((point.Y - CellTop) / grid.CellHeight);
        if (col < 0 || col >= grid.Columns || row < 0 || row >= grid.Rows) return null;
        var index = col * grid.Rows + row;
        return index < Names.Count ? FirstNote + index : null;
    }

    /// <summary>"38 - Acoustic Snare", plus "  [label]" when the drum preset writes a different label for it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The note is outside <see cref="FirstNote"/> to the last name.</exception>
    public static string Label(int midi, string? mapped)
    {
        var index = midi - FirstNote;
        if (index < 0 || index >= Names.Count)
            throw new ArgumentOutOfRangeException(nameof(midi), midi, $"Drum key map notes run {FirstNote} to {FirstNote + Names.Count - 1}.");
        var name = Names[index];
        return string.IsNullOrEmpty(mapped) || mapped == midi.ToString() ? $"{midi} - {name}" : $"{midi} - {name}  [{mapped}]";
    }

    public static double LabelFontSize(Grid grid) => Math.Clamp(grid.CellHeight * 0.62, 9, 13);

    /// <summary>The glow of a hit this long after its onset (1 at the onset, fading to 0); 0 for a hit outside the window.</summary>
    public static double GlowStrength(double ageMs) => ageMs < -5 || ageMs >= GlowWindowMs ? 0 : 1 - Math.Clamp(ageMs / GlowWindowMs, 0, 1);

    /// <summary>The pane height below which the whole map cannot be drawn (the Scales button strip sits top-right).</summary>
    public static double RequiredHeight(double width)
        => Math.Ceiling(Math.Max(10 + (int)Math.Ceiling(Names.Count / (double)ColumnsFor(width > 0 ? width : FallbackWidth)) * MinRowHeight + 2, ScalesButtonStrip));
}
