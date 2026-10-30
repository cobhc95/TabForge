using System.IO;
using TabForge.Models;
using TabForge.Presets;

namespace TabForge.Services;

// Owns: the score templates one user store saves and lists.
// Does not own: creating a new score from them (DocumentController) or the app-wide facade (UserTemplates).
// Tests: ScopedServiceTests.

/// <summary>
/// One user template folder. The app uses the process-wide <see cref="UserTemplates"/> facade; a test or an isolated host may
/// point its own store at another folder. Templates live as .tforge files and appear in File > New from template after the built-ins.
/// </summary>
public sealed class UserTemplatesStore
{
    /// <summary>Tests point this at a scratch folder.</summary>
    public string? FolderOverride { get; set; }

    public string Folder => FolderOverride ?? UserPaths.Templates;

    public IReadOnlyList<string> List()
    {
        try
        {
            if (!Directory.Exists(Folder)) return Array.Empty<string>();
            return Directory.EnumerateFiles(Folder, "*" + UserTemplates.Extension)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .Select(n => n!)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Ui, "user templates: list: " + ex.Message); return Array.Empty<string>(); }
    }

    /// <summary>Saves a copy of the project as a template (the open score keeps its own dirty state). Returns the stored name.</summary>
    public string Save(string name, SongProject project)
    {
        var clean = UserTemplates.CleanName(name) ?? throw new ArgumentException("Give the template a name.", nameof(name));
        var copy = ProjectService.Restore(ProjectService.Snapshot(project));
        UserTemplates.StripToSetup(copy);
        copy.Title = clean;
        Directory.CreateDirectory(Folder);
        ProjectService.Save(Path.Combine(Folder, clean + UserTemplates.Extension), copy);
        return clean;
    }

    /// <summary>A built-in template by name, else a user template file; always an untitled, unsaved project.</summary>
    public SongProject Create(string name)
    {
        if (UserTemplates.BuiltInNames.Contains(name, StringComparer.Ordinal)) return TemplateFactory.Create(name);
        var clean = UserTemplates.CleanName(name) ?? throw new ArgumentException("Unknown template.", nameof(name));
        var project = ProjectService.Load(Path.Combine(Folder, clean + UserTemplates.Extension));
        UserTemplates.StripToSetup(project); // older templates were saved with the whole song in them
        project.IsDirty = true;
        return project;
    }
}
