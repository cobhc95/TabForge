using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presets;

// Owns: the shared helpers of the full demo song: the section and hit-kind enums, the bar, slot and string conventions, the riff grammar (Riff),
//   the structure helpers and the finalise helpers. Note helpers and Hv: FullDemoSongFactory.Notes.cs; drum helpers: FullDemoSongFactory.DrumHelpers.cs.
// Does not own: the song's content, which the owner files FullDemoSongFactory.*.cs write, and the song model (Models).
// Tests: no dedicated test class.
// Shared helpers of the full demo song ("Ashen Meridian", docs/DEMO_SONG_PLAN.md section 6.2).
// The signatures below are the contract between the four owners: do not change them, add new helpers instead.
//
// Conventions every helper follows (plan section 0):
// - bars are 1-based source bars (code index = bar - 1);
// - slots are sixteenth notes from the bar start (a 4/4 bar has 16, 7/8 has 14); fractional slots are allowed;
// - strings are 1-based from the TOP string (s1 = highest), so StringIndex = s - 1; frets are relative to the capo;
// - MidiValue is always the sounding pitch (TrackModel.PitchOf), drums use the GM note, harmonics HarmonicMidi;
// - no Random anywhere: every "human" variation comes from Hv (seeded by the note's position), so builds are byte-identical.
internal static partial class FullDemoSongFactory
{
    /// <summary>Song sections used by the humanisation contour (plan 5.2). <see cref="SectionOf"/> maps a bar to one.</summary>
    internal enum Section
    {
        IntroA, Swell, IntroB, Verse1, Pre, Chorus, Post, Verse2, Interlude, Bridge, Breakdown, Bounce, WindUp,
        Solo1, Solo2, Coda, FinalChorus, Outro, FakeOut
    }

    /// <summary>
    /// What kind of hit a velocity belongs to; it selects the metric accent, the section contour and the clamp in <see cref="Hv"/>.
    /// Kick = single kicks; KickRun = continuous double-bass 16ths ('d'); Ghost = ghost snares (no contour, clamped to 1..60);
    /// Grace = flam grace notes (clamped to 55..70); FillRamp = a ramped fill hit ('r'); Roll = a Roll() hit (as written, no contour);
    /// Chug / Pitched = guitar, bass and keys notes (jitter only, no metric and no contour).
    /// </summary>
    internal enum Lane
    {
        Kick, KickRun, Snare, Ghost, SideStick, Clap, HiHat, Ride, Crash, China, Splash, Tom, FillRamp, Roll, Grace, Chug, Pitched
    }

    /// <summary>The fill kinds of plan 3.9 ("Fill kinds"), placed by <see cref="Fill"/>.</summary>
    internal enum FillKind { P4, P8, F16, Roll39, F6, FTrip8 }

    /// <summary>The eight dynamic velocities (plan 0.2): ppp 16, pp 33, p 49, mp 64, mf 80, f 95, ff 112, fff 127.</summary>
    internal static class Dyn
    {
        public const int ppp = 16, pp = 33, p = 49, mp = 64, mf = 80, f = 95, ff = 112, fff = 127;
    }

    // ------------------------------------------------------------------ track names (plan 1.5; builders find tracks by name)

    internal const string RhyL = "Rhythm Gtr L", RhyR = "Rhythm Gtr R", Lead = "Lead Gtr", LeadHarm = "Lead Harmony", Clean = "Clean Gtr",
        Bass = "Bass", Sub = "Sub Drop", Drums = "Drums", Pad = "Pad / Strings", Piano = "Piano";

    /// <summary>Number of source bars (plan 2.1).</summary>
    internal const int BarCount = 144;

    // ------------------------------------------------------------------ structure

    /// <summary>Runs <paramref name="set"/> on source bar <paramref name="bar"/> (1-based) of EVERY track, so structure agrees on all tracks.</summary>
    internal static void ForAllTracks(SongProject s, int bar, Action<MeasureModel> set)
    {
        foreach (var t in s.Tracks) set(t.Measures[bar - 1]);
    }

