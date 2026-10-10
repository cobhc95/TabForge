using System.IO;
using System.Linq;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge;

// Loss coverage (prevention): what a clean score export actually loses must be listed by the save/export question.
// For every capability fixture (A01..A54), every Guitar Pro fixture (the 15 synthetic songs) and a few edge songs, the song is exported as a clean .gp,
// reopened, and compared with the round-trip semantics (RtScoreFacts + RtAudioFacts, plus the mix change on a rest beat, which the score facts leave out).
// Every measured difference that is a real loss (not a meaning-preserving allowance of the capability record) must be
//   - listed by GpExportPreflight.Analyze (a named feature), or
//   - native-only audio data (the question is asked by Export), or
//   - on the explicit exemption list below, each tied to a capability-record row.
// A new kind of loss that nobody listed fails here, so the question cannot silently fall behind the exporter.
public static partial class SelfTest
{
    private enum LcKind { Feature, NativeAudio, Exempt, Kept }

    /// <summary>
    /// How a measured category is covered. <paramref name="When"/> narrows it to the differences that are a real loss (null: every difference); the others are the same
    /// music (a hold written as the destination offset, a value the importer filled in). <see cref="LcKind.Kept"/>: the feature is written exactly, so any real difference is a bug.
    /// </summary>
    private sealed record LcRule(string Category, LcKind Kind, string Text, Func<RtDiff, bool>? When = null);

    private const string LcFermata = "Fermata on some tracks only";
    private const string LcMix = "Mix-table change on a beat (volume, pan, sound)";
    private const string LcGhost = "Accent, tenuto or staccato on ghost notes only";

    private static readonly LcRule[] LcRules =
    {
        // ---- listed by the preflight, one named feature each
        new("note.dynamic", LcKind.Feature, "Different loudness inside one chord or drum beat"),
        new("note.bend", LcKind.Feature, "Bend curve with more turns than a .gp file keeps", d => d.Category != "note.bend" || !LcSameBend(d.Expected, d.Actual)),
        new("beat.whammy", LcKind.Feature, "Whammy-bar curve over four points"),
        new("beat.mix", LcKind.Feature, LcMix, LcMixIsLoss),
        new("beat.fermata", LcKind.Feature, LcFermata, d => d.Expected == "0" && d.Actual == "1"),
        new("beat.tremoloPick", LcKind.Feature, "Tremolo picking at 1/64", d => d.Expected == "64"),
        new("note.technique:FadeIn.extra", LcKind.Feature, "Fade on part of a chord"),
        new("note.technique:FadeOut.extra", LcKind.Feature, "Fade on part of a chord"),
        new("beat.accent", LcKind.Feature, LcGhost, d => d.Expected != "0" && d.Actual != d.Expected),
        new("beat.staccato", LcKind.Feature, LcGhost, d => d.Expected == "1" && d.Actual == "0"),
        new("beat.tenuto", LcKind.Feature, LcGhost, d => d.Expected == "1" && d.Actual == "0"),
        new("note.ghost", LcKind.Feature, LcGhost, d => d.Expected == "1" && d.Actual == "0"),
        new("track.reverb", LcKind.Feature, "Reverb / chorus sends"), new("track.chorus", LcKind.Feature, "Reverb / chorus sends"),

        // ---- native-only audio data: a clean .gp has nowhere to put it, so the Export question is asked (the Save pair and the embedded project keep it)
        new("track.rig", LcKind.NativeAudio, "plug-in chain"), new("track.plug", LcKind.NativeAudio, "plug-ins"), new("track.clip", LcKind.NativeAudio, "audio and MIDI clips"),
        new("track.clips", LcKind.NativeAudio, "audio and MIDI clips"), new("track.lanes", LcKind.NativeAudio, "clip lanes"), new("track.soundSource", LcKind.NativeAudio, "sound source"),
        new("track.midiSound", LcKind.NativeAudio, "General MIDI sound switch of a plug-in track"),
        new("track.mixerGroup", LcKind.NativeAudio, "mixer group"), new("mixer.", LcKind.NativeAudio, "mixer, group and master chains"),

        // ---- written exactly: a difference is a bug (a value the importer fills in when the source set none is not a difference)
        new("note.trillDur", LcKind.Kept, "trill speed (A17)", d => !RtSourceHadNone(d)),

        // ---- measured, deliberately not listed: each with its capability-record row
        new("note.velocity", LcKind.Exempt, "A01: loudness is kept as the nearest of the eight dynamic marks, so the note keeps its dynamic; only a value between two marks moves"),
        new("note.graceSlots", LcKind.Exempt, "A34: the length of a grace note is normalised by the writer and playback does not use it"),
        new("note.technique:GraceBend.missing", LcKind.Exempt, "A40: only the tag name of a bend grace note is lost, the grace note and its bend are written"),
        new("note.technique:Bend.extra", LcKind.Exempt, "A40: the bend of a bend grace note is written; it reads back as a Bend on the grace note instead of the GraceBend tag"),
        new("note.technique:Tenuto.missing", LcKind.Exempt, "A10: a legacy Tenuto tag is written and read back as the beat's tenuto mark"),
        new("track.midiDevice", LcKind.Exempt, "A42: a MIDI output device number is machine-specific, and would point at hardware that is not there"),
        new("track.audioInput", LcKind.Exempt, "A43: a live recording setting, machine-specific"),
        new("track.recordArm", LcKind.Exempt, "A43: a live recording setting"),
        new("track.monitor", LcKind.Exempt, "A44: a live monitoring setting"),
        new("track.tint", LcKind.Exempt, "A45: a view preference of the track list"),
        new("track.performer", LcKind.Exempt, "A49: a display-only note about the track"),
        new("track.trackNotes", LcKind.Exempt, "A50: a display-only note about the track"),
        new("track.drumMap", LcKind.Exempt, "A52: a TabForge-only notation preset"),
    };

