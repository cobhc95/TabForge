using System.IO;
using System.Text;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Damaged-file notice (required group "long-import"): Guitar Pro 3-5 has no checksum, so a file with damaged bytes still opens. A song with
/// more impossible facts than <see cref="ImportPlausibility.WarnThreshold"/>, or text sections full of unreadable bytes, opens unchanged with one
/// short notice; a clean song, or one odd fact, stays silent. Files here are generated, never a real song.
/// </summary>
public static partial class SelfTest
{
    private static void TestImportPlausibility()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-plausible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            PlausibilityCleanFiles();
            PlausibilityCorruptText(folder);
            PlausibilityThresholds();
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string LyricsLine(int length) => string.Concat(Enumerable.Range(0, length / 6 + 1).Select(i => $"la{i % 10} la ")).Substring(0, length);

    private static void PlausibilityCleanFiles()
    {
        foreach (var version in SyntheticGuitarPro35.Versions)
        {
            var name = SyntheticGuitarPro35.VersionName(version);
            var bytes = SyntheticGuitarPro35.Write(version, 40, 3, lyrics: version >= 400, lyricsText: version >= 400 ? LyricsLine(600) : null);
            var project = GuitarProImporter.ImportBytes(bytes, "clean" + ImportExtension(version));
            Check($"damaged-file notice: a clean {name} file gives no notice", GuitarProImporter.LastDamageNotice is null && ImportPlausibility.Scan(project, bytes).Count == 0 && ImportPlausibility.HeaderTextDamage(bytes) == 0,
                GuitarProImporter.LastDamageNotice);
        }
        // Accents and typographic quotes in a long text are not damage (cp1252 / Latin-1 text).
        var accented = Encoding.Latin1.GetString(Encoding.Latin1.GetBytes(string.Concat(Enumerable.Repeat("café naïve “quoted” … señor ", 40)))).Replace('“', '\u0093').Replace('”', '\u0094').Replace('…', '\u0085');
        var accentedFile = SyntheticGuitarPro35.Write(500, 4, lyrics: true, lyricsText: accented);
        Check("damaged-file notice: a long text with accents and typographic quotes is not flagged", ImportPlausibility.HeaderTextDamage(accentedFile) == 0);
        Check("damaged-file notice: a file that is not Guitar Pro 3-5 (too short, wrong header) is left alone",
            ImportPlausibility.HeaderTextDamage(new byte[10]) == 0 && ImportPlausibility.HeaderTextDamage(Enumerable.Repeat((byte)0xC5, 400).ToArray()) == 0);
    }

    private static void PlausibilityCorruptText(string folder)
    {
        var clean = SyntheticGuitarPro35.Write(500, 40, 3, lyrics: true, lyricsText: LyricsLine(3_000));
        var corrupt = (byte[])clean.Clone();
        var at = corrupt.AsSpan().IndexOf(Encoding.ASCII.GetBytes("la0 la la1"));
        for (var i = at + 1_000; i < at + 1_400; i++) corrupt[i] ^= 0xA5;   // 400 flipped bytes in the middle of the lyrics, as in a damaged download
        var path = Path.Combine(folder, "damaged.gp5");
        File.WriteAllBytes(path, corrupt);
        var cleanProject = GuitarProImporter.ImportBytes(clean, "clean.gp5");
        var project = GuitarProImporter.ImportBytes(corrupt, path);
        var notice = GuitarProImporter.LastDamageNotice;
        Check("damaged-file notice: a file with 400 flipped bytes in its lyrics still opens", project.Tracks.Count == 3 && project.Tracks[0].Measures.Count == 40);
        Check("damaged-file notice: ... with one short plain notice that names no file or program",
            notice is not null && notice.StartsWith("this file may be damaged", StringComparison.Ordinal) && notice.Contains("unreadable", StringComparison.Ordinal) && notice.Length < 140
            && !notice.Contains(folder, StringComparison.OrdinalIgnoreCase) && !notice.Contains("Guitar Pro", StringComparison.OrdinalIgnoreCase) && !notice.Contains(".gp", StringComparison.OrdinalIgnoreCase), notice);
        static string Notes(SongProject p) => string.Join(",", p.Tracks.SelectMany(t => t.Measures.SelectMany(m => m.Cells.SelectMany(c => c.Notes.Select(n => $"{n.StringIndex}/{n.Fret}/{n.MidiValue}")))));
        Check("damaged-file notice: ... and its notes are exactly those of the same file without the damage (the song is not changed)", Notes(project) == Notes(cleanProject) && Notes(project).Length > 0);

        // Through the document controller (the in-process import path) the notice reaches the opened score's notice line.
        var opened = new DocumentController().Open(path);
        Check("damaged-file notice: the open notice line of the document carries it", opened.Notice?.Contains("may be damaged", StringComparison.Ordinal) == true && opened.Project.Tracks.Count == 3, opened.Notice);
        // Through the import worker process, like a normal open.
        var notices = new List<string>();
        string? error = null;
        SongProject? viaWorker = null;
        try { viaWorker = ImportWorker.Import(path, null, notices); }
        catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
        Check("damaged-file notice: the import worker process sends it with the song", viaWorker is { Tracks.Count: 3 } && notices.Any(n => n.Contains("may be damaged", StringComparison.Ordinal)), error ?? string.Join("|", notices));
        // A clean file through the same path adds nothing.
        var cleanPath = Path.Combine(folder, "clean.gp5");
        File.WriteAllBytes(cleanPath, clean);
        var cleanNotices = new List<string>();
        try { ImportWorker.Import(cleanPath, null, cleanNotices); } catch (Exception ex) { error = ex.Message; }
        Check("damaged-file notice: a clean file sends no notice through the worker", cleanNotices.Count == 0, string.Join("|", cleanNotices));
        // Damage in the length fields stops the check quietly, it never throws.
        var broken = (byte[])clean.Clone();
        for (var i = 40; i < 80; i++) broken[i] = 0xFF;
        Check("damaged-file notice: damaged length fields in the title block do not throw", ImportPlausibility.HeaderTextDamage(broken) >= 0);
    }

