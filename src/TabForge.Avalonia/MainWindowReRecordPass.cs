using System.Collections.Generic;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Models;
using TabForge.Documents;

namespace TabForge.Avalonia;

// Owns: the "Re-record this pass" clip command (a repeat take's context-menu row): it starts the one-pass punch,
//   which records only that part's track for one loop length and replaces that part on landing (one undo step in the presenter).
// Does not own: the punch start/auto-stop and landing (RecordingPresenter),
//   the menu row (TimelineMenus) or the take rules (TakeGroups).
// Tests: TabForge.Avalonia.Tests.RepeatTakeLandingTests.
public static class MainWindowReRecordPass
{
    /// <summary>The clip-menu command id of "Re-record this pass".</summary>
    public const string CommandId = "Clip.ReRecordPass";

    public static readonly IReadOnlyList<string> Commands = new[] { CommandId };

    /// <param name="commands">The window's command table.</param>
    /// <param name="songs">The tabs; the command acts on the shown song.</param>
    /// <param name="panes">The latest arrangement pane, whose timeline holds the clip selection.</param>
    /// <param name="session">The window's recording state; the punch replaces the selected pass through it.</param>
    internal static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, RecordingPresenter.RecordingSession session) =>
        MainWindowClipKeys.Add(commands, songs, panes, CommandId,
            target => _ = target.Document is SongDocument song && session.ReRecordPass(song, target.Selection.Track, target.Selection.Clip),
            () => !session.IsRecording && MainWindowClipKeys.TargetOf(songs, panes) is { } target && TakeGroups.IsGrouped(target.Selection.Clip));
}
