using System.IO;
using System.Text;
using TabForge.Models;

namespace TabForge.Services;

/// <summary>What a person chooses when a song uses something a clean Guitar Pro file cannot hold.</summary>
public enum GpExportChoice
{
    /// <summary>Write a full native copy (.tforge, beside the target, never over an existing file), then the compatible .gp.</summary>
    KeepNativeCopy,
    /// <summary>Write only the compatible .gp.</summary>
    ExportCompatible,
    /// <summary>Write nothing.</summary>
    Cancel,
}

/// <summary>Save writes the document's own file; Export writes a copy and leaves the document alone.</summary>
public enum GpExportKind { Save, Export }

/// <summary>One kind of loss: what it is, what the .gp keeps instead, and where.</summary>
public sealed record GpLoss(string Feature, string Result, int Count, string Where);

public sealed class GpPreflightReport
{
    public List<GpLoss> Losses { get; } = new();
    /// <summary>The line telling that audio tracks are left out of a compatible .gp (null when the song has none).</summary>
    public string? AudioTrackNotice { get; set; }
    /// <summary>Plug-in chains, audio clips or mixer groups that a clean .gp stores nowhere (they need the embedded project, a .tfaudio or a .tforge).</summary>
    public bool HasNativeOnlyAudioData { get; set; }
    /// <summary>
    /// The dialog is shown only for a song that actually loses something in the file(s) about to be written (never for a harmless save).
    /// A Save writes the .gp together with its .tfaudio (or an embedded project), which hold the plug-ins, FX chains, clips and mixer groups, so there only
    /// <see cref="Losses"/> count. An Export writes the compatible .gp alone, so native-only audio data is omitted from what is written and is asked about too.
    /// </summary>
    public bool ShouldAskFor(GpExportKind kind) => Losses.Count > 0 || kind == GpExportKind.Export && HasNativeOnlyAudioData;

    public string Summary(int maxLines = 8)
    {
        var sb = new StringBuilder();
        if (AudioTrackNotice is { } audioLine) sb.AppendLine("- " + audioLine);
        foreach (var loss in Losses.Take(maxLines)) sb.AppendLine($"- {loss.Feature} ({loss.Where}): {loss.Result}");
        if (Losses.Count > maxLines) sb.AppendLine($"- and {Losses.Count - maxLines} more");
        if (HasNativeOnlyAudioData) sb.AppendLine("- TabForge audio settings (plug-ins, FX chains, clips, mixer groups): not stored in a compatible .gp file");
        return sb.ToString().TrimEnd();
    }
}

/// <summary>What a choice does to the files and to the document, decided before anything is written.</summary>
public sealed record GpExportPlan(bool Proceed, string CompatiblePath, string? NativeCopyPath, bool MarkDocumentClean, bool ChangeDocumentPath, string Note);

