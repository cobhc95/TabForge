using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: a track moved in the list (also while the song plays) keeps its MIDI channel, so its notes keep their own instrument and route.
public class PlaybackTrackReorderTests
{
    private static TrackModel Make(string name, TrackKind kind, int program) =>
        new() { Name = name, Kind = kind, MidiChannel = 0, MidiProgram = program, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, Measures = TemplateFactory.Measures(1) };

    [Fact]
    public void Tracks_sharing_a_stored_channel_keep_their_channel_after_a_move()
    {
        var song = new SongProject { Title = "T", Tempo = 120 };
        var guitar = Make("Guitar", TrackKind.Guitar, 30); var bass = Make("Bass", TrackKind.Bass, 33); var keys = Make("Keys", TrackKind.Keys, 0);
        song.Tracks.AddRange(new[] { guitar, keys, bass });
        var before = ChannelAllocator.Assign(song);
        var channelOf = song.Tracks.Select((t, i) => (t, ch: before[i])).ToDictionary(x => x.t, x => x.ch);
        Assert.True(song.MoveTrack(2, 0));
        var after = ChannelAllocator.Assign(song);
        for (var i = 0; i < song.Tracks.Count; i++) Assert.Equal(channelOf[song.Tracks[i]], after[i]);
    }

    [Fact]
    public void Live_state_by_track_index_follows_the_tracks_to_the_new_order()
    {
        var engine = new PlaybackEngine(new NullMidiOutput());
        var a = System.Guid.NewGuid(); var b = System.Guid.NewGuid();
        engine.SetAudibleForTest(new[] { true, false });   // a plays, b is muted
        engine.RemapTrackState(new[] { a, b }, new[] { b, a });
        Assert.Equal(new[] { false, true }, engine.AudibleForTest());
    }

