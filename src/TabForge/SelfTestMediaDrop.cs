using System.IO;
using System.Windows;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Audio / MIDI files dropped on the timeline: the drop plan (track, snapped start, length, lane, new lane, files end to end),
/// MIDI files to clips on the song's tempo map, virtual (FileGroupDescriptorW) files, the drop preview geometry and the routing
/// of song files (part of <see cref="SelfTest"/>).
/// </summary>
public static partial class SelfTest
{
    private static SongProject DropSong(int tracks = 3, int bars = 12, int num = 4, int den = 4, int tempo = 120)
    {
        var song = new SongProject { Tempo = tempo, TimeSignatureNumerator = num, TimeSignatureDenominator = den };
        for (var t = 0; t < tracks; t++)
            song.Tracks.Add(new TrackModel
            {
                Name = "T" + t, Kind = TrackKind.Guitar,
                Measures = Enumerable.Range(0, bars).Select(_ => new MeasureModel { TimeSigNum = num, TimeSigDenom = den }).ToList(),
            });
        return song;
    }

    private static DropItem AudioItem(string name, double seconds, bool transient = false) =>
        new() { Path = $@"C:\Media\{name}.wav", Name = name, Kind = DropItemKind.Audio, Seconds = seconds, Transient = transient };

    private static DropItem MidiItem(string name, double quarters, int channel = 0) => new()
    {
        Path = $@"C:\Media\{name}.mid", Name = name, Kind = DropItemKind.Midi,
        Midi = new MidiFileData { Ppq = 96, LengthTicks = (long)(quarters * 96), Notes = new() { new MidiFileNote(0, 48, channel, 36, 100) } },
    };

    private static AudioClip LaneClip(double start, double length, int lane, bool midi = false) => new()
    {
        File = midi ? "" : "x.wav", Name = "c", StartSec = start, SourceLengthSec = length, FileLengthSec = length, Lane = lane,
        Notes = midi ? new List<ClipNote>() : null,
    };

    private static bool Near(double a, double b, double tolerance = 1e-6) => Math.Abs(a - b) <= tolerance;