// Owns: the lossy-export preflight for a clean .gp export: finding what the format cannot hold and planning the files for each
//     choice.
// Does not own: the dialog and the writing (GuitarProExporter).
// Tests: TestGpLossCoverage, TestGpFidelity.
/// <summary>
/// Lossy-export preflight: for a clean score export, find what the score format (as written through alphaTab) cannot hold,
/// and decide the files and document state for each of three choices. Pure logic: the dialog lives in the views (see docs), the writes are DocumentController's.
/// The rules: shown only when something is lost; Export never changes the document; the original is never overwritten by a lossy copy; and a document
/// is marked clean only when the file it now points at holds everything (the native copy), never because a compatible .gp was written.
/// </summary>
public static class GpExportPreflight
{
    public static GpPreflightReport Analyze(SongProject project)
    {
        var report = new GpPreflightReport { AudioTrackNotice = AudioTrackExport.SkippedNotice(project) };
        var found = new Dictionary<string, (string Result, List<(string Track, int Bar)> At)>();
        void Hit(string feature, string result, string track, int bar)
        {
            if (!found.TryGetValue(feature, out var entry)) found[feature] = entry = (result, new());
            entry.At.Add((track, bar));
        }

        var fermataBars = new Dictionary<int, HashSet<int>>();
        for (var t = 0; t < project.Tracks.Count; t++)
        {
            var track = project.Tracks[t];
            if (track.IsAudio) { report.HasNativeOnlyAudioData = true; continue; }   // not written to a compatible .gp (the notice line below)
            var name = string.IsNullOrWhiteSpace(track.Name) ? $"Track {t + 1}" : track.Name;
            var drums = track.Kind == TrackKind.Drums || track.MidiChannel == 9;
            if (track.Reverb != 24 || track.Chorus != 0) Hit("Reverb / chorus sends", "not written to the .gp file; they reopen at the defaults", name, -1);
            if (track.Rig.Plugins.Count > 0 || track.AudioClips.Count > 0 || track.SoundSource != SoundSources.Midi) report.HasNativeOnlyAudioData = true;
            for (var b = 0; b < track.Measures.Count; b++)
            {
                foreach (var cells in new[] { track.Measures[b].Cells, track.Measures[b].Voice2Cells })
                    foreach (var cell in cells)
                    {
                        if (cell.Fermata) { if (!fermataBars.TryGetValue(b, out var set)) fermataBars[b] = set = new(); set.Add(t); }
                        // Beat-level facts first, for a rest as much as for a note beat: the exporter writes a rest's whammy / tremolo picking like any beat's and
                        // writes no mix change on any beat. (Only a beat counts: an unused grid slot is not written at all.)
                        if (cell.Notes.Count == 0 && !cell.IsRest) continue;
                        if (cell.WhammyPoints.Count > 4) Hit("Whammy-bar curve over four points", "reduced to origin, two middle points and end", name, b);
                        if (cell.Mix is { IsEmpty: false } mix && (mix.Volume is not null || mix.Pan is not null || mix.Program is not null || mix.Chorus is not null || mix.Reverb is not null || mix.Phaser is not null || mix.Tremolo is not null))
                            Hit("Mix-table change on a beat (volume, pan, sound)", "not written; the track keeps its starting mix", name, b);
                        if (cell.TremoloPickDenominator >= 64) Hit("Tremolo picking at 1/64", "written as 1/32", name, b);
                        // Note-level facts: a rest has no notes.
                        if (cell.Notes.Count == 0) continue;
                        var dynamics = cell.Notes.Where(n => !n.IsGraceNote).Select(n => Dynamics.NearestIndex(n.Velocity)).Distinct().Count();
                        if (dynamics > 1) Hit("Different loudness inside one chord or drum beat", "every note takes the first note's dynamic", name, b);
                        // Guitar Pro 8 keeps a ghost mark only on a note with no <Accent> element (staccato, accent, heavy accent and tenuto each replaced it in a re-save).
                        // The exporter keeps the ghost mark, and the beat's staccato / accent / heavy accent / tenuto on the other notes of the chord. Lost exactly when a ghost
                        // note would carry a mark and no plain note of the beat does (a beat of ghost notes only, or a legacy per-note Tenuto tag on the ghost notes alone).
                        bool Marked(TabNote n) => cell.Accent > 0 || cell.Tenuto || cell.Staccato || n.Techniques.Contains("Tenuto");
                        if (cell.Notes.Any(n => n.Ghost && Marked(n)) && !cell.Notes.Any(n => !n.Ghost && Marked(n)))
                            Hit("Accent, tenuto or staccato on ghost notes only", "written as ghost notes without the mark (a .gp note keeps a ghost mark only when it has no accent, tenuto or staccato)", name, b);
                        var fadeIn = cell.Notes.Count(n => n.Techniques.Contains("FadeIn")); var fadeOut = cell.Notes.Count(n => n.Techniques.Contains("FadeOut"));
                        if (fadeIn is > 0 && fadeIn < cell.Notes.Count || fadeOut is > 0 && fadeOut < cell.Notes.Count) Hit("Fade on part of a chord", "the fade applies to the whole beat", name, b);
                        foreach (var n in cell.Notes)
                        {
                            // a clean .gp holds origin, one flat middle stretch and destination: a curve those four fields draw (a quick rise then a hold, a release then a hold,
                            // a bend and release with a plateau) is written as drawn; only a curve with more turns than that is reduced to its closest such shape
                            if (n.BendPoints.Count >= 2 && !drums)
                            {
                                var curve = GuitarProBendCurve.Normalise(n.BendPoints.Select(p => (p.Offset, p.Value)));
                                var held = GuitarProBendCurve.Fit(curve) is not null
                                    ? GuitarProBendCurve.HeldExactly(curve)
                                    : GuitarProExporter.SimplifyBend(curve).Count == n.BendPoints.Count;
                                if (!held) Hit("Bend curve with more turns than a .gp file keeps", "reduced to origin, one flat middle stretch and end", name, b);
                            }
                        }
                    }
            }
        }
        // a fermata is a bar-position mark in Guitar Pro: on one track only, it shows on every track
        var notation = project.NotationTracks.ToList();
        for (var b = 0; b < (notation.Count == 0 ? 0 : notation.Max(t => t.Measures.Count)); b++)
            if (fermataBars.TryGetValue(b, out var set) && set.Count < notation.Count(t => b < t.Measures.Count) && notation.Count > 1)
                Hit("Fermata on some tracks only", "a .gp file stores it for every track", "all tracks", b);

        // mixer groups, group / master chains and the other things the audio-data save offers its choices for (plug-ins and clips are caught per track above)
        if (MixerGroups.HasAudioData(project)) report.HasNativeOnlyAudioData = true;

        foreach (var (feature, (result, at)) in found)
            report.Losses.Add(new GpLoss(feature, result, at.Count, Describe(at)));
        return report;
    }

