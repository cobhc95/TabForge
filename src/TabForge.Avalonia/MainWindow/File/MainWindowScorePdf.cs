using TabForge.Avalonia.Views;
using System;
using System.IO;
using Avalonia.Controls;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: File > Export PDF: asks for a .pdf file, writes the shown song's score there (ScorePdfExport) and reports the page count, or the failure.
// Does not own: the menu row (Core MainMenuTable, "File.ExportPdf"), the score drawing and the PDF bytes (ScorePdfExport, ScorePdfWriter), the
//   file dialog (IFileDialogs) and the song tabs (MainWindowTabs).
// Tests: TabForge.Avalonia.Tests.ScorePdfExportTests.
public static class MainWindowScorePdf
{
    /// <summary>The command id of File > Export PDF.</summary>
    public const string CommandId = "File.ExportPdf";

    /// <param name="score">The score view whose track the PDF prints.</param>
    /// <param name="tabs">The song tabs; the active tab is the song written.</param>
    /// <param name="dialogs">Asks where the PDF is written.</param>
    /// <param name="owner">The window the save dialog belongs to.</param>
    /// <param name="settings">The shared settings whose Score rows the page takes; null keeps the defaults.</param>
    /// <param name="report">Shows the outcome or the failure on the status line.</param>
    public static void Register(MainWindowCommands commands, ScorePreviewView score, MainWindowTabs tabs, IFileDialogs dialogs, Window owner,
        MainWindowSettings? settings, Action<string> report) =>
        commands.Add(CommandId, () => Export(score, tabs, dialogs, owner, settings, report));

    private static void Export(ScorePreviewView score, MainWindowTabs tabs, IFileDialogs dialogs, Window owner, MainWindowSettings? settings, Action<string> report)
    {
        var document = tabs.Active;
        var path = dialogs.SaveFile(new FileDialogOptions
        {
            Key = TabForge.Services.DialogKeys.ExportPdf, Title = "Export PDF", Filter = "PDF (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true,
            FileName = SafeFileNames.SafeFileName(document.Project.Title, "Untitled"),
        }, owner);
        if (path is null) return;
        try
        {
            var pages = ScorePdfExport.Write(document, settings?.Settings ?? SettingsDefaults.Fallback(), score.Probe.TrackIndex, path);
            report.Render($"Exported PDF {Path.GetFileName(path)} ({pages} page{(pages == 1 ? "" : "s")})");
        }
        catch (Exception ex)
        {
            report.Render("PDF export failed: " + ex.Message);
        }
    }
}
