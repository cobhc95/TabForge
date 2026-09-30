using System.Text.RegularExpressions;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>
/// Names for extended-range fretted instruments: a bass or guitar with a
/// non-standard string count gets a "(N strings)" suffix ("Electric Bass (Finger) (5 strings)"). The
/// string count itself travels in the tuning, which Guitar Pro reads from the file; the name is display.
/// Other instruments (violin, cello, piano, drums...) are never renamed.
/// </summary>
public static partial class InstrumentNaming
{
    [GeneratedRegex(@"\s*\(\d+ strings\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex StringCountSuffix();

    /// <summary>Standard string count for a bass (4) or guitar (6); null for instruments that are never renamed.</summary>
    public static int? StandardStrings(TrackKind kind) => kind switch
    {
        TrackKind.Bass => 4,
        TrackKind.Guitar => 6,
        _ => null,
    };

    public static string WithoutStringCount(string name) => StringCountSuffix().Replace(name ?? "", "");

    /// <summary>Only guitars and basses by name (a contrabass or violin keeps its name).</summary>
    public static bool IsExtendableRange(string name) =>
        (name.Contains("Guitar", StringComparison.OrdinalIgnoreCase) || name.Contains("Bass", StringComparison.OrdinalIgnoreCase)) &&
        !name.Contains("Contrabass", StringComparison.OrdinalIgnoreCase) && !name.Contains("Synth", StringComparison.OrdinalIgnoreCase);

    public static string ForStringCount(string name, TrackKind kind, int strings)
    {
        var baseName = WithoutStringCount(name);
        return IsExtendableRange(baseName) && StandardStrings(kind) is int standard && strings != standard && strings > 0
            ? $"{baseName} ({strings} strings)"
            : baseName;
    }
}
