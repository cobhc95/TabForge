using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Structure, Rhythm and Layout palette tools in Avalonia, one test per group: bars and markers, voices and beams and stems, line
/// breaks and layout, tuplets and octaves, sounding pitch and duration; and the four bar questions (time and key signature, directions, section).
/// Tests: ScorePreviewEditor.RunPaletteTool, PaletteBarCommands, PaletteToolStates, MainWindowBarTools, BarDialogs.
/// </summary>
public class PaletteToolsTests
{
    private static (ScoreGeometryProbe Probe, ScorePreviewEditor Editor) Make()
    {
        var song = DemoSongFactory.Create();
        var probe = new ScoreGeometryProbe(song, new AvaloniaDrawContext());
        return (probe, new ScorePreviewEditor(probe, song, PreferencesCatalog.DefaultHotkeys()));
    }

    // The first beat that holds a note, with the cursor on that note's string, so note tools find a note under the cursor.
    private static (int Bar, int Beat, TabCell Cell) Spot(SongProject song)
    {
        var measures = song.Tracks[0].Measures;
        for (var bar = 0; bar < measures.Count; bar++)
        {
            var cells = measures[bar].CellsForVoice(0);
            for (var beat = 0; beat < cells.Count; beat++) if (cells[beat].Notes.Count > 0) return (bar, beat, cells[beat]);
        }
        throw new System.InvalidOperationException("The demo song has no note.");
    }

    private static TabCell FirstBeat(ScoreGeometryProbe probe) => Spot(probe.Project!).Cell;

    private static void OnFirstNote(ScoreGeometryProbe probe)
    {
        var (bar, beat, cell) = Spot(probe.Project!);
        probe.Cursor = (bar, beat, cell.Notes[0].StringIndex);
    }

    [Fact]
    public void EveryStructureRhythmAndLayoutToolHasACommandId()
    {
        var tools = PaletteToolCatalog.StructureTab.Concat(PaletteToolCatalog.RhythmTab).Concat(PaletteToolCatalog.LayoutTab).ToList();
        Assert.Equal(41, tools.Count);
        Assert.All(tools, tool => Assert.NotNull(PaletteToolCatalog.CommandIdFor(tool.Id)));
    }

    [AvaloniaFact]
    public void EveryStructureRhythmAndLayoutButtonHasAHandlerInTheWindow()
    {
        var score = new ScorePreviewView();
        var owner = new Window { Content = score };
        var commands = new MainWindowCommands(owner, () => { });
        MainWindowEdit.Register(commands, score, owner, new ContextMenuHost(), () => false, _ => { });
        var missing = PaletteToolCatalog.StructureTab.Concat(PaletteToolCatalog.RhythmTab).Concat(PaletteToolCatalog.LayoutTab)
            .Where(tool => !commands.Handles(PaletteToolCatalog.CommandIdFor(tool.Id)!)).Select(tool => tool.Id).ToList();
        Assert.Empty(missing);
    }

    [AvaloniaFact]
    public void BarAndMarkerToolsEditEveryTrackAndMoveTheCursor()
    {
        var (probe, editor) = Make();
        probe.Project!.Markers.Clear();
        var bars = probe.Project.Tracks[0].Measures.Count;
        probe.Cursor = (2, 0, 0);
        Assert.True(editor.RunCommand("Bar.DoubleBar"));
        Assert.All(probe.Project!.Tracks, track => Assert.True(track.Measures[2].IsDoubleBar));
        Assert.True(editor.RunCommand("Bar.RepeatOneBar"));
        Assert.True(probe.Project!.Tracks[0].Measures[2].SimileOneBar);
        Assert.True(editor.RunCommand("Tool.gp:append_bar"));
        Assert.Equal(bars + 1, probe.Project!.Tracks[0].Measures.Count);
        Assert.Equal(2, probe.Cursor!.Value.Measure);
        Assert.True(editor.RunCommand("Tool.gp:step_forward"));
        Assert.NotEqual((2, 0), (probe.Cursor!.Value.Measure, probe.Cursor.Value.Cell));
        Assert.True(editor.RunCommand("Tool.gp:step_back"));
        Assert.Equal((2, 0), (probe.Cursor!.Value.Measure, probe.Cursor.Value.Cell));

        editor.AddOrEditMarker("Verse", "#2E74B5");
        probe.Cursor = (0, 0, 0);
        Assert.True(editor.RunCommand("Section.Next"));
        Assert.Equal(2, probe.Cursor!.Value.Measure);
        Assert.True(editor.RunCommand("Section.Previous"));
        Assert.Equal(2, probe.Cursor!.Value.Measure);   // no marker before the one at the cursor: the cursor stays
        Assert.True(editor.Undo());
        Assert.Empty(probe.Project!.Markers);
    }

