using System.IO;
using System.Text;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>Exports skip audio tracks, the .gp keeps them in its embedded project, and a song with no notation track is refused.</summary>
public static partial class SelfTest
{
    private static SongProject AudioExportSong(bool withNotation)
    {
        var p = AudioTrackSong(withAudio: true);
        if (!withNotation) p.Tracks.RemoveAll(t => !t.IsAudio);
        p.Tracks[0].AudioClips.Add(new AudioClip { File = "a.wav", Name = "a", SourceLengthSec = 2, FileLengthSec = 2, Lane = 0 });
        if (withNotation)
        {
            var cell = p.Tracks[1].Measures[0].Cells[0];
            cell.Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 43 });
        }
        return p;
    }

    private static string? ExportRefusal(Action export)
    {
        try { export(); return null; }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    private static void TestAudioTrackExports()
    {
        var song = AudioExportSong(withNotation: true);
        var audioOnly = AudioExportSong(withNotation: false);
        var dir = Path.Combine(Path.GetTempPath(), "tf-audio-export-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            // .gp: audio track skipped in the score, kept in the embedded project, restored with its clips by id
            var clean = GuitarProExporter.ToBytes(song, embedProject: false);
            var cleanPath = Path.Combine(dir, "clean.gp");
            File.WriteAllBytes(cleanPath, clean);
            var gp = GuitarProImporter.Import(cleanPath);
            Check("a clean .gp holds the notation tracks only", gp.Tracks.Count == 1 && gp.Tracks[0].Measures.Count == song.Tracks[1].Measures.Count, $"{gp.Tracks.Count} tracks");
            var withProject = GuitarProExporter.ToBytes(song);
            var back = GuitarProExporter.TryReadEmbedded(withProject);
            Check("reopening a .gp restores the audio track with its clip", back is not null && back.Tracks.Count == 2 && back.Tracks[0].IsAudio && back.Tracks[0].AudioClips.Count == 1 && back.Tracks[0].AudioClips[0].File == "a.wav");
            Check("the restored audio track keeps its id and the notation track its note", back is not null && back.Tracks[0].Id == song.Tracks[0].Id && back.Tracks[1].Id == song.Tracks[1].Id && back.Tracks[1].Measures[0].Cells[0].Notes.Count == 1);

            // audio-only: .gp and MusicXML refuse, MIDI writes meta only, ASCII writes the header only
            var gpRefusal = ExportRefusal(() => GuitarProExporter.ToBytes(audioOnly));
            Check(".gp export of an audio-only song is refused with the notice", gpRefusal == AudioTrackExport.NoNotationNotice("a score file") && gpRefusal.Contains("only audio tracks"), gpRefusal);
            Check("the .gp refusal also applies to a clean copy", ExportRefusal(() => GuitarProExporter.ToBytes(audioOnly, embedProject: false)) is not null);
            Check("MusicXML export of an audio-only song is refused with the notice", ExportRefusal(() => MusicXmlExportService.ToBytes(audioOnly)) == AudioTrackExport.NoNotationNotice("MusicXML"));
            Check("PDF export of an audio-only song is refused with the notice", ExportRefusal(() => Views.ScorePdfExporter.Export(audioOnly, 0, Path.Combine(dir, "x.pdf"))) == AudioTrackExport.NoNotationNotice("a PDF score") && !File.Exists(Path.Combine(dir, "x.pdf")));
            Check("the refusal names the way out", AudioTrackExport.NoNotationNotice("MusicXML").Contains(".tforge"));

            // MusicXML: one part for the notation track only
            var xml = Encoding.UTF8.GetString(MusicXmlExportService.ToBytes(song));
            Check("MusicXML writes the notation track only", CountOf(xml, "<score-part ") == 1 && CountOf(xml, "<part id=") == 1 && !xml.Contains("Audio 1"));

            // MIDI
            var mid = Path.Combine(dir, "song.mid");
            MidiExportService.Export(song, mid);
            Check("MIDI writes the conductor and the notation track only", MidiChunkCount(mid) == 2, $"{MidiChunkCount(mid)}");
            var midOnly = Path.Combine(dir, "audio-only.mid");
            MidiExportService.Export(audioOnly, midOnly);
            Check("MIDI export of an audio-only song writes tempo and meta only", MidiChunkCount(midOnly) == 1, $"{MidiChunkCount(midOnly)}");

            // ASCII
            var txt = Path.Combine(dir, "song.txt");
            AsciiExportService.Export(song, txt);
            var ascii = File.ReadAllText(txt);
            Check("ASCII lists the notation track only", ascii.Contains("[" + song.Tracks[1].Name + "]") && !ascii.Contains("[Audio 1]"));
            var txtOnly = Path.Combine(dir, "audio-only.txt");
            AsciiExportService.Export(audioOnly, txtOnly);
            Check("ASCII of an audio-only song is the header only", !File.ReadAllText(txtOnly).Contains("[Audio"));

            // preflight notice
            var report = GpExportPreflight.Analyze(song);
            Check("the preflight carries the audio-track notice, and an export asks about it", report.AudioTrackNotice == "Audio tracks (1): not written; the file holds the notation tracks only" && report.Summary().Contains("Audio tracks (1)") && report.ShouldAskFor(GpExportKind.Export));
            Check("a song without audio tracks has no such notice", GpExportPreflight.Analyze(AudioTrackSong(withAudio: false)).AudioTrackNotice is null);
            Check("a save keeps the embedded project, so audio tracks alone do not ask", !report.ShouldAskFor(GpExportKind.Save));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static int CountOf(string text, string needle)
    {
        var n = 0; var at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
        return n;
    }

    private static int MidiChunkCount(string path)
    {
        var b = File.ReadAllBytes(path);
        return b[10] << 8 | b[11];
    }
}