    /// <summary>
    /// Grows the bar so it holds at least as many cells as its signature has slots (MusicTime.BarSlots of the bar's own
    /// signature, 4/4 when unset): voice 1 always, voice 2 only when the bar already has one. Never shrinks and never
    /// touches existing cells. Call it after changing a bar's time signature (the skeleton already does, on every track).
    /// </summary>
    internal static void EnsureSlots(MeasureModel m)
    {
        var slots = SlotsOf(m);
        while (m.Cells.Count < slots) m.Cells.Add(new TabCell());
        if (m.Voice2Cells.Count > 0) while (m.Voice2Cells.Count < slots) m.Voice2Cells.Add(new TabCell());
    }

    /// <summary>The track called <paramref name="name"/> (use the name constants: RhyL, Lead, Drums...). Throws when missing.</summary>
    internal static TrackModel Track(SongProject s, string name) =>
        s.Tracks.FirstOrDefault(t => t.Name == name) ?? throw new InvalidOperationException($"Demo song: no track named '{name}'.");

    /// <summary>The section a source bar (1-based) belongs to (plan 2.1).</summary>
    internal static Section SectionOf(int bar) => bar switch
    {
        <= 9 => Section.IntroA,
        10 => Section.Swell,
        <= 18 => Section.IntroB,
        <= 31 => Section.Verse1,
        <= 39 => Section.Pre,
        <= 47 => Section.Chorus,
        <= 50 => Section.Post,
        <= 62 => Section.Verse2,
        <= 70 => Section.Pre,
        <= 78 => Section.Chorus,
        <= 82 => Section.Interlude,
        <= 90 => Section.Bridge,
        <= 98 => Section.Breakdown,
        <= 100 => Section.Bounce,
        101 => Section.WindUp,
        <= 109 => Section.Solo1,
        <= 117 => Section.Solo2,
        118 => Section.Coda,
        <= 134 => Section.FinalChorus,
        <= 140 => Section.Outro,
        _ => Section.FakeOut,
    };

    /// <summary>Slots of a bar from its own signature (4/4 when unset, the song signature).</summary>
    internal static int SlotsOf(MeasureModel m) => MusicTime.BarSlots(m.TimeSigNum ?? 4, m.TimeSigDenom ?? 4);

    // ------------------------------------------------------------------ riff grammar (plan 0.1)

