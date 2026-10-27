using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia;

// Owns: the Note menu rows that ask for the cursor beat's chord name or text and the grace note editor row, run on the shown song.
// Does not own: the chord and text write rules (Core ScoreEditCommands.SetCursorChordName and SetCursorBeatText), the grace editor (OrnamentEditorFlow), the prompt window (Views.TextPromptWindow).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (every row is handled).
public static class MainWindowNoteDialogs
{
    /// <summary>The command ids this class registers (the menu test runs each, so the list cannot drift from the handlers).</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Note.Chord", "Note.Text", "Note.GraceEditor" };

    private const string ChordTitle = "Chord name";
    private const string ChordLabel = "Chord name (e.g. Am, G7):";
    private const string TextTitle = "Beat text";
    private const string TextLabel = "Beat text:";

    /// <param name="openGrace">Opens the grace note editor on the shown song (the editor the trill, grace and harmonic palette buttons open).</param>
    public static void Register(MainWindowCommands commands, ScorePreviewView score, Window owner, Action openGrace)
    {
        commands.Add("Note.Chord", () => _ = AskChordName(score, owner));
        commands.Add("Note.Text", () => _ = AskBeatText(score, owner));
        commands.Add("Note.GraceEditor", openGrace);
    }

    private static async Task AskChordName(ScorePreviewView score, Window owner)
    {
        var edits = score.Editor.Effects;
        if (!edits.HasCursorBeat) return;
        var chord = await DialogLauncher.Ask<TextPromptWindow, string?>(owner, () => new TextPromptWindow(ChordTitle, ChordLabel, edits.CursorChordName ?? ""));
        if (chord is not null) score.Edit(editor => editor.Effects.SetCursorChordName(chord));
    }

    private static async Task AskBeatText(ScorePreviewView score, Window owner)
    {
        var edits = score.Editor.Effects;
        if (!edits.HasCursorBeat) return;
        var text = await DialogLauncher.Ask<TextPromptWindow, string?>(owner, () => new TextPromptWindow(TextTitle, TextLabel, edits.CursorBeatText ?? ""));
        if (text is not null) score.Edit(editor => editor.Effects.SetCursorBeatText(text));
    }
}
