using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AlphaTab;
using AlphaTab.Importer;
using TabForge.Models;
using TabForge.Plugins;
using static TabForge.Services.GuitarProBarConverter;
using static TabForge.Services.GuitarProBeatReader;
using static TabForge.Services.GuitarProReflection;
using static TabForge.Services.GuitarProScoreInfoReader;
using static TabForge.Services.GuitarProTrackConverter;

namespace TabForge.Services;

// Owns: converting a Guitar Pro file into a song, with the fidelity rules for ticks, voices and staves.
// Does not own: the export (GuitarProExporter) and the process isolation (ImportWorker).
// Tests: TestGuitarProFiles, TestTupletImport, TestCapoRepitchesNotes.
/// <summary>
/// Boundary converter for Guitar Pro files (.gp3/.gp4/.gp5/.gpx/.gp) built on alphaTab.
/// </summary>
/// <remarks>
/// Conversion rules that matter for fidelity:
/// - beats are placed by their real tick position (alphaTab exposes PlayStart), so triplets and
///   short notes never run off the 16-slot editor grid and truncate the rest of the bar;
/// - every voice and every staff of a track is merged into the bar grid instead of only the first;
/// - repeats / alternate endings / sections / tempo / time signature come from the master bars;
/// - ties are folded into the previous note's duration instead of creating a new attack;
/// - reflection is used throughout so a future alphaTab upgrade degrades gracefully.
/// </remarks>
public static class GuitarProImporter
{
    public static readonly string[] SupportedExtensions = FileTypes.GuitarPro;
    private static readonly byte[] Gp3To5Signature = System.Text.Encoding.ASCII.GetBytes("FICHIER GUITAR PRO v");

    /// <summary>
    /// A few stray bytes before a Guitar Pro 3-5 header (e.g. a line break added by a download) made the file
    /// unreadable. When the signature starts within the first 16 bytes and its length byte matches, skip them.
    /// </summary>
    internal static byte[] WithoutLeadingJunk(byte[] data)
    {
        var at = data.AsSpan(0, Math.Min(data.Length, 16 + 1 + Gp3To5Signature.Length)).IndexOf(Gp3To5Signature);
        if (at <= 1) return data; // already at 1 (after the length byte) or not a GP3-5 file
        var lengthByte = at - 1;
        return data[lengthByte] is >= 20 and <= 40 ? data[lengthByte..] : data;
    }

    /// <summary>
    /// Re-reads a Guitar Pro 3-5 file that alphaTab rejected and returns the bar it had reached (1-based), or null
    /// when that cannot be told. alphaTab keeps the partly built score, whose tracks hold the bars read so far.
    /// </summary>
    internal static int? LocateFailedBar(byte[] data)
    {
        try
        {
            if (data.AsSpan(0, Math.Min(data.Length, 32)).IndexOf(Gp3To5Signature) < 0) return null;
            // The partly built score comes from the compatibility boundary (the only place with private alphaTab access).
            var partial = AlphaTabBoundary.ReadPartial(data);
            if (partial is null) return null;
            var reached = partial.Tracks.Select(t => t.Staves.Count > 0 ? t.Staves[0].Bars.Count : 0).DefaultIfEmpty(0).Max();
            return Math.Max(1, (int)reached);
        }
        catch (Exception) { return null; } // Not logged: bar count hint only
    }

    /// <summary>
    /// alphaTab's ScoreLoader through <see cref="AlphaTabBoundary"/>: the patched alphaTab build reads Guitar Pro 3-5 files of up
    /// to TabForge's own bar limit (<see cref="InputLimits.MaxMeasuresPerTrack"/>), set per import, and refuses anything beyond it.
    /// </summary>
    private static object LoadScore(byte[] data) => AlphaTabBoundary.LoadScore(data, InputLimits.MaxMeasuresPerTrack);

