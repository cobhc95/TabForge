using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: answering the paste questions with a window that is asynchronous while the Core paste asks synchronously: a first pass records the questions
//   and cancels (a cancelled paste changes nothing), the window is shown for them, a second pass replays the answers.
// Does not own: which questions apply or what the answers do (PasteCommands, PasteQuestionInfo) or the window (Views.PasteOptionsWindow).
// Tests: TabForge.Avalonia.Tests.MainWindowClipboardTests.
internal static class PasteQuestionFlow
{
    /// <summary>
    /// Runs <paramref name="paste"/> with a recording asker, then (when it raised questions) shows <paramref name="ask"/> and runs it again with the answers.
    /// Returns whether the song changed; false when the user cancelled.
    /// </summary>
    internal static async Task<bool> RunAsync(Func<IPasteQuestionAsker, PasteOutcome?> paste, Func<IReadOnlyCollection<PasteQuestion>, Task<PasteAnswers?>> ask)
    {
        var probe = new Recorder();
        var first = paste(probe);
        if (probe.Questions is null) return first?.Changed == true;
        if (await ask(probe.Questions) is not { } answers) return false;
        return paste(new Fixed(answers))?.Changed == true;
    }

    private sealed class Recorder : IPasteQuestionAsker
    {
        public IReadOnlyCollection<PasteQuestion>? Questions { get; private set; }
        public PasteAnswers? Ask(IReadOnlyCollection<PasteQuestion> questions) { Questions = questions; return null; }
    }

    private sealed class Fixed : IPasteQuestionAsker
    {
        private readonly PasteAnswers _answers;
        public Fixed(PasteAnswers answers) => _answers = answers;
        public PasteAnswers? Ask(IReadOnlyCollection<PasteQuestion> questions) => _answers;
    }
}
