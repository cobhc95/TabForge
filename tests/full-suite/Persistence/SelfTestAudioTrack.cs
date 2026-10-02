using System.IO;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>The audio track kind: defaults, lookups that skip it, file version 3 only while one exists, and load repairs.</summary>
public static partial class SelfTest
{
    private static SongProject AudioTrackSong(bool withAudio)
    {
        var p = new SongProject();
        var tracks = new TrackController();
        p.Tracks.Add(tracks.CreateTrack(p, TrackKind.Guitar));
        if (withAudio) p.Tracks.Insert(0, tracks.CreateTrack(p, TrackKind.Audio));
        return p;
    }

    private static void TestAudioTrackModel()
    {
        var p = AudioTrackSong(withAudio: true);
        var audio = p.Tracks[0];
        var second = new TrackController().CreateTrack(p, TrackKind.Audio);
        Check("an audio track has the Audio kind and no notation", audio.IsAudio && !audio.HasNotation && (int)TrackKind.Audio == 5);
        Check("an audio track has no tuning, frets or instrument and one empty lane on input 1", audio.StringTunings.Count == 0 && audio.NumberOfFrets == 0 && audio.InstrumentName == "" && audio.Lanes.Count == 1 && audio.AudioInput == AudioInputs.Input1 && !audio.MidiSound);
        Check("audio tracks are named Audio 1, Audio 2", audio.Name == "Audio 1" && second.Name == "Audio 2", $"{audio.Name} / {second.Name}");
        Check("an audio track has the song's bar count, all empty", audio.Measures.Count == p.Tracks[1].Measures.Count && audio.Measures.All(m => m.Cells.All(c => c.Notes.Count == 0)));
        Check("NotationTracks and FirstNotationTrack skip the audio track", p.NotationTracks.Count() == 1 && p.FirstNotationTrack == p.Tracks[1]);
        Check("MasterBarTrack is the first notation track", p.MasterBarTrack == p.Tracks[1]);
        var only = new SongProject { Tracks = { audio } };
        Check("an audio-only song has no notation track but a master-bar track", only.FirstNotationTrack is null && !only.NotationTracks.Any() && only.MasterBarTrack == audio);
        Check("the time and tempo lookups cope with an audio-only song", MusicTime.BarOf(only, 0) is not null && MusicTime.TempoAt(only, 3) == only.Tempo);
        Check("an instrument choice never converts an audio track", !new TrackController().ApplyEdit(p, new TrackEditRequest(0, TrackEditKind.SelectInstrument, "Grand Piano")) && audio.Kind == TrackKind.Audio);
        Check("instrument adoption never re-kinds an audio track", !TrackSetup.AdoptInstrumentKind(audio, "Grand Piano", 0) && audio.Kind == TrackKind.Audio);
        var undo = ProjectService.RestoreUndoTrackHeader(ProjectService.UndoTrackHeader(audio));
        Check("an undo snapshot header carries the kind", undo.Kind == TrackKind.Audio);
        var copy = ProjectService.RestoreBytes(ProjectService.PersistBytes(p));
        Check("a persisted copy carries the kind", copy.Tracks[0].Kind == TrackKind.Audio && copy.Tracks[1].Kind == TrackKind.Guitar);
    }

    private static void TestAudioTrackConversion()
    {
        var doc = new DocumentSession { Project = AudioTrackSong(withAudio: true) };
        var tracks = new TrackController();
        var audio = doc.Project.Tracks[0];
        audio.AudioClips.Add(new AudioClip { File = "a.wav", Name = "a", SourceLengthSec = 2, FileLengthSec = 2, Lane = 0 });
        audio.AudioClips.Add(new AudioClip { File = "b.wav", Name = "b", SourceLengthSec = 2, FileLengthSec = 2, Lane = 1 });
        var bars = audio.Measures.Count;
        var undoBefore = doc.Undo.UndoCount;
        var result = tracks.ConvertAudioToInstrument(doc, audio, "Electric Bass (Finger)");
        Check("converting an audio track to a bass track succeeds as one undo step", result.Changed && doc.Undo.UndoCount == undoBefore + 1);
        Check("the converted track has the instrument's kind, strings, frets and sound", audio.Kind == TrackKind.Bass && !audio.IsAudio && audio.HasNotation && audio.StringTunings.Count == 4 && audio.NumberOfFrets > 0 && audio.MidiSound && audio.InstrumentName.Length > 0);
        Check("the converted track keeps its bars, empty", audio.Measures.Count == bars && audio.Measures.All(m => m.Cells.All(c => c.Notes.Count == 0)));
        Check("every clip moves down one lane and lane 0 is free", audio.AudioClips.Count == 2 && audio.AudioClips.All(c => c.Lane >= 1) && audio.AudioClips.Select(c => c.Lane).OrderBy(l => l).SequenceEqual(new[] { 1, 2 }) && audio.Lanes.Count >= 2);
        Check("the song no longer needs format version 3", doc.Project.FormatVersion == 2);
        var guitar = doc.Project.Tracks[1];
        Check("an instrument track never converts to audio or converts again", !tracks.ConvertAudioToInstrument(doc, guitar, "Grand Piano").Changed && guitar.Kind == TrackKind.Guitar);
        var other = new DocumentSession { Project = AudioTrackSong(withAudio: true) };
        Check("an unknown instrument changes nothing", !tracks.ConvertAudioToInstrument(other, other.Project.Tracks[0], "No Such Sound").Changed && other.Project.Tracks[0].IsAudio);
        var drums = tracks.ConvertAudioToInstrument(other, other.Project.Tracks[0], "Drum Kit (Standard)");
        Check("a drum kit gives a drum track on channel 10", drums.Changed && other.Project.Tracks[0].Kind == TrackKind.Drums && other.Project.Tracks[0].MidiChannel == 9);
    }

