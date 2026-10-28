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

    /// <summary>The song's full TabForge copy would replace a file that already exists (<paramref name="fileName"/>, the .tforge beside the chosen .gp).</summary>
    ReplaceFileChoice AskReplaceFullCopy(string fileName);
}

/// <summary>The answer to "that file already exists": overwrite it, write beside it under a numbered name, or write nothing.</summary>
public enum ReplaceFileChoice { Replace, KeepBoth, Cancel }

/// <summary>The wording of the questions the save flow asks that no dialog class owns.</summary>
public static class SaveFlowText
{
    /// <summary>{0} is the existing file's name.</summary>
    public const string ReplaceFullCopy = "{0} already exists. Replace it with this song's full TabForge copy?";
    public const string ReplaceButton = "Replace";
    public const string KeepBothButton = "Keep both";
    /// <summary>A save or export was asked for while another one is still running (its dialogs included).</summary>
    public const string AlreadySaving = "Already saving…";
}

/// <summary>What a save or export did and what the person should be told. Nothing here touches a window.</summary>
/// <param name="Saved">The song was written and is clean (a save only).</param>
/// <param name="Cancelled">The person cancelled a question: nothing was written.</param>
/// <param name="Message">The status text.</param>
/// <param name="Wrote">An export wrote its file (a save reports this through <paramref name="Saved"/>).</param>
public sealed record SaveOutcome(bool Saved, bool Cancelled, string Message, bool Wrote = false);

// Owns: the save and export sequences of one explicit document: which questions are asked, plug-in state collection and the
//     write.
// Does not own: the file formats (ProjectService, exporters) and the dialogs.
// Tests: TestSaveFlowCharacterization, TabForge.Avalonia.Tests.MainWindowSaveTests.
/// <summary>
/// The save and export sequences for one explicit document: which questions are asked (and only when something would be lost), the plug-in states
/// are collected asynchronously, and only then is the file written as a snapshot of the model. Everything it needs is an argument (the document, the
/// path, the state collector, the questions): it never reads "the displayed document", so a save
/// started for one tab is a save of that tab whatever tab is shown when its awaits complete. Dialogs, input gating and status text stay at the UI
/// boundary.
/// </summary>
public sealed class DocumentSaveFlow
{
    private readonly DocumentController _controller;

    /// <summary>How a song with audio data is saved as .gp, chosen once per song.</summary>
    private readonly ConditionalWeakTable<SongProject, object> _audioChoice = new();

    public DocumentSaveFlow(DocumentController controller) => _controller = controller;

    /// <summary>Completes when no save or export is running (see <see cref="DocumentController.WhenIdle"/>).</summary>
    public Task WhenIdle() => _controller.WhenIdle();