    private static string Describe(List<(string Track, int Bar)> at)
    {
        var parts = new List<string>();
        foreach (var group in at.GroupBy(x => x.Track).Take(3))
        {
            var bars = group.Select(x => x.Bar).Where(b => b >= 0).Distinct().OrderBy(b => b).ToList();
            parts.Add(bars.Count == 0 ? group.Key : $"{group.Key}: bar{(bars.Count == 1 ? "" : "s")} {Ranges(bars)}");
        }
        if (at.Select(x => x.Track).Distinct().Count() > 3) parts.Add("more tracks");
        return string.Join("; ", parts);
    }

    private static string Ranges(List<int> zeroBased)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < zeroBased.Count;)
        {
            var j = i;
            while (j + 1 < zeroBased.Count && zeroBased[j + 1] == zeroBased[j] + 1) j++;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(j > i ? $"{zeroBased[i] + 1}-{zeroBased[j] + 1}" : $"{zeroBased[i] + 1}");
            i = j + 1;
            if (sb.Length > 40 && i < zeroBased.Count) { sb.Append(", ..."); break; }
        }
        return sb.ToString();
    }

    /// <summary>True when the file already at <paramref name="path"/> holds native TabForge content a lossy .gp would destroy.</summary>
    public static bool OriginalHoldsNative(string path, bool ignoreSidecar = false)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (path.EndsWith(FileTypes.Project, StringComparison.OrdinalIgnoreCase)) return true;
            if (!path.EndsWith(".gp", StringComparison.OrdinalIgnoreCase)) return false;
            return GuitarProExporter.TryReadEmbedded(path) is not null || (!ignoreSidecar && File.Exists(AudioDataFile.PathFor(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }   // unreadable: assume it matters, never overwrite // Not logged: embedded project probe: false means none
    }

    /// <summary>"name (full copy).tforge", then "(full copy 2)" ...: the first path that does not exist.</summary>
    public static string UniqueSibling(string path, string label)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
        var candidate = Path.Combine(folder, $"{stem} ({label}){ext}");
        for (var i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++) candidate = Path.Combine(folder, $"{stem} ({label} {i}){ext}");
        return candidate;
    }

    public static GpExportPlan Plan(GpPreflightReport report, GpExportChoice choice, GpExportKind kind, string targetGpPath, string? documentPath)
    {
        if (choice == GpExportChoice.Cancel) return new GpExportPlan(false, targetGpPath, null, false, false, "Cancelled: nothing was written.");
        // The compatible file goes to the target unless the target is the original native content (the document's own file, or one holding an embedded
        // project / sidecar / .tforge): a lossy copy never replaces that.
        var sameAsDocument = documentPath is not null && string.Equals(Path.GetFullPath(documentPath), Path.GetFullPath(targetGpPath), StringComparison.OrdinalIgnoreCase);
        // (Not only when the question is asked: an embedded project or sidecar also holds plug-ins, clips and TabForge-only fields the preflight does not list.)
        var compatible = (kind == GpExportKind.Export && sameAsDocument) || OriginalHoldsNative(targetGpPath, ignoreSidecar: kind == GpExportKind.Save) ? UniqueSibling(targetGpPath, "compatible") : targetGpPath;
        if (choice == GpExportChoice.KeepNativeCopy)
        {
            var native = UniqueSibling(Path.ChangeExtension(targetGpPath, FileTypes.Project), "full copy");
            // A Save follows the native copy (it holds everything), so the document is clean there; an Export is a side file and the document is untouched.
            var save = kind == GpExportKind.Save;
            return new GpExportPlan(true, compatible, native, save, save, $"Full copy kept as {Path.GetFileName(native)}; the compatible .gp file is {Path.GetFileName(compatible)}.");
        }
        // A Save with "compatible" writes the song's own clean pair (.gp + .tfaudio, which keeps plug-ins, FX and mixer groups), exactly as the clean save always did,
        // and the song is then saved: the person accepted the listed losses. Only when the target is protected (redirected to a sibling) does the song stay unsaved.
        if (kind == GpExportKind.Save && compatible == targetGpPath) return new GpExportPlan(true, compatible, null, true, true, "");
        return new GpExportPlan(true, compatible, null, false, false,
            report.ShouldAskFor(kind) ? $"Compatible .gp file written as {Path.GetFileName(compatible)}; the song in TabForge still has what it cannot hold and stays unsaved." : "");
    }
}
