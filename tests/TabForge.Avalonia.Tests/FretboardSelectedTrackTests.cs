using System.Collections.Generic;
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
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the instrument panel follows the selected track in the real main window: selecting a bass track shows its four strings, a real pointer move
/// hovers its cell and a click writes the note on that track; an audio track shows the placeholder and takes no picks.
/// Does not own: the note rules (ScoreEditCommands), the timeline's selection (ArrangementView) or the panel's drawing (FretboardNoteEntryTests).
/// Tests: TabForge.Avalonia.Tests.FretboardSelectedTrackTests.
/// </summary>
public sealed class FretboardSelectedTrackTests
{
    /// <summary>The bass track the test selects: a four-string tuning, so the panel's string count shows which track it draws.</summary>
    private const int BassIndex = 3;
    private static readonly int[] BassTuning = { 43, 38, 33, 28 };
    private const int BassStrings = 4;
    private const int BassChannel = 2;
    /// <summary>The cell the test hovers and clicks: the bass's second string at fret 2.</summary>
    private const int CheckString = 1;
    private const int CheckFret = 2;

    [AvaloniaFact]
    public void PanelFollowsTheSelectedTrackAndAClickWritesToIt()
    {
        var project = DemoSongFactory.Create();
        while (project.Tracks.Count <= BassIndex) project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Guitar));
        project.Tracks[BassIndex].StringTunings = BassTuning.ToList();
        project.Tracks[BassIndex].Kind = TrackKind.Guitar;   // the demo song already has a drum track at this index
        project.Tracks[BassIndex].MidiChannel = BassChannel;   // not the drum channel (9), so the track reads as pitched
        project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        var audioIndex = project.Tracks.Count - 1;

        var main = new MainWindow(first: new SongDocument(() => null) { Project = project }) { Width = 1500, Height = 960 };
        main.Show();
        Settle(main);
        var strip = main.GetVisualDescendants().OfType<InstrumentPanelStrip>().Single();
        var score = main.GetLogicalDescendants().OfType<ScorePreviewView>().Single();

        Select(main, BassIndex);
        Assert.False(strip.Panel.AudioTrack);
        Assert.Equal(BassStrings, strip.Panel.FretboardLayout().Strings);

        var at = FretPoint(main, strip, CheckString, CheckFret);
        var before = CellNotesAt(score, CheckString, CheckFret);
        main.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
        Assert.True(strip.Panel.Hovered == (CheckString, CheckFret), $"hover cell is {strip.Panel.Hovered}, not string {CheckString + 1} fret {CheckFret}");

        var picks = new List<InstrumentNotePick>();
        void Record(InstrumentNotePick pick) => picks.Add(pick);
        strip.Panel.NotePicked += Record;
        main.MouseDown(at, MouseButton.Left);
        main.MouseUp(at, MouseButton.Left);
        strip.Panel.NotePicked -= Record;
        Assert.Single(picks);
        Assert.True(picks[0].StringIndex == CheckString && picks[0].Fret == CheckFret, "the click picked another cell");
        Assert.Equal(before + 1, CellNotesAt(score, CheckString, CheckFret));

        Select(main, audioIndex);
        Assert.True(strip.Panel.AudioTrack);
        var audioAt = FretPoint(main, strip, CheckString, CheckFret);
        main.MouseMove(audioAt);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(strip.Panel.Hovered);
        var audioLocal = strip.Panel.FretCentre(CheckString, CheckFret);
        Assert.Null(strip.Panel.Pick(new UiPoint(audioLocal.X, audioLocal.Y)));
        main.Songs.Active.MarkClean();   // the clicks above edited the song; a dirty song would open the save prompt on close
        main.Close();
    }

    /// <summary>A pitched demo track the undo test edits (its notes stay valid under the edit).</summary>
    private const int PitchedIndex = 1;

    [AvaloniaFact]
    public void PanelPointsAtTheRestoredTrackAfterAnUndoReplacesTheSong()
    {
        var project = DemoSongFactory.Create();
        var document = new SongDocument(() => null) { Project = project };
        var main = new MainWindow(first: document) { Width = 1500, Height = 960 };
        main.Show();
        Settle(main);
        var strip = main.GetVisualDescendants().OfType<InstrumentPanelStrip>().Single();
        Select(main, PitchedIndex);
        var shape = strip.Panel.FretInput;

        DocumentEdits.Run(document, song => { song.Tracks[PitchedIndex].NumberOfFrets = shape.FretCount - 2; return true; });
        Settle(main);
        Assert.NotEqual(shape, strip.Panel.FretInput);

        Assert.NotNull(DocumentEdits.Undo(document));
        Settle(main);

        Assert.Equal(shape, strip.Panel.FretInput);
        document.MarkClean();
        main.Close();
    }

    /// <summary>The number of notes on the cursor's cell at this string and fret, for the track the score shows.</summary>
    private static int CellNotesAt(ScorePreviewView score, int stringIndex, int fret) =>
        score.Editor.Effects.CurrentCell()?.Notes.Count(n => n.StringIndex == stringIndex && n.Fret == fret) ?? 0;

    /// <summary>Selects a track the way the arrangement does (the document's selected index), then lets the panel follow.</summary>
    private static void Select(MainWindow main, int index)
    {
        main.Songs.Active.TrackIndex = index;
        Settle(main);
    }

    private static void Settle(MainWindow main)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
        }
    }

    /// <summary>The window point of a fret centre in the strip's panel.</summary>
    private static Point FretPoint(MainWindow main, InstrumentPanelStrip strip, int stringIndex, int fret)
    {
        var local = strip.Panel.FretCentre(stringIndex, fret);
        return strip.Panel.TranslatePoint(new Point(local.X, local.Y), main)!.Value;
    }
}
