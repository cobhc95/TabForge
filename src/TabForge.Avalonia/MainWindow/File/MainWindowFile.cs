using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: File > Open / Open in new tab (the open dialog, then DocumentController.Open for .tforge and Guitar Pro files, each result a new tab),
//   File > Export MIDI, File > Export ASCII tab and File > Export MusicXML (the save dialog, then the Core exporter of that format), File > New from template (the template picker, then UserTemplates.Create as an
//   untitled tab) and File > Save as template (the name prompt, then UserTemplates.Save) of the Avalonia window; a failure is reported and opens nothing.
// Does not own: reading the files (DocumentController, GuitarProImporter, ProjectService), writing MIDI (MidiExportService), the template list and
//   files (UserTemplates), the dialogs (IFileDialogs, TemplatePickerWindow, TextPromptWindow), the tabs (MainWindowTabs), saving (MainWindowSaving).
//   There is no recent-files list in the settings to open from.
// Tests: TabForge.Avalonia.Tests.MainWindowDocumentsTests, TabForge.Avalonia.Tests.MainWindowMenuTests.EveryMenuTableCommandIsHandledOrDisabled.
/// <summary>The window's open, export and template commands. UI thread only.</summary>
public sealed class MainWindowFile
{
    private const string DefaultTemplateName = "My template";
    private const string SaveTemplateTitle = "Save as template";
    private const string SaveTemplateLabel = "Template name";

    /// <summary>The ids of the commands this class handles (File > New from template, File > Save as template, File > Export ASCII tab and File > Export MusicXML).</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "File.NewFromTemplate", "File.SaveAsTemplate", "File.ExportAscii", "File.ExportMusicXml" };

    private readonly ScoreOpenFlow _scores = new(new DocumentController());
    private readonly IFileDialogs _dialogs;
    private readonly object? _owner;
    private readonly MainWindowTabs _tabs;
    private readonly Action<string> _report;

    /// <param name="dialogs">Asks which files to open and where to export.</param>
    /// <param name="owner">The window the dialogs belong to.</param>
    /// <param name="report">Shows the outcome or the failure to the person.</param>
    public MainWindowFile(IFileDialogs dialogs, object? owner, MainWindowTabs tabs, Action<string> report)
    {
        _dialogs = dialogs;
        _owner = owner;
        _tabs = tabs;
        _report = report;
    }

    /// <summary>Asks for score files and opens each in order: in a new tab, or in place of the shown tab when the open-in-current-tab setting is on
    /// and <paramref name="inNewTab"/> is not set.</summary>
    public async Task Open(bool inNewTab = false)
    {
        var chosen = _dialogs.OpenFiles(new FileDialogOptions
        {
            Title = "Open TabForge or .gp file",
            Filter = "Supported scores|*.tforge;*.gp3;*.gp4;*.gp5;*.gpx;*.gp;*.musicxml;*.mxl;*.xml;*.mid;*.midi|TabForge project (*.tforge)|*.tforge|GP files (*.gp3;*.gp4;*.gp5;*.gpx;*.gp)|*.gp3;*.gp4;*.gp5;*.gpx;*.gp|MusicXML (*.musicxml;*.mxl;*.xml)|*.musicxml;*.mxl;*.xml|MIDI (*.mid;*.midi)|*.mid;*.midi",
            Multiselect = true,
        }, _owner);
        if (chosen is null) return;
        foreach (var path in chosen) await OpenPath(path, !inNewTab && _tabs.OpenReplacesActive);
    }

