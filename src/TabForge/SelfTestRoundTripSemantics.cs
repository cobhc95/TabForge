using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using TabForge.Diagnostics;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;

namespace TabForge;

// Cross-feature semantic round trips (external review item 7). Import -> edit -> save -> reopen and export -> independent reader,
// comparing musical MEANING (per-note pitch/string/fret/onset/duration/techniques/dynamics, per-bar signature/tempo/repeats) and not
// parse success or bytes. Only redistributable fixtures: in-code synthetic songs and TabForge's own technique song, never Tabs/.
// Every conversion loss is EXPLICIT: an allow-list per format with the reason; any other difference fails.
// Results and scope: docs/COMPATIBILITY_RESULTS.md.
public static partial class SelfTest
{
    // ------------------------------------------------------------------ facts: a song flattened to comparable key/value pairs

    private sealed record RtDiff(string Category, string Where, string Expected, string Actual);

    /// <summary>One format's expected conversion losses: category (exact, or "prefix*") -> why. Everything not listed must match.</summary>
    private sealed class RtProfile
    {
        public required string Name { get; init; }
        public required Dictionary<string, string> Losses { get; init; }
        /// <summary>Optional narrowing of an allowance: the category is allowed only for differences this accepts (e.g. "the source had no value").</summary>
        public Dictionary<string, Func<RtDiff, bool>> Only { get; init; } = new();
        public string? ReasonFor(string category, RtDiff? d = null)
        {
            string? reason = null; string? matched = null;
            if (Losses.TryGetValue(category, out var exact)) { reason = exact; matched = category; }
            else
                foreach (var (key, r) in Losses)
                    if (key.EndsWith('*') && category.StartsWith(key[..^1], StringComparison.Ordinal)) { reason = r; matched = key; break; }
            if (reason is null || d is null || matched is null) return reason;
            return Only.TryGetValue(matched, out var only) && !only(d) ? null : reason;
        }
        public RtProfile With(params (string Category, string Reason)[] more)
        {
            var losses = new Dictionary<string, string>(Losses);
            var only = new Dictionary<string, Func<RtDiff, bool>>(Only);
            foreach (var (c, r) in more) { losses[c] = r; only.Remove(c); }   // a scenario's own allowance is not narrowed by the base profile's
            return new RtProfile { Name = Name, Losses = losses, Only = only };
        }
    }

