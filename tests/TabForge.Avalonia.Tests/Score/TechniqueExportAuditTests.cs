using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Audit (tests only): what the MIDI export emits per guitar technique and what the
// piano-roll round trip keeps. Failing behaviour is recorded as Skip("BUG: ..."), never fixed here.
public sealed class TechniqueExportAuditTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-tech-audit-{Guid.NewGuid():N}");

    public TechniqueExportAuditTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    // ---------- song builders (model API as in MidiExportProcessorsTests) ----------

    private static SongProject BuildSong(int bars)
    {
        var song = new SongProject
        {
            Title = "TechAudit",
            Tempo = 120,
            TimeSignatureNumerator = 4,
            TimeSignatureDenominator = 4,
        };
        song.Tracks.Add(new TrackModel
        {
            Name = "Lead",
            Kind = TrackKind.Guitar,
            MidiChannel = 0,
            StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
            Measures = TemplateFactory.Measures(bars),
        });
        return song;
    }

    private static TabNote PutNote(TrackModel track, int bar, int cell, int stringIndex, int fret, int denom = 4)
    {
        var beat = track.Measures[bar].Cells[cell];
        beat.DurationDenominator = denom;
        var note = new TabNote
        {
            StringIndex = stringIndex,
            Fret = fret,
            MidiValue = track.PitchOf(stringIndex, fret),
        };
        beat.Notes.Add(note);
        return note;
    }

    private static ScoreTimeline BuildTimeline(SongProject song) =>
        MidiTimelineBuilder.Build(song, new PlaybackOptions { RespectMuteSolo = false });

    private static int NoteOns(ScoreTimeline t, int pitch, int track = 0) =>
        t.Events.Count(e => e.TrackIndex == track && (e.Status & 0xF0) == 0x90 && e.Data1 == pitch && e.Data2 > 0);

    private static List<ScoreEvent> Bends(ScoreTimeline t, int channel) =>
        t.Events.Where(e => !e.IsSetup && (e.Status & 0xF0) == 0xE0 && (e.Status & 0x0F) == channel).ToList();

    private static bool WheelMoves(List<ScoreEvent> bends, int threshold = 200) =>
        bends.Any(e => Math.Abs(((e.Data2 << 7) | e.Data1) - 8192) > threshold);

    // ---------- minimal SMF reader (notes + pitch bend + CC), same file shape as the app writes ----------

    private sealed record MidiNote(int Pitch, int StartTick, int EndTick, int Channel);
    private sealed record MidiBend(int Tick, int Channel, int Value);
    private sealed record MidiCc(int Tick, int Channel, int Controller, int Value);
    private sealed record MidiFileData(int Division, List<MidiNote> Notes, List<MidiBend> Bends, List<MidiCc> Ccs);

    private static int ReadBe16(byte[] b, int at) => (b[at] << 8) | b[at + 1];
    private static int ReadBe32(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

    private static int ReadVarLen(byte[] b, ref int p)
    {
        var value = 0;
        for (var i = 0; i < 4; i++)
        {
            var x = b[p++];
            value = (value << 7) | (x & 0x7F);
            if ((x & 0x80) == 0) return value;
        }
        return value;
    }

    private static MidiFileData ParseMidi(byte[] b)
    {
        Assert.True(b.Length > 14 && b[0] == (byte)'M' && b[1] == (byte)'T' && b[2] == (byte)'h' && b[3] == (byte)'d');
        var trackCount = ReadBe16(b, 10);
        var division = ReadBe16(b, 12);
        var notes = new List<MidiNote>();
        var bends = new List<MidiBend>();
        var ccs = new List<MidiCc>();
        var pending = new Dictionary<(int, int), Queue<int>>();
        var p = 14;
        for (var chunk = 0; chunk < trackCount; chunk++)
        {
            var length = ReadBe32(b, p + 4);
            var end = p + 8 + length;
            var q = p + 8;
            var tick = 0;
            while (q < end)
            {
                tick += ReadVarLen(b, ref q);
                var s = b[q];
                if (s == 0xFF)
                {
                    q++;
                    q++;
                    var len = ReadVarLen(b, ref q);
                    q += len;
                    continue;
                }
                var status = s;
                q++;
                var kind = status & 0xF0;
                var channel = status & 0x0F;
                if (kind == 0xC0 || kind == 0xD0) { q++; continue; }
                var first = b[q++];
                var second = b[q++];
                if (kind == 0xB0) ccs.Add(new MidiCc(tick, channel, first, second));
                else if (kind == 0xE0) bends.Add(new MidiBend(tick, channel, first | (second << 7)));
                else
                {
                    var key = (channel, (int)first);
                    if (kind == 0x90 && second > 0)
                    {
                        if (!pending.TryGetValue(key, out var queue)) pending[key] = queue = new Queue<int>();
                        queue.Enqueue(tick);
                    }
                    else if ((kind == 0x80 || kind == 0x90) && pending.TryGetValue(key, out var queue2) && queue2.Count > 0)
                        notes.Add(new MidiNote(first, queue2.Dequeue(), tick, channel));
                }
            }
            p = end;
        }
        return new MidiFileData(division, notes, bends, ccs);
    }

    // ---------- MIDI export: per-technique assertions ----------

    [Fact]
    public void TechniqueExportAudit_Bend_EmitsWheelOnEffectChannel()
    {
        var song = BuildSong(1);
        var track = song.Tracks[0];
        var note = PutNote(track, 0, 0, 0, 5);
        note.Techniques.Add("Bend");
        note.BendTypeName = "Bend";
        note.BendPoints = new List<BendPointModel>
        {
            new() { Offset = 0, Value = 0 },
            new() { Offset = 60, Value = 4 }, // +2 semitones
        };
        var timeline = BuildTimeline(song);
        var sounded = timeline.Notes.Single(n => n.Midi == note.MidiValue);
        Assert.True(sounded.UsesEffectChannel);
        Assert.NotEqual(track.MidiChannel, sounded.Channel);
        var bends = Bends(timeline, sounded.Channel)
            .Where(e => e.TimeMs >= sounded.OnsetMs && e.TimeMs <= sounded.EndMs + 1).ToList();
        Assert.NotEmpty(bends);
        Assert.True(WheelMoves(bends), "bend curve never leaves centre");
    }

    [Fact]
    public void TechniqueExportAudit_Slide_LegatoMergesDestination()
    {
        var song = BuildSong(1);
        var track = song.Tracks[0];
        var from = PutNote(track, 0, 0, 0, 5);
        from.Techniques.Add("LegatoSlide");
        from.SlideTargetMidi = track.PitchOf(0, 7);
        var to = PutNote(track, 0, 4, 0, 7);
        var timeline = BuildTimeline(song);
        // One attack: the destination merges into the slide origin.
        Assert.Equal(1, NoteOns(timeline, from.MidiValue));
        Assert.Equal(0, NoteOns(timeline, to.MidiValue));
        var sounded = timeline.Notes.Single();
        Assert.NotNull(sounded.Continuations);
        var bends = Bends(timeline, sounded.Channel);
        Assert.True(WheelMoves(bends), "slide never moves the wheel");
    }

    [Fact]
    public void TechniqueExportAudit_Tie_MergesIntoOneNote()
    {
        var song = BuildSong(1);
        var track = song.Tracks[0];
        PutNote(track, 0, 0, 0, 5);
        var tied = PutNote(track, 0, 4, 0, 5);
        tied.Tied = true;
        var timeline = BuildTimeline(song);
        Assert.Equal(1, timeline.Notes.Count(n => n.Midi == track.PitchOf(0, 5)));
        Assert.Equal(1, NoteOns(timeline, track.PitchOf(0, 5)));
    }

    [Fact]
    public void TechniqueExportAudit_Vibrato_EmitsWheelWithoutExtraAttacks()
    {
        var song = BuildSong(1);
        var track = song.Tracks[0];
        var note = PutNote(track, 0, 0, 0, 5);
        note.Techniques.Add("Vibrato");
        var timeline = BuildTimeline(song);
        var sounded = timeline.Notes.Single(n => n.Midi == note.MidiValue);
        Assert.False(sounded.UsesEffectChannel); // vibrato shares the main channel
        Assert.Equal(1, NoteOns(timeline, note.MidiValue));
        var bends = Bends(timeline, sounded.Channel)
            .Where(e => e.TimeMs >= sounded.OnsetMs && e.TimeMs <= sounded.EndMs + 1).ToList();
        Assert.NotEmpty(bends);
        Assert.True(WheelMoves(bends, threshold: 50), "vibrato wheel looks flat");
    }

    [Fact]
    public void TechniqueExportAudit_PalmMute_ShortensButKeepsPitch()
    {
        // Cap is a quarter note (min(quarter, written)): use a half note so the cut shows.
        var plainSong = BuildSong(1);
        PutNote(plainSong.Tracks[0], 0, 0, 0, 5, denom: 2);
        var plain = BuildTimeline(plainSong).Notes.Single();

        var song = BuildSong(1);
        var track = song.Tracks[0];
        var note = PutNote(track, 0, 0, 0, 5, denom: 2);
        note.Techniques.Add("PalmMute");
        var muted = BuildTimeline(song).Notes.Single();
        Assert.Equal(plain.Midi, muted.Midi);
        Assert.True(muted.DurationMs < plain.DurationMs);
        Assert.Equal(1, NoteOns(BuildTimeline(song), note.MidiValue));
    }

    [Fact]
    public void TechniqueExportAudit_DeadNote_IsShortClickAtPitch()
    {
        var song = BuildSong(1);
        var track = song.Tracks[0];
        var note = PutNote(track, 0, 0, 0, 5);
        note.Dead = true;
        var sounded = BuildTimeline(song).Notes.Single();
        Assert.True(sounded.Dead);
        Assert.True(sounded.DurationMs <= 60, $"dead note rings {sounded.DurationMs}ms");
        Assert.Equal(1, NoteOns(BuildTimeline(song), note.MidiValue));
    }

    [Fact]
    public void TechniqueExportAudit_Trill_EmitsExtraAttacksAlternatingTarget()
    {
        var song = BuildSong(1);
        var track = song.Tracks[0];
        var note = PutNote(track, 0, 0, 0, 5);
        note.Techniques.Add("Trill");
        note.TrillTargetMidi = track.PitchOf(0, 7);
        note.TrillDurationDenominator = 16;
        var timeline = BuildTimeline(song);
        var attacks = timeline.Events.Count(e => e.IsNoteOn && e.Data1 == note.MidiValue || e.IsNoteOn && e.Data1 == note.TrillTargetMidi);
        Assert.True(attacks >= 3, $"trill emitted {attacks} attacks");
        Assert.True(NoteOns(timeline, note.TrillTargetMidi) >= 1);
    }

    [Fact]
    public void TechniqueExportAudit_TremoloPick_RepeatsInsideWrittenValue()
    {
        var song = BuildSong(1);
        var track = song.Tracks[0];
        var cell = track.Measures[0].Cells[0];
        cell.DurationDenominator = 4;
        cell.TremoloPickDenominator = 16;
        var note = new TabNote { StringIndex = 0, Fret = 5, MidiValue = track.PitchOf(0, 5) };
        note.Techniques.Add("TremoloPick");
        cell.Notes.Add(note);
        var timeline = BuildTimeline(song);
        Assert.True(NoteOns(timeline, note.MidiValue) >= 3, "tremolo pick did not repeat");
    }

    [Fact]
    public void TechniqueExportAudit_GraceNote_StealsTimeFromPrincipal()
    {
        var song = BuildSong(2);
        var track = song.Tracks[0];
        // Bar 2 so a before-beat grace has room before the beat.
        var cell = track.Measures[1].Cells[4];
        cell.DurationDenominator = 4;
        var principal = new TabNote { StringIndex = 0, Fret = 5, MidiValue = track.PitchOf(0, 5) };
        cell.Notes.Add(principal);
        var grace = new TabNote
        {
            StringIndex = 0,
            Fret = 3,
            MidiValue = track.PitchOf(0, 3),
            IsGraceNote = true,
            GraceBeforeBeat = true,
            GraceDurationSlots = 0.5,
            Velocity = 62,
        };
        grace.Techniques.Add("GraceBefore");
        cell.Notes.Insert(0, grace);
        var timeline = BuildTimeline(song);
        Assert.Equal(1, NoteOns(timeline, principal.MidiValue));
        Assert.Equal(1, NoteOns(timeline, grace.MidiValue));
        var g = timeline.Notes.Single(n => n.Midi == grace.MidiValue);
        var p = timeline.Notes.Single(n => n.Midi == principal.MidiValue);
        Assert.True(g.OnsetMs <= p.OnsetMs);
        Assert.True(g.DurationMs < p.DurationMs);
    }

    [Fact]
    public void TechniqueExportAudit_Harmonic_SoundsAtWrittenPitch()
    {
        var song = BuildSong(1);
        var track = song.Tracks[0];
        var note = PutNote(track, 0, 0, 0, 5);
        note.Techniques.Add("Harmonic");
        note.HarmonicFret = 12;
        var timeline = BuildTimeline(song);
        var sounded = timeline.Notes.Single();
        Assert.Equal(note.MidiValue, sounded.Midi);
        Assert.Equal(1, NoteOns(timeline, note.MidiValue));
        Assert.DoesNotContain(Bends(timeline, sounded.Channel),
            e => e.TimeMs >= sounded.OnsetMs && e.TimeMs <= sounded.EndMs + 1);
    }

    [Fact]
    public void TechniqueExportAudit_Whammy_EmitsWheelOnEffectChannel()
    {
        var song = BuildSong(1);
        var track = song.Tracks[0];
        var cell = track.Measures[0].Cells[0];
        cell.DurationDenominator = 4;
        var note = new TabNote { StringIndex = 0, Fret = 5, MidiValue = track.PitchOf(0, 5) };
        note.Techniques.Add("TremBar");
        cell.Notes.Add(note);
        cell.WhammyPoints = new List<BendPointModel>
        {
            new() { Offset = 0, Value = 0 },
            new() { Offset = 30, Value = -4 },
            new() { Offset = 60, Value = 0 },
        };
        var timeline = BuildTimeline(song);
        var sounded = timeline.Notes.Single();
        Assert.True(sounded.UsesEffectChannel);
        var bends = Bends(timeline, sounded.Channel)
            .Where(e => e.TimeMs >= sounded.OnsetMs && e.TimeMs <= sounded.EndMs + 1).ToList();
        Assert.NotEmpty(bends);
        Assert.True(WheelMoves(bends), "whammy curve never leaves centre");
    }

    [Fact]
    public void TechniqueExportAudit_FileRoundTrip_HasBendRangeRpnAndWheel()
    {
        var song = BuildSong(1);
        var track = song.Tracks[0];
        var note = PutNote(track, 0, 0, 0, 5);
        note.Techniques.Add("Bend");
        note.BendTypeName = "Bend";
        note.BendPoints = new List<BendPointModel>
        {
            new() { Offset = 0, Value = 0 },
            new() { Offset = 60, Value = 4 },
        };
        var path = Path.Combine(_folder, "techniques.mid");
        MidiExportService.Export(song, path);
        var midi = ParseMidi(File.ReadAllBytes(path));
        Assert.Contains(midi.Notes, n => n.Pitch == note.MidiValue);
        // RPN pitch-bend range 12 semitones is declared per played channel.
        var sensitivities = midi.Ccs.Where(c => c.Controller == 6 && c.Value == 12).ToList();
        Assert.NotEmpty(sensitivities);
        var bendChannels = midi.Bends.Select(b => b.Channel).Distinct().ToList();
        Assert.Contains(note.MidiValue == 0 ? -1 : midi.Notes[0].Channel, bendChannels);
        Assert.True(midi.Bends.Any(b => Math.Abs(b.Value - 8192) > 200), "no wheel deviation in file");
        Assert.DoesNotContain(midi.Bends, b => b.Channel == 9);
    }

    // ---------- piano roll round trip (PianoRollTabSource -> PianoRollTabWriter) ----------

    private static TabNote RollNote(TrackModel track, int stringIndex, int fret, params string[] techniques)
    {
        var note = new TabNote { StringIndex = stringIndex, Fret = fret, MidiValue = track.PitchOf(stringIndex, fret), Velocity = 90 };
        foreach (var t in techniques) note.Techniques.Add(t);
        return note;
    }

    private static (SongProject Song, TrackModel Track) RollBar(params (int Slot, int Denominator, TabNote? Note)[] beats)
    {
        var song = SongDocument.BlankProject();
        var track = song.Tracks[0];
        track.Measures[0].Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        foreach (var (slot, denominator, note) in beats)
            track.Measures[0].Cells[slot] = note is null
                ? new TabCell { DurationDenominator = denominator, IsRest = true }
                : new TabCell { DurationDenominator = denominator, Notes = { note } };
        return (song, track);
    }

    private static List<PianoRollNote> RollRead(SongProject song, TrackModel track) =>
        new PianoRollTabSource().Read(song, track, PianoRollTimeMap.Written(song));

    private static bool RollWrite(SongProject song, TrackModel track, List<PianoRollNote> notes) =>
        new PianoRollTabSource().Write(song, track, PianoRollTimeMap.Written(song), notes);

    private static TabNote SoleNote(TrackModel track, int pitch)
    {
        return track.Measures[0].Cells.SelectMany(c => c.Notes).Single(n => n.MidiValue == pitch);
    }

    [Fact]
    public void TechniqueExportAudit_PianoRoll_NoOpKeepsMarks()
    {
        var (song, track) = RollBar();
        var bend = RollNote(track, 0, 5, "Bend");
        bend.BendPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = 4 } };
        var slide = RollNote(track, 1, 3, "LegatoSlide");
        slide.SlideTargetMidi = track.PitchOf(1, 5);
        var trill = RollNote(track, 2, 2, "Trill");
        trill.TrillTargetMidi = track.PitchOf(2, 2) + 2;
        trill.TrillDurationDenominator = 16;
        var vib = RollNote(track, 0, 7, "Vibrato");
        var palm = RollNote(track, 1, 5, "PalmMute");
        track.Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, Notes = { bend } };
        track.Measures[0].Cells[4] = new TabCell { DurationDenominator = 4, Notes = { slide } };
        track.Measures[0].Cells[8] = new TabCell { DurationDenominator = 4, Notes = { trill } };
        track.Measures[0].Cells[12] = new TabCell { DurationDenominator = 4, Notes = { vib, palm } };

        var before = RollRead(song, track);
        Assert.False(RollWrite(song, track, before.ToList())); // no-op: nothing to write

        Assert.Equal(2, SoleNote(track, bend.MidiValue).BendPoints.Count);
        Assert.Equal(slide.SlideTargetMidi, track.Measures[0].Cells.SelectMany(c => c.Notes).First(n => n.Techniques.Contains("LegatoSlide")).SlideTargetMidi);
        Assert.Equal(trill.TrillTargetMidi, track.Measures[0].Cells.SelectMany(c => c.Notes).First(n => n.Techniques.Contains("Trill")).TrillTargetMidi);
        Assert.Contains("Vibrato", SoleNote(track, vib.MidiValue).Techniques);
        Assert.Contains("PalmMute", SoleNote(track, palm.MidiValue).Techniques);
    }

    [Fact]
    public void TechniqueExportAudit_PianoRoll_MoveKeepsNoteMarks()
    {
        var (song, track) = RollBar();
        var bend = RollNote(track, 0, 5, "Bend");
        bend.BendPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = 4 } };
        var slide = RollNote(track, 1, 3, "LegatoSlide");
        slide.SlideTargetMidi = track.PitchOf(1, 5);
        track.Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, Notes = { bend } };
        track.Measures[0].Cells[4] = new TabCell { DurationDenominator = 4, Notes = { slide } };
        track.Measures[0].Cells[8] = new TabCell { DurationDenominator = 4, IsRest = true };
        track.Measures[0].Cells[12] = new TabCell { DurationDenominator = 4, IsRest = true };

        var notes = RollRead(song, track);
        var moved = notes.Select(n => n.Pitch == bend.MidiValue ? n with { Start = n.Start + 2 } : n).ToList();
        Assert.True(RollWrite(song, track, moved));

        var kept = track.Measures[0].Cells.SelectMany(c => c.Notes).First(n => n.Techniques.Contains("Bend"));
        Assert.Equal(2, kept.BendPoints.Count);
        Assert.Equal(4, kept.BendPoints[1].Value);
        var keptSlide = track.Measures[0].Cells.SelectMany(c => c.Notes).First(n => n.Techniques.Contains("LegatoSlide"));
        Assert.Equal(slide.SlideTargetMidi, keptSlide.SlideTargetMidi);
    }

    [Fact]
    public void TechniqueExportAudit_PianoRoll_ResizeKeepsMarks()
    {
        var (song, track) = RollBar();
        var vib = RollNote(track, 0, 5, "Vibrato", "PalmMute");
        track.Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, Notes = { vib } };
        track.Measures[0].Cells[4] = new TabCell { DurationDenominator = 4, IsRest = true };

        var notes = RollRead(song, track);
        var resized = notes.Select(n => n.Pitch == vib.MidiValue ? n with { Length = 0.5 } : n).ToList();
        Assert.True(RollWrite(song, track, resized));

        var kept = track.Measures[0].Cells.SelectMany(c => c.Notes).First(n => n.MidiValue == vib.MidiValue);
        Assert.Contains("Vibrato", kept.Techniques);
        Assert.Contains("PalmMute", kept.Techniques);
    }

    [Fact]
    public void TechniqueExportAudit_PianoRoll_TremoloCellFollowsMove()
    {
        var (song, track) = RollBar();
        var note = RollNote(track, 0, 5, "TremoloPick");
        track.Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, TremoloPickDenominator = 16, Notes = { note } };
        track.Measures[0].Cells[4] = new TabCell { DurationDenominator = 4, IsRest = true };

        var notes = RollRead(song, track);
        var moved = notes.Select(n => n with { Start = n.Start + 1 }).ToList();
        Assert.True(RollWrite(song, track, moved));

        var dest = track.Measures[0].Cells.First(c => !c.IsRest && c.Notes.Count > 0 && c.Notes[0].Techniques.Contains("TremoloPick"));
        Assert.Equal(16, dest.TremoloPickDenominator);
    }

    [Fact]
    public void TechniqueExportAudit_PianoRoll_WhammyCellFollowsMove()
    {
        var (song, track) = RollBar();
        var note = RollNote(track, 0, 5, "TremBar");
        track.Measures[0].Cells[0] = new TabCell
        {
            DurationDenominator = 4,
            Notes = { note },
            WhammyPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = -4 } },
        };
        track.Measures[0].Cells[4] = new TabCell { DurationDenominator = 4, IsRest = true };

        var notes = RollRead(song, track);
        var moved = notes.Select(n => n with { Start = n.Start + 1 }).ToList();
        Assert.True(RollWrite(song, track, moved));

        var dest = track.Measures[0].Cells.First(c => !c.IsRest && c.Notes.Count > 0 && c.Notes[0].Techniques.Contains("TremBar"));
        Assert.NotEmpty(dest.WhammyPoints);
    }

    [Fact]
    public void TechniqueExportAudit_PianoRoll_BeatMarksFollowResize()
    {
        var (song, track) = RollBar();
        var note = RollNote(track, 0, 5, "TremoloPick");
        track.Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, TremoloPickDenominator = 16, Notes = { note } };
        track.Measures[0].Cells[4] = new TabCell { DurationDenominator = 4, IsRest = true };

        var notes = RollRead(song, track);
        var resized = notes.Select(n => n with { Length = 0.5 }).ToList();
        Assert.True(RollWrite(song, track, resized));

        var dest = track.Measures[0].Cells.First(c => !c.IsRest && c.Notes.Count > 0 && c.Notes[0].Techniques.Contains("TremoloPick"));
        Assert.Equal(16, dest.TremoloPickDenominator);
    }

    [Fact]
    public void TechniqueExportAudit_PianoRoll_BeatMarksNoOpUnchanged()
    {
        var (song, track) = RollBar();
        var trem = RollNote(track, 0, 5, "TremoloPick");
        track.Measures[0].Cells[0] = new TabCell { DurationDenominator = 4, TremoloPickDenominator = 16, Notes = { trem } };
        var bar = RollNote(track, 0, 7, "TremBar");
        track.Measures[0].Cells[4] = new TabCell
        {
            DurationDenominator = 4,
            Notes = { bar },
            WhammyPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = -4 } },
        };

        var before = RollRead(song, track);
        Assert.False(RollWrite(song, track, before.ToList()));

        Assert.Equal(16, track.Measures[0].Cells[0].TremoloPickDenominator);
        Assert.Equal(2, track.Measures[0].Cells[4].WhammyPoints.Count);
    }
}
