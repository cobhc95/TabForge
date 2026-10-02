using System.IO;

namespace TabForge.Services;

// Owns: the opt-in debug trace switch and its output to the diagnostics folder.
// Does not own: the traced code and the logging of errors.
// Tests: TestTraceSwitchAreas.
/// <summary>
/// One opt-in debug switch: <c>TABFORGE_TRACE=area,area</c> (areas: playback, engine, layout, import, ui, or all).
/// Traces go to the user diagnostics folder (%LOCALAPPDATA%\TabForge\Diagnostics\trace-&lt;area&gt;.log), bounded in size.
/// The older one-off hooks keep working as aliases: TABFORGE_MIDI_LOG (playback), TABFORGE_LAYOUT_LOG (layout) and
/// TABFORGE_CAPTION_LOG (ui) still write where they always did; the TF_* probe variables are unchanged.
/// Read once at start-up; when the variable is unset nothing is traced and nothing is written.
/// </summary>
internal static class Trace
{
    public const string Variable = "TABFORGE_TRACE";
    public const string Playback = "playback";
    public const string Engine = "engine";
    public const string Layout = "layout";
    public const string Import = "import";
    public const string Ui = "ui";

    private static readonly string[] KnownAreas = { Playback, Engine, Layout, Import, Ui };
    private static readonly HashSet<string> Enabled = ParseAreas(Environment.GetEnvironmentVariable(Variable));

    /// <summary>The areas named in a TABFORGE_TRACE value (comma/semicolon/space separated, case-insensitive; "all" = every area).</summary>
    internal static HashSet<string> ParseAreas(string? value)
    {
        var areas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value)) return areas;
        foreach (var part in value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("all", StringComparison.OrdinalIgnoreCase)) areas.UnionWith(KnownAreas);
            else if (KnownAreas.Contains(part, StringComparer.OrdinalIgnoreCase)) areas.Add(part.ToLowerInvariant());
        }
        return areas;
    }

    /// <summary>True when TABFORGE_TRACE names this area.</summary>
    public static bool IsOn(string area) => Enabled.Contains(area);

    /// <summary>The trace file for an area (or a named capture inside it) in the user diagnostics folder.</summary>
    public static string PathFor(string area) => FilePathPolicy.DefaultDiagnosticsPath($"trace-{area}.log");

    /// <summary>Appends one time-stamped line to the area's trace file when the area is on. Never throws.</summary>
    public static void Write(string area, string line)
    {
        if (!IsOn(area)) return;
        try { DiagnosticFileService.AppendCappedLine(PathFor(area), $"{DateTime.Now:HH:mm:ss.fff} {line}", InputLimits.MaxLayoutLogBytes); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            System.Diagnostics.Debug.WriteLine($"{Variable} write failed: {ex.Message}");
        }
    }
}
