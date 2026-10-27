using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;

namespace TabForge.Avalonia;

// Owns: the Tools.ChordFinder command (the Tools menu's Chord finder row): opens the chord finder over the window, whose
//   Insert name writes the chord name to the beat under the score cursor in one undo step (no beat: the finder says so).
// Does not own: the menu row (MainMenuTable in Core), the commands table (MainWindowCommands), the finder and its choices (ChordFinderWindow, ChordFinderPresenter).
// Tests: TabForge.Avalonia.Tests.ChordFinderWindowTests.
/// <summary>The Tools menu's Chord finder command.</summary>
public static class MainWindowChordFinder
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Tools.ChordFinder" };

    /// <param name="commands">The window's command table that receives the handler.</param>
    /// <param name="owner">The window the dialog opens over.</param>
    /// <param name="score">The score Insert name writes to; null keeps the finder's no-beat report (the Compose call leaves it unset).</param>
    public static void Register(MainWindowCommands commands, Window owner, ScorePreviewView? score = null) =>
        commands.Add("Tools.ChordFinder", () => new ChordFinderWindow(name => InsertChordName(score, name)).ShowDialog(owner));

    /// <summary>Attaches a chord name to the beat under the score cursor, as one undo step; false when there is no beat or no score.</summary>
    public static bool InsertChordName(ScorePreviewView? score, string name) =>
        score?.Edit(editor => editor.Effects.SetCursorChordName(name)) == true;
}