    /// <summary>Only .tforge and .gp can be written in place; a song opened from .gp3 / .gp4 / .gp5 / .gpx asks where to save.</summary>
    public static bool CanSaveInPlace(string? path) =>
        !string.IsNullOrWhiteSpace(path) && (path.EndsWith(".tforge", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Runs <paramref name="body"/> under a save claim taken now (the window takes it before its first dialog); when a save already runs the body does
    /// not run, <paramref name="busy"/> is told why (<see cref="SaveFlowText.AlreadySaving"/>) and the result is false.
    /// </summary>
    public async Task<bool> RunClaimedAsync(Func<DocumentController.SaveHold, Task<bool>> body, Action<string>? busy = null)
    {
        using var hold = _controller.TryBeginSave();
        if (hold is null) { busy?.Invoke(SaveFlowText.AlreadySaving); return false; }
        return await body(hold);
    }

    /// <summary>
    /// Saves <paramref name="document"/> to <paramref name="path"/>. Throws when writing fails (the document then keeps its unsaved state and its
    /// previous file stays as it was); the caller reports it. Runs under <paramref name="hold"/>, the claim the caller took with <see cref="DocumentController.TryBeginSave"/> before its first question.
    /// </summary>
    /// <typeparam name="TDocument">The caller's document type, so its state collector receives the type it reads from.</typeparam>
    /// <param name="collectStates">Reads the plug-in states of this document's tracks from the engine (awaited; the caller's thread is never blocked).</param>
    /// <param name="progress">Status updates while waiting (optional).</param>
    public async Task<SaveOutcome> SaveAsync<TDocument>(TDocument document, string path, ISaveInteractions ask,
        Func<TDocument, Task<StateCollection>> collectStates, DocumentController.SaveHold hold, Action<string>? progress = null) where TDocument : SongDocument
    {
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

        // The full TabForge copy is a second file beside the .gp: never silently over one that is already there.
        var tforgeTarget = choice == AudioDataSaveChoice.TForgeFile ? Path.ChangeExtension(path, ".tforge") : null;
        if (tforgeTarget is not null && File.Exists(tforgeTarget))
        {
            switch (ask.AskReplaceFullCopy(Path.GetFileName(tforgeTarget)))
            {
                case ReplaceFileChoice.Cancel: return new SaveOutcome(false, true, "Save cancelled: nothing was written");
                case ReplaceFileChoice.KeepBoth: tforgeTarget = NumberedSibling(tforgeTarget); break;
            }
        }

        progress?.Invoke("Saving… (reading plug-in states)");
        // Plug-ins keep their settings in the engine: copy them into the song first. Any state that could not be read is reported and the song
        // stays unsaved (never a silent "Saved" with stale plug-in settings).
        var wrote = false;
        var cancelled = false;
        var message = "";
        await _controller.SaveAsync(() => collectStates(document), async capture =>
        {
            if (tforgeTarget is not null)
            {
                await _controller.SaveToFileAsync(document, tforgeTarget);
                message = $"Saved {Path.GetFileName(tforgeTarget)}";
                wrote = true;
            }
            else if (choice == AudioDataSaveChoice.GpPlusDataFile)
            {
                // One capture, taken now that the states are in the song, serves the analysis, the question (only when the song uses something a clean .gp cannot hold) and the write.
                // The question runs inside the export and continues on the UI context (no ConfigureAwait(false) anywhere on this path).
                var result = await _controller.ExportCleanGuitarProAsync(document, path, GpExportKind.Save,
                    report => Task.FromResult(ask.AskGpPreflight(report, GpExportKind.Save, Path.GetFileName(path))));
                if (result.Cancelled) { cancelled = true; return; }
                message = DescribeGpExport(result, path);
                if (result.NativeCopyPath is null && string.Equals(result.CompatiblePath, path, StringComparison.OrdinalIgnoreCase)) message += $" and {Path.GetFileName(AudioDataFile.PathFor(path))}";
                wrote = result.CompatiblePath is not null;
            }
            else
            {
                await _controller.SaveToFileAsync(document, path);
                message = $"Saved {Path.GetFileName(path)}";
                wrote = true;
            }
            if (capture.Warning is { } warning) { document.MarkIncomplete(); message = $"{message}, but {warning}"; }
        }, hold);
        if (cancelled) return new SaveOutcome(false, true, "Save cancelled: nothing was written");
        return new SaveOutcome(wrote && !document.IsDirtyHint, false, message);   // the hint, not a content hash: an edit made while the file was written must not freeze the UI
    }

    internal const int MaxNumberedSiblings = 1000;

    /// <summary>"name (2).tforge", "name (3).tforge" ...: the first path that does not exist.</summary>
    public static string NumberedSibling(string path) => NumberedSibling(path, MaxNumberedSiblings);

    /// <summary>
    /// Overload with an explicit ceiling (additive): batches the existence probe into a single directory
    /// listing instead of up to <paramref name="maxSiblings"/> <c>File.Exists</c> sweeps. Existing callers unchanged.
    /// </summary>
    public static string NumberedSibling(string path, int maxSiblings)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
        // One listing covers both files and directories; falls back to per-candidate stats when it cannot be read.
        HashSet<string>? existing = null;
        try
        {
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                existing = new HashSet<string>(
                    Directory.EnumerateFileSystemEntries(folder).Select(e => Path.GetFileName(e)!),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { existing = null; }
        bool Exists(string candidate) => existing is not null
            ? existing.Contains(Path.GetFileName(candidate))
            : File.Exists(candidate) || Directory.Exists(candidate);
        var candidate = path;
        // Ceiling: 1000 numbered names; past it the folder is treated as broken and the save fails loudly instead of looping.
        for (var i = 2; Exists(candidate); i++)
        {
            if (i > maxSiblings) throw new IOException($"More than {maxSiblings} numbered copies of {Path.GetFileName(path)} exist");
            candidate = Path.Combine(folder, $"{stem} ({i}){ext}");
        }
        return candidate;
    }

    /// <summary>
    /// File > Export compatible Guitar Pro file: a copy for other programs. Never changes the song's own file or unsaved state. Throws when writing
    /// fails; the caller reports it. Runs under <paramref name="hold"/>, the claim taken before the file dialog; <paramref name="choose"/> is asked only when the song holds what a clean .gp cannot (it continues on the UI context).
    /// </summary>
    public async Task<SaveOutcome> ExportGuitarProAsync<TDocument>(TDocument document, string path, Func<GpPreflightReport, Task<GpExportChoice>> choose,
        Func<TDocument, Task<StateCollection>> collectStates, DocumentController.SaveHold hold, Action<string>? progress = null) where TDocument : SongDocument
    {
        DocumentController.GuitarProExportResult? result = null;
        string? warning = null;
        // The plug-ins' current settings are read first (the full copy must hold them), then the song is captured once for the analysis, the question and the write.
        progress?.Invoke("Exporting… (reading plug-in states)");
        await _controller.SaveAsync(() => collectStates(document), async capture =>
        {
            result = await _controller.ExportCleanGuitarProAsync(document, path, GpExportKind.Export, choose);
            if (result.NativeCopyPath is not null) warning = capture.Warning;
        }, hold);
        if (result is null) return new SaveOutcome(false, false, "Export did not run");
        if (result.Cancelled) return new SaveOutcome(false, true, "Export cancelled: nothing was written");
        var text = DescribeGpExport(result, path).Replace("Saved ", "Exported ");
        return new SaveOutcome(false, false, warning is { } w ? $"{text}, but {w}" : text, result.CompatiblePath is not null);
    }

    /// <summary>
    /// Save as .gp5 / .gp4 / .gp3 or File > Export: a copy for older programs. Never changes the song's own file or unsaved state. <paramref name="confirm"/> is asked
    /// (with what the format loses) only when something is lost; false cancels and nothing is written. Throws when writing fails; the caller reports it.
    /// </summary>
    public async Task<SaveOutcome> ExportLegacyAsync(SongDocument document, string path, LegacyGpFormat format, Func<GpPreflightReport, Task<bool>> confirm, DocumentController.SaveHold hold)
    {
        var report = await _controller.ExportLegacyCopyAsync(document, path, format, confirm, hold);
        if (report is null) return new SaveOutcome(false, true, "Export cancelled: nothing was written");
        return new SaveOutcome(false, false, $"Exported {Path.GetFileName(path)} (a copy for older programs; the song stays as it was)", Wrote: true);
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
