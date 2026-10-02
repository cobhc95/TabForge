using System.IO;
using System.Text.Json;
using TabForge.Controllers;

namespace TabForge.Services;

/// <summary>One selectable sound: a General MIDI program (0-127) or a GM drum kit (channel 10).</summary>
public sealed record InstrumentEntry(string Name, string Category, int Program, bool IsDrumKit, string? SvgPath, string Background)
{
    /// <summary>Articulation map the track uses for this sound.</summary>
    public string Map => IsDrumKit ? "GM Drums" : Category == "Guitar" ? "Generic Guitar" : Category == "Bass" ? "Generic Bass" : "Piano";
}

// Owns: the full instrument list with families and artwork names.
// Does not own: sound playback and the instrument pickers.
// Tests: TestInstrumentArtwork.
/// <summary>
/// The full instrument list: all 128 General MIDI programs grouped by the GM families,
/// plus the GM drum kits. Names, families and artwork come from Assets/Instruments/manifest.json.
/// </summary>
public static class InstrumentCatalog
{
    private static IReadOnlyList<InstrumentEntry>? _all;

    /// <summary>The six quick picks shown at the top of the instrument menu.</summary>
    public static readonly string[] CommonNames =
    {
        "Distortion Guitar", "Clean Electric Guitar", "Acoustic Guitar (Steel)", "Electric Bass (Pick)", "Grand Piano", "Drum Kit (Standard)"
    };

    /// <summary>GM drum kits (program numbers on channel 10, as GS/GM2 synths and the reference use them).</summary>
    private static readonly (string Name, int Program)[] DrumKits =
    {
        ("Drum Kit (Standard)", 0), ("Drum Kit (Room)", 8), ("Drum Kit (Power)", 16), ("Drum Kit (Electronic)", 24),
        ("Drum Kit (TR-808)", 25), ("Drum Kit (Jazz)", 32), ("Drum Kit (Brush)", 40), ("Drum Kit (Orchestra)", 48), ("Drum Kit (SFX)", 56),
    };

    public static string AssetRoot => Path.Combine(AppContext.BaseDirectory, "Assets", "Instruments");

    public static IReadOnlyList<InstrumentEntry> All => _all ??= Build();

    /// <summary>Families in GM order, then the drum kits.</summary>
    public static IEnumerable<string> Categories => All.Select(e => e.Category).Distinct();

    public static InstrumentEntry? Find(string? name) =>
        name is null ? null : All.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Best entry for a track: by instrument name, else by drum channel / program.</summary>
    public static InstrumentEntry? ForTrack(string? instrumentName, int program, bool drumChannel) =>
        Find(instrumentName)
        ?? (drumChannel ? All.FirstOrDefault(e => e.IsDrumKit && e.Program == program) ?? All.FirstOrDefault(e => e.IsDrumKit)
                        : All.FirstOrDefault(e => !e.IsDrumKit && e.Program == program));

    private static IReadOnlyList<InstrumentEntry> Build()
    {
        var list = new List<InstrumentEntry>();
        var manifest = new List<(string Name, string Category, int Program, string Svg, string Bg)>();
        try
        {
            var path = Path.Combine(AssetRoot, "manifest.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var program = item.TryGetProperty("midi_program_0_based", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : -1;
                    manifest.Add((item.GetProperty("name").GetString() ?? "", item.GetProperty("category").GetString() ?? "",
                        program, item.GetProperty("svg").GetString() ?? "", item.TryGetProperty("background_fill", out var b) ? b.GetString() ?? "#808080" : "#808080"));
                }
            }
        }
        // The catalogue still works from the GM names below, just without artwork.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or KeyNotFoundException or InvalidOperationException) { }

        string? Svg(string file) => string.IsNullOrEmpty(file) ? null : Path.Combine(AssetRoot, file.Replace('/', Path.DirectorySeparatorChar));
        var byProgram = manifest.Where(m => m.Program >= 0).GroupBy(m => m.Program).ToDictionary(g => g.Key, g => g.First());
        var other = manifest.FirstOrDefault(m => m.Name == "Other Instrument");
        var drumArt = manifest.FirstOrDefault(m => m.Program == 118); // fallback badge
        // Each GM drum kit has its own badge in SVG/18_Drum_Kits (e.g. "Drum Kit (TR-808)" -> 05_Drum_Kit_TR-808.svg).
        (string Svg, string Bg) DrumBadge(string kitName)
        {
            var suffix = kitName.Replace("Drum Kit (", "").TrimEnd(')');
            var folder = Path.Combine(AssetRoot, "SVG", "18_Drum_Kits");
            var file = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.svg").FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).EndsWith("_" + suffix, StringComparison.OrdinalIgnoreCase))
                : null;
            if (file is null) return (drumArt.Svg, drumArt.Bg ?? "#808080");
            var bg = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(file), "id=\"background\"[^>]*fill=\"(#[0-9a-fA-F]{6})\"").Groups[1].Value;
            return (Path.GetRelativePath(AssetRoot, file), string.IsNullOrEmpty(bg) ? "#808080" : bg);
        }

        // The app's own named sounds first (they keep existing songs' names working), then every GM program.
        foreach (var preset in TrackController.InstrumentPresets)
        {
            var drum = preset.Map == "GM Drums";
            var art = byProgram.GetValueOrDefault(preset.Program);
            var (svg, bg) = drum ? DrumBadge(preset.Name) : (art.Svg, art.Bg ?? "#808080");
            list.Add(new InstrumentEntry(preset.Name, drum ? "Drum Kits" : FamilyOf(preset.Program), preset.Program, drum, Svg(svg), bg));
        }
        for (var program = 0; program < 128; program++)
        {
            var name = GeneralMidi.NameOf(program); // The reference / General MIDI names; the manifest only supplies artwork
            if (list.Any(e => !e.IsDrumKit && e.Program == program && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
            if (list.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))) name += $" (GM {program + 1})";
            var art = byProgram.TryGetValue(program, out var hit) ? hit : other;
            list.Add(new InstrumentEntry(name, FamilyOf(program), program, false, Svg(art.Svg), art.Bg ?? "#808080"));
        }
        foreach (var (name, program) in DrumKits)
            if (!list.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                var (svg, bg) = DrumBadge(name);
                list.Add(new InstrumentEntry(name, "Drum Kits", program, true, Svg(svg), bg));
            }

        // GM family order, drum kits last.
        var order = Families.Select((f, i) => (f, i)).ToDictionary(x => x.f, x => x.i);
        return list.OrderBy(e => order.GetValueOrDefault(e.Category, 99)).ThenBy(e => e.IsDrumKit ? e.Program : 0)
            .ThenBy(e => e.IsDrumKit ? 0 : e.Program).ToList();
    }

    public static readonly string[] Families =
    {
        "Piano", "Chromatic Percussion", "Organ", "Guitar", "Bass", "Strings", "Ensemble", "Brass", "Reed", "Pipe",
        "Synth Lead", "Synth Pad", "Synth Effects", "Ethnic", "Percussive", "Sound Effects", "Drum Kits"
    };

    public static string FamilyOf(int program) => Families[Math.Clamp(program / 8, 0, 15)];
}