    /// <param name="context">The import's limits and what it reports afterwards (notices, merged duplicates); null = a fresh context without limits.</param>
    public static SongProject Import(string path, ImportContext? context = null)
    {
        path = FilePathPolicy.ExistingFile(path, "score file", SupportedExtensions);
        context ??= new ImportContext();
        // Cooperative limits: the guard of a background import; none for synchronous headless use.
        context.Check();
        var raw = InputLimits.ReadBoundedBytes(path, InputLimits.MaxGuitarProFileBytes, "score file");
        return ImportBytes(raw, path, context);
    }

    /// <summary>
    /// Imports a Guitar Pro file already read into memory (at most <see cref="InputLimits.MaxGuitarProFileBytes"/>). <paramref name="path"/>
    /// only names it (extension, fallback title, <see cref="SongProject.ImportedFrom"/>); the import worker process uses this.
    /// </summary>
    internal static SongProject ImportBytes(byte[] raw, string path, ImportContext? context = null)
    {
        context ??= new ImportContext();
        if (raw.LongLength > InputLimits.MaxGuitarProFileBytes)
            throw new InvalidDataException("The score file exceeds the supported size limit.");
        // Reset before the embedded-project early return so a lossless .gp load never reports the previous import's duplicates.
        context.SkippedDuplicates = 0;
        context.DuplicateSamples.Clear();
        context.Gp3To5 = false;
        context.RawMixes = null;
        context.Check();
        // Container / header checks on the raw bytes, before anything unpacks or parses them.
        GuitarProPreParse.Validate(WithoutLeadingJunk(raw), context);
        context.Check();
        // A .gp saved by TabForge carries its complete project: load that for a lossless round trip.
        // An embedded project that is present but rejected (too big, damaged, bad version) is reported, not silently treated as absent.
        context.EmbeddedRejection = null;
        context.DamageNotice = null;
        if (path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase))
        {
            if (GuitarProExporter.ReadEmbedded(raw, out var rejected) is { } embedded) return embedded;
            if (rejected is not null) context.EmbeddedRejection = GuitarProExporter.RejectedNotice(rejected);
        }

        var data = WithoutLeadingJunk(raw);
        object score;
        // alphaTab's parse is one uninterruptible call: the guard is checked right before and after it.
        try { score = LoadScore(data); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException and not InvalidDataException and not OperationCanceledException)
        {
            // A missing or mismatched reader component and a bar count above TabForge's limit arrive as InvalidDataException (AlphaTabBoundary).
            var barHint = ex.GetBaseException() is IndexOutOfRangeException or ArgumentOutOfRangeException
                ? LocateFailedBar(data) : null;
            if (barHint is int failedBar)
                throw new InvalidDataException($"This .gp3-.gp5 file could not be read at or near bar {failedBar}: a second voice with no beats after a two-voice bar, which the score reader TabForge uses rejects. Adding a rest to that voice in the program that wrote the file fixes it.", ex);
            throw new InvalidDataException("This score file is invalid, truncated, or unsupported.", ex);
        }
        context.Check();
        context.Gp3To5 = data.AsSpan(0, Math.Min(data.Length, 32)).IndexOf(Gp3To5Signature) >= 0;
        object root = score;
        context.SkippedDuplicates = 0;
        context.DuplicateSamples.Clear();

        var project = new SongProject
        {
            Title = FirstNonEmpty(GetString(root, "Title"), GetString(root, "Name")) ?? Path.GetFileNameWithoutExtension(path),
            Subtitle = GetString(root, "SubTitle") ?? "",
            Artist = GetString(root, "Artist") ?? "",
            Album = GetString(root, "Album") ?? "",
            MusicAuthor = FirstNonEmpty(GetString(root, "Music"), GetString(root, "Author")) ?? "",
            LyricsAuthor = FirstNonEmpty(GetString(root, "Words"), GetString(root, "WordsAndMusic")) ?? "",
            Copyright = GetString(root, "Copyright") ?? "",
            TabAuthor = GetString(root, "Tab") ?? "",
            Instructions = GetString(root, "Instructions") ?? "",
            Notice = GetString(root, "Notices") ?? "",
            Lyrics = GetString(root, "Lyrics") ?? "",
            Tempo = ReadTempo(root),
            ImportedFrom = path,
            Tracks = new List<TrackModel>()
        };

