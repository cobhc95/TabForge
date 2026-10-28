using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.EffectEditors;

namespace TabForge;

// Owns: reading a .gp file's whammy and bend fields (score.gpif, values 100 = one tone, offsets 0..100) into the model's curves and how playback and the editor read them back.
// Does not own: the bend fit written on export (GuitarProBendCurve), the editor UI.
// Tests: TestGpifPitchCurves.
public static partial class SelfTest
{
    private const string GpifDive = "<Whammy originValue=\"0.000000\" middleValue=\"-300.000000\" destinationValue=\"-600.000000\" originOffset=\"1.000000\" middleOffset1=\"35.000000\" middleOffset2=\"35.000000\" destinationOffset=\"100.000000\" />";

    /// <summary>A two-note .gp file (a full pre-bend released by 55 %, then a straight dive of 6 tones from 1 % to the end) with the gpif fields written exactly as the reference app saves them.</summary>
    private static byte[] GpifCurveFile()
    {
        var song = TemplateFactory.Blank();
        var track = song.Tracks[0];
        foreach (var at in new[] { 0, 4 })
        {
            var cell = track.Measures[0].Cells[at];
            cell.DurationDenominator = 4;
            cell.Notes.Add(new TabNote { StringIndex = 1, Fret = at == 0 ? 24 : 0, MidiValue = track.StringTunings[1] + (at == 0 ? 24 : 0) });
        }
        var bent = track.Measures[0].Cells[0].Notes[0];
        bent.Techniques.Add("Bend");
        bent.BendPoints = new() { new() { Offset = 0, Value = 4 }, new() { Offset = 30, Value = 2 }, new() { Offset = 45, Value = 0 } };
        var dive = track.Measures[0].Cells[4];
        dive.Notes[0].Techniques.Add("TremBar");
        dive.WhammyPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = -8 } };
        var bytes = GuitarProExporter.ToBytes(song, embedProject: false);
        using var memory = new MemoryStream();
        memory.Write(bytes);
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.Entries.First(e => e.FullName.EndsWith("score.gpif", StringComparison.OrdinalIgnoreCase));
            string text;
            using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
            text = Regex.Replace(text, "<Whammy [^>]*/>", GpifDive);
            foreach (var (name, value) in new[] { ("BendOriginOffset", "0"), ("BendOriginValue", "100"), ("BendMiddleOffset1", "12"), ("BendMiddleOffset2", "12"),
                         ("BendMiddleValue", "50"), ("BendDestinationOffset", "55"), ("BendDestinationValue", "0") })
                text = Regex.Replace(text, "(<Property name=\"" + name + "\">\\s*<Float>)[^<]*(</Float>)", m => m.Groups[1].Value + value + m.Groups[2].Value);
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("Content/score.gpif").Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
        return memory.ToArray();
    }

    private static string CurveText(System.Collections.Generic.IEnumerable<BendPointModel> points) => string.Join(" ", points.Select(p => $"({p.Offset:0.##},{p.Value:0.##})"));

    private static void TestGpifPitchCurves()
    {
        var file = GpifCurveFile();
        var cells = GuitarProImporter.ImportBytes(file, "curves.gp").Tracks[0].Measures[0].Cells.Where(c => c.Notes.Count > 0).ToList();
        Eq("gpif curves: the file's two notes are read", 2, cells.Count);
        if (cells.Count != 2) return;
        var whammy = cells[1].WhammyPoints;
        Check("gpif curves: a dive 0 to -600 from offset 1 to 100 reads as a straight line (0.6,0) to (60,-24) quarter-tones",
            whammy.Count == 2 && Math.Abs(whammy[0].Offset - 0.6) < 1e-6 && whammy[0].Value == 0 && whammy[1].Offset == 60 && whammy[1].Value == -24, CurveText(whammy));
        Eq("gpif curves: a 0..60 curve with a point at 0.6 is not read as fractions", 60.0, BendPointModel.SpanOf(whammy));
        var editor = new EffectCurve(-24, 24);
        editor.SetPoints(whammy);
        Check("gpif curves: the editor's points keep the dive starting at the note's start (no flat stretch to 58 %)",
            editor.Points.Count == 2 && editor.Points[0].Offset == 0 && editor.Points[0].Value == 0 && editor.Points[1].Offset == 60 && editor.Points[1].Value == -24, CurveText(editor.Points));
        var bend = cells[0].Notes[0];
        Check("gpif curves: a full pre-bend released at 55 % reads as (0,4) to (33,0)",
            bend.BendPoints.Count == 2 && bend.BendPoints[0].Offset == 0 && bend.BendPoints[0].Value == 4 && Math.Abs(bend.BendPoints[1].Offset - 33) < 1e-6 && bend.BendPoints[1].Value == 0,
            CurveText(bend.BendPoints) + " " + bend.BendTypeName);
        var played = ScoreToMidiCompiler.BendCurve(bend);
        Check("gpif curves: playback starts the release 1 tone up and is back at the pitch at 55 % of the note",
            played.Length == 2 && played[0] == (0, 2) && Math.Abs(played[1].Fraction - 0.55) < 1e-6 && played[1].Semitones == 0, string.Join(" ", played));
    }
}