    /// <summary>Where a drop lands: lane choice (hovered, first free, new), files end to end, new track kinds, refusals, the take rules.</summary>
    private static void TestMediaDropPlan()
    {
        var song = DropSong();
        var time = SongQuarterMap.For(song);
        Check("drop plan: the song's quarter map gives 0.5 s a beat at 120 BPM", Near(time.SecAt(4), 2.0, 1e-3) && Near(time.QuarterAt(3.0), 6, 1e-3));

        var two = new[] { AudioItem("A", 3.0), AudioItem("B", 1.5) };
        var plan = MediaDrop.Plan(song, two, 1, 0, 2.0, time);
        Check("drop plan: two files lie end to end from the drop point", plan.Valid && plan.Clips.Count == 2 && Near(plan.Clips[0].StartSec, 2.0) && Near(plan.Clips[1].StartSec, 5.0) && Near(plan.EndSec, 6.5));
        Check("drop plan: a track without lanes gets its first lane (a new lane)", plan.Lane == 0 && plan.NewLane && !plan.NewTrack && plan.TrackIndex == 1);

        var track = song.Tracks[1];
        track.AudioClips.Add(LaneClip(4, 4, 0));
        ClipLanes.Ensure(track, 1);
        plan = MediaDrop.Plan(song, two, 1, 0, 2.0, time);
        Check("drop plan: overlapping the clip on lane 1 makes a new lane 2", plan.Lane == 1 && plan.NewLane);
        track.AudioClips.Add(LaneClip(10, 2, 1));
        ClipLanes.Ensure(track, 2);
        plan = MediaDrop.Plan(song, new[] { AudioItem("A", 4.5) }, 1, 1, 0, time);
        Check("drop plan: the hovered lane is used when it is free over the whole span", plan.Lane == 1 && !plan.NewLane);
        plan = MediaDrop.Plan(song, new[] { AudioItem("A", 4.5) }, 1, 1, 8.5, time);
        Check("drop plan: a busy hovered lane falls back to the first free lane", plan.Lane == 0 && !plan.NewLane);
        plan = MediaDrop.Plan(song, new[] { AudioItem("A", 3) }, 1, 0, 7.5, time);
        Check("drop plan: overlapping both lanes opens a third", plan.Lane == 2 && plan.NewLane);

        plan = MediaDrop.Plan(song, new[] { AudioItem("A", 1) }, song.Tracks.Count, 0, 0, time);
        Check("drop plan: below the last track an audio file makes a new (audio) track", plan.Valid && plan.NewTrack && plan.NewTrackKind == TrackKind.Guitar && plan.Lane == 0 && plan.NewLane);
        Check("drop plan: a drum-channel MIDI file below the last track makes a drum track",
            MediaDrop.Plan(song, new[] { MidiItem("Fill", 4, channel: 9) }, song.Tracks.Count, 0, 0, time).NewTrackKind == TrackKind.Drums);
        Check("drop plan: a channel-1 groove from a drum plug-in (named so) makes a drum track, a riff a keys track",
            MediaDrop.Plan(song, new[] { MidiItem("Groove 12", 4) }, song.Tracks.Count, 0, 0, time).NewTrackKind == TrackKind.Drums
            && MediaDrop.Plan(song, new[] { MidiItem("Piano riff", 4) }, song.Tracks.Count, 0, 0, time).NewTrackKind == TrackKind.Keys);

        var bus = DropSong(1);
        bus.Tracks[0].BusSlot = 0;
        Check("drop plan: a bus track takes no clips", !MediaDrop.Plan(bus, two, 0, 0, 0, SongQuarterMap.For(bus)).Valid);
        Check("drop plan: off every track (the ruler) is refused", MediaDrop.Plan(song, two, -1, 0, 0, time).Problem == "Drop on a track");
        var broken = new DropItem { Path = "x.wav", Name = "x", Problem = "x could not be read as audio" };
        Check("drop plan: only unreadable files: refused with the reason", MediaDrop.Plan(song, new[] { broken }, 0, 0, 0, time).Problem == "x could not be read as audio");
        plan = MediaDrop.Plan(song, new[] { broken, AudioItem("A", 1) }, 0, 0, 0, time);
        Check("drop plan: an unreadable file among good ones is left out", plan.Valid && plan.Clips.Count == 1);
        plan = MediaDrop.Plan(song, new[] { AudioItem("Net", 0) }, 0, 0, 0, time);
        Check("drop plan: a file not measured yet (network) shows one bar until the drop measures it", plan.Valid && plan.Estimated && Near(plan.EndSec, 2.0, 1e-3));
        plan = MediaDrop.Plan(song, new[] { MidiItem("Groove", 8), AudioItem("A", 1) }, 0, 0, 2.0, time);
        Check("drop plan: a MIDI file is as long as its beats at the song tempo (8 beats = 4 s), the next file follows it",
            Near(plan.Clips[0].LengthSec, 4, 1e-3) && Near(plan.Clips[1].StartSec, 6, 1e-3));

        // ---- the take rules on the planned lane ----
        var takes = new TrackModel();
        takes.AudioClips.Add(LaneClip(0, 4, 0));
        takes.AudioClips.Add(LaneClip(0, 4, 1, midi: true));
        ClipLanes.Ensure(takes, 2);
        var dropped = LaneClip(1, 2, 0);
        MediaDrop.AddClips(takes, new[] { dropped }, 2);
        Check("drop: an overlapping audio drop plays on its new lane, the older audio lane greys out, the MIDI lane keeps playing",
            dropped.Lane == 2 && ClipLanes.Plays(takes, 2) && !ClipLanes.Plays(takes, 0) && ClipLanes.Plays(takes, 1) && takes.Lanes.Count == 3);
        var free = new TrackModel();
        free.AudioClips.Add(LaneClip(0, 4, 0));
        ClipLanes.Ensure(free, 1);
        MediaDrop.AddClips(free, new[] { LaneClip(5, 2, 0) }, 0);
        Check("drop: a drop that overlaps nothing changes no lane's playing state", free.Lanes.Count == 1 && ClipLanes.Plays(free, 0) && free.AudioClips.Count == 2);
    }

