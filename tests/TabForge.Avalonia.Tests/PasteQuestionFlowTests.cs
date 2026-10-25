using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a paste that asks questions in Avalonia: the question window's answers give the same song as the Core paste called directly with the same answers
/// (the WPF path), and cancelling the questions changes nothing.
/// Tests: PasteQuestionFlow, MainWindowClipboard.PasteAsync.
/// </summary>
public class PasteQuestionFlowTests
{
    private sealed class Answering : IPasteQuestionAsker
    {
        private readonly PasteAnswers _answers;
        public Answering(PasteAnswers answers) => _answers = answers;
        public PasteAnswers? Ask(IReadOnlyCollection<PasteQuestion> questions) => _answers;
    }

    /// <summary>A song with notes on beats 0 and 2 of bar 0; the beats 0-2 are copied and beats 1-2 are selected, and a paste onto a selection asks.</summary>
    private static (ScorePreviewView View, MainWindowClipboard Flow, ScoreClip Clip) Make(System.Func<IReadOnlyCollection<PasteQuestion>, Task<PasteAnswers?>> ask)
    {
        var view = new ScorePreviewView();
        var track = view.Editor.Project!.Tracks[0];
        foreach (var (cell, fret) in new[] { (0, 3), (2, 5) }) track.Measures[0].CellsForVoice(0)[cell].Notes.Add(new Models.TabNote { StringIndex = 1, Fret = fret, MidiValue = track.PitchOf(1, fret) });
        var system = new AvaloniaScoreClipboard(() => null);
        var flow = new MainWindowClipboard(view, system, () => new EditingSettings(), _ => Task.FromResult<PasteSpecialOptions?>(null), ask);
        view.Editor.SelectRange(0, 0, 0, 2);
        Assert.True(flow.Copy());
        var clip = flow.Clipboard.Current!;
        view.Editor.SelectRange(0, 1, 0, 2);
        return (view, flow, clip);
    }

    private static string Shape(ScorePreviewView view) => string.Join("|", view.Editor.Project!.Tracks[0].Measures.Select(m =>
        string.Join(",", m.CellsForVoice(0).Select(c => string.Join("+", c.Notes.Select(n => n.Fret))))));

    [AvaloniaTheory]
    [InlineData(BeatsOntoNotesAnswer.Replace)]
    [InlineData(BeatsOntoNotesAnswer.Insert)]
    [InlineData(BeatsOntoNotesAnswer.ReplaceBar)]
    public async Task EachAnswerGivesTheSongTheCoreCallGives(BeatsOntoNotesAnswer answer)
    {
        var answers = new PasteAnswers(BeatsOntoNotes: answer);
        IReadOnlyCollection<PasteQuestion>? asked = null;
        var (view, flow, _) = Make(q => { asked = q; return Task.FromResult<PasteAnswers?>(answers); });
        Assert.True(await flow.PasteAsync());
        Assert.Contains(PasteQuestion.BeatsOntoNotes, asked!);

        var (direct, directFlow, clip) = Make(_ => Task.FromResult<PasteAnswers?>(null));
        direct.Edit(editor => editor.Paste((doc, target) => PasteCommands.RunPaste(doc, clip, target, new EditingSettings(), new Answering(answers))).Changed);
        Assert.Equal(Shape(direct), Shape(view));
        Assert.NotNull(directFlow);
    }

    [AvaloniaFact]
    public async Task CancellingTheQuestionsChangesNothing()
    {
        var (view, flow, _) = Make(_ => Task.FromResult<PasteAnswers?>(null));
        var before = Shape(view);
        Assert.False(await flow.PasteAsync());
        Assert.Equal(before, Shape(view));
    }
}
