using System.IO;
using TabForge.Models;
using TabForge.Presets;

namespace TabForge.Services;

// Owns: the score templates the user saves and lists.
// Does not own: creating a new score from them (DocumentController).
// Tests: TestTemplateKeepsSetupOnly, TestUserTemplatesAndFaultedChain.
/// <summary>
/// Score templates saved by the user (File > Save as template). They live as .tforge files in
/// %APPDATA%\TabForge\Templates and appear in File > New from template after the built-in ones.
/// </summary>
public static class UserTemplates
{
    public const string Extension = ".tforge";

    /// <summary>Tests point this at a scratch folder.</summary>
    public static string? FolderOverride { get; set; }

    public static string Folder => FolderOverride ?? UserPaths.Templates;

    public static readonly IReadOnlyList<string> BuiltInNames = new[] { "Blank", "Rock Band", "Modern Metal", "Acoustic Song" };

    /// <summary>A safe file-name stem for a user-typed template name, or null when nothing usable is left.</summary>
    public static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim(' ', '.');
        if (cleaned.Length == 0) return null;
        if (cleaned.Length > 60) cleaned = cleaned[..60].TrimEnd(' ', '.');
        if (BuiltInNames.Contains(cleaned, StringComparer.OrdinalIgnoreCase)) cleaned += " (custom)";
        return cleaned;
    }

    public static IReadOnlyList<string> List()
    {
        try
        {
            if (!Directory.Exists(Folder)) return Array.Empty<string>();
            return Directory.EnumerateFiles(Folder, "*" + Extension)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .Select(n => n!)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Ui, "user templates: list: " + ex.Message); return Array.Empty<string>(); }
    }

    /// <summary>Saves a copy of the project as a template (the open score keeps its own dirty state). Returns the stored name.</summary>
    public static string Save(string name, SongProject project)
    {
        var clean = CleanName(name) ?? throw new ArgumentException("Give the template a name.", nameof(name));
        var copy = ProjectService.Restore(ProjectService.Snapshot(project));
        StripToSetup(copy);
        copy.Title = clean;
        Directory.CreateDirectory(Folder);
        ProjectService.Save(Path.Combine(Folder, clean + Extension), copy);
        return clean;
    }

    /// <summary>Number of empty bars a template (and a new song) starts with.</summary>
    public const int DefaultBars = 32;

    /// <summary>
    /// Reduces a project to its setup: tracks (names, tunings, instruments, capo, mixer levels and FX chain), tempo, time and key
    /// signature. Notes, lyrics, sections / markers, per-bar changes (tempo, time, key, repeats) and recorded audio are removed
    /// and every track gets <see cref="DefaultBars"/> empty bars.
    /// </summary>
    public static void StripToSetup(SongProject project)
    {
        var first = project.MasterBarTrack is { Measures.Count: > 0 } master ? master.Measures[0] : null;
        if (first?.TimeSigNum is { } num && first.TimeSigDenom is { } denom)
        {
            project.TimeSignatureNumerator = num;
            project.TimeSignatureDenominator = denom;
        }
        if (first?.KeySignature is { } key) project.KeySignature = key;
        if (first?.TempoChange is { } tempo && tempo > 0) project.Tempo = tempo;
        project.Lyrics = "";
        project.Markers = new List<MarkerModel>();
        foreach (var track in project.Tracks)
        {
            track.Measures = TemplateFactory.Measures(DefaultBars);
            track.AudioClips = new List<AudioClip>();
            track.Lanes = new List<ClipLane>();
            track.RecordArm = false;
        }
    }

    /// <summary>A built-in template by name, else a user template file; always an untitled, unsaved project.</summary>
    public static SongProject Create(string name)
    {
        if (BuiltInNames.Contains(name, StringComparer.Ordinal)) return TemplateFactory.Create(name);
        var clean = CleanName(name) ?? throw new ArgumentException("Unknown template.", nameof(name));
        var project = ProjectService.Load(Path.Combine(Folder, clean + Extension));
        StripToSetup(project); // older templates were saved with the whole song in them
        project.IsDirty = true;
        return project;
    }
}
