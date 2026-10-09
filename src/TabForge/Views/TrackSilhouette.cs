using System.Collections.Frozen;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TabForge.Services;
using Path = System.Windows.Shapes.Path;

namespace TabForge.Views;

// Owns: the track-list row instrument icons (the owner's line art, vectorised into Assets/TrackRowIcons/owner-icons.json)
// and the GM program -> icon mapping.
// Does not own: the round instrument badges (InstrumentIcon: catalogue, menus, track properties), the row button.
// Tests: TestTrackSilhouetteMap.
/// <summary>
/// Instrument icons for the track-list rows on a 24 x 24 grid, in three layers: the grey shading (the theme brush, faint),
/// the line work (the theme brush, so it follows light and dark) and the accent detail in a fixed coral tint.
/// </summary>
internal static class TrackSilhouette
{
    /// <summary>The icon of an audio track.</summary>
    public const string AudioKey = "AudioWaveform";

    /// <summary>GM program ranges per icon ("Name:0-3,5;..."); drum kits use <see cref="DrumKitKey"/>.</summary>
    private const string ProgramMap =
        "PianoKeys:0-3;RhodesPiano:4,5,7;Harpsichord:6;Xylophone:8-13;Koto1:112;TubularBells:14;Koto2:15,107;" +
        "HammondDrawbars:16-18;ChurchOrgan:19,20;Accordion:21;Harmonica1:23;Harmonica2:22;" +
        "ClassicalHeadstock:24;GibsonHeadstock:25,26;Stratocaster:27,28,31;IbanezHeadstock:29;PointedHeadstock:30;" +
        "AcousticBass:32;PrecisionBass:33,34;RingRaBass:35;StingRayBass:36,37;TB303:38,39;" +
        "Violin:40,41,110;ViolinScroll:44,45,48-51;CelloFHoles:42,43;PedalHarp:46;Timpani:47;" +
        "Choir:52;Microphone:53,54;OrchestraHit:55;Trumpet:56,59,61-63;Trombone:57;Tuba:58;FrenchHorn:60;" +
        "AltoSax:64-67;Eltumory:68,69,111;Clarinet:70,71;Aunbal:72,78;Flute:73;SopranoRecorder:74;PanPipes:75;Whistle:76,77;Ocarina:79;" +
        "Minimoog:80-87;SynthPad:88-95;SynthEffects:96-103;" +
        "Sitar:104;Banjo:105;Shamisen:106;Kalimba:108;Bagpipe:109;" +
        "SteelPan:114;Woodblock:113,115;TaikoDrum:116;SnareDrum:117;DrumKit:118;Cymbal:119;Loudspeaker:120-127";
    private const string DrumKitKey = "DrumKit";

    private static readonly IReadOnlyList<string> ByProgram = BuildProgramMap();
    private static readonly IReadOnlyDictionary<string, (Geometry Ink, Geometry? Accent, Geometry? Shade)> Art = LoadArt();
    private static readonly Brush AccentFill = Frozen(new SolidColorBrush(Color.FromRgb(0xE0, 0x7A, 0x6E)));

    private static Brush Frozen(Brush brush) { brush.Freeze(); return brush; }

    public static string ArtPath => System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "TrackRowIcons", "owner-icons.json");

    private static Geometry? Layer(JsonElement icon, string name)
    {
        if (!icon.TryGetProperty(name, out var value) || value.GetString() is not { Length: > 0 } data) return null;
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static IReadOnlyDictionary<string, (Geometry Ink, Geometry? Accent, Geometry? Shade)> LoadArt()
    {
        var art = new Dictionary<string, (Geometry, Geometry?, Geometry?)>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ArtPath));
            foreach (var icon in doc.RootElement.EnumerateObject())
            {
                if (Layer(icon.Value, "ink") is { } ink) art[icon.Name] = (ink, Layer(icon.Value, "accent"), Layer(icon.Value, "shade"));
            }
        }
        // Without the art file the rows show no icon; nothing else depends on it.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException) { }
        return art.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string[] BuildProgramMap()
    {
        var map = new string[128];
        foreach (var entry in ProgramMap.Split(';'))
        {
            var (name, ranges) = (entry[..entry.IndexOf(':')], entry[(entry.IndexOf(':') + 1)..]);
            foreach (var range in ranges.Split(','))
            {
                var bounds = range.Split('-').Select(int.Parse).ToArray();
                for (var p = bounds[0]; p <= bounds[^1]; p++) map[p] = name;
            }
        }
        return map;
    }

    /// <summary>Every icon the mapping uses, plus the audio-track icon.</summary>
    public static IEnumerable<string> Keys => ByProgram.Append(DrumKitKey).Append(AudioKey).Distinct();

    /// <summary>Icon for a GM program (0-127) or a drum kit.</summary>
    public static string KeyFor(int program, bool drumKit) => drumKit ? DrumKitKey : ByProgram[Math.Clamp(program, 0, 127)];

    public static string KeyFor(InstrumentEntry entry) => KeyFor(entry.Program, entry.IsDrumKit);

    /// <summary>True when <paramref name="key"/> has artwork.</summary>
    public static bool Has(string key) => Art.ContainsKey(key);

    /// <summary>The icon at <paramref name="size"/> DIPs; its line work uses the theme brush <paramref name="brushKey"/>.</summary>
    public static FrameworkElement Element(string key, double size, string brushKey = "TextBrush")
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        if (Art.TryGetValue(key, out var art))
        {
            // The art's lines are about 0.85 grid units wide; small icons thicken them evenly to stay at least ~1.1 device pixels.
            var thicken = Math.Max(0, 1.1 * 24 / size - 0.85);
            if (art.Shade is { } shade)
            {
                var fill = new Path { Data = shade, Opacity = 0.3 };
                fill.SetResourceReference(Shape.FillProperty, brushKey);
                canvas.Children.Add(fill);
            }
            var ink = new Path { Data = art.Ink, StrokeThickness = thicken, StrokeLineJoin = PenLineJoin.Round };
            ink.SetResourceReference(Shape.FillProperty, brushKey);
            if (thicken > 0) ink.SetResourceReference(Shape.StrokeProperty, brushKey);
            canvas.Children.Add(ink);
            if (art.Accent is { } accent)
                canvas.Children.Add(new Path { Data = accent, Fill = AccentFill, Stroke = thicken > 0 ? AccentFill : null, StrokeThickness = thicken / 2 });
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }
}
