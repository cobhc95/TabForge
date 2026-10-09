using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// Owns: the save-and-reopen count guard for the Guitar Pro writer and importer (grace notes, fermatas, triplet feel,
// beat comments, lyrics and a second voice). Does not own: the reference-parser fidelity checks (SelfTestCore).
// Tests: TestGpRoundTripCounts.
public static partial class SelfTest
{
    private static void TestGpRoundTripCounts()
    {
        var folder = RtFolder();
        try
        {
            foreach (var embed in new[] { false, true })
            {
                var label = embed ? "with embedded project" : "clean";
                var project = BuildRoundTripCountsSong();
                var before = RoundTripCounts(project);
                var path = Path.Combine(folder, embed ? "counts-embedded.gp" : "counts-clean.gp");
                GuitarProExporter.Save(project, path, embedProject: embed);
                var reopened = GuitarProImporter.Import(path, new ImportContext());
                var after = RoundTripCounts(reopened);
                Check($"gp round trip ({label}): every note, grace, fermata, comment, lyric, triplet feel and voice-2 note is kept",
                    before == after, $"saved {before}, reopened {after}");
            }
        }
        finally { RtCleanup(folder); }
    }

    /// <summary>One guitar track with notes on both voices, a grace note, a fermata, a beat comment, a lyric and a triplet feel bar.</summary>
    private static SongProject BuildRoundTripCountsSong()
    {
        var track = new TrackModel { Name = "Round trip guitar", Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0, Measures = TemplateFactory.Measures(4) };
        for (var bar = 0; bar < 4; bar++)
            for (var beat = 0; beat < 4; beat++)
            {
                var measure = track.Measures[bar];
                var cell = measure.Cells[beat * 4];
                cell.DurationDenominator = 4;
                var s = (bar + beat) % track.StringTunings.Count;
                var fret = (bar * 3 + beat * 2) % 12;
                cell.Notes.Add(new TabNote { StringIndex = s, Fret = fret, MidiValue = track.StringTunings[s] + fret });
            }
        // Second voice: an eighth on string 2 in every bar.
        for (var bar = 0; bar < 4; bar++)
        {
            var voice2 = track.Measures[bar].CellsForVoice(1, create: true);
            var cell = voice2[8];
            cell.DurationDenominator = 8;
            cell.Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = track.StringTunings[1] + 5 });
        }
        // A grace note before the second beat of bar 2, a fermata on beat 3, a comment on beat 1 and a lyric on bar 4 beat 1.
        track.Measures[1].Cells[4].Notes.Insert(0, new TabNote { StringIndex = 0, Fret = 3, MidiValue = track.StringTunings[0] + 3, IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = 1 });
        track.Measures[1].Cells[4].DurationDenominator = 4;
        // Guitar Pro keeps fermatas per bar and offset, so one on a beat that a second voice also sits on reopens on both voices. Keep it off voice 2's beat.
        track.Measures[2].Cells[4].Fermata = true;
        track.Measures[0].Cells[0].Text = "Round trip comment";
        track.Measures[3].Cells[0].Lyrics = "la";
        track.Measures[1].TripletFeel = true;
        track.Measures[1].TripletFeelKind = TripletFeels.Eighth;
        var project = new SongProject { Tempo = 120, Title = "Round trip counts" };
        project.Tracks.Add(track);
        return project;
    }

    /// <summary>The counts the round trip must keep, as one comparable line.</summary>
    private static string RoundTripCounts(SongProject project)
    {
        var measures = project.Tracks.SelectMany(t => t.Measures).ToList();
        var cells = measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).ToList();
        var notes = cells.Sum(c => c.Notes.Count);
        var voice2Notes = measures.Sum(m => m.Voice2Cells.Sum(c => c.Notes.Count));
        var graces = cells.Count(c => c.IsGrace || c.Notes.Any(n => n.IsGraceNote));
        var fermatas = cells.Count(c => c.Fermata);
        var comments = cells.Count(c => !string.IsNullOrWhiteSpace(c.Text));
        var lyrics = cells.Count(c => !string.IsNullOrWhiteSpace(c.Lyrics));
        var triplets = measures.Count(m => m.TripletFeel || m.TripletFeelKind != TripletFeels.None);
        return $"notes={notes} voice2={voice2Notes} graces={graces} fermatas={fermatas} comments={comments} lyrics={lyrics} triplets={triplets}";
    }
}
