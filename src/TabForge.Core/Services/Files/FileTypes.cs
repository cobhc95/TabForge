namespace TabForge.Services;

// Owns: the list of file types TabForge opens and saves and their extensions.
// Does not own: reading or writing those files.
// Tests: no dedicated test class.
/// <summary>D2: the one list of file types TabForge opens (importer, Explorer association, single-instance hand-off, diagnostics). installer/TabForge.iss repeats it; a self-test compares the two.</summary>
public static class FileTypes
{
    public const string Project = ".tforge";
    /// <summary>TuxGuitar's own old format: named by the open path so it gets its own message, never opened and never in a dialog filter.</summary>
    public const string TuxGuitarGtp = ".gtp";
    public static readonly string[] GuitarPro = { ".gp3", ".gp4", ".gp5", ".gpx", ".gp" };
    public static readonly string[] AllOpenable = { ".gp", ".gp5", ".gp4", ".gp3", ".gpx", ".tforge" };

    /// <summary>MusicXML scores; read by alphaTab like Guitar Pro. Not in <see cref="AllOpenable"/>: those are the Explorer associations and TabForge must not claim every .xml.</summary>
    public static readonly string[] MusicXml = { ".musicxml", ".mxl", ".xml" };
    /// <summary>Standard MIDI files, read by SmfReader. Not in <see cref="AllOpenable"/> either: no Explorer association is added for them.</summary>
    public static readonly string[] Midi = { ".mid", ".midi" };
    /// <summary>Everything the import worker reads (Guitar Pro, MusicXML and MIDI).</summary>
    public static readonly string[] Importable = GuitarPro.Concat(MusicXml).Concat(Midi).ToArray();
    /// <summary>Everything File &gt; Open, drag-drop and the command line accept.</summary>
    public static readonly string[] OpenInApp = AllOpenable.Concat(MusicXml).Concat(Midi).ToArray();

    public static bool IsOpenable(string extension) => Array.Exists(OpenInApp, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
    public static bool IsMusicXml(string extension) => Array.Exists(MusicXml, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
    public static bool IsMidi(string extension) => Array.Exists(Midi, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
    public static bool IsGuitarPro(string extension) => Array.Exists(GuitarPro, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
    public static bool IsTuxGuitarGtp(string extension) => TuxGuitarGtp.Equals(extension, StringComparison.OrdinalIgnoreCase);
}
