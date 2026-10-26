using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a real pointer click on a drum sound in the key map of a selected drum track writes that sound at the cursor's cell, on the drum-map line
/// the track's drum map gives it.
/// Does not own: the drum map's rules (DrumMaps), the key map's geometry (PercussionKeyMapRules) or the note rules (ScoreEditCommands).
/// Tests: TabForge.Avalonia.Tests.FretboardDrumPadTests.
/// </summary>
public sealed class FretboardDrumPadTests
{
    /// <summary>The General MIDI acoustic snare: a sound every drum map has.</summary>
    private const int SnareMidi = 38;

    [AvaloniaFact]
    public void DrumPadClickWritesTheSoundOnTheDrumTrackLine()
    {
        var project = DemoSongFactory.Create();
        project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Drums));
        var drumTrack = project.Tracks[^1];
        var main = new MainWindow(first: new SongDocument(() => null) { Project = project }) { Width = 1500, Height = 960 };
        main.Show();
        Settle(main);
        var strip = main.GetVisualDescendants().OfType<InstrumentPanelStrip>().Single();
        var score = main.GetLogicalDescendants().OfType<ScorePreviewView>().Single();

        main.Songs.Active.TrackIndex = project.Tracks.Count - 1;
        Settle(main);
        var panel = strip.Panel;
        Assert.Equal(InstrumentPanelKind.Drums, panel.Kind);

        var grid = PercussionKeyMapRules.GridFor(panel.Bounds.Width, panel.Bounds.Height);
        var cell = PercussionKeyMapRules.CellRect(grid, SnareMidi - PercussionKeyMapRules.FirstNote);
        var local = new Point(cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
        var at = panel.TranslatePoint(local, main)!.Value;
        var line = FretboardNoteEntry.PercussionLine(drumTrack, SnareMidi);
        int HitsOnLine() => score.Editor.Effects.CurrentCell()?.Notes.Count(n => n.StringIndex == line && n.Fret == SnareMidi) ?? 0;
        var before = HitsOnLine();

        main.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
        main.MouseDown(at, MouseButton.Left);
        main.MouseUp(at, MouseButton.Left);
        Assert.Equal(before + 1, HitsOnLine());
        main.Songs.Active.MarkClean();   // the clicks above edited the song; a dirty song would open the save prompt on close
        main.Close();
    }

    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
        }
    }
}