    private static readonly Regex DurationRx = new(@"^(?<den>64|32|16|8|4|2|1)(?<dots>\.{0,2})(?<tup>t|\((?<n>\d+):(?<d>\d+)\))?:(?<rest>.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex StringFretRx = new(@"^s(?<s>\d+):(?<f>-?\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex PitchNameRx = new(@"^(?<pc>[A-G])(?<acc>#|b)?(?<oct>-?\d)$", RegexOptions.CultureInvariant);
    private static readonly Regex ShapeRx = new(@"^(?<k>O|X|P|Q)(?<n>\d*)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Writes one bar of a pitched track from the plan's riff grammar (section 0.1) and asserts that it fills the bar exactly.
    /// <para><b>Tokens</b> are space separated: <c>DUR:CONTENT[&gt;|&gt;&gt;][+flag...]</c>, e.g. <c>16:O&gt;</c>, <c>8:[s6:0 s5:0]+psu</c>,
    /// <c>4:s5:1+ph+vib</c>, <c>2:B3</c>. A token WITHOUT <c>DUR:</c> reuses the previous token's duration (so the C7 stream
    /// can be written <c>16:O&gt; O P1 O O P3 X</c>); the first token of a bar must have one.</para>
    /// <para><b>DUR</b>: <c>1 2 4 8 16 32 64</c>, then <c>.</c>/<c>..</c>, then <c>t</c> (3:2) or <c>(n:d)</c>: <c>8.</c> = 3 slots,
    /// <c>16(5:4)</c> = 0.8, <c>8(2:3)</c> = 3. Tuplets and fractional onsets get RhythmicPosition automatically (via <see cref="Put"/>).</para>
    /// <para><b>CONTENT</b>: <c>r</c> rest; <c>sN:F</c> string N (1 = top) fret F; <c>[a b c]</c> chord (one cell; each element may carry
    /// its own <c>+flags</c>); <c>O</c>/<c>On</c> = s7:n palm-muted; <c>X</c>/<c>Xn</c> = s7:n dead; <c>Pn</c> = [s7:n s6:n s5:n];
    /// <c>Qn</c> = [s6:n s4:n+2]; a pitch name (<c>A3</c>, <c>F#4</c>, <c>Bb2</c>; C4 = 60) is placed with <see cref="Key"/> (keys tracks only).</para>
    /// <para><b>Accents</b> right after the content: <c>&gt;</c> = cell.Accent 1, <c>&gt;&gt;</c> = 2.</para>
    /// <para><b>Flags</b> (on a chord they apply to every note): pm lr st tn g vib wv h ls ss sib sia sou sod psu psd pd pu tie fin fout wo wc
    /// trem8/16/32/64 ferm bd bu ad au rasg slap pop ds, plus tap (Tapping), ltap (LeftTap) and dead (Dead flag). Cell-level ones:
    /// st (Staccato), tn (Tenuto), ferm (Fermata), trem* (TremoloPickDenominator; the notes get TremoloPick), accents.
    /// <c>h</c> tags a HOPO origin; <c>ls</c>/<c>ss</c> tag a slide: the destination / slide target (the next note on the same string
    /// and voice, even in a later bar) is linked when the song is finalised. <c>tie</c> sets note.Tied (cell.IsTied when every note of the
    /// cell is tied); origins get "Tie" at finalise. <c>ds</c> = DeadSlapped + Slap + Dead.</para>
    /// <para><b>Harmonics</b>: ph (Pinch), nh (Natural), ah (Artificial), th (Tap), sh (Semi), fb (Feedback), optionally with the node:
    /// <c>ph=15</c>. Default node: nh = the written fret, fb = 12, the others = fret + 12. The sounding pitch is
    /// GuitarProImporter.HarmonicMidi (see <see cref="Harm"/>).</para>
    /// <para><b>Velocity</b>: every note gets <paramref name="baseVel"/> + <paramref name="velOffset"/>; palm-muted notes (chugs) also get
    /// the seeded ±3 jitter from <see cref="Hv"/> (Lane.Chug). Ghosts keep the base (playback scales them).</para>
    /// <para><paramref name="transposeFrets"/> is added to every written fret (and to explicit harmonic nodes); on keys tracks it
    /// transposes pitch names by that many semitones. <paramref name="swap"/> rewrites each token before parsing (it may return
    /// several tokens); <paramref name="voice"/> 0 = voice 1, 1 = voice 2 (created on demand).</para>
    /// <para>Throws InvalidOperationException naming the track and bar when a token cannot be parsed or when the durations do not add
    /// up to the bar's slot count (nothing is written in that case). Write each bar and voice once: a second Riff on the same slots
    /// merges into the existing cells.</para>
    /// </summary>
    internal static void Riff(TrackModel t, int bar, string tokens, int baseVel, int voice = 0,
                              int transposeFrets = 0, int velOffset = 0, Func<string, string>? swap = null)
    {
        var m = t.Measures[bar - 1];
        EnsureSlots(m);
        var barSlots = SlotsOf(m);
        var raw = Tokenize(tokens);
        if (swap is not null) raw = raw.SelectMany(x => Tokenize(swap(x))).ToList();
        var parsed = new List<(double Slot, RiffToken Token)>();
        double cursor = 0; RiffDuration? last = null;
        foreach (var text in raw)
        {
            var tok = ParseToken(t, bar, text, ref last);
            parsed.Add((Snap(cursor), tok));
            cursor += tok.Duration.Slots;
        }
        if (Math.Abs(cursor - barSlots) > 1e-6)
            throw new InvalidOperationException(
                $"Demo song: track '{t.Name}' bar {bar} voice {voice + 1}: the tokens add up to {cursor:0.###} slots, the bar has {barSlots}. Tokens: {tokens}");

        var vel = Math.Clamp(baseVel + velOffset, 1, MidiLimits.Max);
        var sec = SectionOf(bar);
        foreach (var (slot, tok) in parsed)
        {
            var d = tok.Duration;
            var cell = Put(m, slot, d.Den, d.Dots, d.TupN, d.TupD, voice);
            if (tok.Rest) { cell.IsRest = true; continue; }
            foreach (var spec in tok.Notes)
            {
                var flags = spec.Flags.Concat(tok.Flags).ToList();
                var note = MakeNote(t, bar, spec, flags, vel, transposeFrets, sec, slot);
                cell.Notes.Add(note);
                ApplyCellFlags(cell, flags);
            }
            if (tok.Accent > 0) cell.Accent = Math.Max(cell.Accent, tok.Accent);
            if (cell.Notes.Count > 0 && cell.Notes.All(n => n.Tied)) cell.IsTied = true;
        }
    }

    private sealed record RiffDuration(int Den, int Dots, int TupN, int TupD)
    {
        public double Slots => 16.0 / Den * (Dots == 1 ? 1.5 : Dots >= 2 ? 1.75 : 1.0) * (TupN > 0 ? TupD / (double)TupN : 1.0);
    }

    private sealed record NoteSpec(string Content, List<string> Flags);

    private sealed record RiffToken(RiffDuration Duration, bool Rest, List<NoteSpec> Notes, int Accent, List<string> Flags);

    /// <summary>Splits on whitespace outside [...].</summary>
    private static List<string> Tokenize(string text)
    {
        var list = new List<string>(); var sb = new StringBuilder(); var depth = 0;
        foreach (var ch in text)
        {
            if (ch == '[') depth++;
            if (ch == ']') depth--;
            if (char.IsWhiteSpace(ch) && depth == 0) { if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); } continue; }
            sb.Append(ch);
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return list;
    }

    private static RiffToken ParseToken(TrackModel t, int bar, string text, ref RiffDuration? last)
    {
        Exception Bad(string why) => new InvalidOperationException($"Demo song: track '{t.Name}' bar {bar}: token '{text}': {why}");
        string body;
        var dm = DurationRx.Match(text);
        if (dm.Success)
        {
            var den = int.Parse(dm.Groups["den"].Value, CultureInfo.InvariantCulture);
            var dots = dm.Groups["dots"].Value.Length;
            int tn = 0, td = 0;
            var tup = dm.Groups["tup"].Value;
            if (tup == "t") { tn = 3; td = 2; }
            else if (tup.Length > 0) { tn = int.Parse(dm.Groups["n"].Value, CultureInfo.InvariantCulture); td = int.Parse(dm.Groups["d"].Value, CultureInfo.InvariantCulture); }
            if (tup.Length > 0 && (tn < 1 || td < 1 || tn > 64 || td > 64)) throw Bad("tuplet out of range");
            last = new RiffDuration(den, dots, tn, td);
            body = dm.Groups["rest"].Value;
        }
        else
        {
            if (last is null) throw Bad("the first token of a bar needs a duration (DUR:CONTENT)");
            body = text;
        }

        // content | accents | +flags (split at depth 0)
        var depth = 0; var cut = body.Length;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '[') depth++;
            else if (body[i] == ']') depth--;
            else if (depth == 0 && (body[i] == '>' || body[i] == '+')) { cut = i; break; }
        }
        var content = body[..cut];
        var tail = body[cut..];
        var accent = 0;
        while (tail.StartsWith('>')) { accent++; tail = tail[1..]; }
        if (accent > 2) throw Bad("at most two accent marks");
        var flags = tail.Split('+', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var f in flags) if (!IsKnownFlag(f)) throw Bad($"unknown flag '{f}'");
        if (content.Length == 0) throw Bad("no content");

        if (content == "r") return new RiffToken(last, true, new(), accent, flags);
        var notes = new List<NoteSpec>();
        if (content.StartsWith('['))
        {
            if (!content.EndsWith(']')) throw Bad("unclosed chord");
            foreach (var element in Tokenize(content[1..^1]))
            {
                var parts = element.Split('+', StringSplitOptions.RemoveEmptyEntries);
                foreach (var f in parts.Skip(1)) if (!IsKnownFlag(f)) throw Bad($"unknown flag '{f}'");
                notes.AddRange(Expand(parts[0], parts.Skip(1).ToList(), Bad));
            }
        }
        else notes.AddRange(Expand(content, new List<string>(), Bad));
        if (notes.Count == 0) throw Bad("empty chord");
        return new RiffToken(last, false, notes, accent, flags);
    }

