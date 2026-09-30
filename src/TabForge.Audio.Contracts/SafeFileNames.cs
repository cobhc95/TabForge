namespace TabForge.Audio.Contracts;

/// <summary>D4: the one file-name sanitiser (UI project and audio engine): invalid characters become '_', edges are trimmed, Windows reserved device names are prefixed.</summary>
public static class SafeFileNames
{
    public static string SafeFileName(string? name, string fallback, int maxLength = 0)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string((name ?? "").Select(c => Array.IndexOf(bad, c) >= 0 ? '_' : c).ToArray()).Trim(' ', '.');
        if (maxLength > 0 && clean.Length > maxLength) clean = clean[..maxLength].TrimEnd(' ', '.');
        return AvoidReserved(clean.Length == 0 ? fallback : clean);
    }

    /// <summary>Prefixes "_" to Windows reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9), with or without an extension.</summary>
    public static string AvoidReserved(string name)
    {
        var dot = name.IndexOf('.');
        var stem = (dot >= 0 ? name[..dot] : name).TrimEnd(' ');
        var reserved = stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
        return reserved ? "_" + name : name;
    }
}