        var masterBars = AsObjects(Get(root, "MasterBars"))
            .Take(InputLimits.MaxMeasuresPerTrack + 1).ToList();
        if (masterBars.Count > InputLimits.MaxMeasuresPerTrack)
            throw new InvalidDataException("The score file contains too many measures.");
        ReadTimeSignature(root, project);

        var sourceTracks = AsObjects(Get(root, "Tracks")).Take(InputLimits.MaxTracks + 1).ToList();
        if (sourceTracks.Count > InputLimits.MaxTracks)
            throw new InvalidDataException("The score file contains too many tracks.");
        var budget = new ImportBudget(context);
        var channel = 0;
        // Mix-table transitions / all-tracks flags alphaTab drops (a reference fade-out lives there).
        try { context.RawMixes = score is AlphaTab.Model.Score typed ? GuitarProMixTableScanner.Scan(data, typed) : null; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Services.Trace.Error(Services.Trace.Import, "mix-table scan: " + ex.Message); context.RawMixes = null; }
        try
        {
            foreach (var sourceTrack in sourceTracks)
            {
                context.Check();
                var track = ConvertTrack(masterBars, sourceTrack, channel, budget);
                if (track.Kind != TrackKind.Drums) channel = NextMelodicChannel(channel);
                project.Tracks.Add(track);
            }
        }
        finally { context.RawMixes = null; }

        // Repeats / endings / sections / tempo map are master-bar level: apply them to every track.
        ReadMasterBarInfo(masterBars, project, context.Gp3To5, new GuitarProMixTableScanner.TempoRampFinder(data));
        // alphaTab's Guitar Pro 7/8 reader drops a double bar on the very last bar (the file has it, the model does not): take it from the file itself.
        if (masterBars.Count > 0 && masterBars[^1] is { } lastMasterBar && !GetBool(lastMasterBar, "IsDoubleBar", false) && LastMasterBarHasDoubleBar(data))
            foreach (var track in project.Tracks)
                if (masterBars.Count - 1 < track.Measures.Count) track.Measures[masterBars.Count - 1].IsDoubleBar = true;
        context.Check();

        if (project.Tracks.Count == 0)
            throw new InvalidDataException("The score file loaded, but contained no tracks.");

