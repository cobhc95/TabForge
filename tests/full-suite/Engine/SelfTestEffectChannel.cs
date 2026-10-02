using System.IO;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;

namespace TabForge;

/// <summary>GP5 effect channel (Audit 3 P-01): bent notes play on the track's second channel (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static TrackModel EffectChannelTrack(int midiChannel, bool bent)
    {
        var track = new TrackModel { Name = $"G{midiChannel}", Kind = TrackKind.Guitar, MidiChannel = midiChannel, Measures = TemplateFactory.Measures(1) };
        var cell = track.Measures[0].Cells[0];
        cell.DurationDenominator = 1;   // a whole note: the held note rings through the whole bend
        cell.Notes.Add(new TabNote { StringIndex = 0, Fret = 0, MidiValue = 64 });
        var second = new TabNote { StringIndex = 1, Fret = 0, MidiValue = 59 };
        if (bent) { second.BendPoints.Add(new BendPointModel { Offset = 0, Value = 0 }); second.BendPoints.Add(new BendPointModel { Offset = 60, Value = 4 }); }   // +2 semitones
        cell.Notes.Add(second);
        return track;
    }

    private static void TestEffectChannel()
    {
        var project = new SongProject { Tempo = 120 };
        project.Tracks.Add(EffectChannelTrack(0, bent: true));
        var drums = new TrackModel { Name = "Dr", Kind = TrackKind.Drums, MidiChannel = 9, Measures = TemplateFactory.Measures(1) };
        drums.Measures[0].Cells[0].DurationDenominator = 4;
        drums.Measures[0].Cells[0].Notes.Add(new TabNote { Fret = 0, MidiValue = 38 });
        project.Tracks.Add(drums);

        var main = ChannelAllocator.Assign(project);
        var effect = ChannelAllocator.AssignEffect(project, main);
        Check("effect channel: a track with a bent note gets a second channel that no track uses, never percussion",
            effect[0] >= 0 && effect[0] != main[0] && effect[0] != main[1] && effect[0] != ChannelAllocator.PercussionChannel, string.Join(",", effect));
        Check("effect channel: drums get none", effect[1] == -1);

        var tl = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        var ch0 = main[0]; var chFx = effect[0];
        var played = tl.Events.Where(e => e.TrackIndex == 0 && !e.IsSetup).ToList();
        Check("effect channel: the held note plays on the track's own channel, the bent note on the effect channel",
            played.Any(e => e.IsNoteOn && e.Data1 == 64 && (e.Status & 0x0F) == ch0) && played.Any(e => e.IsNoteOn && e.Data1 == 59 && (e.Status & 0x0F) == chFx)
            && !played.Any(e => e.IsNoteOn && e.Data1 == 59 && (e.Status & 0x0F) == ch0));
        Check("effect channel: the note's channel is recorded on its NoteEvent",
            tl.Notes.Any(n => n.Midi == 59 && n.UsesEffectChannel && n.Channel == chFx) && tl.Notes.Any(n => n.Midi == 64 && !n.UsesEffectChannel && n.Channel == ch0));
        var wheelMain = played.Where(e => (e.Status & 0xF0) == 0xE0 && (e.Status & 0x0F) == ch0).ToList();
        var wheelFx = played.Where(e => (e.Status & 0xF0) == 0xE0 && (e.Status & 0x0F) == chFx).ToList();
        Check("effect channel: the held note's pitch stays unbent (no pitch wheel on its channel while the bend moves the effect channel)",
            wheelMain.Count == 0 && wheelFx.Any(e => (e.Data1 | (e.Data2 << 7)) != 8192), $"main {wheelMain.Count}, effect {wheelFx.Count}");

        // The effect channel is set up like the main one: program, volume, pan, sends, bend range, and it is in ChannelSetup (mixer refresh, panic, loops).
        var setupMain = tl.ChannelSetup.Where(e => e.TrackIndex == 0 && (e.Status & 0x0F) == ch0).Select(e => (e.Status & 0xF0, e.Data1, e.Data2)).ToList();
        var setupFx = tl.ChannelSetup.Where(e => e.TrackIndex == 0 && (e.Status & 0x0F) == chFx).Select(e => (e.Status & 0xF0, e.Data1, e.Data2)).ToList();
        Check("effect channel: program, CC7 / CC10 / sends and the pitch-bend range are copied to it",
            setupFx.Count > 0 && setupMain.SequenceEqual(setupFx), $"{setupMain.Count} vs {setupFx.Count}");

        // Without a bend nothing changes: no second channel, everything on the track's own channel.
        var plain = new SongProject { Tempo = 120 };
        plain.Tracks.Add(EffectChannelTrack(0, bent: false));
        Check("effect channel: a track without bends gets no second channel",
            ChannelAllocator.AssignEffect(plain, ChannelAllocator.Assign(plain))[0] == -1
            && MidiTimelineBuilder.Build(plain, new PlaybackOptions()).Events.Select(e => e.Status & 0x0F).Distinct().Count() == 1);

        // MIDI export keeps both channels in the track's chunk (events carry the track index).
        var path = Path.Combine(Path.GetTempPath(), $"tf-effect-{Guid.NewGuid():N}.mid");
        try
        {
            TabForge.Services.MidiExportService.Export(project, path);
            var bytes = File.ReadAllBytes(path);
            Check("effect channel: the MIDI export writes note-ons on the effect channel", bytes.Length > 0 && ContainsNoteOn(bytes, chFx));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }

        // No free channel: 15 melodic tracks use every channel but percussion, so the bent note stays on its own channel as before.
        var full = new SongProject { Tempo = 120 };
        for (var c = 0; c < 16; c++) if (c != ChannelAllocator.PercussionChannel) full.Tracks.Add(EffectChannelTrack(c, bent: c == 0));
        var fullMain = ChannelAllocator.Assign(full);
        var fullEffect = ChannelAllocator.AssignEffect(full, fullMain);
        var fullTl = MidiTimelineBuilder.Build(full, new PlaybackOptions());
        Check("effect channel: with no free channel the bent note plays on the track's own channel",
            fullEffect.All(c => c == -1) && fullTl.Events.Where(e => e.TrackIndex == 0 && e.IsNoteOn).All(e => (e.Status & 0x0F) == fullMain[0]));
    }

    private static bool ContainsNoteOn(byte[] file, int channel)
    {
        var status = (byte)(0x90 | channel);
        for (var i = 0; i + 2 < file.Length; i++)
            if (file[i] == status && file[i + 1] is 59 && file[i + 2] > 0) return true;
        return false;
    }
}
