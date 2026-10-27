using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia;

// Owns: the View menu's fixed zoom rows (Zoom 75%, Zoom 100%, Zoom 150%), which set the score zoom to that factor.
// Does not own: the zoom arithmetic and clamping (ScorePreviewView.SetZoom, Core ScoreZoomRules), the zoom in and out rows
//   (MainWindowScoreRows), the menu rows themselves (Core MainMenuTable).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests.
public static class MainWindowZoomPresets
{
    private const double Zoom75Factor = 0.75;
    private const double Zoom100Factor = 1.0;
    private const double Zoom150Factor = 1.5;

    private static readonly IReadOnlyList<(string CommandId, double Factor)> Presets = new (string CommandId, double Factor)[]
    {
        ("View.Zoom75", Zoom75Factor),
        ("View.Zoom100", Zoom100Factor),
        ("View.Zoom150", Zoom150Factor),
    };

    /// <summary>The command ids this class handles, for the menu test that checks every menu row has a handler.</summary>
    public static readonly IReadOnlyList<string> Commands = Presets.Select(preset => preset.CommandId).ToArray();

    /// <param name="commands">The window's command table the View rows run through.</param>
    /// <param name="score">The score view whose zoom the rows set.</param>
    public static void Register(MainWindowCommands commands, ScorePreviewView score)
    {
        foreach (var (commandId, factor) in Presets)
        {
            commands.Add(commandId, () => score.SetZoom(factor));
            commands.AddCheckState(commandId, () => System.Math.Abs(score.Zoom - factor) < 0.005);
        }
    }
}