    /// <summary>Expands the shorthand shapes into plain string:fret notes (or keeps pitch names).</summary>
    private static IEnumerable<NoteSpec> Expand(string content, List<string> flags, Func<string, Exception> bad)
    {
        if (StringFretRx.IsMatch(content) || PitchNameRx.IsMatch(content)) { yield return new NoteSpec(content, flags); yield break; }
        var sm = ShapeRx.Match(content);
        if (!sm.Success) throw bad($"cannot read '{content}'");
        var n = sm.Groups["n"].Value.Length == 0 ? 0 : int.Parse(sm.Groups["n"].Value, CultureInfo.InvariantCulture);
        switch (sm.Groups["k"].Value)
        {
            case "O": yield return new NoteSpec($"s7:{n}", flags.Append("pm").ToList()); break;
            case "X": yield return new NoteSpec($"s7:{n}", flags.Append("dead").ToList()); break;
            case "P":
                yield return new NoteSpec($"s7:{n}", flags.ToList());
                yield return new NoteSpec($"s6:{n}", flags.ToList());
                yield return new NoteSpec($"s5:{n}", flags.ToList());
                break;
            default:
                yield return new NoteSpec($"s6:{n}", flags.ToList());
                yield return new NoteSpec($"s4:{n + 2}", flags.ToList());
                break;
        }
    }

