using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    private static MouseButtonEventArgs RightUp(UIElement source, RoutedEvent ev) =>
        new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = ev, Source = source };

    /// <summary>A right-click on a track-row control goes to that control; only the row background opens Track properties.</summary>
    private static void TestTrackRowRightClick()
    {
        var song = OrderedSong();
        var panel = new ArrangementPanel { PanKnobs = true, VolumeKnobs = true };
        panel.Bind(song, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var window = new Window { Content = panel, Width = 1100, Height = 600 };
        using var alive = KeepAlive();
        try
        {
            ShowTestWindow(window);
            var opened = 0;
            panel.TrackOptionsRequested += (_, _) => opened++;
            var row = VisualDescendants<Border>(panel).First(b => (System.Windows.Automation.AutomationProperties.GetName(b) ?? "").StartsWith("Track 1:"));
            var knob = VisualDescendants<KnobControl>(row).FirstOrDefault();
            var button = VisualDescendants<Button>(row).FirstOrDefault();
            Check("the track row has a knob and a button to test", knob is not null && button is not null);
            if (knob is null || button is null) return;

            var e1 = RightUp(knob, UIElement.PreviewMouseRightButtonUpEvent);
            knob.RaiseEvent(e1);
            Check("right-click on a knob is not swallowed by the row (no properties, not handled)", opened == 0 && !e1.Handled);
            var e2 = RightUp(button, UIElement.PreviewMouseRightButtonUpEvent);
            button.RaiseEvent(e2);
            Check("right-click on a button is left to the button", opened == 0 && !e2.Handled);
            var number = VisualDescendants<FrameworkElement>(row).FirstOrDefault(f => (f.ToolTip as string ?? "").Contains("right-click to change the track colour"));
            if (number is not null)
            {
                var e3 = RightUp(number, UIElement.PreviewMouseRightButtonUpEvent);
                number.RaiseEvent(e3);
                Check("right-click on the track number is left to its colour menu", opened == 0 && !e3.Handled);
            }
            var e4 = RightUp(row, UIElement.PreviewMouseRightButtonUpEvent);
            row.RaiseEvent(e4);
            Check("right-click on the row background opens Track properties", opened == 1 && e4.Handled, $"{opened}");

            // The knob's own handler: a right-click with no menu opens the type-in box.
            knob.RaiseEvent(RightUp(knob, UIElement.MouseRightButtonUpEvent));
            Check("right-click on a knob opens its type-in box", knob.IsEditing);
            knob.FinishEdit(false);
        }
        finally { window.Close(); }
    }

    private static TrackModel GuitarWithNotes()
    {
        var song = new SongProject();
        var t = new Controllers.TrackController().CreateTrack(song, TrackKind.Guitar);
        song.Tracks.Add(t);
        // Voice 1: open A string (index 4) fret 2 and fret 5 on string 0; voice 2: fret 3 on string 2.
        t.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 4, Fret = 2, MidiValue = t.PitchOf(4, 2) });
        t.Measures[1].Cells[3].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = t.PitchOf(0, 5) });
        t.Measures[0].CellsForVoice(1, true)[0].Notes.Add(new TabNote { StringIndex = 2, Fret = 3, MidiValue = t.PitchOf(2, 3) });
        return t;
    }

    private static IEnumerable<TabNote> AllNotes(TrackModel t) =>
        t.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes);

    /// <summary>Instrument choice gives an empty track the instrument's strings; a track with notes keeps its strings.</summary>
    private static void TestInstrumentChoiceStrings()
    {
        var controller = new Controllers.TrackController();
        var song = new SongProject();
        var quickDrums = controller.CreateTrack(song, TrackKind.Drums);
        var presetDrums = new TrackModel { Kind = TrackKind.Drums };
        DrumMaps.Apply(presetDrums, DrumMaps.GuitarPro5);
        Check("quick-add Drums has the default drum preset's line count and placeholder notes",
            quickDrums.StringTunings.Count == DrumMaps.LineCount(DrumMaps.GuitarPro5) && quickDrums.StringTunings.SequenceEqual(presetDrums.StringTunings) &&
            quickDrums.DrumMapPreset == DrumMaps.GuitarPro5, $"{quickDrums.StringTunings.Count} lines");
        var empty = controller.CreateTrack(song, TrackKind.Guitar);
        song.Tracks.Add(empty);
        Check("an empty guitar track picking a bass sound becomes a 4-string bass (E A D G)",
            controller.ApplyEdit(song, new Controllers.TrackEditRequest(0, Controllers.TrackEditKind.SelectInstrument, "Slap Bass")) &&
            empty.Kind == TrackKind.Bass && empty.StringTunings.SequenceEqual(new[] { 43, 38, 33, 28 }), string.Join(",", empty.StringTunings));
        controller.ApplyEdit(song, new Controllers.TrackEditRequest(0, Controllers.TrackEditKind.SelectInstrument, "Grand Piano"));
        Check("then a piano sound makes it a keys track with the key rows", empty.Kind == TrackKind.Keys && empty.StringTunings.SequenceEqual(new[] { 96, 91, 86, 81, 76, 71 }));
        controller.ApplyEdit(song, new Controllers.TrackEditRequest(0, Controllers.TrackEditKind.SelectInstrument, "Distortion Guitar"));
        Check("and a guitar sound brings the six guitar strings back", empty.Kind == TrackKind.Guitar && empty.StringTunings.SequenceEqual(new[] { 64, 59, 55, 50, 45, 40 }));
        empty.StringTunings = new() { 64, 59, 55, 50, 45, 40, 35 };
        controller.ApplyEdit(song, new Controllers.TrackEditRequest(0, Controllers.TrackEditKind.SelectInstrument, "Electric Guitar (Jazz)"));
        Check("another guitar sound leaves a custom string set alone", empty.StringTunings.Count == 7);

        var withNotes = GuitarWithNotes();
        var before = AllNotes(withNotes).Select(n => (n.StringIndex, n.Fret, n.MidiValue)).ToList();
        var song2 = new SongProject(); song2.Tracks.Add(withNotes);
        controller.ApplyEdit(song2, new Controllers.TrackEditRequest(0, Controllers.TrackEditKind.SelectInstrument, "Slap Bass"));
        Check("a track with notes keeps its strings, kind and notes when the instrument changes",
            withNotes.Kind == TrackKind.Guitar && withNotes.StringTunings.Count == 6 && withNotes.MidiProgram == 36 &&
            AllNotes(withNotes).Select(n => (n.StringIndex, n.Fret, n.MidiValue)).SequenceEqual(before));
        Check("voice 2 counts as notes", TrackSetup.HasNotes(withNotes) && !TrackSetup.HasNotes(empty));
    }

    /// <summary>Tools > Transpose: every voice, no drums, optional range, bindable.</summary>
    private static void TestTransposeAllVoices()
    {
        var t = GuitarWithNotes();
        var moved = MusicTheoryService.TransposeTrack(t, 2).Moved;
        Check("transpose moves all three notes, voice 2 included", moved == 3);
        Check("frets and sounding pitches both move by 2 (string kept)",
            AllNotes(t).All(n => n.MidiValue == t.PitchOf(n.StringIndex, n.Fret)) &&
            t.Measures[0].Cells[0].Notes[0].Fret == 4 && t.Measures[0].Voice2Cells[0].Notes[0].Fret == 5);
        // Range: bar 1 only.
        var r = GuitarWithNotes();
        var moved2 = MusicTheoryService.TransposeTrack(r, -1, (0, 0, 0, -1)).Moved;
        Check("a range transposes only the selected bar, in both voices",
            moved2 == 2 && r.Measures[1].Cells[3].Notes[0].Fret == 5 && r.Measures[0].Cells[0].Notes[0].Fret == 1);
        // Drums are skipped.
        var song = new SongProject();
        var drums = new Controllers.TrackController().CreateTrack(song, TrackKind.Drums);
        drums.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 38, MidiValue = 38 });
        Check("a drum track is not transposed", MusicTheoryService.TransposeTrack(drums, 2).Moved == 0 && drums.Measures[0].Cells[0].Notes[0].MidiValue == 38 && drums.Measures[0].Cells[0].Notes[0].Fret == 38);
        Check("Transpose is a bindable command", HotkeyCatalog.All.Any(a => a.Id == "Tools.Transpose"));
        // Fret limits: below fret 0 or past the last fret the note is re-fingered on a free string of its beat.
        var low = GuitarWithNotes();
        var open = low.Measures[0].Cells[0].Notes[0];            // A string (4) fret 2
        low.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 5, Fret = 0, MidiValue = low.PitchOf(5, 0) });   // low E, open: blocks string 5
        var lowE = low.Measures[0].Cells[0].Notes[1];
        var (_, unplacedLow) = MusicTheoryService.TransposeTrack(low, -3, (0, 0, 0, 0));
        Check("A string fret 2 down 3 needs the low E string, which the beat already uses: it keeps its string and fret, its pitch moves",
            open.StringIndex == 4 && open.Fret == 2 && open.MidiValue == low.PitchOf(5, 4));
        Check("a note below the lowest string cannot be placed and is counted (pitch still moves)",
            unplacedLow == 2 && lowE.MidiValue == low.StringTunings[5] - 3 && lowE.Fret == 0, $"{unplacedLow}");
        var high = GuitarWithNotes();
        var top = high.Measures[1].Cells[3].Notes[0];             // high E fret 5
        var (_, unplacedHigh) = MusicTheoryService.TransposeTrack(high, -7, (1, 3, 1, 3));
        Check("high E fret 5 down 7 is re-fingered on the B string (fret 3), same sounding pitch",
            unplacedHigh == 0 && top.StringIndex == 1 && top.Fret == 3 && top.MidiValue == high.PitchOf(1, 3), $"{top.StringIndex}/{top.Fret}");
        var up = GuitarWithNotes();
        var g = up.Measures[0].Voice2Cells[0].Notes[0];          // G string fret 3
        up.NumberOfFrets = 12;
        MusicTheoryService.TransposeTrack(up, 12);
        Check("past the last fret a note moves to a higher free string",
            g.Fret <= 12 && g.MidiValue == up.PitchOf(g.StringIndex, g.Fret) && g.MidiValue == up.PitchOf(2, 3) + 12, $"{g.StringIndex}/{g.Fret}");
    }

    /// <summary>Capo: written frets stay, sounding pitch = open string + capo + fret in playback, the staff source and the fretboard.</summary>
    private static void TestCapoRepitchesNotes()
    {
        var t = GuitarWithNotes();
        var frets = AllNotes(t).Select(n => n.Fret).ToList();
        TrackSetup.SetCapo(t, 5);
        Check("raising the capo keeps every written fret", AllNotes(t).Select(n => n.Fret).SequenceEqual(frets));
        Check("raising the capo moves every sounding pitch to open + capo + fret",
            t.Capo == 5 && AllNotes(t).All(n => n.MidiValue == t.StringTunings[n.StringIndex] + 5 + n.Fret));
        var song = new SongProject(); song.Tracks.Add(t);
        var played = new Playback.ScoreToMidiCompiler(song, new Playback.PlaybackOptions()).Build().Notes.Where(n => !n.Dead).Select(n => n.Midi).OrderBy(x => x).ToList();
        var expect = AllNotes(t).Select(n => n.MidiValue).OrderBy(x => x).ToList();
        Check("playback sounds those pitches", played.Count == 3 && played.SequenceEqual(expect), string.Join(",", played));
        var layout = InstrumentLayoutProbe(t);
        Check("the fretboard's open pitch includes the capo (staff and fretboard read the same pitch)",
            AllNotes(t).All(n => layout.OpenPitch(n.StringIndex) + n.Fret == n.MidiValue));
        // Persistence: the shifted pitch is stored, so a reload never shifts it again (.tforge), and .gp carries capo + fret.
        var facts = AllNotes(t).Select(n => (n.StringIndex, n.Fret, n.MidiValue)).ToList();
        var dir = Path.Combine(Path.GetTempPath(), $"tabforge-capo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var tforge = Path.Combine(dir, "capo.tforge");
            ProjectService.Save(tforge, song);
            var back = ProjectService.Load(tforge).Tracks[0];
            Check(".tforge save and reload keep capo, frets and pitches (no second shift)",
                back.Capo == 5 && AllNotes(back).Select(n => (n.StringIndex, n.Fret, n.MidiValue)).SequenceEqual(facts));
            var gp = Path.Combine(dir, "capo.gp");
            GuitarProExporter.Save(song, gp, embedProject: false);
            var gpBack = GuitarProImporter.Import(gp).Tracks[0];
            var gpFacts = AllNotes(gpBack).Select(n => (n.StringIndex, n.Fret, n.MidiValue)).OrderBy(x => x).ToList();
            Check(".gp export and import keep capo, frets and pitches",
                gpBack.Capo == 5 && gpFacts.SequenceEqual(facts.OrderBy(x => x)), string.Join(" ", gpFacts));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        TrackSetup.SetCapo(t, 0);
        Check("lowering it back restores the original pitches", AllNotes(t).All(n => n.MidiValue == t.StringTunings[n.StringIndex] + n.Fret) && t.Capo == 0);
        var kit = new Controllers.TrackController().CreateTrack(song, TrackKind.Drums);
        kit.Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 38, MidiValue = 38 });
        TrackSetup.SetCapo(kit, 3);
        Check("a drum note is never shifted by a capo", kit.Measures[0].Cells[0].Notes[0].MidiValue == 38);
    }

    private static Services.InstrumentLayout InstrumentLayoutProbe(TrackModel t) => Services.InstrumentLayout.Of(t);
}
