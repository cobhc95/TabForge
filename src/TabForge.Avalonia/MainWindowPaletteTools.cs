using System;
using System.Collections.Generic;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Avalonia;

// Owns: the Tools pane buttons that edit the score at once with no question and have no menu row of their own: Selection cursor, Erase note,
// the eight dynamics, Heavy accent and the two pick strokes. Each runs the Core call the palette makes (ToolPaletteController / MainWindow.Palette).
// Does not own: the menu rows (MainWindowEdit), the tools that ask first (MainWindowBarTools, MainWindowNoteDialogs), the pane (ToolsPaneView).
// Tests: TabForge.Avalonia.Tests.ToolsPaneViewTests.
/// <summary>Registers the Tools pane's no-question commands under their palette command ids (PaletteToolCatalog.CommandIdFor).</summary>
public static class MainWindowPaletteTools
{
    /// <summary>The pick strokes' technique names.</summary>
    private static readonly IReadOnlyList<(string ToolId, string Technique)> PickStrokes = new (string ToolId, string Technique)[]
    {
        ("effect:pickstroke_down", TechniqueNames.PickDown),
        ("effect:pickstroke_up", TechniqueNames.PickUp),
    };

    public static void Register(MainWindowCommands commands, ScorePreviewView score)
    {
        commands.Add(PaletteToolCatalog.CommandIdFor("edit:pointer")!, () => score.Edit(editor => { editor.ClearSelection(); return true; }));
        commands.Add(PaletteToolCatalog.CommandIdFor("edit:erase_note")!, () => score.Edit(editor => { editor.Effects.DeleteNote(); return true; }));
        foreach (var name in Dynamics.Names)
        {
            var velocity = Dynamics.VelocityFor(name);
            commands.Add(PaletteToolCatalog.CommandIdFor("dynamic:" + name)!, () => score.Edit(editor => { editor.Effects.SetDynamicVelocity(velocity); return true; }));
        }
        commands.Add(PaletteToolCatalog.CommandIdFor("effect:heavy_accent")!, () => score.Edit(editor => { editor.Effects.SetAccent(2); return true; }));
        foreach (var (toolId, technique) in PickStrokes)
            commands.Add(PaletteToolCatalog.CommandIdFor(toolId)!, () => score.Edit(editor => { editor.Effects.ToggleTechnique(technique); return true; }));
    }
}
