namespace TabForge.Services;

// Owns: the saved choices of the status bar scale readout: where it reads from, which tracks it listens to, which scale families it considers,
//   whether it prefers common scales when the fit is close, and whether it drives the fretboard scale highlight.
// Does not own: the detection (SectionScaleDetector), the readout texts (ScaleReadoutEngine), the popup (ScalePanel) or the fretboard highlight itself.
// Tests: TabForge.Avalonia.Tests.SectionScaleDetectorTests.

/// <summary>The values of <see cref="ScaleReadoutSettings.ReadFrom"/>.</summary>
public static class ScaleReadFrom
{
    /// <summary>Auto: the notes of the section the position is in (the whole song when the song has no sections).</summary>
    public const string Section = "Section";
    /// <summary>The notes of the whole song.</summary>
    public const string Song = "Song";
    /// <summary>No detection: the song key signature, as the status bar always showed.</summary>
    public const string Key = "Key";
    /// <summary>The selected bars (score or timeline selection); shown only when the song has one.</summary>
    public const string Selection = "Selection";

    public static readonly IReadOnlyList<string> All = new[] { Section, Song, Key, Selection };

    public static string Normalize(string? value) => All.FirstOrDefault(v => v.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? Section;
}

/// <summary>The values of <see cref="ScaleReadoutSettings.ListenTo"/>.</summary>
public static class ScaleListenTo
{
    /// <summary>The selected track only.</summary>
    public const string Track = "Track";
    /// <summary>Every pitched track (drums are left out).</summary>
    public const string Pitched = "Pitched";

    public static readonly IReadOnlyList<string> All = new[] { Track, Pitched };

    public static string Normalize(string? value) => All.FirstOrDefault(v => v.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? Track;
}

/// <summary>The status bar scale readout settings (Editing settings, Scale readout).</summary>
public sealed class ScaleReadoutSettings
{
    /// <summary>Where the readout reads from: <see cref="ScaleReadFrom.Section"/> (default), <see cref="ScaleReadFrom.Song"/> or <see cref="ScaleReadFrom.Key"/>.</summary>
    public string ReadFrom { get; set; } = ScaleReadFrom.Section;

    /// <summary>Which tracks it listens to: <see cref="ScaleListenTo.Track"/> (default) or <see cref="ScaleListenTo.Pitched"/>.</summary>
    public string ListenTo { get; set; } = ScaleListenTo.Track;

    private List<string> _families = ScaleFamilies.DefaultOn.ToList();

    /// <summary>The scale families it considers (see <see cref="ScaleFamilies"/>); Modes, World and Exotic are off by default. A family added after the choice was saved gets its default.</summary>
    public List<string> Families
    {
        get
        {
            foreach (var family in ScaleFamilies.AddedLater.Where(f => !SeenFamilies.Contains(f)).ToList())
            {
                SeenFamilies.Add(family);
                if (ScaleFamilies.DefaultOn.Contains(family) && !_families.Contains(family)) _families.Add(family);
            }
            return _families;
        }
        set => _families = value;
    }

    /// <summary>The added-later families the choice above already knows (so an old saved choice is migrated once, and a later untick is kept).</summary>
    public List<string> SeenFamilies { get; set; } = new();

    /// <summary>The favourite scale types ("Natural Minor"; a favourite applies to all 12 keys). The Favourites family considers them when it is ticked.</summary>
    public List<string> Favourites { get; set; } = new();

    /// <summary>Back to the default families (the favourites list stays).</summary>
    public void ResetFamilies()
    {
        _families = ScaleFamilies.DefaultOn.ToList();
        SeenFamilies = ScaleFamilies.AddedLater.ToList();
    }

    /// <summary>The scale names the detector considers: every scale of the ticked families, plus the favourites when Favourites is ticked (library order, no repeats).</summary>
    public IReadOnlyList<string> ConsideredScales()
    {
        var ticked = ScaleFamilies.Normalize(Families);
        var chosen = new HashSet<string>(ScaleFamilies.ScalesOf(ticked), StringComparer.OrdinalIgnoreCase);
        if (ticked.Contains(ScaleFamilies.Favourites)) chosen.UnionWith(Favourites.Where(MusicTheoryService.Scales.ContainsKey));
        return MusicTheoryService.Scales.Keys.Where(chosen.Contains).ToList();
    }

    /// <summary>When two scales fit about as well, the more common one wins.</summary>
    public bool PreferCommon { get; set; } = true;

    /// <summary>The readout drives the fretboard scale highlight. Off by default: the fretboard shows a scale only when the user picks one there.</summary>
    public bool ShowOnFretboard { get; set; }

    /// <summary>Spell keys like a key signature (E♭ instead of D#). Off by default: sharps only.</summary>
    public bool MusicalKeyNames { get; set; }

    /// <summary>The settings as one string, the key a cached detection is valid for.</summary>
    public string Signature() => $"{ReadFrom}|{ListenTo}|{string.Join(",", ScaleFamilies.Normalize(Families))}|{string.Join(",", Favourites)}|{PreferCommon}|{MusicalKeyNames}";
}