    /// <summary>A bend read back with its final hold written as the destination offset sounds the same (the point list "0:0;15:4;60:4" comes back as "0:0;15:4").</summary>
    private static bool LcSameBend(string expected, string actual)
    {
        static List<(double Offset, double Value)> Parse(string text)
        {
            var points = text.Length == 0 ? new List<(double, double)>() : text.Split(';').Select(p => { var a = p.Split(':'); return (double.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), double.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture)); }).ToList();
            if (points.Count > 0 && points[^1].Item1 < 60) points.Add((60, points[^1].Item2));
            return points;
        }
        return Parse(expected).SequenceEqual(Parse(actual));
    }

    /// <summary>A mix change that is only a tempo is kept as a tempo automation (bar.tempo and the mid-bar tempos are compared strictly), so it is not a loss of the mix.</summary>
    private static bool LcMixIsLoss(RtDiff d)
    {
        var parts = d.Expected.Split('/');   // program/volume/pan/tempo/transition/allTracks
        return parts.Length >= 3 && (parts[0].Length > 0 || parts[1].Length > 0 || parts[2].Length > 0);
    }

    private static LcRule? LcRuleFor(string category) =>
        LcRules.FirstOrDefault(r => category == r.Category || category.StartsWith(r.Category + ".", StringComparison.Ordinal) || r.Category.EndsWith('.') && category.StartsWith(r.Category, StringComparison.Ordinal));

    /// <summary>
    /// A difference the capability record classes as MeaningPreserving (the same music under another spelling, never a loss). A category that has an allowance in the
    /// clean .gp profile counts only where that allowance applies (its narrowing is honoured); one without (the .tfaudio rows) counts as the record says.
    /// </summary>
    private static bool LcIsMeaningPreserving(RtDiff d)
    {
        static bool Matches(string key, string category) => key.EndsWith('*') ? category.StartsWith(key[..^1], StringComparison.Ordinal) : category == key;
        var recorded = GfCases().Any(c => c.Class == GfClass.MeaningPreserving && Matches(c.Allowance.Split(' ')[0], d.Category));
        if (!recorded) return false;
        var inProfile = RtGpCleanProfile.Losses.Keys.Any(k => Matches(k, d.Category));
        return !inProfile || RtGpCleanProfile.ReasonFor(d.Category, d) is not null;
    }

    /// <summary>The mix changes on rest beats, which RtScoreFacts leaves out (a rest is not a fact there). A clean .gp writes none of them.</summary>
    private static Dictionary<string, string> LcRestMixFacts(SongProject p)
    {
        var f = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var t = 0; t < p.Tracks.Count; t++)
            for (var b = 0; b < p.Tracks[t].Measures.Count; b++)
            {
                var measure = p.Tracks[t].Measures[b];
                foreach (var (v, cells) in new[] { (0, measure.Cells), (1, measure.Voice2Cells) })
                    foreach (var (cell, onset) in RtBeats(cells))
                        if (cell.Notes.Count == 0 && cell.Mix is { IsEmpty: false } mix)
                            f[$"t{t}/b{b}/v{v}/@{RtF(onset)}|mix"] = $"{mix.Program}/{mix.Volume}/{mix.Pan}/{mix.Tempo}/{mix.TransitionBeats}/{mix.AllTracks}";
            }
        return f;
    }

    private static Dictionary<string, string> LcFacts(SongProject p)
    {
        var f = RtScoreFacts(p, out _, out _);
        foreach (var (k, v) in RtAudioFacts(p)) f[k] = v;
        // the name, articulation map and auto-pitch of a chain with no plug-in are labels, not data (the importer names the chain after the instrument)
        foreach (var key in f.Keys.Where(k => k.EndsWith("rig.count", StringComparison.Ordinal) && f[k] == "0").ToList())
        {
            var stem = key[..^"count".Length];
            foreach (var label in new[] { "name", "articulationMap", "autoPitch" }) f.Remove(stem + label);
        }
        foreach (var (k, v) in LcRestMixFacts(p)) f[k] = v;
        return f;
    }

    /// <summary>What a clean .gp export of <paramref name="song"/> really loses: the round-trip differences that are not meaning-preserving.</summary>
    private static List<RtDiff> LcMeasureLosses(SongProject song, string folder, string name, HashSet<string>? firedAllowances = null)
    {
        var expected = RtBakedTranspose(song);
        var back = RtViaGp(song, folder, name, embed: false);
        var all = RtCompare(LcFacts(expected), LcFacts(back));
        if (firedAllowances is not null)
            foreach (var d in all.Where(d => RtGpCleanProfile.ReasonFor(d.Category, d) is not null))
                foreach (var key in RtGpCleanProfile.Losses.Keys)
                    if (key == d.Category || key.EndsWith('*') && d.Category.StartsWith(key[..^1], StringComparison.Ordinal)) firedAllowances.Add(key);
        return all.Where(d => !LcIsMeaningPreserving(d)).ToList();
    }

    private static List<(string Name, SongProject Song)> LcSongs()
    {
        var songs = new List<(string, SongProject)>();
        foreach (var c in GfCases()) songs.Add((c.Id, c.Build()));
        foreach (var f in GfFixtures()) songs.Add(("fixture " + f.Id, f.Song));
        // edge songs
        songs.Add(("edge rest-mix", GfSong(1, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 1, 3));
            GfPut(t, 0, 4, 4).Mix = new MixChange { Volume = 40, Pan = 20 };   // a rest beat with a volume and pan change
        })));
        songs.Add(("edge plug-in only", GfSong(1, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 1, 3));
            t.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = RtMissingPlugin, Type = PluginSlotType.Instrument, Format = "VST3", State = "AAAA" });
            t.SoundSource = SoundSources.Plugins;
        })));
        songs.Add(("edge clip only", GfSong(1, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 1, 3));
            t.AudioClips.Add(new AudioClip { File = "", Name = "midi take", StartSec = 1, SourceLengthSec = 2, FileLengthSec = 2, Notes = new List<ClipNote> { new(0, 0.5, 60, 100) } });
        })));
        songs.Add(("edge mixer group only", GfSong(1, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 1, 3));
            t.MixerGroup = MixerGroups.Guitars;
        }, s => s.Mixer.Edit(MixerGroups.Guitars).Volume = 90)));
        songs.Add(("edge plain", GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)))));
        songs.Add(("dense song", RtDenseSong()));
        songs.Add(("showcase song", RtShowcaseSong()));
        songs.Add(("technique song", RtTechniqueSong()));
        songs.Add(("audio song", RtAudioSong()));
        // the shipped demo song: whammy beats of sub-types the technique song lacks (its round trip failed once when allowances for them were removed as "never fired")
        if (SfDemoSongPath() is { } demoPath) songs.Add(("demo song", GuitarProImporter.Import(demoPath)));
        return songs;
    }

    private static void TestGpLossCoverage()
    {
        var folder = RtFolder();
        try
        {
            var songs = LcSongs();
            var fired = new HashSet<string>(StringComparer.Ordinal);
            var withLoss = 0; var exempted = new Dictionary<string, int>(); var unclassified = new HashSet<string>();
            foreach (var (name, song) in songs)
            {
                var id = name.Replace(' ', '-');
                List<RtDiff> lost;
                try { lost = LcMeasureLosses(song, folder, id, fired); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Check($"loss coverage [{name}]: the song exports and reopens", false, $"{ex.GetType().Name}: {ex.Message}"); continue; }
                var report = GpExportPreflight.Analyze(song);
                var features = report.Losses.Select(l => l.Feature).ToHashSet(StringComparer.Ordinal);
                var missing = new List<string>(); var needAsk = false;
                foreach (var group in lost.GroupBy(d => d.Category))
                {
                    var rule = LcRuleFor(group.Key);
                    Log.Add($"  loss  [{name}] {group.Key} x{group.Count()} e.g. {group.First().Where} '{group.First().Expected}' -> '{group.First().Actual}'");
                    var real = rule is null ? group.ToList() : rule.When is null ? group.ToList() : group.Where(rule.When).ToList();
                    if (real.Count == 0) continue;
                    if (rule is null) { missing.Add($"{group.Key} (not classified: {real[0].Where} '{real[0].Expected}' -> '{real[0].Actual}')"); unclassified.Add(group.Key); continue; }
                    switch (rule.Kind)
                    {
                        case LcKind.Feature:
                            needAsk = true;
                            if (!features.Contains(rule.Text)) missing.Add($"{group.Key} (the preflight does not list '{rule.Text}': {real[0].Where})");
                            break;
                        case LcKind.NativeAudio:
                            needAsk = true;
                            if (!report.HasNativeOnlyAudioData) missing.Add($"{group.Key} ({rule.Text}: not reported as native-only audio data)");
                            break;
                        case LcKind.Kept:
                            missing.Add($"{group.Key} ({rule.Text} is written exactly, but differs: {real[0].Where} '{real[0].Expected}' -> '{real[0].Actual}')");
                            break;
                        default:
                            exempted[rule.Category] = exempted.GetValueOrDefault(rule.Category) + 1;
                            break;
                    }
                }
                if (lost.Count > 0) withLoss++;
                Check($"loss coverage [{name}]: every loss the clean .gp really causes is in the preflight", missing.Count == 0, string.Join(" | ", missing));
                if (needAsk) Check($"loss coverage [{name}]: an export that loses something asks", report.ShouldAskFor(GpExportKind.Export), report.Summary());
            }
            Log.Add($"  info  loss coverage: {songs.Count} songs, {withLoss} with a measured difference; exempted categories used: {string.Join(", ", exempted.OrderBy(x => x.Key).Select(x => $"{x.Key} x{x.Value}"))}");
            var stale = RtGpCleanProfile.Losses.Keys.Where(k => !fired.Contains(k)).OrderBy(k => k).ToList();
            Check("loss coverage: every allowance of the clean .gp round-trip profile fires on at least one of these songs (a stale one hides scope)", stale.Count == 0, string.Join(", ", stale));
            Check("loss coverage: more than 60 songs measured (54 capability rows, 15 fixtures, edge songs)", songs.Count > 60, songs.Count.ToString());
            Check("loss coverage: no measured category is left unclassified", unclassified.Count == 0, string.Join(", ", unclassified));
            Check("loss coverage: every exemption names its capability-record row that exists", LcRules.Where(r => r.Kind == LcKind.Exempt).All(r => GfCases().Any(c => r.Text.StartsWith(c.Id + ":", StringComparison.Ordinal))),
                string.Join(", ", LcRules.Where(r => r.Kind == LcKind.Exempt && !GfCases().Any(c => r.Text.StartsWith(c.Id + ":", StringComparison.Ordinal))).Select(r => r.Category)));
            LcQuestionPaths(folder);
        }
        finally { RtCleanup(folder); }
    }

    /// <summary>Which save and export paths ask when the song only has native-only audio data, and which do not (the data is kept there).</summary>
    private static void LcQuestionPaths(string folder)
    {
        var dir = Path.Combine(folder, "paths"); Directory.CreateDirectory(dir);
        SongProject PluginSong() => GfSong(1, t =>
        {
            GfPut(t, 0, 0, 4, RtNote(t, 1, 3));
            t.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = RtMissingPlugin, Type = PluginSlotType.Instrument, Format = "VST3", State = "AAAA" });
            t.SoundSource = SoundSources.Plugins;
        });
        var plugin = GpExportPreflight.Analyze(PluginSong());
        Check("question paths: a song whose only omission is a plug-in lists no loss but is native-only", plugin.Losses.Count == 0 && plugin.HasNativeOnlyAudioData);
        Check("question paths: Export asks about it (the compatible .gp alone has nowhere to keep it)", plugin.ShouldAskFor(GpExportKind.Export));
        Check("question paths: Save does not (the .gp + .tfaudio pair or the embedded project keeps it)", !plugin.ShouldAskFor(GpExportKind.Save));
        var restMix = GpExportPreflight.Analyze(GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3)); GfPut(t, 0, 4, 4).Mix = new MixChange { Program = 30 }; }));
        Check("question paths: a program change on a rest beat is listed", restMix.Losses.Any(l => l.Feature == LcMix) && restMix.ShouldAskFor(GpExportKind.Save));
        var restWhammy = GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3)); var rest = GfPut(t, 0, 4, 4); rest.WhammyPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 10, Value = -4 }, new() { Offset = 20, Value = 2 }, new() { Offset = 30, Value = -8 }, new() { Offset = 60, Value = 0 } }; });
        Check("question paths: a whammy curve of five points on a rest beat is listed too", GpExportPreflight.Analyze(restWhammy).Losses.Any(l => l.Feature.StartsWith("Whammy", StringComparison.Ordinal)));
        var unusedSlot = GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3)); t.Measures[0].Cells[8].Mix = new MixChange { Volume = 10 }; });
        Check("question paths: a mix change on an unused grid slot (no beat is written there) is not listed", GpExportPreflight.Analyze(unusedSlot).Losses.Count == 0);

        var flow = new DocumentSaveFlow(new DocumentController());
        // Export asks
        var ask = new DoAsk { Preflight = GpExportChoice.Cancel };
        var exported = flow.ExportGuitarProAsync(DocumentSession.FromProject(PluginSong(), null), Path.Combine(dir, "exp.gp"), "", ask, DoNoStates).GetAwaiter().GetResult();
        Check("question paths: exporting a plug-in song asks once; Cancel writes nothing", ask.PreflightQuestions == 1 && exported.Cancelled && !File.Exists(Path.Combine(dir, "exp.gp")));
        ask = new DoAsk { Preflight = GpExportChoice.ExportCompatible };
        flow.ExportGuitarProAsync(DocumentSession.FromProject(PluginSong(), null), Path.Combine(dir, "exp2.gp"), "", ask, DoNoStates).GetAwaiter().GetResult();
        Check("question paths: choosing the compatible export writes the .gp (and no .tfaudio)", File.Exists(Path.Combine(dir, "exp2.gp")) && !File.Exists(AudioDataFile.PathFor(Path.Combine(dir, "exp2.gp"))));
        ask = new DoAsk();
        flow.ExportGuitarProAsync(DocumentSession.FromProject(GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3))), null), Path.Combine(dir, "plain.gp"), "", ask, DoNoStates).GetAwaiter().GetResult();
        Check("question paths: exporting a plain song asks nothing", ask.PreflightQuestions == 0 && File.Exists(Path.Combine(dir, "plain.gp")));
        // Save does not ask: the audio data goes into the pair / the embedded project
        ask = new DoAsk { Audio = AudioDataSaveChoice.GpPlusDataFile };
        var saveDoc = DocumentSession.FromProject(PluginSong(), null); saveDoc.Project.IsDirty = true;
        var savePath = Path.Combine(dir, "save.gp");
        var saved = flow.SaveAsync(saveDoc, savePath, "", ask, DoNoStates).GetAwaiter().GetResult();
        Check("question paths: saving a plug-in song as .gp + .tfaudio asks only how to save, not about losses, and writes the pair",
            ask.AudioQuestions == 1 && ask.PreflightQuestions == 0 && saved.Saved && File.Exists(savePath) && File.Exists(AudioDataFile.PathFor(savePath)), saved.Message);
        var reopened = new DocumentController().Open(savePath).Project;
        Check("question paths: the plug-in is not lost by that save (it comes back from the .tfaudio)", reopened.Tracks[0].Rig.Plugins.Count == 1 && reopened.Tracks[0].Rig.Plugins[0].Name == "Synth");
        ask = new DoAsk { Audio = AudioDataSaveChoice.GpWithEmbeddedProject };
        var embedDoc = DocumentSession.FromProject(PluginSong(), null); embedDoc.Project.IsDirty = true;
        var embedPath = Path.Combine(dir, "embed.gp");
        flow.SaveAsync(embedDoc, embedPath, "", ask, DoNoStates).GetAwaiter().GetResult();
        Check("question paths: saving with the embedded project asks no loss question and keeps the plug-in",
            ask.PreflightQuestions == 0 && GuitarProExporter.TryReadEmbedded(embedPath) is not null && new DocumentController().Open(embedPath).Project.Tracks[0].Rig.Plugins.Count == 1);
        // a real loss in a Save with the pair is still asked
        var both = PluginSong(); both.Tracks[0].Reverb = 60;
        ask = new DoAsk { Audio = AudioDataSaveChoice.GpPlusDataFile, Preflight = GpExportChoice.Cancel };
        var bothSaved = flow.SaveAsync(DocumentSession.FromProject(both, null), Path.Combine(dir, "both.gp"), "", ask, DoNoStates).GetAwaiter().GetResult();
        Check("question paths: a Save that also loses a listed feature (a reverb send) is asked once", ask.PreflightQuestions == 1 && bothSaved.Cancelled);
    }

    /// <summary>
    /// `TabForge.exe --gp-loss-coverage &lt;report&gt; [group]`: runs only the loss-coverage test and the compatibility page check; with "group" the whole gp-fidelity group
    /// (the same tests the full self-test runs for it) and its count against the group minimum.
    /// </summary>
    internal static int RunGpLossCoverage(string reportPath, bool wholeGroup = false)
    {
        Log.Clear(); _pass = 0; _fail = 0; _skip = 0; GroupStates.Clear();
        _required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Section("Loss coverage");
        if (wholeGroup)
        {
            _required = NormalizeRequirements(new[] { "gp-fidelity" }, out var unknown);
            GuardGroup("gp-fidelity", TestGpFidelity);
            GuardGroup("gp-fidelity", TestGpMixerExact);
            GuardGroup("gp-fidelity", TestGpTrillSpeed);
            GuardGroup("gp-fidelity", TestGpLossCoverage);
            GuardGroup("gp-fidelity", TestGpCompatibilityDoc);
            ReportRequirements(_required, unknown);
        }
        else
        {
            Guard(TestGpLossCoverage);
            Guard(TestGpCompatibilityDoc);
        }
        Log.Add(""); Log.Add($"loss coverage: {_pass} passed, {_fail} failed, {_skip} skipped");
        try { File.WriteAllText(reportPath, string.Join(Environment.NewLine, Log)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 2; }
        return _fail == 0 ? 0 : 1;
    }
}
