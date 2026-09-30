namespace TabForge.Services;

/// <summary>D2: the one list of file types TabForge opens (importer, Explorer association, single-instance hand-off, diagnostics). installer/TabForge.iss repeats it; a self-test compares the two.</summary>
public static class FileTypes
{
    public const string Project = ".tforge";
    public static readonly string[] GuitarPro = { ".gp3", ".gp4", ".gp5", ".gpx", ".gp" };
    public static readonly string[] AllOpenable = { ".gp", ".gp5", ".gp4", ".gp3", ".gpx", ".tforge" };

    public static bool IsOpenable(string extension) => Array.Exists(AllOpenable, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
    public static bool IsGuitarPro(string extension) => Array.Exists(GuitarPro, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
}
