using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Rendering;
using TabForge.Services;

namespace TabForge;

// Owns: cross-format semantic round-trip check (import-edit-save-reopen-export).

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

    private static bool RtIsDrums(TrackModel t) => t.IsDrumTrack;
    private static int RtDrumMidi(TabNote n) => Math.Clamp(n.MidiValue > 0 ? n.MidiValue : n.Fret, 0, MidiLimits.Max);

    /// <summary>The score of a song as facts. Rests and annotation-only beats are not facts (silence is checked through note onsets/durations).</summary>
    private static Dictionary<string, string> RtScoreFacts(SongProject p, out int noteCount, out int beatCount)
    {
        var f = new Dictionary<string, string>(StringComparer.Ordinal);
        var notes = 0; var beats = 0;
        RtSongAndBarFacts(p, f);
        for (var t = 0; t < p.Tracks.Count; t++) RtTrackFacts(p, t, f, ref notes, ref beats);
        noteCount = notes; beatCount = beats;
        return f;
    }

    /// <summary>The song level facts (tracks, title, markers) and, per bar, the metre, tempo, repeats, endings, key, double bar, directions and triplet feel.</summary>
    private static void RtSongAndBarFacts(SongProject p, Dictionary<string, string> f)
    {
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
    }

    /// <summary>One track: its setup and mixer facts, then the facts of every beat of every voice.</summary>
    private static void RtTrackFacts(SongProject p, int t, Dictionary<string, string> f, ref int notes, ref int beats)
    {
        var track = p.Tracks[t];
        var drums = RtIsDrums(track);
        var tk = $"t{t}|";
        f[tk + "name"] = track.Name; f[tk + "isDrums"] = drums ? "1" : "0";
        f[tk + "tuning"] = drums ? "" : string.Join(",", track.StringTunings);
        f[tk + "capo"] = track.Capo.ToString(CultureInfo.InvariantCulture);
        f[tk + "program"] = track.MidiProgram.ToString(CultureInfo.InvariantCulture);
        f[tk + "channel"] = track.MidiChannel.ToString(CultureInfo.InvariantCulture);
        // The EFFECTIVE level (track value with its mixer group's level/pan and the master pan applied), compared exactly in every format:
        // a clean .gp stores exactly that number (the gpif holds it as a float, TabForge.AlphaTab patch 0002) and a reopened file has no group.
        // The track's own value and its group are kept separately by .tforge, the embedded project and the .tfaudio sidecar (tested in CheckRoundTripMixerGroupNotDoubled).
        f[tk + "volume"] = MixerGroups.Volume(p, track).ToString(CultureInfo.InvariantCulture);
        f[tk + "pan"] = MixerGroups.Pan(p, track).ToString(CultureInfo.InvariantCulture);
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
                    RtBeatFacts(f, cell, onset, t, b, v, drums, ref previousDynamic, ref notes, ref beats);
                }
        }
    }

    /// <summary>The facts of one beat and of each of its notes.</summary>
    private static void RtBeatFacts(Dictionary<string, string> f, TabCell cell, double onset, int t, int b, int v, bool drums, ref int previousDynamic, ref int notes, ref int beats)
    {
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
        // The embedded project is the full .tforge JSON: nothing is lost, so the allow-list is empty (the song.title entry was never a loss).
        Name = ".gp with embedded project", Losses = new()
    };

    private static bool RtSourceHadNone(RtDiff d) => d.Expected is "0" or "" or "(absent)";

    private static readonly RtProfile RtGpCleanProfile = new()
    {
        Name = "clean .gp",
        Losses = new()
        {
            ["note.velocity"] = "GP stores loudness as 8 dynamics steps (ppp..fff); velocity is quantised to the shared table. beat.dynamic is compared strictly instead",
            ["note.dynamic"] = "GP keeps ONE dynamic per beat: the other notes of a chord or drum beat take the first note's dynamic (a drum accent on the kick under a hi-hat is flattened). beat.dynamic is compared strictly",
            ["note.bend"] = "GP7 keeps origin, one flat middle stretch and destination: a hold at the end of a curve is stored as the destination offset (same sound), a curve with more turns is simplified (bend.shape first/peak/last is compared strictly)",
            ["beat.whammy"] = "GP7 keeps at most 4 whammy points: longer curves are simplified (whammy.shape is compared strictly)",
            ["beat.mix"] = "GP5 mix-table changes are TabForge-model data; the GP7 export writes tempo automations only",
            ["beat.fermata"] = "GP keeps a fermata on the bar position (master bar), so a fermata on one track appears on the other tracks' beat at that position; it is never lost, only added",
            ["track.channel"] = "the importer assigns MIDI channels in track order (percussion on 9); the .gp channel is not read back (drums stay on 9, checked through track.isDrums)",
            ["note.slideTarget"] = "derived data: the importer computes the slide's target pitch from the next note; a value the source did not set is filled in",
            ["note.trillTarget"] = "derived data: the importer fills the trill's target pitch when the source only tagged the trill",
            ["note.trillDur"] = "derived data: the importer fills the trill speed (1/16, what a gpif without a speed means) when the source did not set it",
            ["beat.tremoloPick"] = "derived data: the importer fills the tremolo-picking speed (1/8 default) when the source only tagged it; GP7 has three speeds (1/8, 1/16, 1/32), so 1/64 is written as 1/32",
            ["note.technique:FadeIn.extra"] = "GP stores a volume swell per beat: one faded note fades every note of the beat on reopen",
            ["note.technique:FadeOut.extra"] = "GP stores a fade-out per beat: one faded note fades every note of the beat on reopen",
            ["bar.directions"] = "the clean .gp stores directions under Guitar Pro's own names (Segno -> TargetSegno, ToCoda -> JumpDaCoda); the same marks, allowed only when the names are exactly the GP spelling",
            ["bar.tempoChange"] = "a tempo marking that restates the tempo already running is not kept as a change on import (bar.tempo, the tempo actually played, is compared strictly)",
            // capability row A23: the sub-type is re-derived from the curve, so a whammy tag can change name. The demo song (whammy beats of a sub-type the technique song lacks)
            // reads back one as TremBarCustom; the allowance was removed once as "never fired" (the technique song and 78 others do not show it) and the demo round trip failed.
            ["note.technique:TremBar.extra"] ="alphaTab re-derives the whammy sub-type (Dip, Dive, ...) from the curve; the tag may change name (whammy.shape is compared strictly)",
            ["note.technique:TremBarDip.extra"] = "alphaTab re-derives the whammy sub-type (Dip, Dive, ...) from the curve; the tag may change name (whammy.shape is compared strictly)",
            ["note.technique:TremBarCustom.missing"] = "alphaTab re-derives the whammy sub-type (Dip, Dive, ...) from the curve; the tag may change name (whammy.shape is compared strictly)",
            ["note.technique:TremBarDive.missing"] = "alphaTab re-derives the whammy sub-type (Dip, Dive, ...) from the curve; the tag may change name (whammy.shape is compared strictly)",
            ["note.technique:TremBarHold.missing"] = "alphaTab re-derives the whammy sub-type (Dip, Dive, ...) from the curve; the tag may change name (whammy.shape is compared strictly)",
            ["note.technique:TremBarPredive.missing"] = "alphaTab re-derives the whammy sub-type (Dip, Dive, ...) from the curve; the tag may change name (whammy.shape is compared strictly)",
            ["note.technique:TremBarPrediveDive.missing"] = "alphaTab re-derives the whammy sub-type (Dip, Dive, ...) from the curve; the tag may change name (whammy.shape is compared strictly)",
            ["note.technique:TremBarWide.missing"] = "alphaTab re-derives the whammy sub-type (Dip, Dive, ...) from the curve; the tag may change name (whammy.shape is compared strictly)",
            ["note.technique:Harmonic.extra"] = "the importer also tags every harmonic kind (artificial, pinch, tap, semi, feedback) with the generic Harmonic name",
            ["note.technique:Dead.extra"] = "the importer mirrors the dead-note flag as a Dead technique name (the flag itself is compared strictly)",
            ["note.technique:Ghost.extra"] = "the importer mirrors the ghost-note flag as a Ghost technique name (the flag itself is compared strictly)",
            ["note.technique:HOPOOrigin.extra"] = "the importer splits a hammer-on/pull-off tag into origin and destination tags",
            ["note.technique:HOPODestination.extra"] = "the importer splits a hammer-on/pull-off tag into origin and destination tags",
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
            ["note.velocity"] = d => int.TryParse(d.Expected, out _) && int.TryParse(d.Actual, out var a) && Dynamics.Velocities.Contains(a),   // a table value: its own step, or (chord, drum beat) the beat's one dynamic
            ["beat.fermata"] = d => d.Expected == "0", ["track.channel"] = d => d.Expected != "9" && d.Actual != "9",
            ["note.slideTarget"] = RtSourceHadNone, ["note.trillTarget"] = RtSourceHadNone, ["note.trillDur"] = RtSourceHadNone, ["note.graceSlots"] = d => d.Actual == "2",
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
                if (n.TrillTargetMidi > 0) n.TrillTargetMidi = Math.Clamp(n.TrillTargetMidi + semitones, 0, MidiLimits.Max);
            }
        }
        return copy;
    }

    private static List<(int Track, int Midi, double Onset, double Dur, int Velocity)> RtNoteSig(ScoreTimeline tl, double shiftMs = 0) =>
        tl.Notes.Select(n => (n.TrackIndex, n.Midi, Math.Round(n.OnsetMs - shiftMs, 1), Math.Round(n.DurationMs, 1), n.Velocity))
            .OrderBy(n => n.TrackIndex).ThenBy(n => n.Item3).ThenBy(n => n.Midi).ToList();

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

    private static void RtCleanup(string folder) { TryDelete(folder);
 }
}
