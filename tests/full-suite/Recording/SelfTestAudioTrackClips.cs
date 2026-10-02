using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>Clips on audio tracks: a drop below the tracks makes an audio track, any clip kind and lanes, MIDI takes, sections, conversion.</summary>
public static partial class SelfTest
{
    private static void TestAudioTrackClips() => RunInWindowFixture((window, context) =>
    {
        var clips = LtField<ClipEditController>(window, "_clips")!;

        // 1. A dropped audio file and a dropped MIDI file below the tracks each make an audio track with the clip, one undo step.
        var doc = DoOpen(window, DoSong(2));
        var time = SongQuarterMap.For(doc.Project);
        var below = doc.Project.Tracks.Count;
        var audioPlan = MediaDrop.PlanAddTrackLane(doc.Project, new[] { AudioItem("Loop", 2) }, time);
        var midiPlan = MediaDrop.PlanAddTrackLane(doc.Project, new[] { MidiItem("Fill", 4, channel: 9) }, time);
        Check("audio clips: the Add-track lane plans a new audio track for audio and for MIDI",
            audioPlan.Valid && audioPlan.NewTrack && audioPlan.NewTrackKind == TrackKind.Audio && audioPlan.TrackIndex == below
            && midiPlan.Valid && midiPlan.NewTrack && midiPlan.NewTrackKind == TrackKind.Audio);
        var moved = new AudioClip { File = "m.wav", Name = "m", SourceLengthSec = 1, FileLengthSec = 1 };
        Check("audio clips: moving a clip below the last track also plans an audio track", MediaDrop.PlanMove(doc.Project, moved, TrackKind.Drums, below, 0, 0, time).NewTrackKind == TrackKind.Audio);
        var outcome = DoMeasure(window, doc, () => clips.ApplyMediaDrop(doc, audioPlan));
        var audio = doc.Project.Tracks.Count == below + 1 ? doc.Project.Tracks[^1] : null;
        Check("audio clips: an audio file dropped below the tracks creates an audio track holding the clip, one undo step",
            outcome.UndoSteps == 1 && audio is { IsAudio: true } && audio.AudioClips.Count == 1 && !audio.AudioClips[0].IsMidi && audio.Measures.Count == doc.Project.Tracks[0].Measures.Count, outcome.ToString());
        // 2. An audio track takes any clip kind and goes multi-lane when clips overlap.
        var index = doc.Project.Tracks.Count - 1;
        clips.ApplyMediaDrop(doc, MediaDrop.Plan(doc.Project, new[] { AudioItem("Layer", 2) }, index, 0, 0, SongQuarterMap.For(doc.Project)));
        clips.ApplyMediaDrop(doc, MediaDrop.Plan(doc.Project, new[] { MidiItem("Notes", 4) }, index, 0, 0, SongQuarterMap.For(doc.Project)));
        Check("audio clips: an audio track takes audio and MIDI clips and opens lanes for overlapping ones",
            audio!.AudioClips.Count == 3 && audio.AudioClips.Any(c => c.IsMidi) && audio.AudioClips.Select(c => c.Lane).Distinct().Count() >= 2 && ClipLanes.Count(audio) >= 2,
            string.Join(", ", audio.AudioClips.Select(c => $"{c.Name}:{(c.IsMidi ? "midi" : "audio")}:lane{c.Lane}:{c.StartSec:0.##}-{c.EndSec:0.##}")) + $" lanes={ClipLanes.Count(audio)} tracks={doc.Project.Tracks.Count} index={index}");

        // (a different song is shown from here on: a drop is applied to the song on show only)
        var midiDoc = DoOpen(window, DoSong(2));
        clips.ApplyMediaDrop(midiDoc, midiPlan);
        var midiTrack = midiDoc.Project.Tracks[^1];
        Check("audio clips: a MIDI file dropped below the tracks creates an audio track holding a MIDI clip with its notes",
            midiDoc.Project.Tracks.Count == 3 && midiTrack.IsAudio && midiTrack.AudioClips.Count == 1 && midiTrack.AudioClips[0].IsMidi && midiTrack.AudioClips[0].Notes!.Count > 0);

        // 3. A MIDI take recorded onto an audio track is stored (routing and sound are not decided here).
        var take = new AudioClip { Name = "Audio 1 MIDI", StartSec = 1, SourceLengthSec = 1, Notes = new() { new ClipNote(0, 0.5, 60, 90) } };
        var takeTrack = new TrackController().CreateTrack(doc.Project, TrackKind.Audio);
        RecordingController.PlaceTakes(takeTrack, new List<AudioClip> { take }, midi: true);
        Check("audio clips: a MIDI take recorded on an audio track is stored as a clip", takeTrack.AudioClips.Count == 1 && takeTrack.AudioClips[0].IsMidi && takeTrack.AudioClips[0].Notes!.Count == 1);

        // 4. Clips on an audio track move with their section.
        var song = DoSong(1, 8);
        var tracks = new TrackController();
        var secTrack = tracks.CreateTrack(song, TrackKind.Audio);
        song.Tracks.Add(secTrack);
        var barSec = 60.0 / song.Tempo * song.TimeSignatureNumerator;
        var clip = new AudioClip { File = "s.wav", Name = "s", StartSec = barSec * 2, SourceLengthSec = barSec / 2, FileLengthSec = barSec / 2 };
        secTrack.AudioClips.Add(clip);
        var set = SectionClips.Capture(song, 2, 4);
        Check("audio clips: a clip on an audio track is captured with its section", set.Entries.Count == 1 && set.Entries[0].TrackIndex == 1 && ReferenceEquals(set.Entries[0].Clip, clip));
        SectionClips.Place(song, set, 5, add: false);
        Check("audio clips: the section's clip lands at the same place in the section moved to bar 5", Math.Abs(clip.StartSec - barSec * 5) < 0.01, clip.StartSec.ToString());

        // 5. Converting to an instrument keeps the clips on lane + 1.
        var convDoc = new DocumentSession { Project = DoSong(1, 4) };
        var conv = tracks.AddAudioTrack(convDoc).Value!;
        conv.AudioClips.Add(new AudioClip { File = "c.wav", Name = "c", SourceLengthSec = 1, FileLengthSec = 1, Lane = 0 });
        conv.AudioClips.Add(new AudioClip { Name = "n", SourceLengthSec = 1, Notes = new() { new ClipNote(0, 0.5, 60, 90) }, Lane = 1 });
        Check("audio clips: converting to an instrument keeps audio and MIDI clips, one lane lower",
            tracks.ConvertAudioToInstrument(convDoc, conv, "Grand Piano").Changed && !conv.IsAudio && conv.AudioClips.Count == 2 && conv.AudioClips.Select(c => c.Lane).OrderBy(l => l).SequenceEqual(new[] { 1, 2 }) && conv.AudioClips.Any(c => c.IsMidi));
    });
}
