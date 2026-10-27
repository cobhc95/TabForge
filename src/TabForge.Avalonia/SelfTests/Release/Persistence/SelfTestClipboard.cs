using System.Linq;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

// Owns: score-clipboard validation check (untrusted text).

/// <summary>Score clipboard (docs/specs/COPY_PASTE_DESIGN.md chunk C1): text from outside is untrusted input and is validated before it becomes a clip.</summary>
public static partial class SelfTest
{
    /// <summary>Four 4/4 bars on a 6-string guitar: quarters in bar 1 (one with voice 2 and a lyric), a half note in bar 2 (repeat, section).</summary>
    private static SongProject ClipSong()
    {
        var track = new TrackModel { Name = "Lead", Measures = TemplateFactory.Measures(4) };
        var bar0 = track.Measures[0];
        foreach (var (cell, fret) in new[] { (0, 3), (4, 5), (8, 7), (12, 8) })
        {
            bar0.Cells[cell].DurationDenominator = 4;
            bar0.Cells[cell].Notes.Add(new TabNote { StringIndex = 1, Fret = fret, MidiValue = track.PitchOf(1, fret) });
        }
        bar0.Cells[8].Lyrics = "la";
        bar0.CellsForVoice(1, create: true)[0].DurationDenominator = 1;
        bar0.Voice2Cells[0].Notes.Add(new TabNote { StringIndex = 5, Fret = 0, MidiValue = track.PitchOf(5, 0) });
        var bar1 = track.Measures[1];
        bar1.RepeatStart = true;
        bar1.SectionName = "Verse";
        bar1.Cells[0].DurationDenominator = 2;
        bar1.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 12, MidiValue = track.PitchOf(0, 12), Techniques = { "Vibrato" } });
        bar1.Cells[0].Lyrics = "lo";
        var bass = new TrackModel { Name = "Bass", Kind = TrackKind.Bass, StringTunings = new() { 43, 38, 33, 28 }, Measures = TemplateFactory.Measures(4) };
        return new SongProject { Tempo = 100, Tracks = { track, bass } };
    }

    private static void TestScoreClipRejectsUntrustedInput()
    {
        var song = ClipSong();
        var beats = ClipboardService.CaptureBeats(song, 0, 0, 0, 0, 1, 3).ToJson();
        var bars = ClipboardService.CaptureBars(song, new[] { 0 }, 0, 0).ToJson();

        void Rejected(string name, string? text)
        {
            var ok = ScoreClip.TryParse(text, out var clip, out var error);
            Check($"clip reject: {name}", !ok && clip is null && !string.IsNullOrEmpty(error), error);
        }
        Rejected("empty text", "");
        Rejected("plain prose", "Hello, this is not a clip");
        Rejected("truncated JSON", beats[..(beats.Length / 2)]);
        Rejected("another JSON document", "{\"name\":\"x\",\"value\":3}");
        Rejected("wrong format name", beats.Replace("TabForge.ScoreClip", "Other.Clip"));
        Rejected("unknown kind", beats.Replace("\"kind\":\"Beats\"", "\"kind\":\"Chords\""));
        Rejected("string index beyond the tuning", beats.Replace("\"stringIndex\":1", "\"stringIndex\":9"));
        Rejected("fret out of range", beats.Replace("\"fret\":5", "\"fret\":900"));
        Rejected("invalid duration", beats.Replace("\"durationDenominator\":4", "\"durationDenominator\":3"));
        Rejected("beats out of order", beats.Replace("\"offsetSlots\":4", "\"offsetSlots\":-4"));
        Rejected("non-finite length", beats.Replace("\"lengthSlots\":24", "\"lengthSlots\":1e999"));
        Rejected("too many strings", beats.Replace("\"stringTunings\":[64,59,55,50,45,40]", "\"stringTunings\":[" + string.Join(",", Enumerable.Repeat(40, 17)) + "]"));
        Rejected("bars clip with a voice", bars.Replace("\"voice\":null", "\"voice\":0"));
        Rejected("deep nesting", string.Concat(Enumerable.Repeat("{\"a\":", 100)) + "1" + new string('}', 100));
        Rejected("oversize text", "{\"format\":\"" + new string('x', InputLimits.MaxClipboardBytes) + "\"}");

        var twoTracks = ClipboardService.CaptureBeats(song, 0, 0, 0, 0, 0, 0);
        twoTracks.Tracks.Add(twoTracks.Tracks[0]);
        Rejected("a Beats clip over two tracks", twoTracks.ToJson());
        var crowded = ClipboardService.CaptureBeats(song, 0, 0, 0, 0, 0, 0);
        crowded.Tracks[0].Events[0].Cell.Notes = Enumerable.Range(0, 7).Select(_ => new TabNote { StringIndex = 0, Fret = 1, MidiValue = 65 }).ToList();
        Rejected("more notes in a beat than strings", crowded.ToJson());
        var many = ClipboardService.CaptureBeats(song, 0, 0, 0, 0, 0, 0);
        many.Tracks[0].Events = Enumerable.Range(0, InputLimits.MaxClipEvents + 1).Select(_ => new ScoreClipEvent { Cell = new TabCell { IsRest = true } }).ToList();
        Rejected("more beats than the limit", many.ToJson());
    }
}
