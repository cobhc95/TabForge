using System.Linq;
using System.Windows;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Editor input: Shift+click extends the selection; an effect key on an empty beat takes the current note value.</summary>
public static partial class SelfTest
{
    private static void TestDrumEntryAndQuickAddBars()
    {
        var controller = new Controllers.TrackController();
        var song = new SongProject();
        var guitar = controller.CreateTrack(song, TrackKind.Guitar);
        guitar.Measures = guitar.Measures.Take(4).ToList();
        song.Tracks.Add(guitar);
        var drums = controller.CreateTrack(song, TrackKind.Drums);
        Check("quick-add: a new track matches the song's bar count (4), not 32", drums.Measures.Count == 4, $"{drums.Measures.Count}");
        song.Tracks.Add(drums);
        var bass = controller.CreateTrack(song, TrackKind.Bass);
        var audio = controller.CreateTrack(song, TrackKind.Audio);
        Check("new tracks take their kind's colour: guitar dark red, bass dark yellow, drums dark blue, audio light blue",
            guitar.ColorHex == "#A12424" && bass.ColorHex == "#B8930F" && drums.ColorHex == "#1F3F9E" && audio.ColorHex == "#7CC4F2",
            $"{guitar.ColorHex} {bass.ColorHex} {drums.ColorHex} {audio.ColorHex}");
        var picked = controller.CreateTrack(song, TrackKind.Guitar); var before = picked.ColorHex; picked.Kind = TrackKind.Bass;
        Controllers.TrackController.RecolourNewTrack(picked, before);
        var custom = controller.CreateTrack(song, TrackKind.Guitar); var customBefore = custom.ColorHex; custom.Kind = TrackKind.Bass; custom.ColorHex = "#123456";
        Controllers.TrackController.RecolourNewTrack(custom, customBefore);
        Check("the Add track window: a kind changed there recolours the new track unless a colour was picked", picked.ColorHex == "#B8930F" && custom.ColorHex == "#123456");

        var editor = new TabEditorControl { Project = song, SelectedTrackIndex = 1 };
        editor.Measure(new Size(1200, 800));
        editor.Arrange(new Rect(0, 0, 1200, 800));
        var pads = new[] { 27, 35, 36, 38, 42, 46, 49, 51, 57, 59, 87 };
        var written = new List<int>();
        for (var i = 0; i < pads.Length; i++)
        {
            var bar = i / 4;
            editor.SetPosition(bar, (i % 4) * 4, 0, false);
            var line = MainWindow.PercussionPadLine(drums, pads[i]);
            Check($"drum pad {pads[i]} is written", editor.ToggleFretAtPosition(line, pads[i]));
            written.Add(drums.Measures.SelectMany(m => m.Cells).SelectMany(c => c.Notes).Count(n => n.MidiValue == pads[i] && n.Fret == pads[i]));
        }
        Check("every pad from 27 to 87 stored its own note number", written.All(w => w == 1), string.Join(",", written));

        editor.SetPosition(3, 12, 1, false);
        editor.EnterFret(3, autoAdvance: false); editor.EnterFret(8, autoAdvance: false);
        var typed = drums.Measures[3].Cells[12].Notes.FirstOrDefault();
        Check("typing 38 on a drum line gives the snare (38), not 36", typed is { Fret: 38, MidiValue: 38 }, $"{typed?.Fret}");

        DrumMaps.Apply(drums, DrumMaps.DrumTab);
        Check("pad clicks follow the track's drum preset (line-per-instrument snare line)",
            MainWindow.PercussionPadLine(drums, 38) == DrumMaps.Default(DrumMaps.DrumTab, 38).TabLine &&
            MainWindow.PercussionPadLine(drums, 38) != Services.GuitarProImporter.DrumLine(38),
            $"{MainWindow.PercussionPadLine(drums, 38)} vs {Services.GuitarProImporter.DrumLine(38)}");
    }

    private static void TestEditorShiftClickAndEffectDuration()
    {
        var project = Presets.TemplateFactory.Create("Rock Band");
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0 };
        editor.Measure(new Size(1200, 800));
        editor.Arrange(new Rect(0, 0, 1200, 800));

        // E1: Shift+click bar 2 beat 2 from bar 1 beat 1.
        editor.SetPosition(0, 0, 1, false);
        var extended = editor.ShiftClickExtend(1, 1, 1);
        var range = editor.SelectionCellRange;
        Check("shift+click: extends the selection from the cursor beat to the clicked beat",
            extended && editor.HasSelection && range == (0, 0, 1, 1), $"{range}");
        editor.SetPosition(0, 0, 1, false);
        editor.ClearSelection(notify: false);
        Check("shift+click: a plain click path leaves no selection", !editor.HasSelection);

        // E2: effect keys on an empty beat take the current note value.
        foreach (var name in new[] { "technique", "dead note", "ghost note" })
        {
            var fresh = Presets.TemplateFactory.Create("Rock Band");
            var ed = new TabEditorControl { Project = fresh, SelectedTrackIndex = 0 };
            ed.Measure(new Size(1200, 800));
            ed.Arrange(new Rect(0, 0, 1200, 800));
            ed.SetPosition(0, 0, 1, false);
            ed.SetDuration(8);
            var action = name switch
            {
                "technique" => (Action)(() => ed.ToggleTechnique(TechniqueNames.Vibrato)),
                "dead note" => () => ed.ToggleDead(),
                _ => () => ed.ToggleGhost(),
            };
            action();
            var cell = fresh.Tracks[0].Measures[0].Cells.FirstOrDefault(c => c.Notes.Count > 0);
            Check($"effect key on an empty beat ({name}) applies the current note value", cell is { DurationDenominator: 8 }, $"{cell?.DurationDenominator}");
        }

        var existing = Presets.TemplateFactory.Create("Rock Band");
        var ed2 = new TabEditorControl { Project = existing, SelectedTrackIndex = 0 };
        ed2.Measure(new Size(1200, 800));
        ed2.Arrange(new Rect(0, 0, 1200, 800));
        existing.Tracks[0].Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 1, Fret = 5 } } };
        ed2.SetPosition(0, 0, 1, false);
        ed2.SetDuration(8);
        existing.Tracks[0].Measures[0].Cells[0].DurationDenominator = 4;
        ed2.SetPosition(0, 0, 2, false);
        ed2.ToggleTechnique(TechniqueNames.Vibrato);
        Check("effect key on an existing beat keeps its duration", existing.Tracks[0].Measures[0].Cells[0].DurationDenominator == 4);
    }
}