    private static string RtF(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static IEnumerable<(TabCell Cell, double Onset)> RtBeats(List<TabCell> cells)
    {
        var cursor = 0.0;
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell.Notes.Count == 0 && !cell.IsRest) continue;
            var start = cell.RhythmicPosition ?? Math.Max(i, cursor);
            cursor = start + MusicTime.CellSlots(cell);
            yield return (cell, start);
        }
    }

    private static string RtBend(List<BendPointModel> points) =>
        string.Join(";", points.Select(p => $"{RtF(p.Offset <= 1.0 ? p.Offset * 60 : p.Offset)}:{RtF(p.Value)}"));

    private static string RtShape(IReadOnlyList<double> values) =>
        values.Count == 0 ? "" : $"{RtF(values[0])}/{RtF(values.Max())}/{RtF(values[^1])}";

    private static bool RtIsDrums(TrackModel t) => t.Kind == TrackKind.Drums || t.MidiChannel == 9;
    private static int RtDrumMidi(TabNote n) => Math.Clamp(n.MidiValue > 0 ? n.MidiValue : n.Fret, 0, 127);

    /// <summary>The score of a song as facts. Rests and annotation-only beats are not facts (silence is checked through note onsets/durations).</summary>
    private static Dictionary<string, string> RtScoreFacts(SongProject p, out int noteCount, out int beatCount)
    {
        var f = new Dictionary<string, string>(StringComparer.Ordinal);
        var notes = 0; var beats = 0;
        f["song|tracks"] = p.Tracks.Count.ToString(CultureInfo.InvariantCulture);
        f["song|title"] = p.Title ?? ""; f["song|artist"] = p.Artist ?? "";
        f["song|markers"] = string.Join(";", p.Markers.OrderBy(m => m.MeasureIndex).Select(m => $"{m.MeasureIndex}:{m.Title}"));
        var bars = p.Tracks.Count == 0 ? 0 : p.Tracks.Max(t => t.Measures.Count);
        f["song|bars"] = bars.ToString(CultureInfo.InvariantCulture);
        var lastKey = p.KeySignature; var lastMinor = p.KeySignatureMinor;
        for (var b = 0; b < bars; b++)
        {
            var m = MusicTime.BarOf(p, b);
            var key = $"bar{b}|";
            f[key + "timeSig"] = $"{m?.TimeSigNum ?? p.TimeSignatureNumerator}/{m?.TimeSigDenom ?? p.TimeSignatureDenominator}";
            f[key + "tempo"] = MusicTime.TempoAt(p, b).ToString(CultureInfo.InvariantCulture);
            f[key + "tempoChange"] = b == 0 ? "" : m?.TempoChange?.ToString(CultureInfo.InvariantCulture) ?? "";
            f[key + "midTempos"] = string.Join(";", (m?.MidBarTempos ?? new List<TempoPoint>()).Select(t => $"{RtF(t.Slot)}:{t.Tempo}{(t.RampSlots > 0 ? "~" + RtF(t.RampSlots) : "")}"));
            f[key + "repeatStart"] = (m?.RepeatStart ?? false) ? "1" : "0";
            f[key + "repeatEnd"] = (m?.RepeatEnd ?? false) ? "1" : "0";
            f[key + "repeatCount"] = m is { RepeatEnd: true } ? Math.Max(2, m.RepeatCount).ToString(CultureInfo.InvariantCulture) : "";
            f[key + "endings"] = m?.EndingLabel ?? "";
            // a bar without a key of its own is in the song's key (BarSignatures.KeyAt, as the score shows it)
            if (m is not null) { lastKey = m.KeySignature ?? p.KeySignature; lastMinor = m.KeySignatureMinor ?? p.KeySignatureMinor; }
            f[key + "key"] = $"{lastKey}{(lastMinor ? "m" : "")}";
            f[key + "doubleBar"] = (m?.IsDoubleBar ?? false) ? "1" : "0";
            f[key + "directions"] = m?.Directions ?? "";
            f[key + "tripletFeel"] = m?.TripletFeelKind ?? "";
        }
        for (var t = 0; t < p.Tracks.Count; t++)
        {
            var track = p.Tracks[t];
            var drums = RtIsDrums(track);
            var tk = $"t{t}|";
            f[tk + "name"] = track.Name; f[tk + "isDrums"] = drums ? "1" : "0";
            f[tk + "tuning"] = drums ? "" : string.Join(",", track.StringTunings);
            f[tk + "capo"] = track.Capo.ToString(CultureInfo.InvariantCulture);
            f[tk + "program"] = track.MidiProgram.ToString(CultureInfo.InvariantCulture);
            f[tk + "channel"] = track.MidiChannel.ToString(CultureInfo.InvariantCulture);
            f[tk + "volume"] = track.Volume.ToString(CultureInfo.InvariantCulture);
            f[tk + "volume.step8"] = ((int)Math.Round(MixerGroups.Volume(p, track) / 8.0)).ToString(CultureInfo.InvariantCulture);   // group level applied: the .gp bakes it in
            f[tk + "pan"] = track.Pan.ToString(CultureInfo.InvariantCulture);
            f[tk + "pan.step8"] = ((int)Math.Round(MixerGroups.Pan(p, track) / 8.0)).ToString(CultureInfo.InvariantCulture);
            f[tk + "mute"] = track.Mute ? "1" : "0"; f[tk + "solo"] = track.Solo ? "1" : "0";
            f[tk + "bars"] = track.Measures.Count.ToString(CultureInfo.InvariantCulture);
            var previousDynamic = -1;
            for (var b = 0; b < track.Measures.Count; b++)
            {
                var measure = track.Measures[b];
                var voices = new List<List<TabCell>> { measure.Cells };
                if (measure.Voice2Cells.Any(c => c.Notes.Count > 0)) voices.Add(measure.Voice2Cells);
                for (var v = 0; v < voices.Count; v++)
                    foreach (var (cell, onset) in RtBeats(voices[v]))
                    {
                        if (cell.Notes.Count == 0) continue;
                        beats++;
                        var bk = $"t{t}/b{b}/v{v}/@{RtF(onset)}|";
                        var principal = cell.Notes.Where(n => !n.IsGraceNote).ToList();
                        f[bk + "exists"] = "1";
                        f[bk + "durSlots"] = RtF(MusicTime.CellSlots(cell));
                        f[bk + "dynamic"] = Dynamics.Names[Dynamics.NearestIndex(cell.Notes[0].Velocity)];   // GP keeps a dynamic per beat: the first note's
                        f[bk + "spelling"] = $"{cell.DurationDenominator}.{cell.Dots}";
                        f[bk + "tuplet"] = $"{cell.Tuplet.Numerator}:{cell.Tuplet.Denominator}";
                        f[bk + "accent"] = cell.Accent.ToString(CultureInfo.InvariantCulture);
                        f[bk + "staccato"] = cell.Staccato ? "1" : "0"; f[bk + "tenuto"] = cell.Tenuto ? "1" : "0"; f[bk + "fermata"] = cell.Fermata ? "1" : "0";
                        f[bk + "whammy"] = RtBend(cell.WhammyPoints);
                        f[bk + "whammy.shape"] = RtShape(cell.WhammyPoints.OrderBy(w => w.Offset).Select(w => w.Value).ToList());
                        f[bk + "tremoloPick"] = cell.TremoloPickDenominator.ToString(CultureInfo.InvariantCulture);
                        f[bk + "ottava"] = cell.OctaveShiftSemitones.ToString(CultureInfo.InvariantCulture);
                        f[bk + "lyrics"] = cell.Lyrics ?? ""; f[bk + "chord"] = cell.ChordName ?? ""; f[bk + "text"] = cell.Text ?? "";
                        f[bk + "mix"] = cell.Mix is null ? "" : $"{cell.Mix.Program}/{cell.Mix.Volume}/{cell.Mix.Pan}/{cell.Mix.Tempo}/{cell.Mix.TransitionBeats}/{cell.Mix.AllTracks}";
                        if (v == 0 && principal.Count > 0)
                        {
                            var dyn = Dynamics.NearestIndex(principal[0].Velocity);
                            f[bk + "dynamicChange"] = dyn != previousDynamic ? Dynamics.Names[dyn] : "";
                            previousDynamic = dyn;
                        }
                        var used = new Dictionary<string, int>();
                        foreach (var n in cell.Notes.OrderBy(n => n.IsGraceNote ? 0 : 1).ThenBy(n => drums ? RtDrumMidi(n) : n.StringIndex))
                        {
                            var id = $"{(n.IsGraceNote ? "g" : "n")}{(drums ? "d" + RtDrumMidi(n) : "s" + n.StringIndex)}";
                            var ord = used.GetValueOrDefault(id); used[id] = ord + 1;
                            var nk = bk + id + "#" + ord + "|";
                            notes++;
                            f[nk + "exists"] = "1";
                            if (!drums) f[nk + "fret"] = n.Fret.ToString(CultureInfo.InvariantCulture);
                            f[nk + "midi"] = (drums ? RtDrumMidi(n) : n.MidiValue).ToString(CultureInfo.InvariantCulture);
                            f[nk + "velocity"] = n.Velocity.ToString(CultureInfo.InvariantCulture);
                            f[nk + "dynamic"] = Dynamics.Names[Dynamics.NearestIndex(n.Velocity)];
                            f[nk + "ghost"] = n.Ghost ? "1" : "0"; f[nk + "dead"] = n.Dead ? "1" : "0";
                            f[nk + "tied"] = (n.Tied || cell.IsTied) ? "1" : "0";
                            f[nk + "graceBefore"] = n.IsGraceNote ? (n.GraceBeforeBeat ? "1" : "0") : "";
                            f[nk + "graceSlots"] = n.IsGraceNote ? RtF(n.GraceDurationSlots) : "";
                            f[nk + "bend"] = RtBend(n.BendPoints);
                            f[nk + "bend.shape"] = RtShape(n.BendPoints.OrderBy(w => w.Offset).Select(w => w.Value).ToList());
                            f[nk + "harmonicFret"] = n.HarmonicFret is { } hf ? RtF(hf) : "";
                            f[nk + "slideTarget"] = n.SlideTargetMidi.ToString(CultureInfo.InvariantCulture);
                            f[nk + "trillTarget"] = n.TrillTargetMidi.ToString(CultureInfo.InvariantCulture);
                            f[nk + "trillDur"] = n.TrillDurationDenominator.ToString(CultureInfo.InvariantCulture);
                            f[nk + "lhFinger"] = n.LeftHandFinger?.ToString(CultureInfo.InvariantCulture) ?? "";
                            f[nk + "rhFinger"] = n.RightHandFinger?.ToString(CultureInfo.InvariantCulture) ?? "";
                            foreach (var tech in n.Techniques) f[nk + "technique:" + (tech == "Slide" ? "LegatoSlide" : tech)] = "1";
                        }
                    }
            }
        }
        noteCount = notes; beatCount = beats;
        return f;
    }

    /// <summary>The audio/routing side of a song. Track links are resolved to track indexes, so a re-issued track id is not a difference.</summary>
    private static Dictionary<string, string> RtAudioFacts(SongProject p)
    {
        var f = new Dictionary<string, string>(StringComparer.Ordinal);
        string Link(string? id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var i = p.Tracks.FindIndex(t => t.Id.ToString("N").Equals(id, StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? "track#" + i : "unresolved";
        }
        void Rig(string key, RigPreset? rig)
        {
            if (rig is null) { f[key + "rig"] = "null"; return; }
            f[key + "rig.name"] = rig.Name; f[key + "rig.articulationMap"] = rig.ArticulationMap; f[key + "rig.autoPitch"] = rig.AutoPitchMatch?.ToString() ?? "";
            f[key + "rig.count"] = rig.Plugins.Count.ToString(CultureInfo.InvariantCulture);
            for (var j = 0; j < rig.Plugins.Count; j++)
            {
                var s = rig.Plugins[j]; var k = $"{key}plug{j}.";
                f[k + "id"] = s.Id; f[k + "name"] = s.Name; f[k + "path"] = s.Path; f[k + "type"] = s.Type.ToString(); f[k + "enabled"] = s.Enabled.ToString();
                f[k + "format"] = s.Format; f[k + "role"] = s.RoleMode; f[k + "vendor"] = s.Vendor; f[k + "pins"] = s.Pins;
                f[k + "midiIn"] = $"{s.MidiIn.Source}/{s.MidiIn.Channel}/{(s.MidiIn.Source == PluginMidiIn.OtherTrack ? Link(s.MidiIn.TrackId) : "")}";
                f[k + "processors"] = string.Join(";", s.MidiProcessors.Select(m => $"{m.Type}|{m.Enabled}|{m.Params}"));
                f[k + "passThrough"] = s.PassMidiThrough.ToString(); f[k + "midiOutToNext"] = s.MidiOutToNext.ToString(); f[k + "instrumentAudio"] = s.InstrumentAudio;
                f[k + "sidechain"] = Link(s.SidechainTrackId); f[k + "midiForward"] = Link(s.MidiOutTrackId);
                f[k + "outputDb"] = RtF(s.OutputDb); f[k + "wet"] = s.Wet.ToString(CultureInfo.InvariantCulture);
                f[k + "state"] = s.State ?? "(none)";
                f[k + "bindings"] = string.Join(";", s.ArticulationBindings.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Value}"));
                f[k + "autoPitchOffset"] = s.AutoPitchOffset?.ToString(CultureInfo.InvariantCulture) ?? "";
            }
        }
        var mixer = p.Mixer;
        f["mixer|grouping"] = mixer.Grouping; f["mixer|masterPan"] = mixer.MasterPan.ToString(CultureInfo.InvariantCulture);
        f["mixer|showGroups"] = mixer.ShowGroupsInTrackList.ToString(); f["mixer|collapsed"] = string.Join(",", mixer.CollapsedGroups.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var (g, l) in mixer.Groups.OrderBy(x => x.Key, StringComparer.Ordinal)) f[$"mixer|group:{g}"] = $"{l.Volume}/{l.Pan}/{l.Pitch}/{l.Mute}/{l.Solo}";
        foreach (var (g, bus) in mixer.Buses.OrderBy(x => x.Key, StringComparer.Ordinal)) { f[$"mixer|bus:{g}.on"] = bus.On.ToString(); Rig($"mixer|bus:{g}.", bus.Rig); }
        f["mixer|master.on"] = mixer.Master.On.ToString(); Rig("mixer|master.", mixer.Master.Rig);
        for (var i = 0; i < p.Tracks.Count; i++)
        {
            var t = p.Tracks[i]; var k = $"t{i}|";
            f[k + "soundSource"] = t.SoundSource; f[k + "midiSound"] = t.MidiSound.ToString(); f[k + "mixerGroup"] = t.MixerGroup ?? "";
            f[k + "midiDevice"] = t.MidiOutputDeviceId.ToString(CultureInfo.InvariantCulture); f[k + "audioInput"] = t.AudioInput;
            f[k + "recordArm"] = t.RecordArm.ToString(); f[k + "monitor"] = t.MonitorInput.ToString(); f[k + "tint"] = t.TintRow.ToString();
            f[k + "reverb"] = t.Reverb.ToString(CultureInfo.InvariantCulture); f[k + "chorus"] = t.Chorus.ToString(CultureInfo.InvariantCulture);
            f[k + "transpose"] = t.Transpose.ToString(CultureInfo.InvariantCulture);
            f[k + "performer"] = t.Performer; f[k + "trackNotes"] = t.TrackNotes; f[k + "instrumentName"] = t.InstrumentName; f[k + "drumMap"] = t.DrumMapPreset;
            f[k + "color"] = t.ColorHex;
            Rig(k, t.Rig);
            f[k + "lanes"] = string.Join(",", t.Lanes.Select(l => l.Plays ? "1" : "0"));
            for (var c = 0; c < t.AudioClips.Count; c++)
            {
                var clip = t.AudioClips[c];
                f[$"{k}clip{c}"] = $"{clip.Id}|{clip.File}|{clip.Name}|{RtF(clip.StartSec)}|{RtF(clip.OffsetSec)}|{RtF(clip.SourceLengthSec)}|{RtF(clip.FileLengthSec)}|{RtF(clip.GainDb)}|{RtF(clip.Pitch)}|{RtF(clip.Speed)}|{clip.Muted}|{clip.Lane}|" +
                    (clip.Notes is null ? "audio" : string.Join(";", clip.Notes.Select(n => $"{RtF(n.StartSec)},{RtF(n.LengthSec)},{n.Pitch},{n.Velocity}")));
            }
            f[k + "clips"] = t.AudioClips.Count.ToString(CultureInfo.InvariantCulture);
        }
        return f;
    }

    private static string RtCategory(string key)
    {
        var bar = key.IndexOf('|');
        var name = key[(bar + 1)..];
        var head = key[..bar];
        if (head.StartsWith("song", StringComparison.Ordinal)) return "song." + name;
        if (head.StartsWith("bar", StringComparison.Ordinal)) return "bar." + name;
        if (head.StartsWith("mixer", StringComparison.Ordinal)) return "mixer." + Norm(name);
        if (head.Contains('/'))
        {
            // t0/b1/v0/@4|exists = beat fact; t0/b1/v0/@4|n s2#0|midi = note fact.
            var second = name.IndexOf('|');
            return second < 0 ? "beat." + name : "note." + name[(second + 1)..];
        }
        return "track." + Norm(name);

        static string Norm(string s)
        {
            // audio keys carry an index (plug0., clip3) that must not become part of the category
            var sb = new StringBuilder();
            foreach (var part in s.Split('.'))
            {
                var trimmed = part;
                if (trimmed.StartsWith("plug", StringComparison.Ordinal) && trimmed.Length > 4 && char.IsDigit(trimmed[4])) trimmed = "plug";
                else if (trimmed.StartsWith("clip", StringComparison.Ordinal) && trimmed.Length > 4 && char.IsDigit(trimmed[4])) trimmed = "clip";
                else if (trimmed.StartsWith("group:", StringComparison.Ordinal)) trimmed = "group";
                else if (trimmed.StartsWith("bus:", StringComparison.Ordinal)) trimmed = "bus";
                if (sb.Length > 0) sb.Append('.');
                sb.Append(trimmed);
            }
            return sb.ToString();
        }
    }

    private static List<RtDiff> RtCompare(Dictionary<string, string> expected, Dictionary<string, string> actual, bool ignoreExtraActual = false)
    {
        var diffs = new List<RtDiff>();
        // A field of a note/beat that is itself absent on one side is reported once, as the note/beat missing (or extra), not once per field.
        static string Parent(string key) { var i = key.LastIndexOf('|'); return key[..(i + 1)] + "exists"; }
        foreach (var (key, value) in expected)
        {
            if (!actual.TryGetValue(key, out var got))
            {
                if (key.Contains('/') && !key.EndsWith("|exists", StringComparison.Ordinal) && expected.ContainsKey(Parent(key)) && !actual.ContainsKey(Parent(key))) continue;
                diffs.Add(new RtDiff(RtCategory(key) + ".missing", key, value, "(absent)"));
                continue;
            }
            if (got != value) diffs.Add(new RtDiff(RtCategory(key), key, value, got));
        }
        if (!ignoreExtraActual)
            foreach (var (key, value) in actual)
                if (!expected.ContainsKey(key))
                {
                    if (key.Contains('/') && !key.EndsWith("|exists", StringComparison.Ordinal) && actual.ContainsKey(Parent(key)) && !expected.ContainsKey(Parent(key))) continue;
                    diffs.Add(new RtDiff(RtCategory(key) + ".extra", key, "(absent)", value));
                }
        return diffs;
    }

    // ------------------------------------------------------------------ loss accounting

    private static readonly Dictionary<string, Dictionary<string, (int Count, string Reason, string Sample)>> RtLossLog = new();
    private static readonly Dictionary<string, HashSet<string>> RtUsedAllowances = new();
    private static readonly List<(string Scenario, string Format, int Tracks, int Bars, int Beats, int Notes, int Diffs, int Expected)> RtCoverage = new();
    private static readonly HashSet<string> RtProfiles = new();

    private static void RtVerify(string scenario, RtProfile profile, SongProject expected, SongProject actual, bool audio = false, bool scoreToo = true)
    {
        RtProfiles.Add(profile.Name);
        if (profile.Name == RtGpCleanProfile.Name) expected = RtBakedTranspose(expected);   // a .gp has no playback transposition: it is written into string + fret
        var ef = new Dictionary<string, string>(); var af = new Dictionary<string, string>();
        var noteCount = 0; var beatCount = 0; var actualNotes = 0; var actualBeats = 0;
        if (scoreToo) { ef = RtScoreFacts(expected, out noteCount, out beatCount); af = RtScoreFacts(actual, out actualNotes, out actualBeats); }
        if (audio) foreach (var (k, v) in RtAudioFacts(expected)) ef[k] = v;
        if (audio) foreach (var (k, v) in RtAudioFacts(actual)) af[k] = v;
        var all = RtCompare(ef, af);
        var unexpected = new List<RtDiff>();
        var log = RtLossLog.TryGetValue(profile.Name, out var l) ? l : RtLossLog[profile.Name] = new();
        var used = RtUsedAllowances.TryGetValue(profile.Name, out var u) ? u : RtUsedAllowances[profile.Name] = new();
        var expectedLoss = 0;
        foreach (var d in all)
        {
            var reason = profile.ReasonFor(d.Category, d);
            if (reason is null) { unexpected.Add(d); continue; }
            expectedLoss++;
            foreach (var key in profile.Losses.Keys) if (key == d.Category || key.EndsWith('*') && d.Category.StartsWith(key[..^1], StringComparison.Ordinal)) used.Add(key);
            var prev = log.GetValueOrDefault(d.Category);
            log[d.Category] = (prev.Count + 1, reason, prev.Sample ?? $"{scenario}: {d.Where} expected '{d.Expected}' got '{d.Actual}'");
        }
        RtCoverage.Add((scenario, profile.Name, expected.Tracks.Count, expected.Tracks.Count == 0 ? 0 : expected.Tracks.Max(t => t.Measures.Count), beatCount, noteCount, unexpected.Count, expectedLoss));
        var summary = string.Join(" | ", unexpected.GroupBy(d => d.Category).OrderByDescending(g => g.Count()).Take(6)
            .Select(g => $"{g.Key} x{g.Count()} e.g. {g.First().Where} expected '{Trim(g.First().Expected)}' got '{Trim(g.First().Actual)}'"));
        Check($"round trip [{profile.Name}] {scenario}: no unexpected semantic difference ({noteCount} notes, {beatCount} beats, {expectedLoss} allow-listed)", unexpected.Count == 0, summary);
        foreach (var g in unexpected.GroupBy(d => d.Category).OrderByDescending(g => g.Count()))
            Log.Add($"  diff  [{profile.Name}] {scenario}: {g.Key} x{g.Count()} e.g. {g.First().Where} expected '{g.First().Expected}' got '{g.First().Actual}'");
        if (noteCount != actualNotes && !all.Any(d => d.Category.StartsWith("note.") && d.Category.EndsWith(".missing") || d.Category.EndsWith(".extra")))
            Check($"round trip [{profile.Name}] {scenario}: note count", noteCount == actualNotes);
        static string Trim(string s) => s.Length > 80 ? s[..80] + "..." : s;
    }

    // ------------------------------------------------------------------ format profiles: the allow-lists

    private static readonly RtProfile RtTforge = new() { Name = ".tforge (gzip)", Losses = new() };

    private static readonly RtProfile RtGpEmbedded = new()
    {
        Name = ".gp with embedded project", Losses = new()
        {
            ["song.title"] = "not applicable: the embedded project is the full .tforge JSON; empty allow-list except the fields below",
        }
    };

    private static bool RtSourceHadNone(RtDiff d) => d.Expected is "0" or "" or "(absent)";

    private static readonly RtProfile RtGpCleanProfile = new()
    {
        Name = "clean .gp",
        Losses = new()
        {
            ["note.velocity"] = "GP stores loudness as 8 dynamics steps (ppp..fff); velocity is quantised to the shared table. beat.dynamic is compared strictly instead",
            ["note.dynamic"] = "GP keeps ONE dynamic per beat: the other notes of a chord or drum beat take the first note's dynamic (a drum accent on the kick under a hi-hat is flattened). beat.dynamic is compared strictly",
            ["track.volume"] = "GP track volume is 0..16; TabForge 0..127 is quantised (compared as volume.step8)",
            ["track.pan"] = "GP balance is 0..16; TabForge pan 0..127 is quantised (compared as pan.step8)",
            ["note.bend"] = "GP7 keeps origin/middle/destination only: multi-point bend curves are simplified (bend.shape first/peak/last is compared strictly)",
            ["beat.whammy"] = "GP7 keeps at most 4 whammy points: longer curves are simplified (whammy.shape is compared strictly)",
            ["note.lhFinger"] = "left-hand fingering is not written to .gp", ["note.rhFinger"] = "right-hand fingering is not written to .gp",
            ["beat.tenuto"] = "alphaTab's model has no tenuto flag, so it cannot be written to .gp",
            ["note.technique:Tenuto"] = "tenuto has no .gp representation",
            ["beat.mix"] = "GP5 mix-table changes are TabForge-model data; the GP7 export writes tempo automations only",
            ["note.technique:PalmMute.extra"] = "GP stores palm mute per beat: one muted chord tone mutes every note of the chord on reopen",
            ["beat.fermata"] = "GP keeps a fermata on the bar position (master bar), so a fermata on one track appears on the other tracks' beat at that position; it is never lost, only added",
            ["track.channel"] = "the importer assigns MIDI channels in track order (percussion on 9); the .gp channel is not read back (drums stay on 9, checked through track.isDrums)",
            ["note.slideTarget"] = "derived data: the importer computes the slide's target pitch from the next note; a value the source did not set is filled in",
            ["note.trillTarget"] = "derived data: the importer fills the trill's target pitch when the source only tagged the trill",
            ["note.trillDur"] = "derived data: the importer fills the trill speed (1/16) when the source did not set it",
            ["beat.tremoloPick"] = "derived data: the importer fills the tremolo-picking speed (1/8 default) when the source only tagged it; GP7 has three speeds (1/8, 1/16, 1/32), so 1/64 is written as 1/32",
            ["note.technique:FadeIn.extra"] = "GP stores a volume swell per beat: one faded note fades every note of the beat on reopen",
            ["note.technique:FadeOut.extra"] = "GP stores a fade-out per beat: one faded note fades every note of the beat on reopen",
            ["bar.directions"] = "the clean .gp stores directions under Guitar Pro's own names (Segno -> TargetSegno, ToCoda -> JumpDaCoda); the same marks, allowed only when the names are exactly the GP spelling",
            ["bar.tempoChange"] = "a tempo marking that restates the tempo already running is not kept as a change on import (bar.tempo, the tempo actually played, is compared strictly)",
            ["note.technique:TremBar*"] = "alphaTab re-derives the whammy sub-type (Dip, Dive, ...) from the curve; the tag may change name (whammy.shape is compared strictly)",
            ["note.technique:Harmonic.extra"] = "the importer also tags every harmonic kind (artificial, pinch, tap, semi, feedback) with the generic Harmonic name",
            ["note.technique:Dead.extra"] = "the importer mirrors the dead-note flag as a Dead technique name (the flag itself is compared strictly)",
            ["note.technique:Ghost.extra"] = "the importer mirrors the ghost-note flag as a Ghost technique name (the flag itself is compared strictly)",
            ["note.technique:HOPOOrigin.extra"] = "the importer splits a hammer-on/pull-off tag into origin and destination tags",
            ["note.technique:HOPODestination.extra"] = "the importer splits a hammer-on/pull-off tag into origin and destination tags",
            ["note.technique:Legato.missing"] = "generic 'Legato' tag has no .gp representation (hammer-on/pull-off and legato slide do)",
            ["note.technique:Rasgueado.missing"] = "rasgueado has no .gp7 writer support in alphaTab",
            ["note.technique:PickSlideUp.missing"] = "pick slides have no .gp7 writer support in alphaTab",
            ["note.technique:PickSlideDown.missing"] = "pick slides have no .gp7 writer support in alphaTab",
            ["note.technique:LeftTap.missing"] = "the exporter writes it (IsLeftHandTapped) but the importer does not read it back as a tag",
            ["note.graceSlots"] = "a grace beat written as 1 slot (16th) is read back as 2 slots (an 8th): alphaTab's GP7 round trip normalises the grace length (playback does not use it)",
            ["note.technique:GraceBefore.extra"] = "the importer mirrors the grace-note flag as a GraceBefore technique name (the flag itself is compared strictly)",
            ["note.technique:GraceOnBeat.extra"] = "the importer mirrors the on-beat grace flag as a GraceOnBeat technique name (the flag itself is compared strictly)",
            ["note.technique:BrushDown.extra"] = "the importer tags an arpeggio stroke also as a brush stroke (the arpeggio tag itself is compared strictly)",
            ["note.technique:BrushUp.extra"] = "the importer tags an arpeggio stroke also as a brush stroke (the arpeggio tag itself is compared strictly)",
            ["note.technique:HOPO.extra"] = "a hammer-on origin ending a bar marks the next bar's note as its destination (HOPO tag) on import; the source tagged only the origin",
            ["note.technique:GraceBend.missing"] = "a bend grace note has no separate model representation",
        },
        Only = new()
        {
            ["beat.fermata"] = d => d.Expected == "0", ["track.channel"] = d => d.Expected != "9" && d.Actual != "9",
            ["note.slideTarget"] = RtSourceHadNone, ["note.trillTarget"] = RtSourceHadNone, ["note.trillDur"] = RtSourceHadNone,
            ["beat.tremoloPick"] = d => RtSourceHadNone(d) || d.Expected == "64" && d.Actual == "32",
            ["bar.directions"] = d => string.Join(",", GuitarProExporter.GpDirections(d.Expected)) == d.Actual,
            ["bar.tempoChange"] = d => d.Actual == "",
        },
    };

    private static RtProfile RtGpClean(params (string Category, string Reason)[] more) => RtGpCleanProfile.With(more);

    // ------------------------------------------------------------------ scenario songs

    private static TabNote RtNote(TrackModel t, int s, int fret, int velocity = 95, params string[] tech)
    {
        // MidiValue is the SOUNDING pitch (the importer adds the capo); the editor's MidiOf does not (see the KNOWN capo line in the suite)
        var n = new TabNote { StringIndex = s, Fret = fret, MidiValue = t.PitchOf(s, fret), Velocity = velocity };
        foreach (var x in tech) if (x.Length > 0) n.Techniques.Add(x);
        return n;
    }

    private static TabCell RtPut(TrackModel t, int bar, double slot, int den, int dots, params TabNote[] notes)
    {
        var m = t.Measures[bar];
        var idx = (int)slot;
        while (idx < m.Cells.Count - 1 && (m.Cells[idx].Notes.Count > 0 || m.Cells[idx].IsRest)) idx++;
        var c = m.Cells[idx];
        c.DurationDenominator = den; c.Dots = dots;
        if (Math.Abs(slot - Math.Round(slot)) > 0.01 || idx != (int)slot) c.RhythmicPosition = slot;
        if (notes.Length == 0) c.IsRest = true; else c.Notes.AddRange(notes);
        return c;
    }

    /// <summary>The scenario song with every feature the review named that Guitar Pro can carry, in plain code.</summary>
    private static SongProject RtDenseSong()
    {
        var song = new SongProject { Title = "RT dense", Artist = "TabForge self-test", Tempo = 100, KeySignature = 1 };
        const int bars = 12;
        var lead = new TrackModel { Name = "Lead", Kind = TrackKind.Guitar, MidiProgram = 29, MidiChannel = 0, Capo = 2, Volume = 96, Pan = 40, Measures = TemplateFactory.Measures(bars) };
        var dadgad = new TrackModel { Name = "DADGAD", Kind = TrackKind.Guitar, MidiProgram = 25, MidiChannel = 1, StringTunings = new() { 62, 57, 55, 50, 45, 38 }, Measures = TemplateFactory.Measures(bars) };
        var bass5 = new TrackModel { Name = "Bass 5", Kind = TrackKind.Bass, MidiProgram = 33, MidiChannel = 2, StringTunings = new() { 43, 38, 33, 28, 23 }, Measures = TemplateFactory.Measures(bars) };
        var seven = new TrackModel { Name = "Seven", Kind = TrackKind.Guitar, MidiProgram = 30, MidiChannel = 3, StringTunings = new() { 64, 59, 55, 50, 45, 40, 35 }, Measures = TemplateFactory.Measures(bars) };
        var drums = new TrackModel { Name = "Kit", Kind = TrackKind.Drums, MidiProgram = 0, MidiChannel = 9, InstrumentName = "Drum Kit (Standard)", StringTunings = new() { 49, 42, 48, 38, 43, 36 }, Measures = TemplateFactory.Measures(bars) };
        song.Tracks.AddRange(new[] { lead, dadgad, bass5, seven, drums });

        // structure (every track): 4/4, 3/4, 6/8, repeats with alternate endings (1.2. / 3.), tempo changes incl. a mid-bar one, key change
        foreach (var t in song.Tracks)
        {
            var ms = t.Measures;
            ms[1].TimeSigNum = 3; ms[1].TimeSigDenom = 4;
            ms[2].TimeSigNum = 6; ms[2].TimeSigDenom = 8;
            ms[3].TimeSigNum = 4; ms[3].TimeSigDenom = 4;
            ms[4].RepeatStart = true; ms[4].TempoChange = 132;
            ms[6].RepeatEnd = true; ms[6].RepeatCount = 3; ms[6].AlternateEndingMask = 0b011;
            ms[7].AlternateEnding = 3;
            ms[8].TempoChange = 90; ms[8].KeySignature = -2; ms[8].KeySignatureMinor = true;
            ms[9].MidBarTempos = new List<TempoPoint> { new(8, 140) };
            ms[10].IsDoubleBar = true;
        }
        song.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        song.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "Verse" });
        song.Markers.Add(new MarkerModel { MeasureIndex = 8, Title = "Bridge" });

        // Lead: dynamics ladder, bends, tie across a bar, grace note, harmonics, slides, tuplets, voice 2
        for (var k = 0; k < 4; k++) RtPut(lead, 0, k * 4, 4, 0, RtNote(lead, 1, 3 + k, Dynamics.Velocities[k * 2]));
        RtPut(lead, 1, 0, 4, 0, RtNote(lead, 2, 5, Dynamics.Velocities[7], "Bend")).Notes[0].BendPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 15, Value = 4 }, new() { Offset = 60, Value = 4 } };
        RtPut(lead, 1, 4, 4, 0, RtNote(lead, 2, 7, 95, "Bend")).Notes[0].BendPoints = new() { new() { Offset = 0, Value = 4 }, new() { Offset = 60, Value = 4 } };
        RtPut(lead, 1, 8, 4, 0, RtNote(lead, 2, 8, 95, "Bend")).Notes[0].BendPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 20, Value = 4 }, new() { Offset = 40, Value = 4 }, new() { Offset = 60, Value = 0 } };
        for (var k = 0; k < 6; k++) RtPut(lead, 2, k * 2, 8, 0, RtNote(lead, 3, 2 + k % 3));
        RtPut(lead, 3, 0, 2, 0, RtNote(lead, 0, 7));
        RtPut(lead, 3, 8, 4, 0, RtNote(lead, 0, 7)).Notes[0].Tied = true;                       // tie destination of the half note at slot 0
        RtPut(lead, 3, 12, 4, 0, RtNote(lead, 1, 5, 95, "PalmMute"));
        RtPut(lead, 4, 0, 4, 0, RtNote(lead, 4, 3)).Notes[0].Velocity = 60;
        var graceCell = RtPut(lead, 4, 4, 4, 0, RtNote(lead, 4, 5));
        graceCell.Notes.Insert(0, new TabNote { StringIndex = 4, Fret = 3, MidiValue = lead.StringTunings[4] + 3 + lead.Capo, IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = 1, Velocity = 95 });
        RtPut(lead, 4, 8, 4, 0, new TabNote { StringIndex = 1, Fret = 12, MidiValue = GuitarProImporter.HarmonicMidi("Natural", lead.StringTunings[1] + lead.Capo, 12, 12), HarmonicFret = 12, Techniques = { "Harmonic" } });
        RtPut(lead, 4, 12, 4, 0, new TabNote { StringIndex = 1, Fret = 5, MidiValue = GuitarProImporter.HarmonicMidi("Artificial", lead.StringTunings[1] + lead.Capo, 5, 17), HarmonicFret = 17, Techniques = { "ArtificialHarmonic" } });
        RtPut(lead, 5, 0, 4, 0, RtNote(lead, 3, 5, 95, "LegatoSlide")).Notes[0].SlideTargetMidi = lead.StringTunings[3] + 9 + lead.Capo;
        RtPut(lead, 5, 4, 4, 0, RtNote(lead, 3, 9));
        RtPut(lead, 5, 8, 4, 0, RtNote(lead, 3, 7, 95, "ShiftSlide")).Notes[0].SlideTargetMidi = lead.StringTunings[3] + 10 + lead.Capo;
        RtPut(lead, 5, 12, 4, 0, RtNote(lead, 3, 10, 95, "SlideOutDown"));
        for (var k = 0; k < 3; k++) { var c = RtPut(lead, 6, k * 4 / 3.0, 8, 0, RtNote(lead, 2, 5 + k)); c.IsTriplet = true; }
        for (var k = 0; k < 5; k++) { var c = RtPut(lead, 6, 4 + k * 0.8, 16, 0, RtNote(lead, 1, 3 + k)); c.TupletNumerator = 5; c.TupletDenominator = 4; }
        RtPut(lead, 6, 8, 4, 1, RtNote(lead, 0, 3));
        RtPut(lead, 6, 14, 8, 0, RtNote(lead, 0, 5));
        for (var k = 0; k < 4; k++) RtPut(lead, 7, k * 4, 4, 0, RtNote(lead, 2, 4 + k, 95, k == 0 ? "Vibrato" : k == 1 ? "HOPO" : k == 2 ? "LetRing" : "Bend")).Notes[0].BendPoints = k == 3 ? new() { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = 8 } } : new();
        var accent = RtPut(lead, 8, 0, 4, 0, RtNote(lead, 1, 5)); accent.Accent = 1;
        var heavy = RtPut(lead, 8, 4, 4, 0, RtNote(lead, 1, 5)); heavy.Accent = 2;
        var stac = RtPut(lead, 8, 8, 4, 0, RtNote(lead, 1, 5)); stac.Staccato = true;
        var ferm = RtPut(lead, 8, 12, 4, 0, RtNote(lead, 1, 5)); ferm.Fermata = true;
        RtPut(lead, 9, 0, 2, 0, RtNote(lead, 0, 12, 95, "Vibrato"));
        RtPut(lead, 9, 8, 2, 0, RtNote(lead, 0, 10, 95, "WideVibrato"));
        // voice 2 of bar 10 (independent grid)
        for (var k = 0; k < 4; k++) RtPut(lead, 10, k * 4, 4, 0, RtNote(lead, 1, k));
        lead.Measures[10].Voice2Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList();
        for (var k = 0; k < 2; k++) { var c = lead.Measures[10].Voice2Cells[k * 8]; c.DurationDenominator = 2; c.Notes.Add(RtNote(lead, 4, 2 + k)); }
        RtPut(lead, 11, 0, 1, 0, RtNote(lead, 0, 0, 95, "Trill")).Notes[0].TrillTargetMidi = lead.StringTunings[0] + 2 + lead.Capo;
        GuitarProImporter.LinkTieOrigins(lead);

        // DADGAD (drop tuning): power chords and open strings, a tempo-sensitive 32nd run
        for (var b = 0; b < bars; b++)
        {
            var slots = MusicTime.BarSlots(song, b);
            for (var k = 0; k < slots / 4; k++)
                RtPut(dadgad, b, k * 4, 4, 0, RtNote(dadgad, 5, (b + k) % 5), RtNote(dadgad, 4, (b + k) % 5), RtNote(dadgad, 3, 2 + (b % 3), 70 + 5 * k));
        }
        // Bass 5: low B string and a tapped/slap bar
        for (var b = 0; b < bars; b++)
        {
            var slots = MusicTime.BarSlots(song, b);
            for (var k = 0; k < slots / 2; k++) RtPut(bass5, b, k * 2, 8, 0, RtNote(bass5, 4 - (k % 2), k % 4, 95, b == 5 && k % 2 == 0 ? "Slap" : b == 5 ? "Pop" : ""));
        }
        // Seven-string: chromatic climb on the low B, ghost and dead notes
        for (var b = 0; b < bars; b++)
        {
            var slots = MusicTime.BarSlots(song, b);
            for (var k = 0; k < slots / 4; k++)
            {
                var n = RtNote(seven, 6, (b * 2 + k) % 12); n.Ghost = k == 1; n.Dead = k == 3 && b % 2 == 0;
                RtPut(seven, b, k * 4, 4, 0, n);
            }
        }
        // Drums
        for (var b = 0; b < bars; b++)
        {
            var slots = MusicTime.BarSlots(song, b);
            for (var k = 0; k < slots / 2; k++)
            {
                var hits = new List<TabNote> { DrumHit(42, 90) };
                if (k % 4 == 0) hits.Add(DrumHit(36, 110));
                if (k % 4 == 2) hits.Add(DrumHit(38, 105));
                if (k == 0 && b % 4 == 0) hits.Add(DrumHit(49, 115));
                RtPut(drums, b, k * 2, 8, 0, hits.ToArray());
            }
        }
        song.IsDirty = false;
        return song;

        static TabNote DrumHit(int midi, int velocity) => Drum(midi, velocity);
    }

    /// <summary>
    /// The audit's technique song in the model's own conventions (what the editor and the importer produce): accent/staccato/tenuto/fermata on the
    /// beat, ghost/dead on the note, whammy as a curve, harmonics at their SOUNDING pitch with the touched fret, ties linked. The raw audit song tags
    /// notes with technique NAMES only, which is not how the model stores those (Guitar Pro would import them differently by design).
    /// </summary>
    private static SongProject RtTechniqueSong()
    {
        var song = GmSongAudit.TechniqueSong();
        RtCanonicalizeTechniqueBars(song.Tracks[0], 0, GmSongAudit.TechniqueBars.ToList());
        return song;
    }

    /// <summary>Rewrites one bar per technique name (starting at <paramref name="firstBar"/>) into the model's own conventions; see <see cref="RtTechniqueSong"/>.</summary>
    private static void RtCanonicalizeTechniqueBars(TrackModel track, int firstBar, List<string> names)
    {
        for (var bar = 0; bar < names.Count; bar++)
        {
            var i = firstBar + bar;
            var n = names[bar];
            foreach (var c in track.Measures[i].Cells.Where(c => c.Notes.Count > 0))
            {
                if (n == "Accent") c.Accent = 1; else if (n == "HeavyAccent") c.Accent = 2;
                else if (n == "Staccato") c.Staccato = true; else if (n == "Tenuto") c.Tenuto = true; else if (n == "Fermata") c.Fermata = true;
                foreach (var note in c.Notes)
                {
                    if (n == "Ghost") note.Ghost = true;
                    if (n is "Dead" or "DeadSlapped") note.Dead = true;
                    foreach (var x in new[] { "Accent", "HeavyAccent", "Staccato", "Tenuto", "Fermata", "Ghost", "Dead", "DeadSlapped" }) if (n == x) note.Techniques.Remove(x);
                    var kind = note.Techniques.FirstOrDefault(t => t is "Harmonic" or "ArtificialHarmonic" or "PinchHarmonic" or "TapHarmonic" or "SemiHarmonic" or "FeedbackHarmonic");
                    if (kind is not null)
                    {
                        var open = track.StringTunings[note.StringIndex];
                        var type = kind switch { "Harmonic" => "Natural", "ArtificialHarmonic" => "Artificial", "PinchHarmonic" => "Pinch", "TapHarmonic" => "Tap", "SemiHarmonic" => "Semi", _ => "Feedback" };
                        note.HarmonicFret = 12;
                        note.MidiValue = GuitarProImporter.HarmonicMidi(type, open, note.Fret, 12);
                    }
                }
                if (n.StartsWith("TremBar")) c.WhammyPoints = new List<BendPointModel> { new() { Offset = 0, Value = 0 }, new() { Offset = 30, Value = -8 }, new() { Offset = 60, Value = 0 } };
            }
            if (n == "Tie") { track.Measures[i].Cells[4].Notes[0].Tied = true; track.Measures[i].Cells[4].Notes[0].Techniques.Remove("Tie"); }   // a tie destination is not itself an origin
            // a bare Bend tag carries no curve, and a Grace tag is a real grace note in the model
            if (n == "Bend") foreach (var note in track.Measures[i].Cells.SelectMany(c => c.Notes)) note.Techniques.Remove("Bend");
            if (n is "GraceBefore" or "GraceOnBeat")
                foreach (var c in track.Measures[i].Cells.Where(c => c.Notes.Count > 0))
                {
                    var principal = c.Notes[0];
                    principal.Techniques.Remove(n);
                    c.Notes.Insert(0, new TabNote { StringIndex = principal.StringIndex, Fret = principal.Fret + 2, MidiValue = principal.MidiValue + 2, IsGraceNote = true, GraceBeforeBeat = n == "GraceBefore", GraceDurationSlots = 1, Velocity = principal.Velocity });
                }
        }
        GuitarProImporter.LinkTieOrigins(track);
    }

    private static SongProject RtShowcaseSong()
    {
        var song = BuildShowcaseSong();
        var names = GmSongAudit.TechniqueBars.ToList();
        RtCanonicalizeTechniqueBars(song.Tracks[0], 8, names);
        // the showcase tags some notes with the bare name "Accent": the model keeps an accent on the beat
        foreach (var cell in song.Tracks.SelectMany(t => t.Measures).SelectMany(m => m.Cells).Where(c => c.Notes.Any(n => n.Techniques.Contains("Accent"))))
        {
            cell.Accent = Math.Max(cell.Accent, 1);
            foreach (var note in cell.Notes) note.Techniques.Remove("Accent");
        }
        return song;
    }

    /// <summary>A large project: many tracks and bars of dense eighth-note grooves.</summary>
    private static SongProject RtLargeSong(int tracks, int bars)
    {
        var song = new SongProject { Title = "RT large", Tempo = 128 };
        for (var t = 0; t < tracks; t++)
        {
            var kind = t % 4 == 3 ? TrackKind.Bass : TrackKind.Guitar;
            var track = new TrackModel
            {
                Name = $"Track {t + 1}", Kind = kind, MidiChannel = t < 9 ? t : t + 1, MidiProgram = 27 + t % 6,
                StringTunings = kind == TrackKind.Bass ? new() { 43, 38, 33, 28 } : new() { 64, 59, 55, 50, 45, 40 },
                Measures = TemplateFactory.Measures(bars),
            };
            for (var b = 0; b < bars; b++)
                for (var k = 0; k < 8; k++)
                {
                    var s = (b + k + t) % track.StringTunings.Count;
                    var notes = new List<TabNote> { RtNote(track, s, (b * 3 + k * 5 + t) % 15, Dynamics.Velocities[(b + k) % 8]) };
                    if (k % 4 == 0 && s + 1 < track.StringTunings.Count) notes.Add(RtNote(track, s + 1, (b + t) % 9));
                    RtPut(track, b, k * 2, 8, 0, notes.ToArray());
                }
            song.Tracks.Add(track);
        }
        foreach (var m in song.Tracks.SelectMany(t => t.Measures.Select((m, i) => (m, i))).Where(x => x.i % 40 == 0)) m.m.TempoChange = 100 + m.i % 60;
        song.IsDirty = false;
        return song;
    }

    private static readonly string RtMissingPlugin = Path.Combine(Path.GetTempPath(), "tf-no-such-dir", "MissingSynth.vst3");

    /// <summary>Plug-in state references, buses, sidechains, MIDI forwarding, audio clips, and unavailable devices/plug-ins (model level; nothing is loaded).</summary>
    private static SongProject RtAudioSong()
    {
        var song = RtDenseSongSmall();
        var t = song.Tracks;
        string Id(int i) => t[i].Id.ToString("N");
        var synth = new PluginSlot { Name = "Synth A", Path = RtMissingPlugin, Type = PluginSlotType.Instrument, Format = "VST3", Vendor = "Nobody", State = Convert.ToBase64String(new byte[] { 1, 2, 3, 250, 251, 252 }), OutputDb = -3.5, Pins = PluginPins.Mono, MidiOutTrackId = Id(1), Wet = 80 };
        synth.MidiIn = new PluginMidiIn { Source = PluginMidiIn.None, Channel = 3 };
        synth.MidiProcessors.Add(new PluginMidiProcessor { Type = "transpose", Params = "{\"semitones\":5}" });
        synth.ArticulationBindings["PalmMute"] = "KS 3";
        var comp = new PluginSlot { Name = "Comp", Path = @"C:\Nowhere\Comp.dll", Format = "VST2", SidechainTrackId = Id(2), State = "AAAA", Enabled = false, AutoPitchOffset = 2 };
        t[0].Rig.Plugins.AddRange(new[] { synth, comp });
        t[0].SoundSource = SoundSources.Plugins; t[0].MidiSound = false; t[0].MixerGroup = MixerGroups.Guitars; t[0].MidiOutputDeviceId = 77;
        var second = new PluginSlot { Name = "Amp", Path = @"C:\Nowhere\Amp.vst3", Format = "VST3", MidiIn = new PluginMidiIn { Source = PluginMidiIn.OtherTrack, TrackId = Id(0), Channel = 1 }, State = "QQ==" };
        t[1].Rig.Plugins.Add(second); t[1].SoundSource = SoundSources.Plugins;
        t[1].RecordArm = false; t[1].AudioInput = AudioInputs.Input2; t[1].Reverb = 50; t[1].Chorus = 20; t[1].Transpose = -2; t[1].Performer = "Test player"; t[1].TrackNotes = "note";
        var missingClip = new AudioClip { File = Path.Combine(Path.GetTempPath(), "tf-no-such-dir", "take 1.wav"), Name = "take 1", StartSec = 1.5, OffsetSec = 0.25, SourceLengthSec = 4, FileLengthSec = 8, GainDb = -2.5, Pitch = 1, Speed = 1.25, Lane = 1 };
        t[2].AudioClips.Add(missingClip);
        t[2].AudioClips.Add(new AudioClip { File = "", Name = "midi take", StartSec = 3, SourceLengthSec = 2, FileLengthSec = 2, Notes = new List<ClipNote> { new(0, 0.5, 60, 100), new(0.5, 0.5, 64, 90) } });
        t[2].Lanes.Add(new ClipLane { Plays = false }); t[2].Lanes.Add(new ClipLane { Plays = true });
        song.Mixer.Grouping = MixerGrouping.ByInstrument;
        song.Mixer.Edit(MixerGroups.Guitars).Volume = 120; song.Mixer.Edit(MixerGroups.Guitars).Pan = -10;
        var bus = song.Mixer.Bus(MixerGroups.Guitars);
        bus.Rig.Plugins.Add(new PluginSlot { Name = "Bus comp", Path = @"C:\Nowhere\BusComp.vst3", Format = "VST3", SidechainTrackId = Id(2), State = "BUS=" });
        song.Mixer.Master.Rig.Plugins.Add(new PluginSlot { Name = "Limiter", Path = @"C:\Nowhere\Lim.vst3", Format = "VST3", SidechainTrackId = Id(0), State = "TQ==" });
        song.Mixer.MasterPan = 5;
        song.IsDirty = false;
        return song;
    }

    private static SongProject RtDenseSongSmall()
    {
        var song = new SongProject { Title = "RT audio", Tempo = 110 };
        foreach (var name in new[] { "Alpha", "Beta", "Gamma", "Beta" })   // a duplicate name on purpose
        {
            var t = new TrackModel { Name = name, Measures = TemplateFactory.Measures(2), MidiChannel = song.Tracks.Count };
            RtPut(t, 0, 0, 4, 0, RtNote(t, 1, 3 + song.Tracks.Count)); RtPut(t, 1, 0, 2, 0, RtNote(t, 2, 5));
            song.Tracks.Add(t);
        }
        return song;
    }

    // ------------------------------------------------------------------ format round trips (real code paths)

    private static SongProject RtViaTforge(SongProject s, string folder, string name)
    {
        var path = Path.Combine(folder, name + ".tforge");
        ProjectService.Save(path, s);
        return ProjectService.Load(path);
    }

    /// <summary>When the .gp export throws, isolate the track and bar that alphaTab chokes on (reported in the failure text).</summary>
    private static string RtIsolateExportFailure(SongProject s)
    {
        var found = new List<string>();
        for (var t = 0; t < s.Tracks.Count; t++)
        {
            var solo = RtCopy(s);
            solo.Tracks = new List<TrackModel> { solo.Tracks[t] };
            try { GuitarProExporter.ToBytes(solo, false); continue; } catch (Exception) { }
            var bars = solo.Tracks[0].Measures.Count;
            for (var b = 0; b < bars; b++)
            {
                var one = RtCopy(s);
                var keep = one.Tracks[t];
                one.Tracks = new List<TrackModel> { keep };
                for (var k = 0; k < keep.Measures.Count; k++) if (k != b) { keep.Measures[k].Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList(); keep.Measures[k].Voice2Cells = new(); }
                try { GuitarProExporter.ToBytes(one, false); } catch (Exception) { found.Add($"track {t} '{s.Tracks[t].Name}' bar {b}"); }
            }
            if (found.Count == 0) found.Add($"track {t} '{s.Tracks[t].Name}' (only with other bars)");
        }
        return string.Join(", ", found);
    }

    private static SongProject RtViaGp(SongProject s, string folder, string name, bool embed)
    {
        try { return RtViaGpCore(s, folder, name, embed); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new InvalidOperationException($"{ex.GetType().Name}: {ex.Message}; isolated: {RtIsolateExportFailure(s)}", ex); }
    }

    private static SongProject RtViaGpCore(SongProject s, string folder, string name, bool embed)
    {
        var path = Path.Combine(folder, name + (embed ? "-embed" : "-clean") + ".gp");
        GuitarProExporter.Save(s, path, embedProject: embed);
        return GuitarProImporter.Import(path);
    }

    private static SongProject RtViaGpPair(SongProject s, string folder, string name)
    {
        var path = Path.Combine(folder, name + "-pair.gp");
        var controller = new DocumentController();
        controller.SaveCleanGuitarProWithAudioData(DocumentSession.FromProject(RtCopy(s), null), path, s.Lyrics);
        var opened = controller.Open(path);
        return opened.Project;
    }

    private static SongProject RtCopy(SongProject s) => ProjectService.Restore(ProjectService.Snapshot(s));

    /// <summary>What a clean .gp must hold for a song whose tracks play transposed (Guitar Pro has no playback transposition): the same
    /// sounding pitches, carried by a shifted tuning with the frets unchanged (guitar, bass) or written as string + fret (other tracks).</summary>
    internal static SongProject RtBakedTranspose(SongProject s)
    {
        var copy = RtCopy(s);
        foreach (var t in copy.Tracks.Where(x => x.Kind != TrackKind.Drums && x.MidiChannel != 9))
        {
            var semitones = MixerGroups.Transpose(copy, t);
            if (semitones == 0) continue;
            var tuning = GuitarProExporter.TuningCarriesTranspose(t, semitones);
            var notes = t.Measures.SelectMany(m => m.Cells.Concat(m.Voice2Cells)).SelectMany(c => c.Notes).ToList();
            if (tuning) t.StringTunings = t.StringTunings.Select(v => v + semitones).ToList();
            foreach (var n in notes)
            {
                if (!tuning) (n.StringIndex, n.Fret) = GuitarProExporter.TransposedPosition(t, n.StringIndex, n.Fret, semitones);
                n.MidiValue = t.PitchOf(n.StringIndex, n.Fret);
                if (n.TrillTargetMidi > 0) n.TrillTargetMidi = Math.Clamp(n.TrillTargetMidi + semitones, 0, 127);
            }
        }
        return copy;
    }

    // ------------------------------------------------------------------ independent reader: MusicXML

    private static Dictionary<string, string> RtMusicXmlFacts(byte[] bytes, SongProject model, out int noteCount)
    {
        var f = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = System.Xml.XmlReader.Create(new MemoryStream(bytes), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore });
        var root = XDocument.Load(reader).Root!;
        var notes = 0;
        var parts = root.Elements("part").ToList();
        f["song|tracks"] = parts.Count.ToString(CultureInfo.InvariantCulture);
        var names = root.Element("part-list")!.Elements("score-part").Select(sp => (string?)sp.Element("part-name") ?? "").ToList();
        for (var t = 0; t < parts.Count; t++)
        {
            var part = parts[t];
            f[$"t{t}|name"] = names[t];
            var staves = (int?)part.Descendants("staves").FirstOrDefault() ?? 1;
            var tabStaff = staves >= 2 ? "2" : "1";
            var divisions = 1.0; var barNo = 0;
            var endingNow = "";
            foreach (var measure in part.Elements("measure"))
            {
                var key = $"bar{barNo}|";
                if (t == 0)
                {
                    var d = measure.Descendants("divisions").FirstOrDefault(); if (d is not null) divisions = (double)d;
                }
                else { var d = measure.Descendants("divisions").FirstOrDefault(); if (d is not null) divisions = (double)d; }
                var slotDiv = divisions / 4.0;
                if (t == 0)
                {
                    var time = measure.Element("attributes")?.Element("time");
                    if (time is not null) f[key + "timeSig"] = $"{(string?)time.Element("beats")}/{(string?)time.Element("beat-type")}";
                    var tempo = measure.Elements("sound").Select(s => (string?)s.Attribute("tempo")).FirstOrDefault(x => x is not null)
                                ?? measure.Descendants("sound").Select(s => (string?)s.Attribute("tempo")).FirstOrDefault(x => x is not null);
                    f[key + "tempoMark"] = tempo ?? "";
                    var repeats = measure.Elements("barline").Elements("repeat").ToList();
                    f[key + "repeatStart"] = repeats.Any(r => (string?)r.Attribute("direction") == "forward") ? "1" : "0";
                    var back = repeats.FirstOrDefault(r => (string?)r.Attribute("direction") == "backward");
                    f[key + "repeatEnd"] = back is not null ? "1" : "0";
                    f[key + "repeatCount"] = back is not null ? (string?)back.Attribute("times") ?? "" : "";
                    foreach (var ending in measure.Elements("barline").Elements("ending"))
                        if ((string?)ending.Attribute("type") == "start") endingNow = ((string?)ending.Attribute("number") ?? "").Replace(",", ".") + ".";
                    f[key + "endings"] = endingNow;
                    if (measure.Elements("barline").Elements("ending").Any(e => (string?)e.Attribute("type") == "stop")) endingNow = "";
                    var fifths = measure.Element("attributes")?.Element("key")?.Element("fifths");
                    if (fifths is not null) f[key + "keyChange"] = $"{(string?)fifths}{((string?)measure.Element("attributes")!.Element("key")!.Element("mode") == "minor" ? "m" : "")}";
                    f[key + "rehearsal"] = (string?)measure.Descendants("rehearsal").FirstOrDefault() ?? "";
                }
                // walk
                double pos = 0, lastOnset = 0;
                var used = new Dictionary<string, int>();
                var tiedFlags = new HashSet<string>();
                                foreach (var el in measure.Elements())
                {
                    switch (el.Name.LocalName)
                    {
                        case "backup": pos -= (double)el.Element("duration")!; break;
                        case "forward": pos += (double)el.Element("duration")!; break;
                        case "direction":
                            var dyn = el.Descendants("dynamics").FirstOrDefault()?.Elements().FirstOrDefault();
                            if (dyn is not null && (string?)el.Element("staff") == "1") { f[$"t{t}/b{barNo}/@{RtF(pos / slotDiv)}|dynamicMark"] = dyn.Name.LocalName ; }
                            break;
                        case "note":
                            var staff = (string?)el.Element("staff") ?? "1";
                            var isGrace = el.Element("grace") is not null; var isChord = el.Element("chord") is not null;
                            var dur = el.Element("duration") is { } de ? (double)de : 0;
                            var onset = isChord ? lastOnset : pos;
                            if (!isChord) lastOnset = pos;
                            if (!isChord && !isGrace) pos += dur;
                            if (staff != tabStaff || el.Element("rest") is not null) break;
                            var drums = el.Element("unpitched") is not null;
                            var voice = (int)el.Element("voice")! - (tabStaff == "2" ? 5 : 1);
                            notes++;
                            var bk = $"t{t}/b{barNo}/v{voice}/@{RtF(onset / slotDiv)}|";
                            var tech = el.Element("notations")?.Element("technical");
                            var stringNo = (int?)tech?.Element("string");
                            var midi = drums ? int.Parse(((string)el.Element("instrument")!.Attribute("id")!).Split("-I")[1], CultureInfo.InvariantCulture) : PitchToMidi(el.Element("pitch")!);
                            var id = $"{(isGrace ? "g" : "n")}{(drums ? "d" + midi : "s" + ((stringNo ?? 1) - 1))}";
                            var ord = used.GetValueOrDefault(bk + id); used[bk + id] = ord + 1;
                            var nk = bk + id + "#" + ord + "|";
                            f[bk + "exists"] = "1";
                            f[nk + "exists"] = "1"; f[nk + "midi"] = midi.ToString(CultureInfo.InvariantCulture);
                            if (!drums) f[nk + "fret"] = ((int?)tech?.Element("fret") ?? -1).ToString(CultureInfo.InvariantCulture);
                            if (!isGrace) f[bk + "durSlots"] = RtF(dur / slotDiv);
                            var tm = el.Element("time-modification");
                            if (!isGrace) f[bk + "tuplet"] = tm is null ? "0:0" : $"{(string?)tm.Element("actual-notes")}:{(string?)tm.Element("normal-notes")}";
                            f[nk + "tieStop"] = el.Elements("tie").Any(x => (string?)x.Attribute("type") == "stop") ? "1" : "0";
                            f[nk + "tieStart"] = el.Elements("tie").Any(x => (string?)x.Attribute("type") == "start") ? "1" : "0";
                            var head = (string?)el.Element("notehead");
                            f[nk + "dead"] = head == "x" && !drums ? "1" : "0";
                            f[nk + "ghost"] = el.Element("notehead")?.Attribute("parentheses") is not null && !drums ? "1" : "0";
                            var first = !isChord && !isGrace;
                            var art = el.Element("notations")?.Element("articulations");
                            if (first)
                            {
                                f[bk + "accentMark"] = art?.Element("strong-accent") is not null ? "2" : art?.Element("accent") is not null ? "1" : "0";
                                f[bk + "staccato"] = art?.Element("staccato") is not null ? "1" : "0";
                                f[bk + "tenuto"] = art?.Element("tenuto") is not null ? "1" : "0";
                                f[bk + "fermata"] = el.Element("notations")?.Element("fermata") is not null ? "1" : "0";
                            }
                            if (!drums)
                            {
                                f[nk + "hopoStart"] = tech?.Elements().Any(x => (x.Name.LocalName is "hammer-on" or "pull-off") && (string?)x.Attribute("type") == "start") == true ? "1" : "0";
                                f[nk + "slideStart"] = tech?.Elements("slide").Any(x => (string?)x.Attribute("type") == "start") == true ? "1" : "0";
                                f[nk + "bend"] = tech?.Element("bend") is not null ? "1" : "0";
                                f[nk + "harmonic"] = tech?.Element("harmonic") is not null ? "1" : "0";
                                f[nk + "tap"] = tech?.Element("tap") is not null ? "1" : "0";
                                f[nk + "palmMute"] = (string?)el.Element("play")?.Element("mute") == "palm" ? "1" : "0";
                                if (!isChord && !isGrace) f[nk + "scoopPlopDoitFall"] = string.Concat(new[] { "scoop", "plop", "doit", "falloff" }.Where(a => art?.Element(a) is not null).Select(a => a[0]));
                            }
                            break;
                    }
                }
                barNo++;
            }
        }
        noteCount = notes;
        return f;

        static int PitchToMidi(XElement pitch)
        {
            var step = (string)pitch.Element("step")!;
            var alter = (int?)pitch.Element("alter") ?? 0;
            var oct = (int)pitch.Element("octave")!;
            var semis = step switch { "C" => 0, "D" => 2, "E" => 4, "F" => 5, "G" => 7, "A" => 9, _ => 11 };
            return (oct + 1) * 12 + semis + alter;
        }
    }

    /// <summary>The same view of the model as the MusicXML reader produces (tab staff / percussion staff, what the format can say).</summary>
    private static Dictionary<string, string> RtMusicXmlExpected(SongProject p, out int noteCount)
    {
        var f = new Dictionary<string, string>(StringComparer.Ordinal);
        var notes = 0;
        f["song|tracks"] = p.Tracks.Count.ToString(CultureInfo.InvariantCulture);
        for (var t = 0; t < p.Tracks.Count; t++)
        {
            var track = p.Tracks[t]; var drums = RtIsDrums(track);
            f[$"t{t}|name"] = string.IsNullOrWhiteSpace(track.Name) ? "Track " + (t + 1) : track.Name;
            var previousDynamic = -1;
            var lastKey = p.KeySignature; var lastMinor = p.KeySignatureMinor; var lastTime = "";
            var markers = p.Markers.ToDictionary(m => m.MeasureIndex, m => m.Title);
            var barCount = p.Tracks.Max(x => x.Measures.Count);
            for (var b = 0; b < barCount; b++)
            {
                var m = b < track.Measures.Count ? track.Measures[b] : null;
                var master = MusicTime.BarOf(p, b);
                var slots = MusicTime.BarSlots(p, b);
                if (t == 0)
                {
                    var key = $"bar{b}|";
                    var time = $"{master?.TimeSigNum ?? p.TimeSignatureNumerator}/{master?.TimeSigDenom ?? p.TimeSignatureDenominator}";
                    if (b == 0 || time != lastTime) f[key + "timeSig"] = time;
                    lastTime = time;
                    f[key + "tempoMark"] = (b == 0 ? master?.TempoChange ?? p.Tempo : master?.TempoChange)?.ToString(CultureInfo.InvariantCulture) ?? "";
                    f[key + "repeatStart"] = master is { RepeatStart: true } ? "1" : "0";
                    f[key + "repeatEnd"] = master is { RepeatEnd: true } ? "1" : "0";
                    f[key + "repeatCount"] = master is { RepeatEnd: true } ? Math.Max(2, master.RepeatCount).ToString(CultureInfo.InvariantCulture) : "";
                    f[key + "endings"] = master?.EndingLabel ?? "";
                    var ks = master is null ? lastKey : master.KeySignature ?? p.KeySignature; var km = master is null ? lastMinor : master.KeySignatureMinor ?? p.KeySignatureMinor;
                    if (b == 0 || ks != lastKey || km != lastMinor) f[key + "keyChange"] = $"{Math.Clamp(ks, -7, 7)}{(km ? "m" : "")}";
                    lastKey = ks; lastMinor = km;
                    f[key + "rehearsal"] = markers.TryGetValue(b, out var section) ? section : "";
                }
                if (m is null) continue;
                var voices = new List<List<TabCell>> { m.Cells };
                if (m.Voice2Cells.Any(c => c.Notes.Count > 0)) voices.Add(m.Voice2Cells);
                for (var v = 0; v < voices.Count; v++)
                {
                    if (!voices[v].Any(c => c.Notes.Count > 0)) continue;
                    var written = 0.0;
                    foreach (var (cell, onset) in RtBeats(voices[v]))
                    {
                        if (onset >= slots - 0.01) break;
                        var dur = Math.Min(Math.Max(1, Math.Round(MusicTime.CellSlots(cell) * 60)) / 60.0, slots - written);
                        written += Math.Max(0, MusicTime.CellSlots(cell));
                        if (cell.Notes.Count == 0) continue;
                        var bk = $"t{t}/b{b}/v{v}/@{RtF(onset)}|";
                        var principal = cell.Notes.Where(n => !n.IsGraceNote).ToList();
                        var graces = cell.Notes.Where(n => n.IsGraceNote).ToList();
                        if (principal.Count == 0) { principal = graces; graces = new(); }
                        if (v == 0 && principal.Count > 0)
                        {
                            var dyn = Dynamics.NearestIndex(principal[0].Velocity);
                            if (dyn != previousDynamic) { f[$"t{t}/b{b}/@{RtF(onset)}|dynamicMark"] = Dynamics.Names[dyn]; previousDynamic = dyn; }
                        }
                        f[bk + "exists"] = "1"; f[bk + "durSlots"] = RtF(dur);
                        f[bk + "tuplet"] = $"{cell.Tuplet.Numerator}:{cell.Tuplet.Denominator}";
                        f[bk + "accentMark"] = cell.Accent.ToString(CultureInfo.InvariantCulture);
                        f[bk + "staccato"] = cell.Staccato ? "1" : "0"; f[bk + "tenuto"] = cell.Tenuto ? "1" : "0"; f[bk + "fermata"] = cell.Fermata ? "1" : "0";
                        var used = new Dictionary<string, int>();
                        var firstPrincipal = principal.Count > 0 ? principal[0] : null;
                        var ordered = graces.Select(n => (n, true)).Concat(principal.Select(n => (n, false))).OrderBy(x => x.Item2 ? 0 : 1).ThenBy(x => drums ? RtDrumMidi(x.n) : x.n.StringIndex).ToList();
                        for (var i = 0; i < ordered.Count; i++)
                        {
                            var (n, grace) = ordered[i];
                            var midi = drums ? RtDrumMidi(n) : n.MidiValue > 0 ? n.MidiValue : 40;
                            var id = $"{(grace ? "g" : "n")}{(drums ? "d" + midi : "s" + n.StringIndex)}";
                            var ord = used.GetValueOrDefault(id); used[id] = ord + 1;
                            var nk = bk + id + "#" + ord + "|";
                            notes++;
                            var tech = n.Techniques;
                            f[nk + "exists"] = "1"; f[nk + "midi"] = midi.ToString(CultureInfo.InvariantCulture);
                            if (!drums) f[nk + "fret"] = n.Fret.ToString(CultureInfo.InvariantCulture);
                            f[nk + "tieStop"] = (n.Tied || cell.IsTied) ? "1" : "0"; f[nk + "tieStart"] = tech.Contains("Tie") ? "1" : "0";
                            f[nk + "dead"] = n.Dead && !drums ? "1" : "0"; f[nk + "ghost"] = n.Ghost && !n.Dead && !drums ? "1" : "0";
                            if (!drums)
                            {
                                f[nk + "hopoStart"] = tech.Contains("HOPOOrigin") || tech.Contains("HOPO") && !tech.Contains("HOPODestination") ? "1" : "0";
                                f[nk + "slideStart"] = tech.Contains("LegatoSlide") || tech.Contains("ShiftSlide") || tech.Contains("Slide") ? "1" : "0";
                                var peak = n.BendPoints.Count == 0 ? 0 : n.BendPoints.Max(x => x.Value);
                                var firstB = n.BendPoints.Count == 0 ? 0 : n.BendPoints.OrderBy(x => x.Offset).First().Value;
                                f[nk + "bend"] = firstB > 0 || peak > 0 ? "1" : "0";
                                f[nk + "harmonic"] = tech.Any(x => x.Contains("Harmonic")) ? "1" : "0";
                                f[nk + "tap"] = tech.Contains("Tapping") ? "1" : "0";
                                f[nk + "palmMute"] = TechniqueNames.HasPalmMute(tech) ? "1" : "0";
                                if (!grace && ReferenceEquals(n, firstPrincipal)) f[nk + "scoopPlopDoitFall"] = string.Concat(new[] { ("SlideInBelow", 's'), ("SlideInAbove", 'p'), ("SlideOutUp", 'd'), ("SlideOutDown", 'f') }.Where(a => tech.Contains(a.Item1)).Select(a => a.Item2));
                            }
                        }
                    }
                }
            }
        }
        noteCount = notes;
        return f;
    }

    // ------------------------------------------------------------------ playback compile consistency

    private static readonly PlaybackOptions RtLiveOptions = new() { Metronome = true, CountIn = true, LiveMetronomeEvents = true, RespectMuteSolo = true };

    private static List<(int Track, int Midi, double Onset, double Dur, int Velocity)> RtNoteSig(ScoreTimeline tl, double shiftMs = 0) =>
        tl.Notes.Select(n => (n.TrackIndex, n.Midi, Math.Round(n.OnsetMs - shiftMs, 1), Math.Round(n.DurationMs, 1), n.Velocity))
            .OrderBy(n => n.TrackIndex).ThenBy(n => n.Item3).ThenBy(n => n.Midi).ToList();

    /// <summary>Playback compile vs offline-render compile of the same song: the same note events at the same times (count-in aside).</summary>
    private static void RtCheckLiveVsRender(string scenario, SongProject song)
    {
        var live = MidiTimelineBuilder.Build(song, RtLiveOptions.Clone());
        var render = RenderSpecBuilder.Compile(song);
        var a = RtNoteSig(live, live.CountInMs); var b = RtNoteSig(render);
        var same = a.SequenceEqual(b);
        var firstDiff = same ? "" : $"live {a.Count} notes, render {b.Count} notes; first mismatch at {Enumerable.Range(0, Math.Min(a.Count, b.Count)).FirstOrDefault(i => !a[i].Equals(b[i]))}";
        Check($"playback compile vs offline render [{scenario}]: same {b.Count} note events (track, pitch, onset, duration, velocity), count-in offset {live.CountInMs:0} ms removed", same, firstDiff);
        double Ev(ScoreTimeline tl, double shift) => tl.Events.Where(e => !e.IsMetronome && e.TrackIndex >= 0).Sum(e => Math.Round(e.TimeMs - shift, 1) * 0.0 + e.Status + e.Data1 * 3 + e.Data2 * 7 + Math.Round(e.TimeMs - shift, 1));
        Check($"playback compile vs offline render [{scenario}]: same channel-event checksum", Math.Abs(Ev(live, live.CountInMs) - Ev(render, 0)) < 0.5);
        // The render tempo map must reproduce the performed positions (frames at 48 kHz): every bar start, and every slot of a
        // bar with mid-bar tempo changes or ramps (the same per-slot tempo data as playback and the MIDI export).
        var map = RenderSpecBuilder.TempoMap(render, 48000, song.Tempo, song);
        var worstBar = 0.0; var worstSlot = 0.0; var barPpq = 0.0; var midBars = 0;
        foreach (var bar in render.Bars)
        {
            double PpqAt(double ms)
            {
                var idx = map.FindLastIndex(m => m.Frame <= RenderSpecBuilder.ToFrames(ms, 48000));
                if (idx < 0) return double.NaN;
                var seg = map[idx];
                return seg.Ppq + (ms - seg.Frame * 1000.0 / 48000) / 60000.0 * seg.Tempo;
            }
            worstBar = Math.Max(worstBar, Math.Abs(PpqAt(bar.StartMs) - barPpq));
            var measure = MusicTime.BarOf(song, bar.Bar);
            if (measure?.MidBarTempos is { Count: > 0 })
            {
                midBars++;
                for (var slot = 1; slot < bar.Slots; slot++)
                    worstSlot = Math.Max(worstSlot, Math.Abs(PpqAt(bar.StartMs + MusicTime.OffsetMs(measure, slot, bar.Tempo)) - (barPpq + slot / 4.0)));
            }
            barPpq += bar.Slots / 4.0;
        }
        Check($"offline render tempo map [{scenario}]: reproduces every performed bar start (worst {worstBar:0.0000} quarter notes)", worstBar < 0.01);
        if (midBars > 0) Check($"offline render tempo map [{scenario}]: follows the mid-bar tempo changes and ramps of {midBars} bar(s) slot by slot (worst {worstSlot:0.0000} quarter notes)", worstSlot < 0.05);
    }

    /// <summary>Note events of a reopened song must equal the original's (pitch, onset within 1 ms, duration within tolerance).</summary>
    private static void RtCheckTimelineSame(string scenario, string format, SongProject original, SongProject reopened, bool exactVelocity)
    {
        var a = RtNoteSig(RenderSpecBuilder.Compile(original)); var b = RtNoteSig(RenderSpecBuilder.Compile(reopened));
        var problems = new List<string>();
        if (a.Count != b.Count) problems.Add($"note count {a.Count} -> {b.Count}");
        else
            for (var i = 0; i < a.Count && problems.Count < 4; i++)
            {
                if (a[i].Track != b[i].Track || a[i].Midi != b[i].Midi || Math.Abs(a[i].Onset - b[i].Onset) > 1.0) problems.Add($"#{i} {a[i]} vs {b[i]}");
                else if (Math.Abs(a[i].Dur - b[i].Dur) > Math.Max(30, a[i].Dur * 0.15)) problems.Add($"#{i} duration {a[i].Dur} vs {b[i].Dur}");
                else if (exactVelocity && a[i].Velocity != b[i].Velocity) problems.Add($"#{i} velocity {a[i].Velocity} vs {b[i].Velocity}");
            }
        Check($"playback timeline [{format}] {scenario}: same note events after reopen ({a.Count} notes)", problems.Count == 0, string.Join(" | ", problems));
    }

    // ------------------------------------------------------------------ the suite

    private static string RtFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void RtCleanup(string folder) { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static void TestRoundTripSemantics()
    {
        var folder = RtFolder();
        try
        {
            RtLossLog.Clear(); RtUsedAllowances.Clear(); RtCoverage.Clear(); RtProfiles.Clear();
            var scenarios = new List<(string Name, SongProject Song)>
            {
                ("technique song", RtTechniqueSong()),
                ("showcase", RtShowcaseSong()),
                ("dense (bends, ties, grace, harmonics, slides, tuplets, dynamics, repeats, endings, tempo, voices, tunings, drums)", RtDenseSong()),
            };
            var gpClean = RtGpClean();
            foreach (var (name, song) in scenarios)
            {
                try {
                var expected = RtCopy(song);
                var tforge = RtViaTforge(song, folder, "s");
                RtVerify(name, RtTforge, expected, tforge);
                RtVerify(name, RtGpEmbedded, expected, RtViaGp(song, folder, "s", embed: true));
                var clean = RtViaGp(song, folder, "s", embed: false);
                RtVerify(name, gpClean, expected, clean);
                RtCheckTimelineSame(name, ".tforge", expected, tforge, exactVelocity: true);
                RtCheckTimelineSame(name, "clean .gp", expected, clean, exactVelocity: false);
                RtCheckLiveVsRender(name, expected);
                }
                catch (Exception ex) { Check($"round trip scenario '{name}' completed without throwing", false, $"{ex.GetType().Name}: {ex.Message} at {string.Join(" <- ", (ex.StackTrace ?? "").Split((char)10).Take(3).Select(x => x.Trim()))}"); }
            }
            // Playback vs offline render on tempo shapes the dense song lacks: a ramp inside a bar, a mid-bar step after it, and a whole-bar ramp.
            var shaped = RtCopy(RtDenseSong());
            foreach (var t in shaped.Tracks)
            {
                t.Measures[10].MidBarTempos = new List<TempoPoint> { new(2, 180, 8), new(12, 70) };
                t.Measures[11].MidBarTempos = new List<TempoPoint> { new(0, 60, 16) };
            }
            RtCheckLiveVsRender("dense + mid-bar tempo change and ramps", shaped);
        }
        finally { RtCleanup(folder); }
    }

    private static void TestRoundTripSemanticsMusicXml()
    {
        foreach (var (name, song) in new[] { ("technique song", RtTechniqueSong()), ("showcase", RtShowcaseSong()), ("dense", RtDenseSong()) })
        {
            var expected = RtCopy(song);
            var bytes = MusicXmlExportService.ToBytes(expected);
            var actual = RtMusicXmlFacts(bytes, expected, out var actualNotes);
            var model = RtMusicXmlExpected(expected, out var modelNotes);
            var profile = new RtProfile { Name = "MusicXML (independent reader)", Losses = new() };
            var all = RtCompare(model, actual);
            if (all.Count > 0) Log.Add($"  info  MusicXML {name} first actual keys: {string.Join(" ; ", actual.Keys.Where(k => k.StartsWith("t0/b0")).Take(14))}");
            var unexpected = all;
            RtProfiles.Add(profile.Name);
            RtCoverage.Add((name, profile.Name, expected.Tracks.Count, expected.Tracks.Max(t => t.Measures.Count), 0, modelNotes, unexpected.Count, 0));
            var summary = string.Join(" | ", unexpected.GroupBy(d => d.Category).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key} x{g.Count()} e.g. {g.First().Where} expected '{g.First().Expected}' got '{g.First().Actual}'"));
            Check($"MusicXML read back independently [{name}]: notes, strings, frets, onsets, durations, ties, tuplets, dynamics, bars ({modelNotes} notes)", unexpected.Count == 0 && actualNotes == modelNotes, $"{actualNotes}/{modelNotes} notes; {summary}");
        }
    }

    /// <summary>Import -> edit -> save -> reopen through the real document controller (the user's flow), on the dense song.</summary>
    private static void TestRoundTripImportEditSaveReopen()
    {
        var folder = RtFolder();
        try
        {
            var controller = new DocumentController();
            var source = RtDenseSong();
            var gpPath = Path.Combine(folder, "start.gp");
            GuitarProExporter.Save(source, gpPath, embedProject: false);
            var opened = controller.Open(gpPath);
            Check("import: the clean .gp opens as a Guitar Pro import", opened.ImportedFromGuitarPro);
            var project = opened.Project;
            // edit: rename, reorder, retune a note, change a tempo, delete a note, add a note
            var before = project.Tracks.Count;
            project.Tracks[1].Name = "Renamed DADGAD";
            project.MoveTrack(0, 2);
            var lead = project.Tracks.First(t => t.Name == "Lead");
            var firstNote = lead.Measures[0].Cells.First(c => c.Notes.Count > 0).Notes[0];
            firstNote.Fret += 2; firstNote.MidiValue += 2; firstNote.Velocity = Dynamics.Velocities[2];
            project.Tracks[0].Measures[3].TempoChange = 77;
            lead.Measures[2].Cells.First(c => c.Notes.Count > 0).Notes.Clear();   // a deleted note leaves a gap (rest)
            lead.Measures[4].Cells[8].Notes.Add(RtNote(lead, 2, 4, lead.Measures[4].Cells[8].Notes[0].Velocity, "PalmMute"));   // a chord tone on an existing beat
            project.IsDirty = true;
            var edited = RtCopy(project);
            var session = DocumentSession.FromProject(project, null);
            var tforgePath = Path.Combine(folder, "edited.tforge");
            controller.Save(session, tforgePath, project.Lyrics);
            var reopened = controller.Open(tforgePath).Project;
            RtVerify("import -> edit -> save .tforge -> reopen", RtTforge, edited, reopened);
            Eq("edit flow: track count kept", before, reopened.Tracks.Count);
            Check("edit flow: rename and reorder survive", reopened.Tracks[1].Name == edited.Tracks[1].Name && reopened.Tracks.Select(t => t.Name).SequenceEqual(edited.Tracks.Select(t => t.Name)));
            // and again as a clean .gp + .tfaudio pair
            var pairPath = Path.Combine(folder, "edited.gp");
            controller.SaveCleanGuitarProWithAudioData(DocumentSession.FromProject(RtCopy(edited), null), pairPath, edited.Lyrics);
            var pair = controller.Open(pairPath).Project;
            RtVerify("import -> edit -> save clean .gp+.tfaudio -> reopen", RtGpClean(), edited, pair);
            Check("edit flow: the .tfaudio pair was written beside the .gp", File.Exists(AudioDataFile.PathFor(pairPath)));
        }
        finally { RtCleanup(folder); }
    }

    private static void TestRoundTripAudioAndRouting()
    {
        var folder = RtFolder();
        try
        {
            var song = RtAudioSong();
            var expected = RtCopy(song);
            RtVerify("plug-in state, buses, sidechains, MIDI forwarding, clips, missing devices/plug-ins", RtTforge, expected, RtViaTforge(song, folder, "a"), audio: true);
            RtVerify("plug-in state, buses, sidechains, MIDI forwarding, clips, missing devices/plug-ins", RtGpEmbedded, expected, RtViaGp(song, folder, "a", true), audio: true);
            // clean .gp + .tfaudio: routing is re-pointed by matching tracks; device numbers and per-track live settings live in neither file.
            var pair = RtViaGpPair(song, folder, "a");
            var pairProfile = RtGpClean(("track.midiDevice", "MIDI output device numbers are machine-specific; not stored in .gp or .tfaudio"),
                ("track.audioInput", "record input choice is a live setting, not stored in .gp or .tfaudio"),
                ("track.monitor", "input monitoring is a live setting, not stored"), ("track.tint", "row tint is a view preference (.tforge only)"),
                ("track.reverb", "the GP7 export does not write reverb sends"), ("track.chorus", "the GP7 export does not write chorus sends"),
                ("track.transpose", "the GP7 export does not write per-track transpose"), ("track.performer", "performer is display-only (.tforge only)"),
                ("track.trackNotes", "track notes are display-only (.tforge only)"), ("track.instrumentName", "the importer names instruments from the GM program"),
                ("track.drumMap", "the drum-map preset is TabForge-only (.tforge)"), ("track.color", "track colour: see the GP7 export (colour is written; the importer may re-derive it)"));
            RtVerify("plug-in state, buses, sidechains, MIDI forwarding, clips, missing devices/plug-ins", pairProfile, expected, pair, audio: true);
            // dangling / unresolvable links after a track is deleted: cleared and reported, never silently kept
            var broken = RtCopy(song);
            var goneId = broken.Tracks[2].Id.ToString("N");
            broken.Tracks.RemoveAt(2);
            var notices = new List<string>();
            var brokenPath = Path.Combine(folder, "broken.gp");
            GuitarProExporter.Save(broken, brokenPath, false);
            var again = GuitarProImporter.Import(brokenPath);
            var otherTracks = RtCopy(song); // the sidecar written for the FULL song, applied to the shortened one
            File.WriteAllBytes(AudioDataFile.PathFor(brokenPath), AudioDataFile.Serialize(otherTracks));
            Check("missing track: the .tfaudio still applies and reports what it could not link", AudioDataFile.TryApply(again, brokenPath, notices));
            Check("missing track: sidechain/MIDI links to the deleted track are cleared, not left dangling",
                again.Tracks.All(t => t.Rig.Plugins.All(p => p.SidechainTrackId != goneId && (p.SidechainTrackId == "" || again.Tracks.Any(x => x.Id.ToString("N") == p.SidechainTrackId)))) &&
                again.Mixer.Buses.Values.All(b => b.Rig.Plugins.All(p => p.SidechainTrackId == "" || again.Tracks.Any(x => x.Id.ToString("N") == p.SidechainTrackId))));
            Check("missing track: the user is told", notices.Any(n => n.Contains("routing link")), string.Join(" / ", notices));
            // model-level: a song with unavailable plug-ins and devices still compiles for playback and render
            var timeline = MidiTimelineBuilder.Build(RtCopy(song), new PlaybackOptions());
            Check("missing devices: a song pointing at MIDI device 77 and missing plug-ins still compiles a timeline", timeline.Events.Count > 0 && timeline.Notes.Count > 0);
            Check("missing devices: events keep the track's device id (the router decides at play time)", timeline.Events.Where(e => e.TrackIndex == 0).All(e => e.DeviceId == 77 || e.DeviceId == -1 || e.IsMetronome));
        }
        finally { RtCleanup(folder); }
    }

    private static void TestRoundTripReorderRename()
    {
        var folder = RtFolder();
        try
        {
            var song = RtAudioSong();
            song.Tracks[0].Name = "Renamed A";
            song.MoveTrack(0, 3);                     // Beta, Gamma, Beta, Renamed A
            song.MoveTrack(1, 0);                     // Gamma, Beta, Beta, Renamed A
            // sidechain / forward links still point at the SAME tracks (by id) after the move
            var expected = RtCopy(song);
            Check("reorder: expected order Gamma, Beta, Beta, Renamed A", expected.Tracks.Select(t => t.Name).SequenceEqual(new[] { "Gamma", "Beta", "Beta", "Renamed A" }));
            RtVerify("track reorder + rename with duplicate names", RtTforge, expected, RtViaTforge(song, folder, "r"), audio: true);
            RtVerify("track reorder + rename with duplicate names", RtGpEmbedded, expected, RtViaGp(song, folder, "r", true), audio: true);
            var pair = RtViaGpPair(song, folder, "r");
            var profile = RtGpClean(("track.midiDevice", "machine-specific, not in .gp/.tfaudio"), ("track.audioInput", "live setting, not stored"), ("track.monitor", "live setting, not stored"),
                ("track.tint", "view preference"), ("track.reverb", "not in GP7 export"), ("track.chorus", "not in GP7 export"), ("track.transpose", "not in GP7 export"),
                ("track.performer", "display-only"), ("track.trackNotes", "display-only"), ("track.instrumentName", "re-derived from program"), ("track.drumMap", "TabForge-only"),
                ("track.color", "colour is re-derived on import"));
            RtVerify("track reorder + rename with duplicate names", profile, expected, pair, audio: true);
            // the identity of the two same-named tracks: the sidechain of the first "Beta" and of the second must land on their own tracks
            Check("reorder: duplicate track names keep their own rig (the first Beta keeps its amp, the second Beta has none)", pair.Tracks[1].Rig.Plugins.Count == 1 && pair.Tracks[2].Rig.Plugins.Count == 0);
        }
        finally { RtCleanup(folder); }
    }

    private static void TestRoundTripLargeAndRepeated()
    {
        var folder = RtFolder();
        try
        {
            var large = RtLargeSong(10, 180);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var expected = RtCopy(large);
            var t1 = RtViaTforge(large, folder, "big");
            var tforgeMs = watch.ElapsedMilliseconds; watch.Restart();
            RtVerify("large project (10 tracks x 180 bars)", RtTforge, expected, t1);
            var gp = RtViaGp(large, folder, "big", false);
            var gpMs = watch.ElapsedMilliseconds;
            RtVerify("large project (10 tracks x 180 bars)", RtGpClean(), expected, gp);
            Log.Add($"  info  large project: .tforge save+load {tforgeMs} ms ({new FileInfo(Path.Combine(folder, "big.tforge")).Length / 1024} KiB), clean .gp export+import {gpMs} ms ({new FileInfo(Path.Combine(folder, "big-clean.gp")).Length / 1024} KiB)");
            RtCheckLiveVsRender("large project", expected);

            // repeated document switching: A (.tforge), B (clean .gp), C (.gp + .tfaudio), reopened round-robin; nothing leaks between them
            var controller = new DocumentController();
            var a = RtCopy(RtDenseSong()); var b = RtCopy(RtShowcaseSong()); var c = RtCopy(RtAudioSong());
            var aPath = Path.Combine(folder, "a.tforge"); ProjectService.Save(aPath, a);
            var bPath = Path.Combine(folder, "b.gp"); GuitarProExporter.Save(b, bPath, false);
            var cPath = Path.Combine(folder, "c.gp"); controller.SaveCleanGuitarProWithAudioData(DocumentSession.FromProject(RtCopy(c), null), cPath, c.Lyrics);
            var baseB = RtScoreFacts(controller.Open(bPath).Project, out _, out _); var baseC = RtAudioFacts(controller.Open(cPath).Project);
            var baseA = RtScoreFacts(controller.Open(aPath).Project, out _, out _);
            var drift = new List<string>();
            for (var i = 0; i < 24; i++)
            {
                switch (i % 3)
                {
                    case 0: if (RtCompare(baseA, RtScoreFacts(controller.Open(aPath).Project, out _, out _)).Count > 0) drift.Add($"A@{i}"); break;
                    case 1: if (RtCompare(baseB, RtScoreFacts(controller.Open(bPath).Project, out _, out _)).Count > 0) drift.Add($"B@{i}"); break;
                    default: if (RtCompare(baseC, RtAudioFacts(controller.Open(cPath).Project)).Count > 0) drift.Add($"C@{i}"); break;
                }
            }
            Check("repeated document switching: 24 reopens of three different files (A .tforge, B .gp, C .gp+.tfaudio) never drift or leak", drift.Count == 0, string.Join(",", drift));

            // repeated save/reopen cycles: a fixed point after the first pass
            var cur = RtCopy(RtDenseSong());
            var first = RtScoreFacts(RtViaTforge(cur, folder, "cyc"), out _, out _);
            var stableTforge = true;
            var p = cur;
            for (var i = 0; i < 5; i++) { p = RtViaTforge(p, folder, "cyc"); stableTforge &= RtCompare(first, RtScoreFacts(p, out _, out _)).Count == 0; }
            Check("repeated .tforge save/load x5: no drift", stableTforge);
            var g1 = RtViaGp(RtDenseSong(), folder, "cyc", false);
            var baseline = RtScoreFacts(g1, out _, out _);
            var g = g1; var stableGp = true; var detail = "";
            for (var i = 0; i < 4; i++)
            {
                g = RtViaGp(g, folder, "cyc", false);
                var diffs = RtCompare(baseline, RtScoreFacts(g, out _, out _)).Where(d => RtGpClean().ReasonFor(d.Category, d) is null).ToList();
                if (diffs.Count > 0) { stableGp = false; detail = $"{diffs[0].Category} {diffs[0].Where} '{diffs[0].Expected}' -> '{diffs[0].Actual}' (+{diffs.Count - 1})"; break; }
            }
            Check("repeated clean .gp export/import x4: the second and later passes change nothing (idempotent after the first)", stableGp, detail);
        }
        finally { RtCleanup(folder); }
    }

    /// <summary>Local extra: the user's own songs, if present. Never required, never committed, never named in results.</summary>
    private static void TestRoundTripLocalExtra()
    {
        var tabs = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Tabs");
        if (!Directory.Exists(tabs)) { Skip("round trip local extra (Tabs/ songs)", "no Tabs/ folder next to the repository (expected on a clean checkout / CI)"); return; }
        var files = Directory.EnumerateFiles(tabs).Where(f => f.EndsWith(".gp5", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gp", StringComparison.OrdinalIgnoreCase)).Take(1).ToList();
        if (files.Count == 0) { Skip("round trip local extra (Tabs/ songs)", "Tabs/ has no Guitar Pro file"); return; }
        var folder = RtFolder();
        try
        {
            var song = GuitarProImporter.Import(files[0]);
            var expected = RtCopy(song);
            RtVerify("local song #1 (name withheld)", RtTforge, expected, RtViaTforge(song, folder, "local"));
            RtVerify("local song #1 (name withheld)", RtGpClean(), expected, RtViaGp(song, folder, "local", false));
        }
        finally { RtCleanup(folder); }
    }

    /// <summary>A mixer group's level/pan is an offset on each track's own value: it must count once, not twice, after .gp + .tfaudio save and reopen.</summary>
    private static void TestRoundTripMixerGroupNotDoubled()
    {
        var folder = RtFolder();
        try
        {
            var song = RtDenseSongSmall();
            foreach (var t in song.Tracks) { t.MixerGroup = MixerGroups.Guitars; t.Volume = 100; t.Pan = 60; }
            var group = song.Mixer.Edit(MixerGroups.Guitars); group.Volume = 110; group.Pan = 10;
            song.Mixer.MasterPan = 3;
            var before = (Vol: MixerGroups.Volume(song, song.Tracks[0]), Pan: MixerGroups.Pan(song, song.Tracks[0]));
            var pair = RtViaGpPair(song, folder, "grp");
            var after = (Vol: MixerGroups.Volume(pair, pair.Tracks[0]), Pan: MixerGroups.Pan(pair, pair.Tracks[0]));
            Check("mixer group: track 100 in a 110% group sounds at 110 before saving", before.Vol == 110 && before.Pan == 73, $"{before}");
            Check("mixer group: effective volume and pan are identical after .gp + .tfaudio reopen", after == before, $"before {before}, after {after}");
            Check("mixer group: the track keeps its own volume/pan (the group is not baked into it)", pair.Tracks.All(t => t.Volume == 100 && t.Pan == 60), $"{pair.Tracks[0].Volume}/{pair.Tracks[0].Pan}");
            Check("mixer group: the group itself is restored once", pair.Mixer.Levels(MixerGroups.Guitars).Volume == 110 && pair.Mixer.Levels(MixerGroups.Guitars).Pan == 10);

            // A .tfaudio written before FormatVersion 2 has no per-track values: the .gp's baked values are un-baked on open.
            var gpPath = Path.Combine(folder, "old-pair.gp");
            var gp = GuitarProExporter.ToBytes(song, embedProject: false);
            File.WriteAllBytes(gpPath, gp);
            var node = System.Text.Json.Nodes.JsonNode.Parse(AudioDataFile.Serialize(song, AudioDataFile.Sha256Hex(gp)))!.AsObject();
            node["FormatVersion"] = 1;
            foreach (var t in node["Tracks"]!.AsArray()) { t!.AsObject().Remove("Volume"); t.AsObject().Remove("Pan"); }
            File.WriteAllText(AudioDataFile.PathFor(gpPath), node.ToJsonString());
            var old = new DocumentController().Open(gpPath).Project;
            var oldAfter = (Vol: MixerGroups.Volume(old, old.Tracks[0]), Pan: MixerGroups.Pan(old, old.Tracks[0]));
            Check("mixer group: an older .gp + .tfaudio pair (baked values, no per-track data) is un-baked to about the same level",
                Math.Abs(oldAfter.Vol - before.Vol) <= 8 && Math.Abs(oldAfter.Pan - before.Pan) <= 8, $"before {before}, old pair {oldAfter}");
        }
        finally { RtCleanup(folder); }
    }

    /// <summary>Open issues found while writing the suite: measured live and logged (KNOWN), so the numbers in COMPATIBILITY_RESULTS.md come from a run.</summary>
    private static void TestRoundTripKnownIssues()
    {
        var folder = RtFolder();
        try
        {
            // 1. capo (fixed convention): frets are relative to the capo, sounding pitch = tuning + capo + fret,
            //    one rule (TrackModel.PitchOf) shared by editor entry, playback, import and export.
            var capoSong = new SongProject { Title = "capo" };
            var track = new TrackModel { Name = "Capo", Capo = 2, Measures = TemplateFactory.Measures(1) };
            var typedFrets = new[] { (S: 1, F: 3), (S: 0, F: 0), (S: 4, F: 7), (S: 5, F: 12) };
            for (var i = 0; i < typedFrets.Length; i++)
                RtPut(track, 0, i * 4, 4, 0, new TabNote { StringIndex = typedFrets[i].S, Fret = typedFrets[i].F, MidiValue = track.PitchOf(typedFrets[i].S, typedFrets[i].F) });   // exactly what the editor stores
            capoSong.Tracks.Add(track);
            Check("capo: PitchOf = tuning + capo + fret", track.PitchOf(1, 3) == track.StringTunings[1] + 2 + 3 && track.FretOf(1, track.PitchOf(1, 3)) == 3);
            bool CapoSame(SongProject back, string via)
            {
                var cells = back.Tracks[0].Measures[0].Cells.Where(c => c.Notes.Count > 0).ToList();
                var ok = back.Tracks[0].Capo == 2 && cells.Count == typedFrets.Length;
                for (var i = 0; ok && i < typedFrets.Length; i++)
                {
                    var n = cells[i].Notes[0];
                    ok = n.StringIndex == typedFrets[i].S && n.Fret == typedFrets[i].F && n.MidiValue == track.PitchOf(typedFrets[i].S, typedFrets[i].F);
                }
                Log.Add($"  capo-2 track, notes typed, {via}: {(ok ? "same frets and sounding pitch" : "DIFFERENT: " + string.Join(",", cells.Select(c => $"{c.Notes[0].Fret}/{c.Notes[0].MidiValue}")))}");
                return ok;
            }
            Check("capo-2 track: .gp round trip keeps frets and sounding pitch", CapoSame(RtViaGp(capoSong, folder, "capo", false), ".gp"));
            Check("capo-2 track: .gp (embedded project) round trip keeps frets and sounding pitch", CapoSame(RtViaGp(capoSong, folder, "capoe", true), ".gp embedded"));
            Check("capo-2 track: .tforge round trip keeps frets and sounding pitch", CapoSame(RtViaTforge(capoSong, folder, "capot"), ".tforge"));
            // 2. mixer group level baked into the .gp and re-applied from the .tfaudio
            var song = RtDenseSongSmall();
            song.Mixer.Edit(MixerGroups.Guitars).Volume = 110;
            foreach (var t in song.Tracks) t.MixerGroup = MixerGroups.Guitars;
            var pair = RtViaGpPair(song, folder, "grp");
            Log.Add($"  KNOWN mixer group: track volume {song.Tracks[0].Volume} with a Guitars group at 110% sounds at {MixerGroups.Volume(song, song.Tracks[0])}; after .gp + .tfaudio reopen the track is {pair.Tracks[0].Volume} and sounds at {MixerGroups.Volume(pair, pair.Tracks[0])}");
        }
        finally { RtCleanup(folder); }
    }

    /// <summary>Negative controls: the comparer must notice each kind of musical change (otherwise "0 unexpected differences" would prove nothing).</summary>
    private static void TestRoundTripComparerDetectsChanges()
    {
        var baseline = RtScoreFacts(RtCopy(RtDenseSong()), out _, out _);
        var mutations = new (string Name, string Category, Action<SongProject> Apply)[]
        {
            ("fret", "note.fret", p => p.Tracks[0].Measures[0].Cells[0].Notes[0].Fret += 1),
            ("pitch", "note.midi", p => p.Tracks[0].Measures[0].Cells[0].Notes[0].MidiValue += 1),
            ("string", "note.exists.missing", p => p.Tracks[0].Measures[0].Cells[0].Notes[0].StringIndex = 3),
            ("duration", "beat.durSlots", p => p.Tracks[0].Measures[0].Cells[0].DurationDenominator = 8),
            ("onset", "beat.exists.missing", p => p.Tracks[0].Measures[0].Cells[4].RhythmicPosition = 5.0),
            ("tempo", "bar.tempoChange", p => p.Tracks[0].Measures[4].TempoChange = 133),
            ("mid-bar tempo", "bar.midTempos", p => p.Tracks[0].Measures[9].MidBarTempos![0] = new TempoPoint(8, 141)),
            ("repeat count", "bar.repeatCount", p => p.Tracks[0].Measures[6].RepeatCount = 4),
            ("alternate ending", "bar.endings", p => p.Tracks[0].Measures[7].AlternateEnding = 2),
            ("time signature", "bar.timeSig", p => p.Tracks[0].Measures[1].TimeSigNum = 5),
            ("key", "bar.key", p => p.Tracks[0].Measures[8].KeySignatureMinor = false),
            ("dynamic", "note.dynamic", p => p.Tracks[0].Measures[0].Cells[0].Notes[0].Velocity = 127),
            ("tie", "note.tied", p => p.Tracks[0].Measures[3].Cells[8].Notes[0].Tied = false),
            ("technique", "note.technique:Bend.missing", p => p.Tracks[0].Measures[1].Cells[0].Notes[0].Techniques.Remove("Bend")),
            ("bend curve", "note.bend", p => p.Tracks[0].Measures[1].Cells[0].Notes[0].BendPoints[1].Value = 8),
            ("grace", "note.exists.missing", p => p.Tracks[0].Measures[4].Cells[4].Notes.RemoveAll(n => n.IsGraceNote)),
            ("tuplet", "beat.tuplet", p => p.Tracks[0].Measures[6].Cells[0].IsTriplet = false),
            ("tuning", "track.tuning", p => p.Tracks[1].StringTunings[0] += 1),
            ("track name", "track.name", p => p.Tracks[2].Name = "x"),
            ("drum sound", "note.exists.missing", p => p.Tracks[4].Measures[0].Cells[0].Notes[0].MidiValue = 43),
            ("voice 2 note", "note.exists.missing", p => p.Tracks[0].Measures[10].Voice2Cells[0].Notes.Clear()),
        };
        var missed = new List<string>();
        foreach (var (name, category, apply) in mutations)
        {
            var copy = RtCopy(RtDenseSong());
            apply(copy);
            var diffs = RtCompare(baseline, RtScoreFacts(copy, out _, out _));
            if (!diffs.Any(d => d.Category == category || d.Category.StartsWith(category, StringComparison.Ordinal))) missed.Add($"{name} (wanted {category}, got {string.Join("/", diffs.Select(d => d.Category).Distinct().Take(3))})");
        }
        Check($"comparer negative controls: all {mutations.Length} kinds of musical change are detected", missed.Count == 0, string.Join("; ", missed));
        // and the allow-list stays honest: an unlisted category is never excused, a conditional one only in its own case
        Check("allow-list: an unlisted category is not excused", RtGpClean().ReasonFor("note.fret", new RtDiff("note.fret", "k", "1", "2")) is null);
        Check("allow-list: a derived-data allowance applies only where the source had no value",
            RtGpClean().ReasonFor("note.slideTarget", new RtDiff("note.slideTarget", "k", "0", "60")) is not null && RtGpClean().ReasonFor("note.slideTarget", new RtDiff("note.slideTarget", "k", "59", "61")) is null);
    }

    private static void TestRoundTripSemanticsSuite()
    {
        TestRoundTripComparerDetectsChanges();
        TestRoundTripSemantics();
        TestRoundTripSemanticsMusicXml();
        TestRoundTripImportEditSaveReopen();
        TestRoundTripAudioAndRouting();
        TestRoundTripReorderRename();
        TestRoundTripLargeAndRepeated();
        TestRoundTripLocalExtra();
        TestRoundTripMixerGroupNotDoubled();
        TestRoundTripKnownIssues();
        // The allow-list is the contract: report what was actually lost, and any allowance that never fired (stale or too broad).
        foreach (var (profile, losses) in RtLossLog.OrderBy(x => x.Key))
            foreach (var (category, (count, reason, sample)) in losses.OrderBy(x => x.Key))
                Log.Add($"  loss  [{profile}] {category} x{count}: {reason}  (e.g. {sample})");
        Log.Add($"  info  round-trip coverage: {RtCoverage.Count} scenario x format runs, {RtCoverage.Sum(c => c.Notes)} notes / {RtCoverage.Sum(c => c.Beats)} beats compared, {RtCoverage.Sum(c => c.Expected)} allow-listed differences, {RtCoverage.Sum(c => c.Diffs)} unexpected");
    }

    /// <summary>`TabForge.exe --roundtrip-semantics &lt;report&gt;`: runs only the semantic round-trip suite (see docs/COMPATIBILITY_RESULTS.md).</summary>
    internal static int RunRoundTripSemantics(string reportPath)
    {
        Log.Clear(); _pass = 0; _fail = 0; _skip = 0;
        _required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Section("Semantic round trips");
        Guard(TestRoundTripSemanticsSuite);
        Log.Add(""); Log.Add($"round-trip suite: {_pass} passed, {_fail} failed, {_skip} skipped");
        try { File.WriteAllText(reportPath, string.Join(Environment.NewLine, Log)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 2; }
        Console.Out.WriteLine(string.Join(Environment.NewLine, Log));
        return _fail == 0 ? 0 : 1;
    }
}