    private sealed class Capture : IMidiOutput
    {
        public readonly List<(int Status, int D1, int D2)> Sent = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = System.Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { lock (Sent) Sent.Add((status, data1, data2)); }
        public int NoteOns(int channel, int from) { lock (Sent) return Sent.Skip(from).Count(e => e.Status == (0x90 | channel) && e.D2 > 0); }
        public int Count { get { lock (Sent) return Sent.Count; } }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    [Fact]
    public void A_track_moved_while_playing_keeps_sounding_on_its_channel_after_the_swap()
    {
        var song = new SongProject { Title = "T", Tempo = 240 };
        var tracks = new[] { Make("Guitar", TrackKind.Guitar, 30), Make("Keys", TrackKind.Keys, 0), Make("Bass", TrackKind.Bass, 33) };
        for (var i = 0; i < 3; i++) tracks[i].MidiChannel = i;
        foreach (var t in tracks)
        {
            t.Measures = TemplateFactory.Measures(40);
            foreach (var m in t.Measures) { m.Cells[0].DurationDenominator = 4; m.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = t.PitchOf(0, 3) }); }
        }
        song.Tracks.AddRange(tracks);
        var output = new Capture();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions(), _ => { }, () => { });
        Wait(() => output.NoteOns(2, 0) > 0);
        song.MoveTrack(2, 0);
        engine.RefreshArrangement(song, Enumerable.Range(0, 40).ToArray());
        System.Threading.Thread.Sleep(600);   // past the next bar line (a bar is 0.25 s)
        var mark = output.Count;
        System.Threading.Thread.Sleep(1200);
        Assert.True(output.NoteOns(2, mark) > 0, "bass (channel 2) still plays after the swap");
        Assert.True(output.NoteOns(0, mark) > 0 && output.NoteOns(1, mark) > 0);
    }

    [Fact]
    public void A_live_mix_push_between_the_move_and_the_swap_sends_each_channel_its_own_tracks_sound()
    {
        var song = new SongProject { Title = "T", Tempo = 60 };   // slow: the next bar line is far away, the playing timeline keeps the old order
        var tracks = new[] { Make("Guitar", TrackKind.Guitar, 30), Make("Keys", TrackKind.Keys, 5), Make("Bass", TrackKind.Bass, 33) };
        for (var i = 0; i < 3; i++) { tracks[i].MidiChannel = i; tracks[i].Measures = TemplateFactory.Measures(40); }
        song.Tracks.AddRange(tracks);
        var output = new Capture();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions(), _ => { }, () => { });
        Wait(() => engine.PublishedTrackIds.Length == 3 && output.Count > 0);
        System.Threading.Thread.Sleep(200);
        song.MoveTrack(2, 0);   // bass first; the timeline still numbers guitar first
        var mark = output.Count;
        engine.SetMuteSolo(song);
        engine.SetMasterVolume(song, 80);
        System.Threading.Thread.Sleep(200);
        List<(int Status, int D1, int D2)> sent; lock (output.Sent) sent = output.Sent.Skip(mark).ToList();
        var programs = new[] { 30, 5, 33 };
        foreach (var e in sent.Where(e => (e.Status & 0xF0) == 0xC0)) Assert.Equal(programs[e.Status & 0x0F], e.D1);
        Assert.Contains(sent, e => e.Status == (0xC0 | 2) && e.D1 == 33);   // the bass's own channel got the bass sound
        Assert.DoesNotContain(sent, e => e.Status == 0xB2 && e.D1 == 7 && e.D2 == 0);   // and a level above zero
    }

    private static SongProject MatrixSong(int tempo)
    {
        var song = new SongProject { Title = "T", Tempo = tempo };
        var programs = new[] { 30, 5, 33, 48, 24 };
        for (var i = 0; i < 5; i++)
        {
            var t = Make("T" + i, i == 2 ? TrackKind.Bass : TrackKind.Guitar, programs[i]);
            t.MidiChannel = i; t.Pan = 20 + i * 20; t.Volume = 60 + i * 8; t.Measures = TemplateFactory.Measures(60);
            song.Tracks.Add(t);
        }
        song.Tracks[1].Mute = true;
        return song;
    }

    // Every live piece of per-track state, found by track id, must be the track's own: the program, level and pan on its channel, and its audibility.
    private static void AssertOwnState(PlaybackEngine engine, SongProject song, Capture output, int mark, string when)
    {
        List<(int Status, int D1, int D2)> sent; lock (output.Sent) sent = output.Sent.Skip(mark).ToList();
        foreach (var track in song.Tracks)
        {
            var ch = track.MidiChannel;
            foreach (var e in sent.Where(e => (e.Status & 0x0F) == ch))
            {
                if ((e.Status & 0xF0) == 0xC0) Assert.True(track.MidiProgram == e.D1, $"{when}: program on channel {ch} sent {e.D1} want {track.MidiProgram} track {track.Name} setup " + string.Join(";", engine.Timeline!.ChannelSetup.Where(x => (x.Status & 0xF0) == 0xC0).Select(x => $"{x.TrackIndex}:{x.Status & 15}:{x.Data1}")) + " ids " + string.Join(",", engine.PublishedTrackIds.Select(g => song.Tracks.First(t => t.Id == g).Name)));
                if ((e.Status & 0xF0) == 0xB0 && e.D1 == 10) Assert.True(MixerGroups.Pan(song, track) == e.D2, $"{when}: pan on channel {ch}");
            }
        }
        var ids = engine.PublishedTrackIds; var mask = engine.AudibleForTest();
        for (var i = 0; i < ids.Length && mask is not null; i++)
        {
            var track = song.Tracks.First(t => t.Id == ids[i]);
            Assert.True(mask[i] == MixerGroups.IsAudible(song, track, false), $"{when}: audibility of {track.Name}");
        }
    }

    [Fact]
    public void Several_quick_moves_keep_every_tracks_own_state_before_and_after_the_swaps()
    {
        var song = MatrixSong(60);
        var output = new Capture();
        using var engine = new PlaybackEngine(output);
        engine.Start(song, new PlaybackOptions(), _ => { }, () => { });
        Wait(() => engine.PublishedTrackIds.Length == 5 && output.Count > 0);
        var random = new System.Random(7);
        for (var round = 0; round < 6; round++)
        {
            var from = random.Next(5); var to = random.Next(5);
            var mark = output.Count;
            song.MoveTrack(from, to);   // a second and third move land before the first swap
            if (round % 2 == 1) { song.Tracks[random.Next(5)].Solo = round == 3; }
            engine.SetMuteSolo(song);
            engine.PushLiveChannelState(song);
            AssertOwnState(engine, song, output, mark, "after move " + round);
        }
        engine.RefreshArrangement(song, Enumerable.Range(0, 60).ToArray());
        Wait(() => engine.PublishedTrackIds.SequenceEqual(song.Tracks.Select(t => t.Id)));   // the next bar line (4 s at 60 bpm): the swap lands
        var after = output.Count;
        engine.SetMuteSolo(song); engine.PushLiveChannelState(song);
        AssertOwnState(engine, song, output, after, "after the swap");
        Assert.Equal(song.Tracks.Select(t => t.Id), engine.PublishedTrackIds);
    }

    [Fact]
    public void The_active_track_stays_the_same_track_through_many_moves()
    {
        var song = MatrixSong(120);
        var random = new System.Random(11);
        var active = 3; var id = song.Tracks[active].Id;
        for (var i = 0; i < 40; i++)
        {
            var before = song.Tracks.Select(t => t.Id).ToList();
            song.MoveTrack(random.Next(5), random.Next(5));
            active = TabForge.Avalonia.MainWindowTrackStructure.ActiveAfter(before, song.Tracks.Select(t => t.Id).ToList(), active);
            Assert.Equal(id, song.Tracks[active].Id);
        }
    }

    private static void Wait(System.Func<bool> c) { var end = System.DateTime.UtcNow.AddSeconds(5); while (!c() && System.DateTime.UtcNow < end) System.Threading.Thread.Sleep(20); }
}