    private static void PlausibilityThresholds()
    {
        var bytes = SyntheticGuitarPro35.Write(500, 40, 2);
        var project = GuitarProImporter.ImportBytes(bytes, "t.gp5");
        var notes = project.Tracks.SelectMany((track, trackIndex) => track.Measures.Select((m, bar) => (track, trackIndex, bar, m))
            .SelectMany(x => x.m.Cells.SelectMany(c => c.Notes.Select(n => (n, x.bar, x.trackIndex, x.track))))).OrderBy(x => x.bar).ThenBy(x => x.trackIndex).ToList();
        Check("damaged-file notice: the synthetic song has notes to damage", notes.Count >= 30, notes.Count.ToString());
        var before = notes.Select(x => x.n.MidiValue).ToList();

        void Damage(int count, Action<TabNote> how) { for (var i = 0; i < count; i++) how(notes[i].n); }
        var stringBefore = notes.Select(x => x.n.StringIndex).ToList();
        var fretBefore = notes.Select(x => x.n.Fret).ToList();
        void Reset() { for (var i = 0; i < notes.Count; i++) { notes[i].n.MidiValue = before[i]; notes[i].n.StringIndex = stringBefore[i]; notes[i].n.Fret = fretBefore[i]; } }

        Damage(ImportPlausibility.WarnThreshold - 1, n => n.MidiValue = 300);
        Check($"damaged-file notice: {ImportPlausibility.WarnThreshold - 1} impossible notes stay silent (a real file with a few odd values never nags)",
            ImportPlausibility.Scan(project).Count == ImportPlausibility.WarnThreshold - 1 && ImportPlausibility.Notice(project) is null);
        Reset();
        Damage(ImportPlausibility.WarnThreshold, n => n.MidiValue = -5);
        var atThreshold = ImportPlausibility.Notice(project);
        Check($"damaged-file notice: {ImportPlausibility.WarnThreshold} impossible pitches warn, with the count, the first bar and the track name",
            atThreshold is not null && atThreshold.Contains($"{ImportPlausibility.WarnThreshold} notes", StringComparison.Ordinal) && atThreshold.Contains($"first at bar {notes[0].bar + 1}", StringComparison.Ordinal) && atThreshold.Contains($"track '{notes[0].track.Name}'", StringComparison.Ordinal), atThreshold);
        Reset();
        Damage(ImportPlausibility.WarnThreshold, n => n.StringIndex = 9);
        Check("damaged-file notice: a note on a string the track does not have counts", ImportPlausibility.Scan(project).ByKind.GetValueOrDefault("string") == ImportPlausibility.WarnThreshold && ImportPlausibility.Notice(project) is not null);
        Reset();
        Damage(ImportPlausibility.WarnThreshold, n => n.Fret = 99);
        Check("damaged-file notice: a fret far past any neck counts", ImportPlausibility.Scan(project).ByKind.GetValueOrDefault("fret") == ImportPlausibility.WarnThreshold && ImportPlausibility.Notice(project) is not null);
        Reset();
        // The first odd fact is the earliest bar, even when it lies in a later track.
        var late = notes.Where(x => x.trackIndex == 1).Skip(5).Take(ImportPlausibility.WarnThreshold - 1).ToList();
        foreach (var x in late) x.n.MidiValue = 500;
        notes.First(x => x.trackIndex == 1 && x.bar < late[0].bar).n.MidiValue = 500;
        var firstBar = notes.First(x => x.trackIndex == 1 && x.bar < late[0].bar).bar + 1;
        Check("damaged-file notice: the first reported bar is the earliest odd bar in the song", ImportPlausibility.Notice(project)?.Contains($"first at bar {firstBar}", StringComparison.Ordinal) == true, ImportPlausibility.Notice(project));
        Reset();
        // Control characters in a name and a tempo outside the format's range are facts too; a tempo of 120 is fine.
        project.Tracks[0].Name = "Lead\u0001\u0002";
        var originalTempo = project.Tempo;
        project.Tempo = 9_000;
        Check("damaged-file notice: a control character in a track name and an out-of-range song tempo each count once", ImportPlausibility.Scan(project).Count == 2 && ImportPlausibility.Notice(project) is null);
        project.Tracks[0].Name = "Guitar"; project.Tempo = originalTempo;
        Check("damaged-file notice: scanning never changes the song", notes.Select(x => x.n.MidiValue).SequenceEqual(before) && ImportPlausibility.Scan(project).Count == 0);
    }
}