    /// <summary>Opens one file in a new tab, or in place of the shown tab when <paramref name="replaceActive"/> is set; false when it could not be
    /// read (the reason is reported) or the person kept the shown song.</summary>
    public async Task<bool> OpenPath(string path, bool replaceActive = false)
    {
        if (FileTypes.IsTuxGuitarGtp(Path.GetExtension(path))) { _report.Error(GuitarProImporter.GtpNotSupportedMessage); return false; }
        try
        {
            if (FilePathBoundary.RefusalReason(path) is { } refusal)
            {
                _report.Error($"Could not open {Path.GetFileName(path)}: {refusal}.");
                return false;
            }
            var opened = await _scores.OpenAsync(path);
            var song = new SongDocument(() => null) { Project = opened.Project, Path = opened.SessionPath, ImportSourcePath = opened.SessionPath is null ? opened.SourcePath : null };
            song.IsNew = opened.SessionPath is null;
            if (opened.SourcePath is not null) song.Media.SetSourceDirectory(Path.GetDirectoryName(opened.SourcePath));   // an imported song's relative media resolves beside the imported file
            song.MarkClean();
            if (replaceActive) { if (!await _tabs.ReplaceActive(song)) return false; }
            else _tabs.Open(song);
            // The tab that appears is the confirmation; only a notice (a damaged file, merged duplicates) is worth a status line, and never the song's name.
            if (opened.Notice is { Length: > 0 } notice) _report((opened.ImportedFromGuitarPro ? "Imported: " : "Opened: ") + notice);
            return true;
        }
        catch (Exception ex)
        {
            // Reported, not swallowed: a bad file, a limit or a path rule leaves the open tabs as they were.
            _report.Error($"Could not open {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Asks for a .mid file and writes the shown song there.</summary>
    public Task ExportMidi()
    {
        var project = _tabs.Active.Project;
        return ExportTo(project, ExportDialog("Export MIDI", "MIDI file (*.mid)|*.mid", ".mid", project.Title), "MIDI export failed", MidiExportService.Export);
    }

    /// <summary>File > Export ASCII tab: asks for a .txt file and writes the shown song's tablature there.</summary>
    public Task ExportAscii()
    {
        var project = _tabs.Active.Project;
        return ExportTo(project, ExportDialog("Export ASCII tab", "Text (*.txt)|*.txt", ".txt", project.Title), "ASCII export failed", AsciiExportService.Export);
    }

    /// <summary>File > Export MusicXML: asks for a .musicxml file and writes the shown song there.</summary>
    public Task ExportMusicXml()
    {
        var project = _tabs.Active.Project;
        return ExportTo(project, ExportDialog("Export MusicXML", "MusicXML (*.musicxml;*.xml)|*.musicxml;*.xml", ".musicxml", project.Title), "MusicXML export failed", MusicXmlExportService.Export);
    }

    private static FileDialogOptions ExportDialog(string title, string filter, string extension, string songTitle) =>
        new() { Title = title, Filter = filter, DefaultExt = extension, AddExtension = true, FileName = SafeFileNames.SafeFileName(songTitle, "Untitled") };

    /// <summary>
    /// Shows the save dialog; when a path is chosen, copies the song here (the copy is cheap; edits made meanwhile cannot reach it), runs the
    /// write on a worker and reports the outcome or the failure back on this thread.
    /// </summary>
    private async Task ExportTo(TabForge.Models.SongProject project, FileDialogOptions options, string failurePrefix, Action<TabForge.Models.SongProject, string> write)
    {
        var path = _dialogs.SaveFile(options, _owner);
        if (path is null) return;
        try
        {
            var copy = TabForge.Playback.SongSnapshot.Take(project, new ProjectStateEncoder(ProjectStateEncoder.PlaybackParallelEncodeBars));
            await Task.Run(() => write(copy.Song, path));
            _report.Render("Exported " + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _report.Render(failurePrefix + ": " + ex.Message);
        }
    }

    /// <summary>File > New from template: asks for a starting point and opens it as an untitled, unsaved tab after the shown one.</summary>
    public async Task NewFromTemplate()
    {
        var names = UserTemplates.BuiltInNames.Concat(UserTemplates.List()).ToList();
        var name = await DialogLauncher.Ask<TemplatePickerWindow, string?>(OwnerWindow(), () => new TemplatePickerWindow(names));
        if (name is null) return;
        SongDocument song;
        try
        {
            song = new SongDocument(() => null) { Project = UserTemplates.Create(name), IsNew = true };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            _report.Error($"Could not open the template \"{name}\": {ex.Message}");
            return;
        }
        _tabs.Open(song);
        _report("Created template: " + name);
    }

    /// <summary>File > Save as template: asks for a name and stores a copy of the shown song's setup in the templates folder.</summary>
    public async Task SaveAsTemplate()
    {
        var project = _tabs.Active.Project;
        var suggested = string.IsNullOrWhiteSpace(project.Title) ? DefaultTemplateName : project.Title;
        var typed = await DialogLauncher.Ask<TextPromptWindow, string?>(OwnerWindow(), () => new TextPromptWindow(SaveTemplateTitle, SaveTemplateLabel, suggested));
        if (UserTemplates.CleanName(typed) is null) return;
        try
        {
            var stored = UserTemplates.Save(typed!, project);
            _report($"Saved template \"{stored}\" (File > New from template)");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            _report.Error("Could not save the template: " + ex.Message);
        }
    }

    private Window OwnerWindow() => _owner as Window ?? throw new InvalidOperationException("The template dialogs need the window that owns them.");
}