    [AvaloniaFact]
    public void VoiceBeamAndStemToolsSetTheirStateOnTheBeat()
    {
        var (probe, editor) = Make();
        OnFirstNote(probe);
        Assert.True(editor.RunCommand("Tool.gp:voice_2"));
        Assert.Equal(1, editor.ActiveVoiceIndex);
        Assert.True(editor.ToolState("gp:voice_2"));
        Assert.True(editor.RunCommand("Tool.gp:voice_1"));
        Assert.Equal(0, editor.ActiveVoiceIndex);
        OnFirstNote(probe);

        Assert.True(editor.RunCommand("Tool.gp:beam_force"));
        Assert.Equal(BeamMode.Force, FirstBeat(probe).BeamMode);
        Assert.True(editor.ToolState("gp:beam_force"));
        Assert.True(editor.RunCommand("Tool.gp:beam_break"));
        Assert.Equal(BeamMode.Break, FirstBeat(probe).BeamMode);
        Assert.True(editor.RunCommand("Tool.gp:beam_auto"));
        Assert.Equal(BeamMode.Auto, FirstBeat(probe).BeamMode);
        Assert.True(editor.RunCommand("Tool.gp:stem_invert"));
        Assert.Equal(StemDirection.Invert, FirstBeat(probe).StemDirection);
        Assert.True(editor.RunCommand("Tool.gp:stem_invert"));
        Assert.Equal(StemDirection.Auto, FirstBeat(probe).StemDirection);
    }

    [AvaloniaFact]
    public void SongWideBarToolsHitEveryTrackAndSimileOnlyTheShownTrackAndUndo()
    {
        var (probe, editor) = Make();
        var tracks = probe.Project!.Tracks;
        Assert.True(tracks.Count > 2);
        probe.ShowTrack(2);
        probe.Cursor = (5, 0, 0);
        Assert.True(editor.RunCommand("Tool.gp:triplet_feel"));
        Assert.All(tracks, track => Assert.Equal(TripletFeels.Eighth, TripletFeels.Effective(track.Measures[5])));
        Assert.True(editor.RunCommand("Bar.DoubleBar"));
        Assert.All(tracks, track => Assert.True(track.Measures[5].IsDoubleBar));
        Assert.True(editor.RunCommand("Tool.gp:force_line_break"));
        Assert.All(tracks, track => Assert.True(track.Measures[5].ForceLineBreak));

        probe.Cursor = (6, 0, 0);
        Assert.True(editor.RunCommand("Bar.RepeatOneBar"));
        for (var i = 0; i < tracks.Count; i++) Assert.Equal(i == 2, tracks[i].Measures[6].SimileOneBar);
        Assert.True(editor.Undo());
        Assert.All(probe.Project!.Tracks, track => Assert.False(track.Measures[6].SimileOneBar));
        probe.Cursor = (5, 0, 0);
        Assert.True(editor.RunCommand("Tool.gp:triplet_feel"));
        Assert.True(editor.Undo());
        Assert.All(probe.Project!.Tracks, track => Assert.Equal(TripletFeels.Eighth, TripletFeels.Effective(track.Measures[5])));
    }

    [AvaloniaFact]
    public void LineBreakAndGrayVoiceToolsToggleAndUndo()
    {
        var (probe, editor) = Make();
        probe.Cursor = (1, 0, 0);
        Assert.True(editor.RunCommand("Tool.gp:force_line_break"));
        Assert.All(probe.Project!.Tracks, track => Assert.True(track.Measures[1].ForceLineBreak));
        Assert.True(editor.ToolState("gp:force_line_break"));
        Assert.True(editor.RunCommand("Tool.gp:prevent_line_break"));
        Assert.True(probe.Project!.Tracks[0].Measures[1].PreventLineBreak);
        Assert.True(editor.RunCommand("Tool.gp:inactive_voice_gray"));
        Assert.True(probe.Project!.GrayInactiveVoice);
        Assert.True(editor.Undo());
        Assert.False(probe.Project!.GrayInactiveVoice);
        Assert.True(editor.RunCommand("Tool.gp:force_line_break"));
        Assert.All(probe.Project!.Tracks, track => Assert.False(track.Measures[1].ForceLineBreak));
    }

    [AvaloniaFact]
    public void TupletAndOctaveToolsMarkTheBeat()
    {
        var (probe, editor) = Make();
        OnFirstNote(probe);
        Assert.True(editor.RunCommand("Tool.gp:octave_8va"));
        Assert.Equal(12, FirstBeat(probe).OctaveShiftSemitones);
        Assert.True(editor.ToolState("gp:octave_8va"));
        Assert.True(editor.RunCommand("Tool.gp:octave_15mb"));
        Assert.Equal(-24, FirstBeat(probe).OctaveShiftSemitones);
        Assert.True(editor.ToolEnabled("gp:octave_8va"));
        editor.SetTuplet(3, 2);
        Assert.Equal((3, 2), FirstBeat(probe).Tuplet);
    }

