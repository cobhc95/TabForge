using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the score pane's switch between the score and the piano roll: a tab track opens in the score and a MIDI track in the piano roll; the two
//   menu rows switch this track only, each as one undo step that marks the song changed (so it is saved); Undo switches back; another track and
//   another song follow their own choice; Select all and the Edit verbs go to the piano roll only while it has focus.
// Does not own: the view rules themselves (PianoRollSourceTests) or the piano roll's editing (PianoRollViewTests).
// Tests: EditorPaneSwitch.
public sealed class PianoRollPaneSwitchTests
{
    private static (EditorPaneSwitch Switch, ScrollViewer Score, MainWindowTabs Tabs, MainWindowCommands Commands, Window Window) Build(SongDocument first)
    {
        var tabs = new MainWindowTabs(first, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var score = new ScorePreviewView();
        var panes = new MainWindowDocumentPanes(tabs, score);
        var scroll = new ScrollViewer { Content = score };
        var pane = new EditorPaneSwitch(scroll, score, tabs, panes, null, null, () => null, _ => { });
        var window = new Window { Width = 900, Height = 600, Content = pane.Host };
        window.Show();
        var commands = new MainWindowCommands(window, () => { });
        pane.Register(commands);
        return (pane, scroll, tabs, commands, window);
    }

    [AvaloniaFact]
    public void TheMenuRowsSwitchThisTrackAsOneSavedUndoStepAndUndoSwitchesBack()
    {
        var document = PianoRollSongs.Tab();
        document.MarkClean();
        var (pane, score, _, commands, window) = Build(document);
        Assert.False(pane.ShowsPianoRoll);
        Assert.True(score.IsVisible);
        var steps = document.Undo.UndoCount;

        commands.Run(ScoreMenus.ShowInPianoRollId);

        Assert.True(pane.ShowsPianoRoll);
        Assert.False(score.IsVisible);
        Assert.True(pane.PianoRoll.IsVisible);
        Assert.Equal(TrackEditorView.PianoRoll, document.Project.Tracks[0].EditorView);
        Assert.Equal(steps + 1, document.Undo.UndoCount);
        Assert.True(document.Project.IsDirty);
        Assert.True(pane.PianoRoll.Presenter.IsTab);

        commands.Run(PianoRollMenus.ShowInScoreId);
        Assert.False(pane.ShowsPianoRoll);
        Assert.Equal(TrackEditorView.Score, document.Project.Tracks[0].EditorView);

        DocumentEdits.Undo(document);
        Assert.True(pane.ShowsPianoRoll);
        window.Close();
    }

    [AvaloniaFact]
    public void AMidiTrackOpensInThePianoRollAndEachTrackKeepsItsOwnView()
    {
        var document = PianoRollSongs.Midi();
        document.Project.Tracks.Add(PianoRollSongs.Tab().Project.Tracks[0]);
        var (pane, _, tabs, _, window) = Build(document);
        Assert.True(pane.ShowsPianoRoll);
        Assert.False(pane.PianoRoll.Presenter.IsTab);
        Assert.Equal(2, pane.PianoRoll.Presenter.Notes.Count);

        document.TrackIndex = 1;   // the tab track: the score
        Assert.False(pane.ShowsPianoRoll);
        document.TrackIndex = 0;
        Assert.True(pane.ShowsPianoRoll);

        tabs.Open(PianoRollSongs.Tab());   // another song follows its own track's view
        Assert.False(pane.ShowsPianoRoll);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePillsSwitchTheModeAndTheInstrumentPanelFollowsTheMode()
    {
        var document = PianoRollSongs.Tab();
        var (pane, _, _, _, window) = Build(document);
        var modes = new System.Collections.Generic.List<bool>();
        pane.ModeChanged = modes.Add;
        Assert.True(pane.Pills.IsEnabled);
        Assert.False(pane.Pills.PianoShown);
        Assert.DoesNotContain(pane.Pills, pane.Host.Children);   // they live in the menu row, not over the panes

        pane.Pills.Click(1);   // the Piano pill
        Assert.True(pane.ShowsPianoRoll);
        Assert.True(pane.Pills.PianoShown);
        pane.Pills.Click(93);   // the Tab pill
        Assert.False(pane.ShowsPianoRoll);
        Assert.Equal(new[] { true, false }, modes);   // hide the instrument panel, then show it again
        Assert.Equal(TrackEditorView.Score, document.Project.Tracks[0].EditorView);
        window.Close();
    }

    [AvaloniaFact]
    public void SelectingATrackThatChangesThePaneTellsTheWindowSoThePanelFollowsThePane()
    {
        var document = PianoRollSongs.Midi();
        document.Project.Tracks.Add(PianoRollSongs.Tab().Project.Tracks[0]);
        var (pane, _, _, _, window) = Build(document);
        var modes = new System.Collections.Generic.List<bool>();
        pane.ModeChanged = modes.Add;
        document.TrackIndex = 1;   // roll -> score by selection
        document.TrackIndex = 0;   // and back
        Assert.True(pane.ShowsPianoRoll);
        Assert.Equal(new[] { false, true }, modes);   // the window's InstrumentPanelGate restores or holds the panel on every change, never leaving it hidden
        window.Close();
    }

    [AvaloniaFact]
    public void ThePillsFollowTheSelectedTrackAndAreDisabledWithoutOne()
    {
        var document = PianoRollSongs.Midi();
        document.Project.Tracks.Add(PianoRollSongs.Tab().Project.Tracks[0]);
        var (pane, _, _, _, window) = Build(document);
        Assert.True(pane.Pills.PianoShown);
        document.TrackIndex = 1;
        Assert.False(pane.Pills.PianoShown);
        Assert.True(pane.Pills.IsEnabled);
        document.TrackIndex = -1;
        Assert.False(pane.Pills.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void EditVerbsGoToThePianoRollOnlyWhileItHasFocus()
    {
        var document = PianoRollSongs.Midi();
        var (pane, _, _, _, window) = Build(document);
        Assert.False(pane.RunEdit(EditVerb.Copy));   // no focus: the window's routing runs
        pane.PianoRoll.Surface.Focus();
        pane.PianoRoll.Presenter.Select(new[] { 0 });
        Assert.True(pane.RunEdit(EditVerb.Copy));
        Assert.Single(pane.PianoRoll.Presenter.Clipboard);
        window.Close();
    }
}
