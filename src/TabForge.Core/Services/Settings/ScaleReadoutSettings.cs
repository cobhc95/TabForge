namespace TabForge.Services;

// Owns: the saved choices of the status bar scale readout: where it reads from, which tracks it listens to, which scale families it considers,
//   whether it prefers common scales when the fit is close, and whether it drives the fretboard scale highlight.
// Does not own: the detection (SectionScaleDetector), the readout texts (ScaleReadoutEngine), the popup (ScaleReadoutPopup) or the fretboard highlight itself.
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

    public static readonly IReadOnlyList<string> All = new[] { Section, Song, Key };

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

    /// <summary>The scale families it considers (see <see cref="ScaleFamilies"/>); Modes and Exotic are off by default.</summary>
    public List<string> Families { get; set; } = ScaleFamilies.DefaultOn.ToList();

    /// <summary>When two scales fit about as well, the more common one wins.</summary>
    public bool PreferCommon { get; set; } = true;

    /// <summary>The readout drives the fretboard scale highlight. Off by default: the fretboard shows a scale only when the user picks one there.</summary>
    public bool ShowOnFretboard { get; set; }

    /// <summary>Spell keys like a key signature (E♭ instead of D#). Off by default: sharps only.</summary>
    public bool MusicalKeyNames { get; set; }

    /// <summary>The settings as one string, the key a cached detection is valid for.</summary>
    public string Signature() => $"{ReadFrom}|{ListenTo}|{string.Join(",", ScaleFamilies.Normalize(Families))}|{PreferCommon}|{MusicalKeyNames}";
}
