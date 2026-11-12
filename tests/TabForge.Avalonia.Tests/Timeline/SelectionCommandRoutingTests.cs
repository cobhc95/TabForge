using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Commands;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: who answers the window's Select all and Invert selection: the piano roll while it has keyboard focus (its notes), else the score (Select all)
//   or nobody (Invert); and that the loop button's status line goes to the notice sink the selection link was registered with.
// Does not own: what the piano roll or the score select (PianoRollSelectTests, ScoreSelectAll) or the loop rules (SelectionLoopLinkTests).
// Tests: SelectionLoopLink, EditorPaneSwitch.
public sealed class SelectionCommandRoutingTests
{
    private static (EditorPaneSwitch Pane, MainWindowDocumentPanes Panes, MainWindowCommands Commands, List<string> Notices, Window Window) Build(SongDocument document)
    {
        var tabs = new MainWindowTabs(document, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var score = new ScorePreviewView();
        var notices = new List<string>();
        var panes = new MainWindowDocumentPanes(tabs, score, selection: new SelectionLoopLink(tabs, score, null, notice: notices.Add));
        var pane = new EditorPaneSwitch(new ScrollViewer { Content = score }, score, tabs, panes, null, null, () => null, _ => { });
        var window = new Window { Width = 900, Height = 600, Content = pane.Host };
        window.Show();
        var commands = new MainWindowCommands(window, () => { });
        pane.Register(commands);
        panes.Selection.Register(commands, window, new ContextMenuHost());
        pane.RegisterSelectionCommands(commands);   // as the window composes it
        return (pane, panes, commands, notices, window);
    }

    [AvaloniaFact]
    public void SelectAllGoesToTheFocusedPianoRollElseToTheScore()
    {
        var (pane, panes, commands, _, window) = Build(PianoRollSongs.Midi());
        commands.Run("Edit.SelectAll");                       // no focus: the score selects
        Assert.Empty(pane.PianoRoll.Presenter.Selected);
        Assert.True(panes.Selection.ActiveScore.Editor.HasSelection);
        panes.Selection.ActiveScore.Editor.ClearSelection();

        pane.PianoRoll.Surface.Focus();
        commands.Run("Edit.SelectAll");                       // focus in the piano roll: its notes, not the score
        Assert.Equal(pane.PianoRoll.Presenter.Notes.Count, pane.PianoRoll.Presenter.Selected.Count);
        Assert.False(panes.Selection.ActiveScore.Editor.HasSelection);
        window.Close();
    }

    [AvaloniaFact]
    public void InvertGoesToTheFocusedPianoRollAndDoesNothingElsewhere()
    {
        var (pane, panes, commands, _, window) = Build(PianoRollSongs.Midi());
        pane.PianoRoll.Presenter.Select(new[] { 0 });
        commands.Run("Edit.InvertSelection");                 // no focus: nothing inverts
        Assert.Equal(new[] { 0 }, pane.PianoRoll.Presenter.Selected.ToArray());
        Assert.False(panes.Selection.ActiveScore.Editor.HasSelection);

        pane.PianoRoll.Surface.Focus();
        commands.Run("Edit.InvertSelection");
        Assert.Equal(Enumerable.Range(1, pane.PianoRoll.Presenter.Notes.Count - 1), pane.PianoRoll.Presenter.Selected.OrderBy(i => i));
        window.Close();
    }

    [AvaloniaFact]
    public void TheLoopButtonsStatusLineGoesToTheRegisteredNoticeSink()
    {
        var (_, _, commands, notices, window) = Build(PianoRollSongs.Midi());
        commands.Run(CommandIds.Transport.Loop);
        Assert.Single(notices);
        commands.Run(CommandIds.Transport.Loop);
        Assert.Equal(2, notices.Count);
        window.Close();
    }
}
