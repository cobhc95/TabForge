namespace TabForge.Services;

// Owns: the three older binary score formats TabForge can write a copy in, their file extensions, version numbers and the wording shown to the person.
// Does not own: the bytes (LegacyGpWriter) or the questions asked before writing (LegacyGpPreflight).
// Tests: TestLegacyGpExport.
/// <summary>The older binary formats a copy can be saved in for programs that predate the current one.</summary>
public enum LegacyGpFormat { Gp3, Gp4, Gp5 }

public static class LegacyGpFormats
{
    public static readonly IReadOnlyList<LegacyGpFormat> All = new[] { LegacyGpFormat.Gp5, LegacyGpFormat.Gp4, LegacyGpFormat.Gp3 };

    public static string Extension(LegacyGpFormat format) => format switch { LegacyGpFormat.Gp3 => ".gp3", LegacyGpFormat.Gp4 => ".gp4", _ => ".gp5" };

    /// <summary>The version the file declares, as the writers of that era did: 3.00, 4.00 and 5.10.</summary>
    public static int Version(LegacyGpFormat format) => format switch { LegacyGpFormat.Gp3 => 300, LegacyGpFormat.Gp4 => 400, _ => 510 };

    /// <summary>The name in the Save as file-type list and the export dialog ("older programs": the format is for programs that cannot open the current one).</summary>
    public static string Label(LegacyGpFormat format) => $"{Extension(format)} file (older programs)";

    /// <summary>The file filter of a save dialog for this format: "label (*.gp5)|*.gp5".</summary>
    public static string DialogFilter(LegacyGpFormat format) => $"{Label(format)} (*{Extension(format)})|*{Extension(format)}";

    public static bool TryFromPath(string? path, out LegacyGpFormat format)
    {
        format = LegacyGpFormat.Gp5;
        var ext = System.IO.Path.GetExtension(path ?? "");
        foreach (var candidate in All)
            if (string.Equals(ext, Extension(candidate), StringComparison.OrdinalIgnoreCase)) { format = candidate; return true; }
        return false;
    }
}
