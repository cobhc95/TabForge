using System.IO;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Clip lanes, loop takes, MIDI clips to notation, clip hotkeys and group row geometry (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static void TestClips()
    {
        AudioClip Clip(double start, double length, int lane = 0, bool midi = false) => new()
        {
            File = "x.wav", Name = "c", StartSec = start, SourceLengthSec = length, FileLengthSec = length, Lane = lane,
            Notes = midi ? new List<ClipNote>() : null,
        };

        // ---- lanes ----
        var track = new TrackModel { Name = "T" };
        Check("a track with no clips and no arm has no lanes", ClipLanes.Count(track) == 0 && !ArrangementPanel.HasAudioLane(track));
        track.RecordArm = true;
        Check("an armed track shows one lane", ClipLanes.Count(track) == 1);
        track.AudioClips.Add(Clip(0, 4));
        Check("a busy stretch pushes the next take to a new lane, a free one keeps lane 0",
            ClipLanes.FreeLane(track, 1, 3) == 1 && ClipLanes.FreeLane(track, 4, 6) == 0);
        track.AudioClips.Add(Clip(1, 3, lane: 1));
        Check("FreeLane skips every busy lane", ClipLanes.FreeLane(track, 1, 3) == 2);
        ClipLanes.Ensure(track, 3);
        ClipLanes.PlayNewTake(track, 1, midi: false);
        Check("a new audio take becomes the only playing audio lane (older takes grey out)",
            !ClipLanes.Plays(track, 0) && ClipLanes.Plays(track, 1) && !ClipLanes.Audible(track, track.AudioClips[0]) && ClipLanes.Audible(track, track.AudioClips[1]));
        var withMidi = new TrackModel();
        withMidi.AudioClips.Add(Clip(0, 4, 0));
        withMidi.AudioClips.Add(Clip(0, 4, 1, midi: true));
        ClipLanes.Ensure(withMidi, 3);
        withMidi.AudioClips.Add(Clip(4, 4, 2));
        ClipLanes.PlayNewTake(withMidi, 2, midi: false);
        Check("a new audio take never greys out a MIDI-only lane", ClipLanes.Plays(withMidi, 1) && !ClipLanes.Plays(withMidi, 0) && ClipLanes.Plays(withMidi, 2));
        ClipLanes.ClickPlay(track, 0, add: false);
        Check("clicking a lane's play button plays only that lane", ClipLanes.Plays(track, 0) && !ClipLanes.Plays(track, 1) && !ClipLanes.Plays(track, 2));
        ClipLanes.ClickPlay(track, 1, add: true);
        Check("Ctrl+click adds a lane", ClipLanes.Plays(track, 0) && ClipLanes.Plays(track, 1));
        track.AudioClips.RemoveAll(c => c.Lane == 1);
        track.RecordArm = false;
        ClipLanes.Trim(track);
        Check("empty lanes at the bottom are removed", track.Lanes.Count == 1 && ClipLanes.Count(track) == 1);
        Check("an audio clip's end follows its speed", Math.Abs(new AudioClip { StartSec = 1, SourceLengthSec = 4, Speed = 2 }.EndSec - 3) < 1e-9);

        // ---- clicking a take selects its lane ----
        var takes3 = new TrackModel();
        takes3.AudioClips.Add(Clip(0, 4, 0)); takes3.AudioClips.Add(Clip(0, 4, 1)); takes3.AudioClips.Add(Clip(0, 4, 2, midi: true));
        ClipLanes.Ensure(takes3, 3);
        Check("clicking an audio take plays only that audio lane and leaves a MIDI lane alone",
            ClipLanes.SelectTake(takes3, 1, midi: false, add: false) && !ClipLanes.Plays(takes3, 0) && ClipLanes.Plays(takes3, 1) && ClipLanes.Plays(takes3, 2));
        Check("clicking the take that already plays changes nothing", !ClipLanes.SelectTake(takes3, 1, midi: false, add: false));
        Check("Ctrl+click adds another take", ClipLanes.SelectTake(takes3, 0, midi: false, add: true) && ClipLanes.Plays(takes3, 0) && ClipLanes.Plays(takes3, 1));

        // ---- loop recording passes ----
        var passes = RecordingPasses.Split(10, 9, (4, 8));   // began after the loop end
        Check("a recording that starts past the loop is a single take", passes.Count == 1 && passes[0].Length == 9);
        passes = RecordingPasses.Split(5, 10, (4, 8));       // 3 s to the loop end, then 4 s + 3 s
        Check("loop recording gives one take per pass", passes.Count == 3, $"{passes.Count}");
        Check("the first pass ends at the loop end", Math.Abs(passes[0].SongStart - 5) < 1e-9 && Math.Abs(passes[0].Length - 3) < 1e-9);
        Check("later passes start at the loop start, in step with the file",
            Math.Abs(passes[1].SongStart - 4) < 1e-9 && Math.Abs(passes[1].FileOffset - 3) < 1e-9 && Math.Abs(passes[1].Length - 4) < 1e-9
            && Math.Abs(passes[2].FileOffset - 7) < 1e-9 && Math.Abs(passes[2].Length - 3) < 1e-9);
        Check("without a loop the recording is one take", RecordingPasses.Split(2, 5, null).Count == 1);

        // ---- MIDI clip into notation ----
        var song = new SongProject { Tempo = 120 };
        var guitar = new TrackModel { Name = "G", Measures = TemplateFactory.Measures(2) };
        song.Tracks.Add(guitar);
        // 4/4 at 120 bpm: a bar is 2 s, a sixteenth 0.125 s.
        (int Bar, double Fraction) BarAt(double sec) => ((int)(sec / 2), sec % 2 / 2);
        var clip = Clip(0, 4, midi: true);
        clip.Notes!.Add(new ClipNote(0, 0.5, 64, 90));        // bar 1, slot 0 (open high E)
        clip.Notes.Add(new ClipNote(0.5, 0.5, 59, 100));      // bar 1, slot 4 (open B)
        clip.Notes.Add(new ClipNote(2.0, 0.25, 55, 80));      // bar 2, slot 0 (open G)
        clip.Notes.Add(new ClipNote(2.0, 0.25, 50, 80));      // bar 2, slot 0 (open D, same beat)
        var written = MidiClipToTab.Write(song, guitar, clip, BarAt);
        Check("a MIDI clip's notes are written into the bars they fall in", written == 4, $"{written}");
        var b1 = guitar.Measures[0].Cells;
        Check("notes land on the sixteenth they were played on, on the lowest playable fret",
            b1[0].Notes.Count == 1 && b1[0].Notes[0].MidiValue == 64 && b1[0].Notes[0].Fret == 0 && b1[0].Notes[0].StringIndex == 0
            && b1[4].Notes.Count == 1 && b1[4].Notes[0].MidiValue == 59);
        Check("notes played together form one beat on different strings",
            guitar.Measures[1].Cells[0].Notes.Count == 2 && guitar.Measures[1].Cells[0].Notes.Select(n => n.StringIndex).Distinct().Count() == 2);
        Check("a beat is as long as the gap to the next one", MusicTime.CellSlots(b1[0]) <= 4.01 && MusicTime.CellSlots(b1[0]) >= 3.99, $"{MusicTime.CellSlots(b1[0])}");
        var empty = MidiClipToTab.Write(song, guitar, Clip(0, 1, midi: true), BarAt);
        Check("an empty MIDI clip writes nothing", empty == 0);

        // ---- validator ----
        var valid = new SongProject();
        var vt = new TrackModel { Name = "V", Measures = TemplateFactory.Measures(1) };
        var vc = Clip(0, 2, lane: 1, midi: true);
        vc.Notes!.Add(new ClipNote(0, 1, 60, 100));
        vt.AudioClips.Add(vc);
        vt.Lanes.Add(new ClipLane()); vt.Lanes.Add(new ClipLane());
        valid.Tracks.Add(vt);
        var accepted = true;
        try { ProjectValidator.Validate(valid); } catch (InvalidDataException) { accepted = false; }
        Check("a project with a MIDI clip on a second lane validates", accepted);
        vc.Notes.Add(new ClipNote(0, 1, 200, 100));
        var rejected = false;
        try { ProjectValidator.Validate(valid); } catch (InvalidDataException) { rejected = true; }
        Check("a MIDI clip with an impossible pitch is refused", rejected);

        // ---- clip hotkeys: their own context ----
        var normal = HotkeyCatalog.BuildMap(new HotkeySettings());
        var clips = HotkeyCatalog.BuildMap(new HotkeySettings(), clipContext: true);
        Check("clip keys live in their own map: Delete deletes a clip only while a clip is selected",
            clips.TryGetValue("Delete", out var del) && del == "Clip.Delete" && !normal.Values.Any(HotkeyCatalog.IsClipAction));
        Check("clip keys cover the standard set", new[] { "Ctrl+C", "Ctrl+X", "Ctrl+V", "Ctrl+D", "Escape", "Left", "Right", "Up", "Down", "F2" }.All(clips.ContainsKey));
        Check("Ctrl+R records and no other command uses it", normal.TryGetValue("Ctrl+R", out var rec) && rec == "Transport.Record");
        Check("clip and score commands may share a key but not within one context",
            HotkeyCatalog.SameContext("Clip.Copy", "Clip.Cut") && !HotkeyCatalog.SameContext("Clip.Copy", "Edit.Copy"));

        // ---- mixer scales (track list and mixer share them) ----
        Check("volume 104 is step 13 of 16 and pan 64 is centre",
            ArrangementPanel.VolumeStep(104) == 13 && ArrangementPanel.PanStep(64) == 0 && ArrangementPanel.PanStep(0) == -8 && ArrangementPanel.PanStep(127) == 8);

        // ---- group rows in the track list ----
        var groups = new SongProject();
        foreach (var (name, kind, program) in new[] { ("G1", TrackKind.Guitar, 30), ("G2", TrackKind.Guitar, 30), ("B", TrackKind.Bass, 33), ("D", TrackKind.Drums, 0) })
            groups.Tracks.Add(new TrackModel { Name = name, InstrumentName = name, Kind = kind, MidiProgram = program, MidiChannel = kind == TrackKind.Drums ? 9 : 0, Measures = TemplateFactory.Measures(1) });
        var flat = ArrangementPanel.RowTopOf(groups, 3);
        groups.Mixer.ShowGroupsInTrackList = true;
        var h = ArrangementPanel.GroupHeaderHeight; var r = ArrangementPanel.DefaultTrackRowHeight;
        Check("without groups rows stack directly", Math.Abs(flat - 3 * r) < 1e-9);
        Check("each group adds one header row", Math.Abs(ArrangementPanel.RowTopOf(groups, 2) - (h + 2 * r + h)) < 1e-9 && Math.Abs(ArrangementPanel.RowTopOf(groups, 3) - (h + 2 * r + h + r + h)) < 1e-9);
        Check("the group runs are found in order", ArrangementPanel.GroupRuns(groups).Select(x => (x.Group, x.Start, x.Count)).SequenceEqual(new[]
            { (MixerGroups.Guitars, 0, 2), (MixerGroups.Basses, 2, 1), (MixerGroups.Drums, 3, 1) }));
        Check("a row is found from its y position, headers count as their group's first track",
            ArrangementPanel.RowIndexAt(groups, h + r / 2) == 0 && ArrangementPanel.RowIndexAt(groups, 2) == 0
            && ArrangementPanel.RowIndexAt(groups, h + 2 * r + h + r / 2) == 2 && ArrangementPanel.GroupHeaderAt(groups, h + 2 * r + 3) == 2);
        groups.Mixer.CollapsedGroups.Add(MixerGroups.Guitars);
        Check("a collapsed group takes no row space (its header stays)",
            Math.Abs(ArrangementPanel.RowTopOf(groups, 2) - (h + h)) < 1e-9 && ArrangementPanel.RowHeight(groups, 0) == 0
            && Math.Abs(ArrangementPanel.RowsHeight(groups) - (h + h + r + h + r)) < 1e-9);
        Check("group colours: every group has a default and a custom colour wins",
            TrackColouring.ColourOf(MixerGroups.Guitars, null) != TrackColouring.ColourOf(MixerGroups.Basses, null)
            && TrackColouring.ColourOf(MixerGroups.Guitars, new Dictionary<string, string> { [MixerGroups.Guitars] = "#112233" }) == "#112233");
        TrackColouring.ByGroup(groups, null);
        Check("colouring by group gives every track of a group the same colour",
            groups.Tracks[0].ColorHex == groups.Tracks[1].ColorHex && groups.Tracks[0].ColorHex != groups.Tracks[2].ColorHex);
    }
}
