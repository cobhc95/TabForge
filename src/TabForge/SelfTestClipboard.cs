using System.IO;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

/// <summary>Score clipboard (docs/COPY_PASTE_DESIGN.md chunk C1): clip capture, JSON format, untrusted-input validation, clipboard fallback.</summary>
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

    private static void TestScoreClipCapture()
    {
        var song = ClipSong();

        var bars = ClipboardService.CaptureSelection(song, 0, 0, 0, 0, 1, -1, "song-a");
        Check("clip: a whole-bar selection gives a Bars clip", bars.Kind == ScoreClipKind.Bars && bars.BarCount == 2);
        var barTrack = bars.Tracks[0];
        Check("clip: Bars clip keeps both voices", barTrack.Bars[0].Voice2Cells.Count > 0 && barTrack.Bars[0].Voice2Cells[0].Notes.Count == 1 && barTrack.Voice is null);
        Check("clip: Bars clip keeps the bar settings", barTrack.Bars[1].RepeatStart && barTrack.Bars[1].SectionName == "Verse");
        Check("clip: Bars clip carries no lyrics", barTrack.Bars.SelectMany(b => b.Cells.Concat(b.Voice2Cells)).All(c => c.Lyrics.Length == 0));
        Check("clip: copying does not change the song", song.Tracks[0].Measures[0].Cells[8].Lyrics == "la");
        Check("clip: source instrument recorded",
            barTrack.StringCount == 6 && barTrack.StringTunings.SequenceEqual(song.Tracks[0].StringTunings) && !barTrack.IsDrums &&
            barTrack.SourceTrackId == song.Tracks[0].Id && barTrack.SourceTrackIndex == 0 && bars.SourceTrackCount == 2 && bars.SourceSongId == "song-a");
        Check("clip: Bars clip meter and tempo", bars.TimeSignatureOf(0) == (4, 4) && bars.BarSlots(1) == 16 && bars.StartTempo == 100);

        var beats = ClipboardService.CaptureSelection(song, 0, 0, 0, 8, 1, 3);
        var events = beats.Tracks[0].Events;
        Check("clip: a mid-bar selection gives a Beats clip of one voice", beats.Kind == ScoreClipKind.Beats && beats.Tracks[0].Voice == 0);
        Check("clip: beats keep onsets across the bar line (bar lines not stored)",
            events.Select(e => e.OffsetSlots).SequenceEqual(new[] { 0.0, 4.0, 8.0 }),
            string.Join(",", events.Select(e => e.OffsetSlots)));
        Eq("clip: beats length runs to the end of the last beat", 16.0, beats.Tracks[0].LengthSlots);
        Check("clip: beats carry notes and techniques, not lyrics or positions",
            events[2].Cell.Notes[0].Techniques.Contains("Vibrato") && events.All(e => e.Cell.Lyrics.Length == 0 && e.Cell.RhythmicPosition is null));

        var single = ClipboardService.CaptureBeats(song, 0, 0, 0, 4, 0, 4);
        Check("clip: a lone cursor beat is a one-beat clip",
            single.Tracks[0].Events.Count == 1 && single.Tracks[0].Events[0].Cell.Notes[0].Fret == 5 && single.Tracks[0].LengthSlots == 4);
        var voice2 = ClipboardService.CaptureBeats(song, 0, 1, 0, 0, 0, 15);
        Check("clip: beats copy the chosen voice", voice2.Tracks[0].Voice == 1 && voice2.Tracks[0].Events.Count == 1 && voice2.Tracks[0].LengthSlots == 16);
        var empty = false;
        try { ClipboardService.CaptureBeats(song, 0, 0, 2, 0, 2, 15); }
        catch (InvalidDataException) { empty = true; }
        Check("clip: copying only empty slots reports nothing to copy", empty);

        var all = ClipboardService.CaptureBars(song, new[] { 0, 1 }, 1, 2);
        Check("clip: all-tracks Bars clip holds each track in order",
            all.Tracks.Count == 2 && all.Tracks[1].TrackKind == TrackKind.Bass && all.Tracks[1].StringCount == 4 && all.BarCount == 2);
        var model = all.Tracks[1].ToTrackModel();
        Check("clip: track header gives a pitch-mapping stand-in", model.PitchOf(3, 5) == 33 && model.Measures.Count == 0);
    }

    private static void TestScoreClipJson()
    {
        var song = ClipSong();
        foreach (var clip in new[] { ClipboardService.CaptureBars(song, new[] { 0, 1 }, 0, 1, "s"), ClipboardService.CaptureBeats(song, 0, 0, 0, 8, 1, 3) })
        {
            var json = clip.ToJson();
            var ok = ScoreClip.TryParse(json, out var back, out var error);
            Check($"clip json: {clip.Kind} clip round trip parses", ok && back is not null, error);
            if (back is null) continue;
            Eq($"clip json: {clip.Kind} clip round trip is identical", json, back.ToJson());
            Check($"clip json: {clip.Kind} header written in full", json.Contains("\"format\":\"TabForge.ScoreClip\"") && json.Contains("\"version\":1") &&
                json.Contains($"\"kind\":\"{clip.Kind}\""));
        }

        var legacyBeat = ProjectService.Snapshot(new SongProject
        {
            Tracks = { new TrackModel { Measures = { new MeasureModel { Cells = new List<TabCell> { new TabCell { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 2, Fret = 2, MidiValue = 57 } } } } } } } }
        });
        Check("clip json: legacy single-beat text becomes a Beats clip",
            ScoreClip.TryParse(legacyBeat, out var legacy, out _) && legacy is { Kind: ScoreClipKind.Beats, IsLegacy: true } &&
            legacy.Tracks[0].Events[0].Cell.Notes[0].Fret == 2 && legacy.Tracks[0].LengthSlots == 4);
        var legacyBars = ProjectService.Snapshot(new SongProject { Tracks = { new TrackModel { Measures = TemplateFactory.Measures(2) } } });
        Check("clip json: legacy bar text becomes a Bars clip",
            ScoreClip.TryParse(legacyBars, out var legacy2, out _) && legacy2 is { Kind: ScoreClipKind.Bars, BarCount: 2 });

        var current = ClipboardService.CaptureBeats(song, 0, 0, 0, 0, 0, 0).ToJson();
        Check("clip json: a newer version is refused with an update hint",
            !ScoreClip.TryParse(current.Replace("\"version\":1", "\"version\":2"), out _, out var newer) && newer?.Contains("newer TabForge") == true, newer);
        Check("clip json: a missing version is refused",
            !ScoreClip.TryParse(current.Replace("\"version\":1,", ""), out _, out _));
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

    private sealed class FakeScoreClipboard : IScoreClipboard
    {
        public uint SequenceNumber { get; set; } = 1;
        public bool Available { get; set; } = true;
        public string? Text { get; set; }
        public int Reads { get; private set; }
        public bool TryWrite(string json)
        {
            if (!Available) return false;
            Text = json;
            SequenceNumber++;
            return true;
        }
        public ScoreClipboardRead TryRead(int maxBytes)
        {
            Reads++;
            if (!Available) return new ScoreClipboardRead(false, null);
            return Text is { } text && text.Length > maxBytes ? new ScoreClipboardRead(true, null, true) : new ScoreClipboardRead(true, Text);
        }
    }

    private static void TestClipboardServiceFallback()
    {
        var song = ClipSong();
        var clip = ClipboardService.CaptureBeats(song, 0, 0, 0, 0, 0, 0);
        var system = new FakeScoreClipboard();
        var service = new ClipboardService(system);
        Check("clipboard: copy writes the system clipboard", service.Copy(clip) && system.Text is not null && system.Text.Contains("TabForge.ScoreClip"));
        Check("clipboard: unchanged clipboard returns the cached clip without re-reading",
            ReferenceEquals(service.TryGetClip(out _), clip) && service.CanPaste && system.Reads == 0);

        system.Text = "some text from another program";
        system.SequenceNumber++;
        Check("clipboard: foreign text is not TabForge notes",
            service.TryGetClip(out var foreign) is null && foreign == ClipboardService.NotTabForgeNotesMessage && !service.CanPaste);

        system.Text = clip.ToJson();
        system.SequenceNumber++;
        var fromOtherWindow = service.TryGetClip(out _);
        Check("clipboard: JSON from another window is read and validated", fromOtherWindow is { Kind: ScoreClipKind.Beats } && !ReferenceEquals(fromOtherWindow, clip));

        system.Available = false;
        var memoryOnly = ClipboardService.CaptureBars(song, new[] { 0 }, 0, 0);
        Check("clipboard: copy with the system clipboard unavailable keeps the clip in memory",
            !service.Copy(memoryOnly) && ReferenceEquals(service.Current, memoryOnly));
        system.SequenceNumber++;
        Check("clipboard: unavailable system clipboard falls back to the in-memory clip", ReferenceEquals(service.TryGetClip(out _), memoryOnly));

        var offline = new ClipboardService(null);
        Check("clipboard: without a system clipboard paste has nothing until a copy", offline.TryGetClip(out var none) is null && none is not null);
        offline.Copy(clip);
        Check("clipboard: in-memory clipboard returns the copied clip", ReferenceEquals(offline.TryGetClip(out _), clip));
    }
}
