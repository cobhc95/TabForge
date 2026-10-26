using System.Collections.Generic;
using System;
using System.IO;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: File > Save, Save As and Export compatible .gp file of the Avalonia window: the path (the song's own .tforge / .gp, else the save dialog) and the write through
//   DocumentSaveFlow; a failure is reported and leaves the song unsaved. The .gp export asks the lossy-export preflight question first (when
//   GpExportPreflight finds losses) and hands the answer to DocumentSaveFlow.ExportGuitarProAsync; the song itself is never changed by an export.
// Does not own: the media copy offered before a write (MediaSaveStep), the file format and the save sequence (DocumentSaveFlow, DocumentController), the plug-in states (SongPluginStates), the dialog (IFileDialogs), the tabs (MainWindowTabs).
// Tests: TabForge.Avalonia.Tests.MainWindowSaveTests.
/// <summary>The window's save commands. UI thread only.</summary>
public sealed class MainWindowSaving
{
    /// <summary>The menu command ids this class handles (registered in MainWindow).</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "File.ExportGuitarPro" };

    /// <summary>Completes when no save or export is running: closing a window or tab waits on it.</summary>
    public Task WhenIdle() => _flow.WhenIdle();

    private readonly DocumentSaveFlow _flow = new(new DocumentController());
    private readonly IFileDialogs _dialogs;
    private readonly object? _owner;
    private readonly Action<string> _report;
    private readonly SongPluginStates _pluginStates;
    private readonly Func<string> _defaultSaveFormat;
    private readonly Func<SavePrompt, Task<PromptButton>> _ask;
    private readonly Func<DegradedSaveGate> _degraded;
    private readonly MediaSaveStep? _media;

    /// <param name="dialogs">Asks where to save.</param>
    /// <param name="owner">The window the dialog belongs to.</param>
    /// <param name="report">Shows the outcome or the failure to the person.</param>
    /// <param name="pluginStates">Reads the song's plug-in states from the engine before a write, so the file holds the settings the plug-ins have now.</param>
    /// <param name="defaultSaveFormat">The Default save format setting: "tforge", or anything else for .gp (the Save As dialog starts on it).</param>
    /// <param name="ask">Shows a question with buttons (the degraded-mode overwrite question) and returns the button the person pressed.</param>
    /// <param name="degraded">The degraded mode of the process (read at each save: it is set by an unexpected error at any time).</param>
    /// <param name="media">The step run before each write that offers to copy audio from outside the song folder into it; null (tests) does nothing.</param>
    public MainWindowSaving(IFileDialogs dialogs, object? owner, Action<string> report, SongPluginStates pluginStates, Func<string> defaultSaveFormat,
        Func<SavePrompt, Task<PromptButton>> ask, Func<DegradedSaveGate> degraded, MediaSaveStep? media = null)
    {
        _media = media;
        _dialogs = dialogs;
        _owner = owner;
        _report = report;
        _pluginStates = pluginStates;
        _defaultSaveFormat = defaultSaveFormat;
        _ask = ask;
        _degraded = degraded;
    }

    /// <summary>
    /// Saves to the song's own file, or asks where when it has none it can write; true when the song was written and is clean. After an unexpected
    /// error an existing file is replaced only after the person chooses to overwrite it (the other choices: save as a new file, or cancel).
    /// </summary>
    public Task<bool> Save(SongDocument document) => _flow.RunClaimedAsync(hold => SaveHeld(document, hold), _report);

    // The claim is taken before the first question (the degraded-mode overwrite question and the file dialog included).
    private async Task<bool> SaveHeld(SongDocument document, DocumentController.SaveHold hold)
    {
        if (!DocumentSaveFlow.CanSaveInPlace(document.Path)) return await SaveAsHeld(document, hold);
        var path = document.Path!;
        if (_degraded().MustConfirmOverwrite(path) && File.Exists(path))
        {
            switch (SavePromptPresenter.DegradedChoice(await _ask(SavePromptPresenter.DegradedOverwrite(Path.GetFileName(path)))))
            {
                case DegradedSaveChoice.DoNotSave: return false;
                case DegradedSaveChoice.SaveAsNewFile: return await SaveAsHeld(document, hold);
            }
        }
        return await SaveTo(document, path, hold);
    }

    /// <summary>
    /// Asks for a new file (.gp or .tforge; the Default save format decides which one the dialog starts on) and saves there; false when the person
    /// cancelled or writing failed.
    /// </summary>
    public Task<bool> SaveAs(SongDocument document) => _flow.RunClaimedAsync(hold => SaveAsHeld(document, hold), _report);

    // The claim is taken before the file dialog, so a close or an exit-save waits for the whole sequence, the dialog included.
    private Task<bool> SaveAsHeld(SongDocument document, DocumentController.SaveHold hold)
    {
        const string gpFilter = ".gp file (*.gp) - keeps every TabForge feature|*.gp";
        const string tforgeFilter = "TabForge project (*.tforge)|*.tforge";
        var gpDefault = !string.Equals(_defaultSaveFormat(), "tforge", StringComparison.OrdinalIgnoreCase);
        var path = _dialogs.SaveFile(new FileDialogOptions
        {
            Title = "Save song", Filter = gpDefault ? $"{gpFilter}|{tforgeFilter}" : $"{tforgeFilter}|{gpFilter}",
            DefaultExt = gpDefault ? ".gp" : ".tforge", AddExtension = true,
            FileName = SafeFileNames.SafeFileName(Path.GetFileNameWithoutExtension(document.LabelPath) is { Length: > 0 } name ? name : document.Project.Title, "Untitled"),
        }, _owner);
        return path is null ? Task.FromResult(false) : SaveTo(document, path, hold);
    }

    /// <summary>
    /// Asks for a .gp path, asks <paramref name="askPreflight"/> (report, file name) when the song holds what a clean .gp cannot, and exports.
    /// True when a file was written; a cancel or a failure writes nothing and is reported.
    /// </summary>
    public Task<bool> ExportGuitarPro(SongDocument document, Func<GpPreflightReport, string, Task<GpExportChoice>> askPreflight) =>
        _flow.RunClaimedAsync(hold => ExportGuitarProHeld(document, askPreflight, hold), _report);

    private async Task<bool> ExportGuitarProHeld(SongDocument document, Func<GpPreflightReport, string, Task<GpExportChoice>> askPreflight, DocumentController.SaveHold hold)
    {
        var path = _dialogs.SaveFile(new FileDialogOptions
        {
            Title = "Export compatible .gp file", Filter = ".gp file (*.gp)|*.gp", DefaultExt = ".gp", AddExtension = true,
            FileName = SafeFileNames.SafeFileName(Path.GetFileNameWithoutExtension(document.LabelPath) is { Length: > 0 } name ? name : document.Project.Title, "Untitled"),
        }, _owner);
        if (path is null) return false;
        try
        {
            var outcome = await _flow.ExportGuitarProAsync(document, path, report => askPreflight(report, Path.GetFileName(path)), _pluginStates.Collect, hold);
            if (outcome.Message.Length > 0) _report(outcome.Message);
            return outcome.Wrote;
        }
        catch (Exception ex)
        {
            // Reported, not swallowed: DocumentController writes the pair atomically, so a failure leaves the old files as they were.
            _report(".gp export failed: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> SaveTo(SongDocument document, string path, DocumentController.SaveHold hold)
    {
        try
        {
            // Audio outside the song folder: offered a copy first (a closed question cancels the save); the paths are written relative by the save itself.
            if (_media is not null && !await _media.BeforeSave(document, path)) return false;
            var outcome = await _flow.SaveAsync(document, path, new TforgeOnlyAnswers(), _pluginStates.Collect, hold);
            if (outcome.Message.Length > 0) _report(outcome.Message);
            if (outcome.Saved) _media?.AfterSave(document);
            // A file the person chose to write while degraded is not asked about again; a failed write confirms nothing.
            if (outcome.Saved) _degraded().ConfirmOverwrite(path);
            return outcome.Saved;
        }
        catch (Exception ex)
        {
            // Reported, not swallowed: DocumentSaveFlow leaves the old file as it was and the song unsaved.
            _report("Save failed: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// The questions a plain save asks. A .gp save keeps the whole project embedded (nothing is lost, so no question is needed); the other
    /// questions cancel, so nothing is written.
    /// </summary>
    private sealed class TforgeOnlyAnswers : ISaveInteractions
    {
        public AudioDataSaveChoice? AskAudioDataChoice(string fileName) => AudioDataSaveChoice.GpWithEmbeddedProject;
        public GpExportChoice AskGpPreflight(GpPreflightReport report, GpExportKind kind, string fileName) => GpExportChoice.Cancel;
        public ReplaceFileChoice AskReplaceFullCopy(string fileName) => ReplaceFileChoice.Cancel;
    }
}
