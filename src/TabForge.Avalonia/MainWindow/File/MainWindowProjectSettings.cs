using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the File.ProjectSettings command (the File menu's Project settings row and the menu-row button): opens the project settings dialog
// for the shown song and, on OK, applies the edited credits, tempo, time signature, key, notes and lyrics as one undo step.
// Does not own: the menu rows (MainMenuTable in Core), the commands table (MainWindowCommands), the dialog and its checks (ProjectSettingsWindow,
//   ProjectSettingsPresenter), the bar signature rules (BarSignatures in Core).
// Tests: TabForge.Avalonia.Tests.MainWindowProjectSettingsTests.
/// <summary>The File menu's Project settings command, bound to the shown song.</summary>
public static class MainWindowProjectSettings
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "File.ProjectSettings" };

    /// <param name="songs">The tabs; the command acts on the shown song.</param>
    /// <param name="owner">The window the dialog opens over.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, Window owner) =>
        commands.Add("File.ProjectSettings", () => _ = EditAsync(songs.Active, owner));

    private static async Task EditAsync(IEditableDocument document, Window owner)
    {
        var project = document.Project;
        var bars = project.Tracks.Count == 0 ? 0 : project.Tracks.Max(track => track.Measures.Count);
        var edited = await DialogLauncher.Ask<ProjectSettingsWindow, ProjectSettingsResult?>(owner,
            () => new ProjectSettingsWindow(ResultOf(project), project.Tracks.Count, bars, project.Markers.Count));
        if (edited is not null) Apply(document, edited);
    }

    /// <summary>The song's current settings as the dialog's starting values.</summary>
    public static ProjectSettingsResult ResultOf(SongProject p) => new(
        p.Title, p.Subtitle, p.Artist, p.Album, p.MusicAuthor, p.LyricsAuthor, p.TabAuthor, p.Copyright,
        p.Tempo, p.TimeSignatureNumerator, p.TimeSignatureDenominator, p.KeySignature, p.KeySignatureMinor, p.GrayInactiveVoice,
        p.Instructions, p.Notice, p.Lyrics);

    /// <summary>Writes the dialog's result to the song as one undo step; false when nothing changed.</summary>
    public static bool Apply(IEditableDocument document, ProjectSettingsResult edited) =>
        DocumentEdits.Run(document, p =>
        {
            var changed =
                !string.Equals(p.Title, edited.Title, StringComparison.Ordinal) ||
                !string.Equals(p.Subtitle, edited.Subtitle, StringComparison.Ordinal) ||
                !string.Equals(p.Artist, edited.Artist, StringComparison.Ordinal) ||
                !string.Equals(p.Album, edited.Album, StringComparison.Ordinal) ||
                !string.Equals(p.MusicAuthor, edited.MusicAuthor, StringComparison.Ordinal) ||
                !string.Equals(p.LyricsAuthor, edited.LyricsAuthor, StringComparison.Ordinal) ||
                !string.Equals(p.TabAuthor, edited.TabAuthor, StringComparison.Ordinal) ||
                !string.Equals(p.Copyright, edited.Copyright, StringComparison.Ordinal) ||
                !string.Equals(p.Instructions, edited.Instructions, StringComparison.Ordinal) ||
                !string.Equals(p.Notice, edited.Notice, StringComparison.Ordinal) ||
                !string.Equals(p.Lyrics, edited.Lyrics, StringComparison.Ordinal) ||
                p.GrayInactiveVoice != edited.GrayInactiveVoice ||
                p.Tempo != edited.Tempo ||
                edited.TimeSigNum != p.TimeSignatureNumerator || edited.TimeSigDenom != p.TimeSignatureDenominator ||
                edited.KeySignature != p.KeySignature || edited.KeyMinor != p.KeySignatureMinor;
            if (!changed) return false;
            p.Title = edited.Title; p.Subtitle = edited.Subtitle; p.Artist = edited.Artist; p.Album = edited.Album;
            p.MusicAuthor = edited.MusicAuthor; p.LyricsAuthor = edited.LyricsAuthor;
            p.TabAuthor = edited.TabAuthor; p.Copyright = edited.Copyright;
            p.Instructions = edited.Instructions; p.Notice = edited.Notice;
            p.Lyrics = edited.Lyrics;
            p.GrayInactiveVoice = edited.GrayInactiveVoice;
            p.Tempo = edited.Tempo;
            if (edited.TimeSigNum != p.TimeSignatureNumerator || edited.TimeSigDenom != p.TimeSignatureDenominator)
                BarSignatures.SetSongTime(p, edited.TimeSigNum, edited.TimeSigDenom);
            if (edited.KeySignature != p.KeySignature || edited.KeyMinor != p.KeySignatureMinor)
                BarSignatures.SetSongKey(p, edited.KeySignature, edited.KeyMinor);
            return true;
        }, label: "Project settings").Changed;
}