        ProjectValidator.Validate(project);
        context.DamageNotice = ImportPlausibility.Notice(project, data);
        project.IsDirty = false;
        return project;
    }

    /// <summary>
    /// A tie is stored on its destination note (<see cref="TabNote.Tied"/>); the "Tie" technique marks its origin.
    /// alphaTab leaves the origin unflagged when it cannot link a tie (drum ties, some GP3-5 files), so a re-export
    /// and re-import then "gained" origin flags. Marking the previous note on the same string here makes the two agree.
    /// </summary>
    internal static void LinkTieOrigins(TrackModel track)
    {
        foreach (var voice in new[] { 0, 1 })
        {
            var cells = track.Measures.SelectMany(m => voice == 0 ? m.Cells : m.Voice2Cells).ToList();
            for (var k = 1; k < cells.Count; k++)
                foreach (var destination in cells[k].Notes)
                {
                    if (!destination.Tied || destination.IsGraceNote) continue;
                    for (var j = k - 1; j >= 0 && j >= k - 64; j--)
                    {
                        var origin = cells[j].Notes.FirstOrDefault(n => !n.IsGraceNote && n.StringIndex == destination.StringIndex);
                        if (origin is null) continue;
                        origin.Techniques.Add("Tie");
                        break;
                    }
                }
        }
    }

    /// <summary>
    /// GM drum sound of a percussion note. Guitar Pro 3-5 store the GM number itself. Guitar Pro 6/7/8
    /// (.gpx/.gp) store an index into the track's percussion articulation list, whose entry names the
    /// sounding MIDI number (OutputMidiNumber): reading that index as a GM number dropped every GP7/8 drum
    /// note (indices 0..26 fall outside the drum range) and turned GPX drums into wrong or missing hits.
    /// </summary>
    internal static int DrumPitch(object sourceNote, IReadOnlyList<object>? articulations = null)
    {
        // A tie destination takes its sound from its origin; the origin of a long tie can itself be a tie destination without a sound
        // of its own, so follow the chain to the note that has one.
        var pitchSource = sourceNote;
        for (var step = 0; step < 64 && GetBool(pitchSource, "IsTieDestination", false) && Get(pitchSource, "TieOrigin") is { } origin; step++)
        {
            pitchSource = origin;
            if (GetInt(origin, "PercussionArticulation", -1) > 0 || GetInt(origin, "Fret", -1) >= 0 || GetInt(origin, "RealValue", 0) > 0) break;
        }
        var articulation = GetInt(pitchSource, "PercussionArticulation", -1);
        if (articulations is { Count: > 0 } && articulation >= 0 && articulation < articulations.Count)
        {
            var output = GetInt(articulations[articulation], "OutputMidiNumber", 0);
            if (output is >= 27 and <= 87) return output;
        }
        if (articulation is >= 27 and <= 87) return articulation;
        // the reference's own articulation ids above the GM range (91 snare rim shot, 92 half-open hi-hat,
        // 93 ride edge, 95 splash, 97 crash...): alphaTab's Guitar Pro articulation table gives the MIDI sound.
        if (articulation > 87 && GpArticulations.OutputMidi(articulation) is >= 27 and <= 87 and var known)
            return known;
        // a GP7/8 articulation list entry outside the GM drum range (a kit sound with its own output number, or one TabForge wrote
        // for a value outside it): its output number is the sound, and reading anything else turned it into another drum
        if (articulations is { Count: > 0 } && articulation >= 0 && articulation < articulations.Count
            && GetInt(articulations[articulation], "OutputMidiNumber", 0) is > 0 and <= 127 and var outside) return outside;
        var real = GetInt(pitchSource, "RealValue", 0);
        if (real is >= 27 and <= 87) return real;
        // Guitar Pro 3-5 write the GM drum number as the note's fret; many drum hits arrive with neither an
        // articulation nor a sounding value, which dropped up to 40% of a GP3-5 drum part.
        var fret = GetInt(pitchSource, "Fret", 0);
        return fret is >= 27 and <= 87 ? fret : real;
    }

    /// <summary>
    /// Written tremolo-picking speed as a note denominator (8, 16, 32), or 0 for none. Guitar Pro 3-5 files write
    /// the number of slashes (1 = 1/8, 2 = 1/16, 3 = 1/32) and alphaTab exposes that as TremoloPicking.Marks, but
    /// its TremoloSpeed name is one step slow there (1/8 reads as Quarter), which halved the playback speed.
    /// </summary>
    internal static int TremoloDenominator(object beat, bool gp3To5)
    {
        // alphaTab 1.8 derives TremoloSpeed from the marks one step slow for every format (a GP7 1/16 read back as 1/4 or 1/8), so the marks come first.
        var marks = GetDouble(Get(beat, "TremoloPicking"), "Marks", double.NaN);
        if (double.IsFinite(marks) && marks >= 1)
            return Math.Clamp(8 << (int)Math.Round(marks - 1), 8, 64);
        var speed = Get(beat, "TremoloSpeed");
        if (IsSet(speed)) return DurationToDenominator(speed);
        if (double.IsFinite(marks) && marks >= 1)
            return Math.Clamp(8 << (int)Math.Round(marks - 1), 8, 64);
        return 0;
    }

    /// <summary>alphaTab finger (Thumb, IndexFinger, MiddleFinger, AnnularFinger, LittleFinger) as 0..4; null when unknown or none.</summary>
    internal static int? FingerOf(object? finger) => finger?.ToString() switch
    {
        "Thumb" => 0,
        "IndexFinger" => 1,
        "MiddleFinger" => 2,
        "AnnularFinger" => 3,
        "LittleFinger" => 4,
        _ => null
    };

    /// <summary>
    /// Sounding pitch of a harmonic: natural = the string's harmonic at that fret; artificial and pinch =
    /// the fretted note plus the written harmonic interval (a third above the octave is 16, not 12); tapped = the tapped
    /// fret plus an octave; semi = the harmonic on top of the fundamental (playback adds the fundamental separately).
    /// </summary>
    internal static int HarmonicMidi(string harmonicType, int openPitch, int fret, double node)
    {
        // Kept from before: the fifth (7/19) and double octave (5/24) nodes are frets, not semitone intervals.
        var interval = Math.Round(node, 1) is 5 or 7 or 19 or 24 ? ArtificialInterval(node)
            : node >= 1 && node <= 48 ? (int)Math.Round(node) : 12;
        var midi = harmonicType switch
        {
            "Natural" => openPitch + HarmonicInterval(fret),
            "Tap" => openPitch + interval + 12,
            "Artificial" or "Pinch" => openPitch + fret + interval,
            _ => openPitch + fret + ArtificialInterval(node)
        };
        return Math.Clamp(midi, 0, 127);
    }

    /// <summary>Artificial/pinch/tapped harmonics sound an octave above the fretted note unless the node is an explicit fifth (7) or double octave (5).</summary>
    public static int ArtificialInterval(double node) => Math.Round(node, 1) switch
    {
        7 or 19 => 19,
        5 or 24 => 24,
        _ => 12,
    };

    /// <summary>Semitones above the open string sounded by a harmonic touched at the given fret (the standard table).</summary>
    public static int HarmonicInterval(double fret) => Math.Round(fret, 1) switch
    {
        12 or 24 => 12,
        7 or 19 => 19,
        5 => 24,
        4 or 9 or 16 => 28,
        3.2 or 3 => 31,
        2.7 => 34,
        2.4 => 36,
        >= 11.9 and <= 12.1 => 12,
        _ => fret >= 12 ? 12 : fret >= 7 ? 19 : fret >= 5 ? 24 : 28,
    };

    /// <summary>standard drum TAB lines: cymbals top, then hi-hat, high toms, snare, low toms, kick.</summary>
    public static int DrumLine(int midi) => midi switch
    {
        49 or 51 or 52 or 53 or 55 or 57 or 59 => 0, // crash / ride / china / splash
        42 or 44 or 46 => 1,                         // hi-hat
        48 or 50 => 2,                               // high toms
        37 or 38 or 39 or 40 => 3,                   // snare / rim / clap
        41 or 43 or 45 or 47 => 4,                   // floor / low / mid toms
        35 or 36 => 5,                               // kick
        _ => 2,
    };

    /// <summary>
    /// A per-note duration fraction from alphaTab as a percent, or null to keep 100%. Only GPX / .gp can carry one; the value
    /// must be finite and within 0.05..2.0 (the editor allows 1..200%), which rejects alphaTab's garbage for GP3-5 palm mutes.
    /// </summary>
    internal static int? SanePerNoteDurationPercent(double fraction, bool gp3To5)
    {
        if (gp3To5 || !double.IsFinite(fraction) || fraction < 0.05 || fraction > 2.0 || Math.Abs(fraction - 1.0) <= 0.005) return null;
        return Math.Clamp((int)Math.Round(fraction * 100), 5, 200);
    }


    /// <summary>Diagnostics: "Type.Name" of every property the importer asked for that alphaTab does not have.</summary>
    internal static IReadOnlyCollection<string> MissingPropertyNames => GuitarProReflection.MissingPropertyNames;
}