    // ---- a tiny Standard MIDI File writer for the tests ----
    private static byte[] Vlq(long v)
    {
        var bytes = new List<byte> { (byte)(v & 0x7F) };
        while ((v >>= 7) > 0) bytes.Insert(0, (byte)(0x80 | (v & 0x7F)));
        return bytes.ToArray();
    }

    private static byte[] Track(params (long Delta, byte[] Event)[] events)
    {
        var body = new List<byte>();
        foreach (var (delta, ev) in events) { body.AddRange(Vlq(delta)); body.AddRange(ev); }
        var chunk = new List<byte>(System.Text.Encoding.ASCII.GetBytes("MTrk"));
        chunk.AddRange(BitConverter.GetBytes(body.Count).Reverse());
        chunk.AddRange(body);
        return chunk.ToArray();
    }

    private static byte[] Smf(int division, params byte[][] tracks)
    {
        var file = new List<byte>(System.Text.Encoding.ASCII.GetBytes("MThd")) { 0, 0, 0, 6, 0, 1, 0, (byte)tracks.Length, (byte)(division >> 8), (byte)(division & 0xFF) };
        foreach (var t in tracks) file.AddRange(t);
        return file.ToArray();
    }

    /// <summary>MIDI files to clips: merged tracks, running status, drum channel choice, length to whole beats, the song's tempo map (3/4).</summary>
    private static void TestMidiFileToClip()
    {
        const int q = 96;
        var tempoTrack = Track((0, new byte[] { 0xFF, 0x51, 3, 0x0A, 0x2C, 0x2B }), (0, new byte[] { 0xFF, 0x58, 4, 4, 2, 24, 8 }), (0, new byte[] { 0xFF, 0x2F, 0 }));   // 90 BPM in the file
        var drums = Track(
            (0, new byte[] { 0x99, 36, 100 }), (q / 2, new byte[] { 36, 0 }),            // kick; running status, velocity 0 = off
            (q / 2, new byte[] { 0x99, 38, 90 }), (q / 2, new byte[] { 0x89, 38, 0 }),     // snare on beat 2
            (q * 3 / 2, new byte[] { 0x99, 42, 80 }), (q / 4, new byte[] { 0x89, 42, 0 }), // hat on beat 4 (quarter 3)
            (q / 2, new byte[] { 0xFF, 0x2F, 0 }));                                       // ends at 3.75 beats
        var bass = Track((q * 2, new byte[] { 0x90, 40, 70 }), (q / 2, new byte[] { 0x80, 40, 0 }), (0, new byte[] { 0xFF, 0x2F, 0 }));
        var file = MidiFileImport.Parse(Smf(q, tempoTrack, drums, bass));
        Check("MIDI file: all tracks are merged (4 notes), musical ticks at 96 per quarter", file.Notes.Count == 4 && file.Ppq == q && file.Musical);
        Check("MIDI file: running status and velocity-0 note-offs are read", file.Notes[0] is { Pitch: 36, OnTick: 0, OffTick: q / 2, Channel: 9 });
        Check("MIDI file: the length (3.75 beats) is rounded up to whole beats", Near(file.LengthQuarters, 4));
        Check("MIDI file: a drum track takes the drum channel, a melodic track the rest",
            file.NotesFor(drumTrack: true).All(n => n.Channel == 9) && file.NotesFor(true).Count() == 3 && file.NotesFor(false).Single().Pitch == 40);

        // A 3/4 song at 120 BPM: bars of 1.5 s. A 4-beat groove dropped on bar 2 runs into bar 3 by one beat; the file's 90 BPM is ignored.
        var song = DropSong(1, 8, 3, 4, 120);
        var time = SongQuarterMap.For(song);
        Check("MIDI file: the 3/4 song's bars are 3 quarters (1.5 s) long", Near(time.SecAt(3), 1.5, 1e-3) && Near(time.SecAt(6), 3.0, 1e-3));
        var (notes, length) = MidiFileImport.ToClipNotes(file, 1.5, time, drumTrack: true);
        var hat = notes.Single(n => n.Pitch == 42);
        Check("MIDI file: placed at the song tempo, beat 4 of the groove lands exactly on the next 3/4 bar line", Near(1.5 + hat.StartSec, 3.0, 1e-6));
        Check("MIDI file: the clip is 4 beats long at the song tempo (2 s) and notes keep their lengths", Near(length, 2.0, 1e-6) && Near(notes.Single(n => n.Pitch == 36).LengthSec, 0.25, 1e-6));
        Check("MIDI file: velocities are kept", notes.Single(n => n.Pitch == 38).Velocity == 90);

        var smpte = MidiFileImport.Parse(Smf(0xE728, Track((500, new byte[] { 0x90, 60, 64 }), (250, new byte[] { 0x80, 60, 0 }), (0, new byte[] { 0xFF, 0x2F, 0 }))));
        var (timed, timedLength) = MidiFileImport.ToClipNotes(smpte, 0, time, drumTrack: false);
        Check("MIDI file: an SMPTE-time file (25 fps x 40) keeps its own seconds", !smpte.Musical && Near(timed[0].StartSec, 0.5) && Near(timed[0].LengthSec, 0.25) && Near(timedLength, 0.75));

        var failures = 0;
        foreach (var bad in new[] { new byte[] { 1, 2, 3 }, Smf(q).Take(10).ToArray(), Smf(q, Track((0, new byte[] { 0xFF, 0x2F, 0 }))), Smf(q, Track((0, new byte[] { 40, 1 }))) })
            try { MidiFileImport.Parse(bad); } catch (InvalidDataException) { failures++; }
        Check("MIDI file: garbage, a cut-short header, a file without notes and data without a status are refused cleanly", failures == 4);

        var folder = Path.Combine(Path.GetTempPath(), "tf-selftest-mididrop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "Groove 7.mid");
            File.WriteAllBytes(path, Smf(q, tempoTrack, drums, bass));
            var item = MediaDropSession.Measure(path, DropItemKind.Midi, MediaDrop.IsTransient(path));
            Check("MIDI file: a .mid on disk is read once into the drop item (and a temp file is marked to be kept)", item.Problem is null && item.Midi?.Notes.Count == 4 && item.Transient);
            File.WriteAllBytes(Path.Combine(folder, "bad.mid"), new byte[] { 0, 1 });
            Check("MIDI file: an unreadable .mid gives a reason instead of a clip", MediaDropSession.Measure(Path.Combine(folder, "bad.mid"), DropItemKind.Midi, true).Problem is { Length: > 0 });
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    /// <summary>Virtual files: the descriptor, a synthetic DataObject with FileContents, several entries, unsafe names, kept copies.</summary>
    private static void TestVirtualFileDrop()
    {
        var entries = VirtualFileDrop.Parse(VirtualFileDrop.Build(new[] { (@"..\evil.wav", 10L), ("notes.txt", 3L), ("Groove.mid", 64L) }), unicode: true);
        Check("virtual files: a FileGroupDescriptorW lists names and sizes", entries.Count == 3 && entries[0].Name == @"..\evil.wav" && entries[2].Size == 64);
        Check("virtual files: a short or empty descriptor gives no entries", VirtualFileDrop.Parse(new byte[] { 5, 0 }, true).Count == 0 && VirtualFileDrop.Parse(new byte[] { 9, 0, 0, 0 }, true).Count == 0);
        Check("virtual files: names lose folders, device names and bad characters", MediaDrop.SafeFileName(@"..\..\evil.wav") == "evil.wav"
            && MediaDrop.SafeFileName("a:b?.wav") == "a_b_.wav" && MediaDrop.SafeFileName("CON.wav") == "Dropped.wav" && MediaDrop.SafeFileName("") == "Dropped");

        var folder = Path.Combine(Path.GetTempPath(), "tf-selftest-virtual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        MediaDropSession? session = null;
        try
        {
            var wav = Path.Combine(folder, "source.wav");
            WriteTestWav(wav, 0.25);
            var bytes = File.ReadAllBytes(wav);
            var data = new DataObject();
            data.SetData(VirtualFileDrop.DescriptorW, new MemoryStream(VirtualFileDrop.Build(new[] { ("Kick 01.wav", (long)bytes.Length) })));
            data.SetData(VirtualFileDrop.Contents, new MemoryStream(bytes));
            Check("virtual files: the drag's identity names the virtual file", MediaDropSession.KeyOf(data) is { } key && key.StartsWith("virtual|") && key.Contains("Kick 01.wav"));
            session = MediaDropSession.From(data);
            var item = session?.Items.SingleOrDefault();
            Check("virtual files: a synthetic DataObject's file is written to TabForge's staging folder and measured",
                item is { Kind: DropItemKind.Audio, Transient: true, Problem: null } && Near(item.Seconds, 0.25, 0.01)
                && MediaPathPolicy.IsInside(item.Path, MediaDrop.StagingRoot) && File.Exists(item.Path));
            var staged = session?.StagingFolder;
            var kept = MediaDrop.KeepCopy(item!.Path, Path.Combine(folder, "Song Media"));
            var keptAgain = MediaDrop.KeepCopy(item.Path, Path.Combine(folder, "Song Media"));
            Check("virtual files: the drop keeps a copy in the song's media folder, never over an existing file",
                File.Exists(kept) && Path.GetFileName(kept) == "Kick 01.wav" && Path.GetFileName(keptAgain) == "Kick 01 (2).wav");
            session!.Dispose();
            Check("virtual files: the staging folder is removed after the drop", staged is not null && !Directory.Exists(staged));

            // Several entries, read by index (the COM path in the app; injected here).
            var midi = Smf(96, Track((0, new byte[] { 0x99, 36, 100 }), (48, new byte[] { 0x89, 36, 0 }), (0, new byte[] { 0xFF, 0x2F, 0 })));
            var multi = new DataObject();
            multi.SetData(VirtualFileDrop.DescriptorW, new MemoryStream(VirtualFileDrop.Build(new[] { (@"..\evil.wav", (long)bytes.Length), ("readme.txt", 4L), ("Groove 2.mid", (long)midi.Length) })));
            var asked = new List<int>();
            var target = Path.Combine(folder, "staging");
            var written = VirtualFileDrop.Materialise(multi, target, i => { asked.Add(i); return new MemoryStream(i == 0 ? bytes : midi); }, out var unsupported);
            Check("virtual files: each supported entry is read by its own index; a text file is skipped",
                written.Select(Path.GetFileName).SequenceEqual(new[] { "evil.wav", "Groove 2.mid" }) && asked.SequenceEqual(new[] { 0, 2 }) && unsupported == 1
                && written.All(w => MediaPathPolicy.IsInside(Path.GetFullPath(w), target)));
            Check("virtual files: the MIDI entry reads as a MIDI file", MidiFileImport.Read(written[1]).Notes.Count == 1);

            // A source that serves its contents only on drop: placeholders during the drag, a one-bar estimate.
            using var later = MediaDropSession.From(multi, _ => null);
            var song = DropSong();
            var estimate = MediaDrop.Plan(song, later!.Items, 0, 0, 0, SongQuarterMap.For(song));
            Check("virtual files: contents not served during the drag become placeholders read again on drop",
                later.HasDeferred && later.Items.Count == 2 && later.Items.All(i => i.Deferred && i.Path == "") && estimate.Valid && estimate.Estimated
                && estimate.Clips[1].Item.IsMidi);

            // A big virtual file is not read while the pointer only passes over the timeline (UI thread): it is read on drop.
            var big = new DataObject();
            big.SetData(VirtualFileDrop.DescriptorW, new MemoryStream(VirtualFileDrop.Build(new[] { ("Huge.wav", VirtualFileDrop.HoverBudgetBytes + 1), ("Small.wav", (long)bytes.Length) })));
            var bigAsked = new List<int>();
            using (var hover = MediaDropSession.From(big, i => { bigAsked.Add(i); return new MemoryStream(bytes); }))
                Check("virtual files: an entry over the drag-time budget is a placeholder (not read) while dragging; a small one is read",
                    hover is not null && bigAsked.SequenceEqual(new[] { 1 }) && hover.Items.Count(i => i.Deferred) == 1 && hover.Items.Count(i => !i.Deferred) == 1);
            bigAsked.Clear();
            using (var dropped = MediaDropSession.From(big, i => { bigAsked.Add(i); return new MemoryStream(bytes); }, onDrop: true))
                Check("virtual files: the drop reads every entry", dropped is not null && bigAsked.SequenceEqual(new[] { 0, 1 }) && dropped.Items.All(i => !i.Deferred));
        }
        finally
        {
            session?.Dispose();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }

        Check("dropped files: temp-folder files are kept as copies, others are linked where they are",
            MediaDrop.IsTransient(Path.Combine(Path.GetTempPath(), "x.wav")) && MediaDrop.IsTransient(@"D:\Plugin\Temp\drag.wav") && !MediaDrop.IsTransient(@"D:\Music\Loops\x.wav"));
    }

    /// <summary>Song files dropped anywhere open in tabs; audio/MIDI go to the timeline; other files are refused; the drop effect.</summary>
    private static void TestSongFileDropRouting()
    {
        Check("drop routing: scores and projects are songs, audio and MIDI are media, anything else is unsupported",
            MediaDrop.RoleOf("a.gp5") == MediaDrop.FileRole.Song && MediaDrop.RoleOf("b.TFORGE") == MediaDrop.FileRole.Song && MediaDrop.RoleOf("c.gp") == MediaDrop.FileRole.Song
            && MediaDrop.RoleOf("d.gpx") == MediaDrop.FileRole.Song && MediaDrop.RoleOf("e.flac") == MediaDrop.FileRole.Audio && MediaDrop.RoleOf("f.MID") == MediaDrop.FileRole.Midi
            && MediaDrop.RoleOf("g.midi") == MediaDrop.FileRole.Midi && MediaDrop.RoleOf("h.exe") == MediaDrop.FileRole.Unsupported);
        var songsOnly = new DataObject(DataFormats.FileDrop, new[] { @"C:\Songs\a.gp5", @"C:\Songs\b.tforge" });
        Check("drop routing: the window opens both song files", DroppedSongs.In(songsOnly).Length == 2);
        Check("drop routing: the timeline leaves a drag of only songs to the window", MediaDropSession.From(songsOnly) is null);
        var other = new DataObject(DataFormats.FileDrop, new[] { @"C:\x\setup.exe" });
        using (var refused = MediaDropSession.From(other))
            Check("drop routing: an unsupported file is the timeline's to refuse (no items, one unsupported)", refused is { Items.Count: 0, Unsupported: 1 } && DroppedSongs.In(other).Length == 0);
        var songAndOther = new DataObject(DataFormats.FileDrop, new[] { @"C:\Songs\a.gp5", @"C:\x\notes.txt" });
        Check("drop routing: a song dropped together with a file of another kind still opens (the timeline leaves it to the window)",
            MediaDropSession.From(songAndOther) is null && DroppedSongs.In(songAndOther).Length == 1);
        Check("drop routing: a MIDI file on a device path is refused and never read",
            MediaDropSession.Measure(@"\\.\PhysicalDrive0\x.mid", DropItemKind.Midi, false) is { Problem: not null, Midi: null });
        Check("drop routing: text or tab drags are not files", MediaDropSession.KeyOf(new DataObject(DataFormats.Text, "hello")) is null && DroppedSongs.In(new DataObject(DataFormats.Text, "x")).Length == 0);

        Check("drop effect: Copy whenever the source allows it", ArrangementPanel.EffectFor(DragDropEffects.Copy, DragDropEffects.Copy | DragDropEffects.Move, null) == DragDropEffects.Copy);
        Check("drop effect: a move-only source is refused for a file that stays where it is", ArrangementPanel.EffectFor(DragDropEffects.Copy, DragDropEffects.Move, null) == DragDropEffects.None);
        Check("drop effect: None stays None", ArrangementPanel.EffectFor(DragDropEffects.None, DragDropEffects.All, null) == DragDropEffects.None);
    }

    /// <summary>The drop preview: snapped block, length in pixels, lane and slot, zoom, vertical scroll, below the last track, Alt, the drag cycle.</summary>
    private static void TestMediaDropPreviewGeometry()
    {
        var song = DropSong();
        var timeline = new TrackTimeline
        {
            Project = song, MeasureWidth = 30,
            BarStartSec = b => b * 2.0, BarOfSec = s => ((int)Math.Floor(s / 2), s / 2 - Math.Floor(s / 2)),
            Snap = new SnapSettings { Enabled = true, Grid = "Bar", ToGrid = true, ToItems = false, ToPlayhead = false, GridAtAnyDistance = true },
        };
        var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        var row = ArrangementPanel.DefaultTrackRowHeight;
        var items = new[] { AudioItem("Loop", 4.0) };
        var p = new Point(timeline.XOfBar(3) + 9, gridTop + row + 10);   // a third into bar 4, on track 2's row
        var preview = timeline.DropPreviewAt(items, p);
        Check("drop preview: the block starts on the snapped bar line and is as long as the file (4 s = 2 bars)",
            preview.Valid && Near(preview.Block.X, timeline.XOfBar(3)) && Near(preview.Block.Width, 60) && Near(preview.Plan.StartSec, 6));
        Check("drop preview: on a track without lanes the block sits in a new lane slot under the row",
            preview.Plan.NewLane && preview.Slot is { } slot && Near(slot.Y, gridTop + row * 2) && Near(preview.Block.Y, slot.Y + 3) && preview.Detail.Contains("new lane"));
        Check("drop preview: the label is the file name and the length", preview.Label == "Loop" && preview.Detail.StartsWith(TrackTimeline.LengthText(4.0)));
        Check("drop preview: Alt turns snapping off", Near(timeline.DropPreviewAt(items, p, altHeld: true).Block.X, p.X, 1e-6));

        timeline.MeasureWidth = 60;
        var zoomed = timeline.DropPreviewAt(items, new Point(timeline.XOfBar(3) + 18, p.Y));
        Check("drop preview: zoomed in, the same file is twice as wide", Near(zoomed.Block.Width, 120) && Near(zoomed.Block.X, timeline.XOfBar(3)));
        timeline.MeasureWidth = 30;
        timeline.VerticalScrollOffset = row;
        var scrolled = timeline.DropPreviewAt(items, p);
        Check("drop preview: after scrolling one row the same point is the next track, and the block follows the scroll",
            scrolled.Plan.TrackIndex == 2 && Near(scrolled.Block.Y, gridTop + row * 3 - row + 3));
        timeline.VerticalScrollOffset = 0;

        song.Tracks[1].AudioClips.Add(LaneClip(4, 8, 0));
        ClipLanes.Ensure(song.Tracks[1], 1);
        var laneY = gridTop + row * 2 + 10;   // inside track 2's lane 1 (its row is now taller)
        var onLane = timeline.DropPreviewAt(items, new Point(timeline.XOfBar(7) + 2, laneY));
        Check("drop preview: a free stretch of an existing lane uses it (no slot)", onLane.Plan.Lane == 0 && !onLane.Plan.NewLane && onLane.Slot is null);
        var overlap = timeline.DropPreviewAt(items, new Point(timeline.XOfBar(3) + 2, laneY));
        Check("drop preview: over the existing clip the block drops to a new lane 2 slot",
            overlap.Plan.Lane == 1 && overlap.Plan.NewLane && overlap.Slot is { } s2 && Near(s2.Y, gridTop + row * 2 + ArrangementPanel.AudioLaneHeight) && Near(overlap.Block.Y, s2.Y + 3));

        var below = timeline.DropPreviewAt(new[] { MidiItem("Drum fill", 4, 9) }, new Point(40, gridTop + ArrangementPanel.RowsHeight(song) + 15));
        Check("drop preview: below the last track a new drum track slot opens with the block in its lane",
            below.Plan.NewTrack && below.Slot is { } s3 && Near(s3.Y, gridTop + ArrangementPanel.RowsHeight(song)) && Near(s3.Height, row + ArrangementPanel.AudioLaneHeight)
            && Near(below.Block.Y, s3.Y + row + 3) && below.Detail.Contains("new drum track"));
        var ruler = timeline.DropPreviewAt(items, new Point(50, 10));
        Check("drop preview: over the ruler the block shows the reason and is not a drop target", !ruler.Valid && ruler.Label == "Drop on a track");
        var many = timeline.DropPreviewAt(new[] { AudioItem("A", 2), AudioItem("B", 2) }, new Point(timeline.XOfBar(8) + 1, gridTop + 5));
        Check("drop preview: several files show as one block with a divider per file", many.Label == "2 files" && many.Splits.Length == 1 && Near(many.Splits[0], 30) && Near(many.Block.Width, 60));

        // The drag cycle through a real file drop: measured once, the same spot does not recompute, the drop hands over the plan.
        var folder = Path.Combine(Path.GetTempPath(), "tf-selftest-dropcycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var panel = new ArrangementPanel();
        var window = new Window { Content = panel, Width = 900, Height = 420 };
        using var alive = KeepAlive();
        try
        {
            var wav = Path.Combine(folder, "Take.wav");
            WriteTestWav(wav, 1.0);
            var data = new DataObject(DataFormats.FileDrop, new[] { wav });
            MediaDropPlan? dropped = null;
            timeline.MediaDropped += plan => dropped = plan;
            var at = new Point(timeline.XOfBar(9) + 1, gridTop + 5);
            Check("drop cycle: a drag with an audio file shows Copy", timeline.MediaDragOver(data, at, false) == DragDropEffects.Copy && timeline.CurrentDropPreview is { Valid: true });
            var first = timeline.CurrentDropPreview;
            timeline.MediaDragOver(data, new Point(at.X + 3, at.Y), false);
            Check("drop cycle: moving within the same snapped spot keeps the same preview (nothing recomputed)", ReferenceEquals(first, timeline.CurrentDropPreview));
            timeline.MediaDragOver(data, new Point(timeline.XOfBar(10) + 1, at.Y), false);
            Check("drop cycle: the next bar moves the block", timeline.CurrentDropPreview is { } moved && Near(moved.Block.X, timeline.XOfBar(10)));
            Check("drop cycle: the drop hands over the plan and hides the preview",
                timeline.DropMedia(data, at, false) == DragDropEffects.Copy && dropped is { Valid: true, TrackIndex: 0 } && Near(dropped.Clips[0].LengthSec, 1.0, 0.01) && timeline.CurrentDropPreview is null);
            Check("drop cycle: a drag of song files only is left to the window",
                timeline.MediaDragOver(new DataObject(DataFormats.FileDrop, new[] { @"C:\x\a.gp5" }), at, false) is null);

            // The panel's ghost element mirrors the preview.
            panel.Bind(DropSong(), Array.Empty<Playback.MidiOutputDeviceInfo>());
            ShowTestWindow(window);
            var ghost = panel.SimulateMediaDrag(MediaDropSession.FromItems(items), new Point(panel.TimelineForTest.XOfBar(2) + 1, gridTop + 5));
            Check("drop ghost: the panel's ghost sits where the preview says and shows the new lane slot",
                panel.DropGhostShown && panel.DropGhostBlock is { } b && Near(b.X, ghost.Block.X) && Near(b.Y, ghost.Block.Y) && panel.DropGhostSlot == ghost.Slot);
            panel.EndSimulatedMediaDrag();
            Check("drop ghost: hidden when the drag ends", !panel.DropGhostShown && panel.DropGhostBlock is null);
        }
        finally
        {
            window.Close();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
