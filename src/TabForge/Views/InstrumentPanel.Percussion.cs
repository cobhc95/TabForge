using System.Windows;
using System.Windows.Media;
using TabForge.Visualization;

namespace TabForge.Views;

// InstrumentPanel: the drum key map (GM percussion 27-87 in columns), its click-to-write hit test and its hit glow.
public sealed partial class InstrumentPanel
{
    public static readonly string[] PercussionNames =
    {
        "High Q", "Slap", "Scratch Push", "Scratch Pull", "Sticks", "Square Click", "Metronome Click", "Metronome Bell",
        "Acoustic Bass Drum", "Bass Drum 1", "Side Stick", "Acoustic Snare", "Hand Clap", "Electric Snare", "Low Floor Tom",
        "Closed Hi-Hat", "High Floor Tom", "Pedal Hi-Hat", "Low Tom", "Open Hi-Hat", "Low-Mid Tom", "Hi-Mid Tom",
        "Crash Cymbal 1", "High Tom", "Ride Cymbal 1", "Chinese Cymbal", "Ride Bell", "Tambourine", "Splash Cymbal",
        "Cowbell", "Crash Cymbal 2", "Vibraslap", "Ride Cymbal 2", "High Bongo", "Low Bongo", "Mute Hi Conga",
        "Open Hi Conga", "Low Conga", "High Timbale", "Low Timbale", "High Agogo", "Low Agogo", "Cabasa", "Maracas",
        "Short Whistle", "Long Whistle", "Short Guiro", "Long Guiro", "Claves", "Hi Wood Block", "Low Wood Block",
        "Mute Cuica", "Open Cuica", "Mute Triangle", "Open Triangle", "Shaker", "Jingle Bell", "Bell Tree",
        "Castinets", "Mute Surdo", "Open Surdo",
    };
    private const int FirstPercussion = 27;
    /// <summary>Drum tracks: the label the track's drum preset writes on the TAB for a sound (shown in the key map).</summary>
    public Func<int, string>? DrumLabel { get; set; }

    private (int Columns, int Rows, double CellW, double CellH) PercussionGrid(double w, double h)
    {
        var count = PercussionNames.Length;
        var columns = Math.Clamp((int)(w / 190), 4, 13);
        var rows = (int)Math.Ceiling(count / (double)columns);
        return (columns, rows, (w - 16) / columns, (h - 10) / rows);
    }

    /// <summary>GM percussion note under a point of the key map (drum tracks), for click-to-write.</summary>
    public bool TryHitPercussion(Point point, out int midi)
    {
        midi = 0;
        if (_state?.Kind != InstrumentKind.Drums) return false;
        var (columns, rows, cw, ch) = PercussionGrid(ActualWidth <= 0 ? 900 : ActualWidth, ActualHeight <= 0 ? 168 : ActualHeight);
        var col = (int)((point.X - 8) / cw); var row = (int)((point.Y - 5) / ch);
        if (col < 0 || col >= columns || row < 0 || row >= rows) return false;
        var index = col * rows + row;
        if (index >= PercussionNames.Length) return false;
        midi = FirstPercussion + index;
        return true;
    }

    /// <summary>True while a drum hit is still fading, so the host redraws per frame only then.</summary>
    public bool IsAnimating { get; private set; }

    private void DrawPercussionMap(DrawingContext dc, InstrumentVisualState state, double w, double h)
    {
        var (columns, rows, cw, ch) = PercussionGrid(w, h);
        // Hit recently (within 260 ms of its onset) -> glow that fades; sounding -> steady highlight.
        var glow = new Dictionary<int, double>();
        foreach (var note in state.Notes)
        {
            var age = state.NowMs - note.OnsetMs;
            if (age < -5 || age > 260) continue;
            var strength = 1 - Math.Clamp(age / 260, 0, 1);
            glow[note.Midi] = Math.Max(glow.GetValueOrDefault(note.Midi), strength);
        }
        IsAnimating = glow.Count > 0;
        var fontSize = Math.Clamp(ch * 0.62, 9, 13);
        for (var i = 0; i < PercussionNames.Length; i++)
        {
            var col = i / rows; var row = i % rows;
            var rect = new Rect(8 + col * cw, 5 + row * ch, cw - 4, ch - 1);
            var midi = FirstPercussion + i;
            if (glow.TryGetValue(midi, out var g) && g > 0)
                dc.DrawRoundedRectangle(Draw.Solid(_theme.Current, 0.25 + 0.55 * g), Draw.Pen(_theme.Current, 1, 0.9), rect, 3, 3);
            var mapped = DrumLabel?.Invoke(midi);
            var text = string.IsNullOrEmpty(mapped) || mapped == midi.ToString() ? $"{midi} - {PercussionNames[i]}" : $"{midi} - {PercussionNames[i]}  [{mapped}]";
            Draw.At(dc, text, rect.X + 4, rect.Y + (ch - fontSize * 1.35) / 2, fontSize,
                Draw.Solid(g > 0 ? _theme.Text : _theme.Muted), g > 0.3);
        }
    }
}
