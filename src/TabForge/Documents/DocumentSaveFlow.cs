using System.IO;
using System.Runtime.CompilerServices;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Documents;

/// <summary>How a song that holds plug-ins / FX / mixer groups is saved as .gp (Guitar Pro does not know those settings).</summary>
public enum AudioDataSaveChoice { GpWithEmbeddedProject, GpPlusDataFile, TForgeFile }

/// <summary>The questions a save may have to ask; the window answers them with dialogs, a test answers them directly. Both receive only what they need to ask.</summary>
public interface ISaveInteractions
{
    /// <summary>The song has audio data and goes to a .gp file: which way to save it. Null: the person cancelled.</summary>
    AudioDataSaveChoice? AskAudioDataChoice(string fileName);

    /// <summary>A clean .gp cannot hold everything the song uses (<paramref name="report"/> says what): continue, keep a full native copy, or cancel.</summary>
    GpExportChoice AskGpPreflight(GpPreflightReport report, GpExportKind kind, string fileName);
}

/// <summary>What a save or export did and what the person should be told. Nothing here touches a window.</summary>
/// <param name="Saved">The song was written and is clean (a save only).</param>
/// <param name="Cancelled">The person cancelled a question: nothing was written.</param>
/// <param name="Message">The status text.</param>
public sealed record SaveOutcome(bool Saved, bool Cancelled, string Message);

/// <summary>
/// The save and export sequences for one explicit document: which questions are asked (and only when something would be lost), the plug-in states
/// are collected asynchronously, and only then is the file written as a snapshot of the model. Everything it needs is an argument (the document, the
/// path, the lyrics as they were when the command started, the state collector, the questions): it never reads "the displayed document", so a save
/// started for one tab is a save of that tab whatever tab is shown when its awaits complete. Dialogs, input gating and status text stay at the UI
/// boundary.
/// </summary>
public sealed class DocumentSaveFlow
{
    private readonly DocumentController _controller;

    /// <summary>How a song with audio data is saved as .gp, chosen once per song.</summary>
    private readonly ConditionalWeakTable<SongProject, object> _audioChoice = new();

    public DocumentSaveFlow(DocumentController controller) => _controller = controller;