    private static readonly Dictionary<string, string> NoteFlagTechniques = new(StringComparer.Ordinal)
    {
        ["pm"] = "PalmMute", ["lr"] = "LetRing", ["vib"] = "Vibrato", ["wv"] = "WideVibrato",
        ["ls"] = "LegatoSlide", ["ss"] = "ShiftSlide", ["sib"] = "SlideInBelow", ["sia"] = "SlideInAbove",
        ["sou"] = "SlideOutUp", ["sod"] = "SlideOutDown", ["psu"] = "PickSlideUp", ["psd"] = "PickSlideDown",
        ["pd"] = "PickDown", ["pu"] = "PickUp", ["fin"] = "FadeIn", ["fout"] = "FadeOut", ["wo"] = "WahOpen", ["wc"] = "WahClose",
        ["bd"] = "BrushDown", ["bu"] = "BrushUp", ["ad"] = "ArpeggioDown", ["au"] = "ArpeggioUp", ["rasg"] = "Rasgueado",
        ["slap"] = "Slap", ["pop"] = "Pop", ["tap"] = "Tapping", ["ltap"] = "LeftTap",
    };

    private static readonly Dictionary<string, string> HarmonicFlags = new(StringComparer.Ordinal)
    {
        ["ph"] = "Pinch", ["nh"] = "Natural", ["ah"] = "Artificial", ["th"] = "Tap", ["sh"] = "Semi", ["fb"] = "Feedback",
    };

    private static readonly HashSet<string> OtherFlags = new(StringComparer.Ordinal)
    {
        "st", "tn", "g", "h", "tie", "ferm", "ds", "dead", "trem8", "trem16", "trem32", "trem64",
    };

