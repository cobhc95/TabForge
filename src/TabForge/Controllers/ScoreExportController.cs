using System.IO;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Controllers;

/// <summary>What <see cref="ScoreExportController"/> needs from its window: the song shown, the dialogs and the status line.</summary>
internal interface IScoreExportHost
{
    SongProject Project { get; }
    /// <summary>Asks where to write (null: cancelled). <paramref name="suggestedName"/> is a safe file name without extension.</summary>
    string? PickSavePath(string title, string filter, string defaultExt, string suggestedName);
    /// <summary>The template name the user typed (null: cancelled).</summary>
    string? PromptTemplateName(string suggested);
    /// <summary>Writes the PDF for the track shown; returns the page count.</summary>
    int WritePdf(string path);
    void ShowError(string title, string message);
    void SetStatus(string text);
}

// File > Export (MIDI, ASCII tab, MusicXML, PDF) and File > Save as template: a file dialog, the export service, one status line.
// Owns: the dialog sequence, the failure message and the status text of the simple one-shot exports and of storing a template.
// Does not own: saving the song (DocumentSaveFlow), the .gp export (it shares the save claim and states), render and video export windows.
// Tests: TestScoreExportController, TestMidiExport, TestAsciiExport, TestMusicXmlExport, TestEssentialExports, TestUserTemplatesAndFaultedChain.
internal sealed class ScoreExportController
{
    private readonly IScoreExportHost _host;

    public ScoreExportController(IScoreExportHost host) => _host = host;

    private static string SafeName(string text) => TabForge.Audio.Contracts.SafeFileNames.SafeFileName(text, "Untitled");

    private void Run(string title, string filter, string ext, string failedTitle, Func<string, string> export)
    {
        if (_host.PickSavePath(title, filter, ext, SafeName(_host.Project.Title)) is not { } path) return;
        try { _host.SetStatus(export(path)); }
        catch (Exception ex) { Services.Trace.Error(Services.Trace.Ui, "export: " + ex.Message); _host.ShowError(failedTitle, ex.Message); }
    }

    public void ExportMidi() => Run("Export MIDI", "MIDI (*.mid)|*.mid", ".mid", "MIDI export failed",
        path => { MidiExportService.Export(_host.Project, path); return $"Exported MIDI {Path.GetFileName(path)}"; });

    public void ExportAscii() => Run("Export ASCII tab", "Text (*.txt)|*.txt", ".txt", "ASCII export failed",
        path => { AsciiExportService.Export(_host.Project, path); return $"Exported ASCII {Path.GetFileName(path)}"; });

    public void ExportMusicXml() => Run("Export MusicXML", "MusicXML (*.musicxml;*.xml)|*.musicxml;*.xml", ".musicxml", "MusicXML export failed",
        path => { MusicXmlExportService.Export(_host.Project, path); return $"Exported MusicXML {Path.GetFileName(path)}"; });

    public void ExportPdf() => Run("Export PDF", "PDF (*.pdf)|*.pdf", ".pdf", "PDF export failed", path =>
    {
        var pages = _host.WritePdf(path);
        return $"Exported PDF {Path.GetFileName(path)} ({pages} page{(pages == 1 ? "" : "s")})";
    });

    /// <summary>File > Save as template: stores a copy of the song shown in the templates folder.</summary>
    public void SaveAsTemplate()
    {
        var project = _host.Project;
        var name = _host.PromptTemplateName(string.IsNullOrWhiteSpace(project.Title) ? "My template" : project.Title);
        if (UserTemplates.CleanName(name) is null) return;
        try
        {
            var stored = UserTemplates.Save(name!, project);
            _host.SetStatus($"Saved template \"{stored}\" (File > New from template)");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            Services.Trace.Error(Services.Trace.Ui, "save template: " + ex.Message);
            _host.ShowError("Save as template", $"Could not save the template.\n\n{ex.Message}");
        }
    }
}