    /// <summary>Only .tforge and .gp can be written in place; a song opened from .gp3 / .gp4 / .gp5 / .gpx asks where to save.</summary>
    public static bool CanSaveInPlace(string? path) =>
        !string.IsNullOrWhiteSpace(path) && (path.EndsWith(".tforge", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Saves <paramref name="document"/> to <paramref name="path"/>. Throws when writing fails (the document then keeps its unsaved state and its
    /// previous file stays as it was); the caller reports it. Returns at once with <c>Message = "Already saving…"</c> when a save is running.
    /// </summary>
    /// <param name="lyrics">The lyrics to write, taken when the command started.</param>
    /// <param name="collectStates">Reads the plug-in states of this document's tracks from the engine (awaited; the caller's thread is never blocked).</param>
    /// <param name="progress">Status updates while waiting (optional).</param>
    public async Task<SaveOutcome> SaveAsync(DocumentSession document, string path, string? lyrics, ISaveInteractions ask,
        Func<DocumentSession, Task<StateCollection>> collectStates, Action<string>? progress = null)
    {
        if (_controller.IsSaving) return new SaveOutcome(false, false, "Already saving…");
        var project = document.Project;
        var asGp = path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase);

        // Songs with plug-ins / FX / mixer groups: Guitar Pro does not know these settings, so ask how to save (before any waiting).
        AudioDataSaveChoice? choice = null;
        if (asGp && MixerGroups.HasAudioData(project))
        {
            if (_audioChoice.TryGetValue(project, out var remembered)) choice = (AudioDataSaveChoice)remembered;
            else
            {
                choice = ask.AskAudioDataChoice(Path.GetFileName(path));
                if (choice is null) return new SaveOutcome(false, true, "");
                _audioChoice.AddOrUpdate(project, choice.Value);
            }
        }
        // A clean .gp cannot hold everything: ask before writing, only when the song actually uses something it cannot (never for a harmless save).
        GpExportChoice? cleanChoice = null;
        if (choice == AudioDataSaveChoice.GpPlusDataFile)
        {
            var report = GpExportPreflight.Analyze(project);
            if (report.ShouldAsk)
            {
                var picked = ask.AskGpPreflight(report, GpExportKind.Save, Path.GetFileName(path));
                if (picked == GpExportChoice.Cancel) return new SaveOutcome(false, true, "Save cancelled: nothing was written");
                cleanChoice = picked;
            }
        }

        progress?.Invoke("Saving… (reading plug-in states)");
        // Plug-ins keep their settings in the engine: copy them into the song first. Any state that could not be read is reported and the song
        // stays unsaved (never a silent "Saved" with stale plug-in settings).
        var wrote = false;
        var message = "";
        await _controller.SaveAsync(() => collectStates(document), capture =>
        {
            if (choice == AudioDataSaveChoice.TForgeFile)
            {
                var tforge = Path.ChangeExtension(path, ".tforge");
                _controller.Save(document, tforge, lyrics);
                message = $"Saved {Path.GetFileName(tforge)}";
            }
            else if (choice == AudioDataSaveChoice.GpPlusDataFile && cleanChoice is { } cleanPick)
            {
                var result = _controller.ExportCleanGuitarPro(document, path, GpExportKind.Save, cleanPick, lyrics);
                message = DescribeGpExport(result, path);
                if (result.NativeCopyPath is null && string.Equals(result.CompatiblePath, path, StringComparison.OrdinalIgnoreCase)) message += $" and {Path.GetFileName(AudioDataFile.PathFor(path))}";
                wrote = result.CompatiblePath is not null;
            }
            else if (choice == AudioDataSaveChoice.GpPlusDataFile)
            {
                _controller.SaveCleanGuitarProWithAudioData(document, path, lyrics);
                message = $"Saved {Path.GetFileName(path)} and {Path.GetFileName(AudioDataFile.PathFor(path))}";
            }
            else
            {
                _controller.Save(document, path, lyrics);
                message = $"Saved {Path.GetFileName(path)}";
            }
            wrote |= cleanChoice is null;
            if (capture.Warning is { } warning) { document.MarkIncomplete(); message = $"{message}, but {warning}"; }
        });
        return new SaveOutcome(wrote && !document.HasUnsavedChanges, false, message);
    }

    /// <summary>
    /// File > Export compatible Guitar Pro file: a copy for other programs. Never changes the song's own file or unsaved state. Throws when writing
    /// fails; the caller reports it.
    /// </summary>
    public async Task<SaveOutcome> ExportGuitarProAsync(DocumentSession document, string path, string? lyrics, ISaveInteractions ask,
        Func<DocumentSession, Task<StateCollection>> collectStates, Action<string>? progress = null)
    {
        if (_controller.IsSaving) return new SaveOutcome(false, false, "Already saving…");
        var project = document.Project;
        var report = GpExportPreflight.Analyze(project);
        var choice = report.ShouldAsk ? ask.AskGpPreflight(report, GpExportKind.Export, Path.GetFileName(path)) : GpExportChoice.ExportCompatible;
        if (choice == GpExportChoice.Cancel) return new SaveOutcome(false, true, "Export cancelled: nothing was written");
        DocumentController.GuitarProExportResult? result = null;
        string? warning = null;
        if (choice == GpExportChoice.KeepNativeCopy)
        {
            // The full copy must hold the plug-ins' current settings: read them from the engine first, as Save does.
            progress?.Invoke("Exporting… (reading plug-in states)");
            await _controller.SaveAsync(() => collectStates(document), capture =>
            {
                warning = capture.Warning;
                result = _controller.ExportCleanGuitarPro(document, path, GpExportKind.Export, choice, lyrics);
            });
        }
        else result = _controller.ExportCleanGuitarPro(document, path, GpExportKind.Export, choice, lyrics);
        if (result is null) return new SaveOutcome(false, false, "Export did not run");
        var text = DescribeGpExport(result, path).Replace("Saved ", "Exported ");
        return new SaveOutcome(false, false, warning is { } w ? $"{text}, but {w}" : text);
    }

    /// <summary>Status text for a clean .gp write: the files that were written, including a "(compatible)" sibling used instead of the requested name.</summary>
    public static string DescribeGpExport(DocumentController.GuitarProExportResult result, string requestedPath)
    {
        if (result.CompatiblePath is null) return "Nothing was written";
        var text = $"Saved {Path.GetFileName(result.CompatiblePath)}";
        if (!string.Equals(result.CompatiblePath, requestedPath, StringComparison.OrdinalIgnoreCase)) text += $" (compatible copy; {Path.GetFileName(requestedPath)} was left as it was)";
        if (result.NativeCopyPath is { } native) text += $" and the full copy {Path.GetFileName(native)}";
        return text;
    }
}