    private static void TestAudioTrackPersistence()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-audiotrack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var plain = AudioTrackSong(withAudio: false);
            Check("a song without audio writes format version 2", plain.FormatVersion == 2);
            var first = Path.Combine(folder, "a.tforge");
            ProjectService.Save(first, plain);
            var second = Path.Combine(folder, "b.tforge");
            ProjectService.Save(second, ProjectService.Load(first));
            Check("a version 2 file is byte-identical after load and re-save", File.ReadAllBytes(first).AsSpan().SequenceEqual(File.ReadAllBytes(second)));

            var song = AudioTrackSong(withAudio: true);
            song.Tracks[0].AudioClips.Add(new AudioClip { File = Path.Combine(folder, "x.wav"), Name = "x", SourceLengthSec = 2, FileLengthSec = 2 });
            Check("a song with an audio track reports format version 3", song.FormatVersion == 3);
            var v3 = Path.Combine(folder, "c.tforge");
            ProjectService.Save(v3, song);
            var back = ProjectService.Load(v3);
            Check("a version 3 file round-trips the track kind, clips and bar counts", back.FormatVersion == 3 && back.Tracks[0].IsAudio && back.Tracks[0].AudioClips.Count == 1
                && back.Tracks[0].Measures.Count == back.Tracks[1].Measures.Count && back.Tracks[0].Name == "Audio 1");
            var v3again = Path.Combine(folder, "d.tforge");
            ProjectService.Save(v3again, back);
            Check("a version 3 file is byte-identical after load and re-save", File.ReadAllBytes(v3).AsSpan().SequenceEqual(File.ReadAllBytes(v3again)));

            back.Tracks.RemoveAt(0);
            Check("removing the last audio track writes version 2 again", back.FormatVersion == 2);

            var damaged = AudioTrackSong(withAudio: true);
            var audio = damaged.Tracks[0];
            audio.Measures[2].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
            audio.Measures.RemoveRange(20, audio.Measures.Count - 20);
            var repaired = ProjectService.RestoreBytes(ProjectService.PersistBytes(damaged));
            ProjectValidator.Validate(repaired);
            var fixedAudio = repaired.Tracks[0];
            Check("a note on an audio track is cleared on load", fixedAudio.Measures.All(m => m.Cells.All(c => c.Notes.Count == 0)));
            Check("a short audio track is padded to the song's bar count", fixedAudio.Measures.Count == repaired.Tracks[1].Measures.Count);
            var future = AudioTrackSong(withAudio: false);
            future.FormatVersion = 4;
            Check("an unknown format version is refused", Throws(() => ProjectValidator.Validate(future)));
            var legacy = AudioTrackSong(withAudio: false);
            legacy.FormatVersion = 1;
            Check("format version 1 is accepted", !Throws(() => ProjectValidator.Validate(legacy)));

            var onlyAudio = new SongProject();
            onlyAudio.Tracks.Add(new TrackController().CreateTrack(onlyAudio, TrackKind.Audio));
            var onlyPath = Path.Combine(folder, "e.tforge");
            ProjectService.Save(onlyPath, onlyAudio);
            var onlyBack = ProjectService.Load(onlyPath);
            Check("an audio-only song saves and loads", onlyBack.Tracks.Count == 1 && onlyBack.FirstNotationTrack is null && onlyBack.FormatVersion == 3);
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }
}
