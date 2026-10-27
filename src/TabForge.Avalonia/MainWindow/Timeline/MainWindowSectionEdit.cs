using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Documents;
using TabForge.ScoreEditing;

namespace TabForge.Avalonia;

// Owns: the Section.Edit command (Shift+Insert, the Section menu row): asks for the name of the section at the cursor's bar and sets it on
// that bar of every track as one undo step.
// Does not own: the menu row (MainMenuTable in Core), the commands table (MainWindowCommands), the measure update (MeasureProperties in Core),
//   the section lane's own rename (TimelineMenuController).
// Tests: TabForge.Avalonia.Tests.MainWindowSectionEditTests.
/// <summary>The Section menu's Edit command, bound to the shown song and its cursor bar.</summary>
public static class MainWindowSectionEdit
{
    private const string PromptTitle = "Section";
    private const string PromptLabel = "Section name:";

    /// <param name="songs">The tabs; the command acts on the shown song.</param>
    /// <param name="score">The score whose cursor bar is edited.</param>
    /// <param name="owner">The window the name prompt opens over.</param>
    /// <summary>The Section menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Section.Edit" };

    public static void Register(MainWindowCommands commands, MainWindowTabs songs, ScorePreviewView score, Window owner) =>
        commands.Add("Section.Edit", () => _ = EditSectionAsync(songs.Active, score.Editor.SelectedMeasure, owner));

    private static async Task EditSectionAsync(IEditableDocument document, int bar, Window owner)
    {
        var current = SectionNameAt(document, bar);
        if (current is null) return;
        var name = await DialogLauncher.Ask<TextPromptWindow, string?>(owner, () => new TextPromptWindow(PromptTitle, PromptLabel, current));
        if (name is not null) SetSectionName(document, bar, name);
    }

    /// <summary>The section name of <paramref name="bar"/> in the selected track; null when the bar is outside the song.</summary>
    public static string? SectionNameAt(IEditableDocument document, int bar)
    {
        var tracks = document.Project.Tracks;
        var index = MainWindowTrackMenu.SelectedIndex(document);
        if (index < 0 || index >= tracks.Count) return null;
        var measures = tracks[index].Measures;
        return bar >= 0 && bar < measures.Count ? measures[bar].SectionName : null;
    }

    /// <summary>Sets the section name of <paramref name="bar"/> in every track as one undo step; false when nothing changed.</summary>
    public static bool SetSectionName(IEditableDocument document, int bar, string name) =>
        DocumentEdits.Run(document, project => MeasureProperties.UpdateAtBar(project, bar, measure => measure.SectionName = name), label: "Rename section").Changed;
}
