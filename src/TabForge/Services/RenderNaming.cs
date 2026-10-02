using System.IO;
using System.Text;

namespace TabForge.Services;

// Owns: the file names of rendered audio files.
// Does not own: the render itself.
// Tests: TestRenderNaming.
/// <summary>File names for File > Render: wildcards $project $track $tracknumber $date $time $bpm, plus a safe "increment if it exists".</summary>
public static class RenderNaming
{
    public static string Expand(string pattern, string project, string track, int trackNumber, DateTime now, double bpm)
    {
        var text = string.IsNullOrWhiteSpace(pattern) ? "$project" : pattern;
        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '$') { sb.Append(text[i]); continue; }
            string? value = null; var len = 0;
            // Longest names first ($tracknumber before $track).
            foreach (var (name, get) in new (string, Func<string>)[]
            {
                ("tracknumber", () => trackNumber.ToString("00")), ("project", () => project), ("track", () => track),
                ("date", () => now.ToString("yyyy-MM-dd")), ("time", () => now.ToString("HH-mm-ss")),
                ("bpm", () => Math.Round(bpm).ToString("0")),
            })
                if (string.CompareOrdinal(text, i + 1, name, 0, name.Length) == 0) { value = get(); len = name.Length; break; }
            if (value is null) { sb.Append('$'); continue; }
            sb.Append(value); i += len;
        }
        return Sanitize(sb.ToString());
    }

    public static string Sanitize(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => Array.IndexOf(bad, c) >= 0 ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return clean.Length == 0 ? "render" : AvoidReserved(clean);
    }
    /// <summary>Prefixes "_" to Windows reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9), with or without an extension.</summary>
    public static string AvoidReserved(string name)
        => TabForge.Audio.Contracts.SafeFileNames.AvoidReserved(name);

    /// <summary>"name.wav" -> "name (2).wav" when taken (also avoiding names in <paramref name="reserved"/>).</summary>
    public static string Unique(string directory, string fileName, ISet<string>? reserved = null)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName); var ext = Path.GetExtension(fileName);
        var candidate = fileName;
        for (var n = 2; File.Exists(Path.Combine(directory, candidate)) || (reserved?.Contains(candidate) ?? false); n++) candidate = $"{stem} ({n}){ext}";
        return candidate;
    }
}