    private static bool IsKnownFlag(string flag)
    {
        var name = flag.Split('=')[0];
        return NoteFlagTechniques.ContainsKey(name) || OtherFlags.Contains(flag) ||
               HarmonicFlags.ContainsKey(name) && (!flag.Contains('=') || double.TryParse(flag[(name.Length + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out _));
    }

    private static TabNote MakeNote(TrackModel t, int bar, NoteSpec spec, List<string> flags, int vel, int transpose, Section sec, double slot)
    {
        TabNote note;
        var pitch = PitchNameRx.Match(spec.Content);
        if (pitch.Success)
        {
            if (t.Kind != TrackKind.Keys)
                throw new InvalidOperationException($"Demo song: track '{t.Name}' bar {bar}: pitch names ('{spec.Content}') are for keys tracks only.");
            note = Key(t, ParsePitch(pitch) + transpose, vel);
        }
        else
        {
            var sf = StringFretRx.Match(spec.Content);
            var s = int.Parse(sf.Groups["s"].Value, CultureInfo.InvariantCulture);
            var fret = int.Parse(sf.Groups["f"].Value, CultureInfo.InvariantCulture) + transpose;
            var harmonic = flags.Select(f => f.Split('=')).FirstOrDefault(p => HarmonicFlags.ContainsKey(p[0]));
            if (harmonic is not null)
            {
                var kind = HarmonicFlags[harmonic[0]];
                var node = harmonic.Length > 1
                    ? double.Parse(harmonic[1], CultureInfo.InvariantCulture) + transpose
                    : kind == "Natural" ? fret : kind == "Feedback" ? 12 : fret + 12;
                note = Harm(t, s, fret, kind, node, vel);
            }
            else note = Fret(t, s, fret, vel);
        }
        foreach (var f in flags)
        {
            if (NoteFlagTechniques.TryGetValue(f, out var tech)) note.Techniques.Add(tech);
            switch (f)
            {
                case "g": note.Ghost = true; break;
                case "dead": note.Dead = true; break;
                case "tie": note.Tied = true; break;
                case "h": note.Techniques.Add("HOPO"); note.Techniques.Add("HOPOOrigin"); break;
                case "ds": note.Techniques.Add("DeadSlapped"); note.Techniques.Add("Slap"); note.Dead = true; break;
                case "trem8" or "trem16" or "trem32" or "trem64": note.Techniques.Add("TremoloPick"); break;
            }
        }
        if (TechniqueNames.HasPalmMute(note.Techniques))
            note.Velocity = Hv(t.Name, bar, slot, note.Fret, note.Velocity, 3, sec, Lane.Chug);
        return note;
    }

    private static void ApplyCellFlags(TabCell cell, List<string> flags)
    {
        foreach (var f in flags)
            switch (f)
            {
                case "st": cell.Staccato = true; break;
                case "tn": cell.Tenuto = true; break;
                case "ferm": cell.Fermata = true; break;
                case "trem8": cell.TremoloPickDenominator = 8; break;
                case "trem16": cell.TremoloPickDenominator = 16; break;
                case "trem32": cell.TremoloPickDenominator = 32; break;
                case "trem64": cell.TremoloPickDenominator = 64; break;
            }
    }

    private static int ParsePitch(Match m)
    {
        var pc = "C D EF G A B".IndexOf(m.Groups["pc"].Value[0]);
        var acc = m.Groups["acc"].Value == "#" ? 1 : m.Groups["acc"].Value == "b" ? -1 : 0;
        return 12 * (int.Parse(m.Groups["oct"].Value, CultureInfo.InvariantCulture) + 1) + pc + acc;
    }

    /// <summary>MIDI pitch of a name such as "A3", "F#4" or "Bb2" (C4 = 60). For keys parts and checks.</summary>
    internal static int PitchOf(string name)
    {
        var m = PitchNameRx.Match(name);
        if (!m.Success) throw new ArgumentException($"Demo song: '{name}' is not a pitch name.", nameof(name));
        return ParsePitch(m);
    }

    // ------------------------------------------------------------------ finalise helpers (owned by the skeleton)

    /// <summary>Unfilled cells handed out by Put become rests; cells with notes are never rests.</summary>
    private static void NormaliseCells(SongProject song)
    {
        foreach (var t in song.Tracks)
            foreach (var m in t.Measures)
                foreach (var c in m.Cells.Concat(m.Voice2Cells))
                {
                    if (c.Notes.Count > 0) c.IsRest = false;
                    else if (Claimed.TryGetValue(c, out _) && !c.HasAnnotation) c.IsRest = true;
                }
    }

    /// <summary>Same-string HOPO destinations and slide targets: the next non-grace note on the origin's string and voice (any later bar).</summary>
    private static void LinkLegato(TrackModel t)
    {
        foreach (var voice in new[] { 0, 1 })
        {
            var hopo = new Dictionary<int, TabNote>();
            var slide = new Dictionary<int, TabNote>();
            foreach (var m in t.Measures)
            {
                var cells = voice == 0 ? m.Cells : m.Voice2Cells;
                foreach (var (index, _) in Beats(cells))
                    foreach (var note in cells[index].Notes.Where(x => !x.IsGraceNote))
                    {
                        if (hopo.Remove(note.StringIndex, out var origin) && !note.Tied)
                        { note.Techniques.Add("HOPO"); note.Techniques.Add("HOPODestination"); }
                        if (slide.Remove(note.StringIndex, out var from) && from.SlideTargetMidi == 0) from.SlideTargetMidi = note.MidiValue;
                        if (note.Techniques.Contains("HOPOOrigin") && !PairedOrigin.TryGetValue(note, out _)) hopo[note.StringIndex] = note;
                        if ((note.Techniques.Contains("LegatoSlide") || note.Techniques.Contains("ShiftSlide")) && note.SlideTargetMidi == 0) slide[note.StringIndex] = note;
                    }
            }
        }
    }

    /// <summary>
    /// Attaches a mix-table change to the beat at (bar, slot) of voice 1: the beat starting there; else the beat sounding at that
    /// point (the last one starting before it); else the first one after; an empty bar gets rests (split at the slot) to carry it.
    /// Returns a note when the change had to move. Mixes on an existing cell merge (the new values win).
    /// </summary>
    private static string? AttachMix(TrackModel t, int bar, double slot, MixChange mix)
    {
        var m = t.Measures[bar - 1];
        var beats = Beats(m.Cells);
        string? moved = null;
        TabCell target;
        var at = beats.Where(b => Math.Abs(b.Onset - slot) < 1e-6).Select(b => m.Cells[b.Index]).FirstOrDefault();
        if (at is not null) target = at;
        else if (beats.Count == 0)
        {
            var n = SlotsOf(m);
            foreach (var (from, to) in slot > 0 ? new[] { (0.0, slot), (slot, (double)n) } : new[] { (0.0, (double)n) })
            {
                var pos = from;
                while (to - pos > 0.2)
                {
                    var v = NoteValues.First(x => x.Slots <= to - pos + 1e-9);
                    var r = Put(m, pos, v.Den, v.Dots);
                    r.IsRest = true;
                    pos += v.Slots;
                }
            }
            target = m.Cells[Beats(m.Cells).First(b => Math.Abs(b.Onset - slot) < 1e-6).Index];
        }
        else
        {
            var before = beats.LastOrDefault(b => b.Onset < slot);
            var (index, onset) = before != default ? before : beats.First();
            target = m.Cells[index];
            moved = $"{t.Name} bar {bar} slot {slot:0.##}: no beat starts there, the mix sits on the beat at slot {onset:0.##}";
        }
        if (target.Mix is null) target.Mix = mix.Clone();
        else
        {
            var x = target.Mix;
            x.Program = mix.Program ?? x.Program; x.Volume = mix.Volume ?? x.Volume; x.Pan = mix.Pan ?? x.Pan; x.Chorus = mix.Chorus ?? x.Chorus;
            x.Reverb = mix.Reverb ?? x.Reverb; x.Phaser = mix.Phaser ?? x.Phaser; x.Tremolo = mix.Tremolo ?? x.Tremolo; x.Tempo = mix.Tempo ?? x.Tempo;
            x.TransitionBeats = Math.Max(x.TransitionBeats, mix.TransitionBeats); x.AllTracks |= mix.AllTracks;
        }
        return moved;
    }
}