    [AvaloniaFact]
    public void SoundingDurationTempoAndSignatureEditsReachTheBars()
    {
        var (probe, editor) = Make();
        OnFirstNote(probe);
        editor.SetSoundDuration(500);
        Assert.Equal(200, FirstBeat(probe).SoundDurationPercent);
        Assert.True(editor.ToolState("gp:sound_duration"));
        probe.Cursor = (1, 0, 0);
        editor.SetTempoChange(90);
        Assert.All(probe.Project!.Tracks, track => Assert.Equal(90, track.Measures[1].TempoChange));
        editor.SetTimeSignature(3, 4, onlyThisBar: true);
        Assert.Equal((3, 4), (probe.Project!.Tracks[0].Measures[1].TimeSigNum, probe.Project.Tracks[0].Measures[1].TimeSigDenom));
        editor.SetKeySignature(2, minor: true, onlyThisBar: true);
        Assert.Equal((2, true), (probe.Project!.Tracks[0].Measures[1].KeySignature, probe.Project.Tracks[0].Measures[1].KeySignatureMinor));
        editor.SetDirections("Segno", 2);
        Assert.Equal(("Segno", 2), (probe.Project!.Tracks[0].Measures[1].Directions, probe.Project.Tracks[0].Measures[1].AlternateEnding));
    }

    [AvaloniaFact]
    public async Task SoundDurationToolAsksThenSetsThePercent()
    {
        var view = new ScorePreviewView();
        view.Show(new SongDocument(() => null) { Project = FullDemoSongFactory.Create() });
        var owner = new Window { Content = view };
        owner.Show();
        var commands = new MainWindowCommands(owner, () => { });
        MainWindowEdit.Register(commands, view, owner, new ContextMenuHost(), () => false, _ => { });
        var (bar, index, beat) = Spot(view.Editor.Project!);
        view.Probe.Cursor = (bar, index, beat.Notes[0].StringIndex);
        commands.Run("Tool.gp:sound_duration");
        Dispatcher.UIThread.RunJobs();
        var prompt = owner.OwnedWindows.OfType<TextPromptWindow>().Single();
        Named<TextBox>(prompt, "PromptText").Text = "60";
        Click(Named<Button>(prompt, "OkButton"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(60, beat.SoundDurationPercent);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task BarQuestionsReturnTheirAnswers()
    {
        var owner = new Window();
        owner.Show();
        var time = new TimeSignatureWindow(4, 4, null);
        var timeResult = time.ShowDialog<TimeSignatureAnswer?>(owner);
        Named<TextBox>(time, "Beats").Text = "7";
        Named<ComboBox>(time, "BeatValue").SelectedItem = "8";
        Named<CheckBox>(time, "OnlyThisBar").IsChecked = true;
        Click(Named<Button>(time, "OkButton"));
        Assert.Equal(new TimeSignatureAnswer(7, 8, true), await timeResult);

        var key = new KeySignatureWindow(0, false, "bars 2-3");
        var keyResult = key.ShowDialog<KeySignatureAnswer?>(owner);
        Assert.False(Named<CheckBox>(key, "OnlyThisBar").IsVisible);
        Named<ComboBox>(key, "Key").SelectedIndex = 9;   // D: two sharps
        Named<CheckBox>(key, "Minor").IsChecked = true;
        Click(Named<Button>(key, "OkButton"));
        Assert.Equal(new KeySignatureAnswer(2, true, false), await keyResult);

        var directions = new DirectionsWindow("Segno,Custom", 0);
        var directionsResult = directions.ShowDialog<DirectionsAnswer?>(owner);
        Named<TextBox>(directions, "Ending").Text = "2";
        Click(Named<Button>(directions, "OkButton"));
        Assert.Equal(new DirectionsAnswer("Custom,Segno", 2), await directionsResult);

        var marker = new MarkerWindow("Add", "Chorus", "#2E74B5", 4);
        var markerResult = marker.ShowDialog<MarkerAnswer?>(owner);
        Click(Named<Button>(marker, "OkButton"));
        var answer = await markerResult;
        Assert.Equal(("Chorus", "#2E74B5"), (answer!.Title, answer.ColourHex.ToUpperInvariant()));
        owner.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BarQuestionsAreReadableInBothThemes(bool light)
    {
        using var theme = UseTheme(light);
        foreach (var window in new Window[]
        {
            new TimeSignatureWindow(4, 4, null), new KeySignatureWindow(0, false, null), new DirectionsWindow("Segno", 0), new MarkerWindow("Add", "Section", "#2E74B5", 0),
        })
        {
            SavePng(window, nameof(PaletteToolsTests), $"{window.GetType().Name}-{(light ? "light" : "dark")}.png");
            foreach (var text in window.GetLogicalDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)))
                Assert.True(Contrast(text.Foreground, SurfaceBehind(text, window), $"{window.GetType().Name} text '{text.Text}' (light: {light})") >= 4.5);
            window.Close();
        }
    }
}
