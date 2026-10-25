using System;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Autosave;

// Owns: writing one recovery copy of a song: the project's raw .tforge JSON with no validation gate (an invalid song is what recovery exists for),
//     the validation error beside it as "<name>.errors.txt", and the song's unsaved flag left as it was.
// Does not own: which songs are copied and when (AutosaveRunner, the window's autosave controller), the file names (AutosaveService).
// Tests: MainWindowAutosaveTests.
/// <summary>The one writer of recovery copies: the bytes a normal save would write, gzip-wrapped, written atomically.</summary>
public static class RecoveryCopyWriter
{
    /// <summary>
    /// Writes <paramref name="project"/> to <paramref name="path"/> WITHOUT validation: an invalid model is exactly what recovery exists for. Validation
    /// still runs, and its error is written beside the copy as "&lt;name&gt;.errors.txt" (null: the model is valid). The project's unsaved-changes flag
    /// is left as it was (a recovery copy must not make closing skip the save prompt). Returns the validation error text, or null when the copy is valid.
    /// </summary>
    public static string? Write(SongProject project, string path)
    {
        ArgumentNullException.ThrowIfNull(project);
        var wasDirty = project.IsDirty;
        string? errors = null;
        try
        {
            var toWrite = project;
            try { if (project.Tracks?.Any(t => t?.StartupTemplateId is not null) == true) toWrite = project.WithoutStartupTracks(); }
            catch (Exception ex) { Trace.Error(Trace.Ui, "startup: leave out startup tracks: " + ex.Message); errors = $"Startup tracks could not be left out ({ex.GetType().Name}): {ex.Message}"; }
            try { ProjectValidator.Validate(toWrite); }
            catch (Exception ex) { Trace.Error(Trace.Ui, "recovery: validate copy: " + ex.Message); errors = (errors is null ? "" : errors + Environment.NewLine) + $"{ex.GetType().Name}: {ex.Message}"; }
            project.IsDirty = false;   // the file on disk records a saved project, as a normal save would
            // PersistBytes is the disk JSON (every property, FormatVersion included), gzip-wrapped: the same format a .tforge save writes,
            // so it is written as is (ProjectService.Load detects the gzip header).
            var persisted = ProjectService.PersistBytes(toWrite, InputLimits.MaxTforgeFileBytes);   // the same bound the recovery reader applies
            FilePathPolicy.WriteAtomically(path, stream => stream.Write(persisted), createDirectory: true);
        }
        finally { project.IsDirty = wasDirty; }
        if (errors is not null) WriteErrorList(path, errors);
        return errors;
    }

    /// <summary>The validation error beside the copy; a failure to write it is traced, the copy itself stays.</summary>
    private static void WriteErrorList(string path, string errors)
    {
        var sidecar = Path.ChangeExtension(path, ".errors.txt");
        var text = System.Text.Encoding.UTF8.GetBytes(
            $"This recovery copy was written without validation. It failed validation with:{Environment.NewLine}{errors}{Environment.NewLine}");
        try { FilePathPolicy.WriteAtomically(sidecar, s => s.Write(text)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.Error(Trace.Ui, "recovery: error list: " + ex.Message); }
    }
}
