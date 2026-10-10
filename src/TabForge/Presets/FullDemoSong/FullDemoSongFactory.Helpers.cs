using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presets;

// Owns: the shared helpers of the full demo song: the section and hit-kind enums, the bar, slot and string conventions, note helpers,
//   and the seeded humanising of velocities (Hv).
// Does not own: the song's content, which the owner files FullDemoSongFactory.*.cs write, and the song model (Models).
// Tests: TestFullDemoSong.
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

        var vel = Math.Clamp(baseVel + velOffset, 1, 127);
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

    // ------------------------------------------------------------------ cells and notes

    /// <summary>Cells handed out by Put that the caller may not have filled yet: they count as beats while building.</summary>
    private static readonly ConditionalWeakTable<TabCell, object> Claimed = new();
    /// <summary>Drum cells whose written value is fixed (Roll and tuplet hits): the drum re-duration keeps them.</summary>
    private static readonly ConditionalWeakTable<TabCell, object> FixedValue = new();
    /// <summary>Rests the drum re-duration added (removed and recomputed on the next pass).</summary>
    private static readonly ConditionalWeakTable<TabCell, object> AutoRest = new();
    /// <summary>Drum cells cut to a 16th ('c' choke).</summary>
    private static readonly ConditionalWeakTable<TabCell, object> Choked = new();
    /// <summary>HOPO origins already paired by Hopo(): finalise does not link them again.</summary>
    private static readonly ConditionalWeakTable<TabNote, object> PairedOrigin = new();
    private static readonly object Mark = new();

    private static bool IsBeat(TabCell c) => c.Notes.Count > 0 || c.IsRest || c.HasAnnotation || Claimed.TryGetValue(c, out _);

    /// <summary>Beat cells of a voice with their onsets (the MusicTime rule: RhythmicPosition ?? max(index, consumed)).</summary>
    internal static List<(int Index, double Onset)> Beats(List<TabCell> cells)
    {
        var list = new List<(int, double)>();
        double consumed = 0;
        for (var i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (!IsBeat(c)) { if (consumed <= i) consumed = i + 1; continue; }
            var start = c.RhythmicPosition ?? Math.Max(i, consumed);
            list.Add((i, start));
            consumed = Math.Max(consumed, start + MusicTime.CellSlots(c));
        }
        return list;
    }

    private static double Snap(double x) => Math.Abs(x - Math.Round(x)) < 1e-7 ? Math.Round(x) : Math.Round(x, 9);

    private static bool IsWhole(double x) => Math.Abs(x - Math.Round(x)) < 1e-9;

    /// <summary>
    /// The cell of a new beat starting at <paramref name="slot"/> in voice <paramref name="voice"/> (0 or 1; voice 2 is created on
    /// demand), with its written value set (DurationDenominator, Dots, tuplet ratio; 3:2 also sets IsTriplet).
    /// <para>Cells stay in time order: a beat after the last one takes the cell at the slot index (or the next free index);
    /// RhythmicPosition is set when the slot is fractional or differs from the cell index (the same rule as SelfTestGpFixture.Put).
    /// A beat placed before existing beats re-lays the bar out (every beat keeps its onset). The bar grows when a tuplet run needs
    /// more cells than it has.</para>
    /// <para>When a beat already starts at <paramref name="slot"/> in that voice, that cell is returned and its value overwritten.</para>
    /// <para>The returned cell is empty: add notes, or set IsRest (unfilled cells become rests at finalise).</para>
    /// </summary>
    internal static TabCell Put(MeasureModel m, double slot, int den, int dots = 0, int tupN = 0, int tupD = 0, int voice = 0)
    {
        var cell = Place(m, slot, voice, out _);
        SetValue(cell, den, dots, tupN, tupD);
        return cell;
    }

    private static void SetValue(TabCell c, int den, int dots, int tupN, int tupD)
    {
        c.DurationDenominator = den; c.Dots = dots;
        c.TupletNumerator = tupN > 0 && tupD > 0 ? tupN : 0;
        c.TupletDenominator = tupN > 0 && tupD > 0 ? tupD : 0;
        c.IsTriplet = tupN == 3 && tupD == 2;
    }

    /// <summary>Finds or creates the beat cell at an onset (see <see cref="Put"/>); <paramref name="existed"/> tells which.</summary>
    private static TabCell Place(MeasureModel m, double slot, int voice, out bool existed)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        slot = Snap(slot);
        var cells = voice <= 0 ? m.Cells : m.CellsForVoice(1, create: true);
        var beats = Beats(cells);
        foreach (var (index, onset) in beats)
            if (Math.Abs(onset - slot) < 1e-6) { existed = true; return cells[index]; }
        existed = false;
        var fresh = new TabCell();
        Claimed.AddOrUpdate(fresh, Mark);
        if (beats.Count == 0 || beats[^1].Onset < slot)
        {
            var idx = Math.Max((int)Math.Floor(slot + 1e-9), beats.Count == 0 ? 0 : beats[^1].Index + 1);
            var lastEnd = beats.Count == 0 ? 0 : beats[^1].Onset + MusicTime.CellSlots(cells[beats[^1].Index]);
            while (cells.Count <= idx) cells.Add(new TabCell());
            // an unused cell may carry data the caller set by index (a mix or beam override): keep that object
            var target = cells[idx];
            if (!IsBeat(target) && target.Notes.Count == 0) { Claimed.AddOrUpdate(target, Mark); fresh = target; }
            else cells[idx] = fresh;
            fresh.RhythmicPosition = IsWhole(slot) && idx == (int)Math.Round(slot) && lastEnd <= slot + 1e-9 ? null : slot;
            return fresh;
        }
        var order = beats.Select(b => (b.Onset, Cell: cells[b.Index])).ToList();
        order.Add((slot, fresh));
        Relayout(cells, order.OrderBy(o => o.Onset).ToList());
        return fresh;
    }

    /// <summary>
    /// Rebuilds a voice's cell list from beats in time order: each beat at max(previous index + 1, floor(onset)). RhythmicPosition
    /// stays null only when the index is the onset and the previous beat has ended by then (else the onset rule would move it).
    /// </summary>
    private static void Relayout(List<TabCell> cells, List<(double Onset, TabCell Cell)> order)
    {
        var count = cells.Count;
        cells.Clear();
        var last = -1;
        var prevEnd = 0.0;
        foreach (var (onset, cell) in order)
        {
            var idx = Math.Max(last + 1, (int)Math.Floor(onset + 1e-9));
            while (cells.Count < idx) cells.Add(new TabCell());
            cells.Add(cell);
            cell.RhythmicPosition = IsWhole(onset) && idx == (int)Math.Round(onset) && prevEnd <= onset + 1e-9 ? null : Snap(onset);
            prevEnd = Math.Max(prevEnd, onset + MusicTime.CellSlots(cell));
            last = idx;
        }
        while (cells.Count < count) cells.Add(new TabCell());
    }

    /// <summary>A fretted note: string <paramref name="s"/> (1 = top), fret relative to the capo, MidiValue = t.PitchOf, the given velocity and technique names.</summary>
    internal static TabNote Fret(TrackModel t, int s, int fret, int vel, params string[] tech)
    {
        if (s < 1 || s > t.StringTunings.Count)
            throw new ArgumentOutOfRangeException(nameof(s), $"Demo song: track '{t.Name}' has {t.StringTunings.Count} strings, s{s} does not exist.");
        if (fret < 0 || fret > 127) throw new ArgumentOutOfRangeException(nameof(fret), $"Demo song: track '{t.Name}': fret {fret}.");
        var n = new TabNote { StringIndex = s - 1, Fret = fret, MidiValue = t.PitchOf(s - 1, fret), Velocity = Math.Clamp(vel, 1, 127) };
        foreach (var x in tech) if (!string.IsNullOrEmpty(x)) n.Techniques.Add(x);
        return n;
    }

    /// <summary>
    /// A harmonic on string <paramref name="s"/> (1 = top) at <paramref name="fret"/>: kind is Natural, Artificial, Pinch, Tap, Semi or Feedback.
    /// HarmonicFret = <paramref name="harmonicFret"/>; MidiValue = GuitarProImporter.HarmonicMidi(kind, open string + capo, fret, harmonicFret),
    /// i.e. Natural sounds the string's harmonic at the written fret; Artificial/Pinch sound fret + the node read as an interval
    /// (5/7/19/24 as the fifth/double-octave nodes); Tap = node + 12 above the open string; Semi/Feedback = fret + ArtificialInterval(node).
    /// Adds the matching technique name (Harmonic, ArtificialHarmonic, PinchHarmonic, TapHarmonic, SemiHarmonic, FeedbackHarmonic).
    /// </summary>
    internal static TabNote Harm(TrackModel t, int s, int fret, string kind, double harmonicFret, int vel)
    {
        var tech = kind switch
        {
            "Natural" => "Harmonic", "Artificial" => "ArtificialHarmonic", "Pinch" => "PinchHarmonic",
            "Tap" => "TapHarmonic", "Semi" => "SemiHarmonic", "Feedback" => "FeedbackHarmonic",
            _ => throw new ArgumentException($"Demo song: unknown harmonic kind '{kind}'.", nameof(kind)),
        };
        var n = Fret(t, s, fret, vel, tech);
        n.HarmonicFret = harmonicFret;
        n.MidiValue = GuitarProImporter.HarmonicMidi(kind, t.StringTunings[s - 1] + Math.Max(0, t.Capo), fret, harmonicFret);
        return n;
    }

    /// <summary>
    /// Bend on a note: adds "Bend", sets BendTypeName = <paramref name="type"/> (Bend, Release, BendRelease, Prebend, PrebendBend,
    /// PrebendRelease, Hold, Custom; BendStyleName stays default) and BendPoints from (offset 0..60, value in quarter-tones; 4 = one tone).
    /// </summary>
    internal static void Bend(TabNote n, string type, params (int off, int val)[] pts)
    {
        n.Techniques.Add("Bend");
        n.BendTypeName = type;
        n.BendPoints = pts.Select(p => new BendPointModel { Offset = p.off, Value = p.val }).ToList();
    }

    /// <summary>
    /// Whammy: adds the tag <paramref name="subtype"/> to the note (TremBar, TremBarWide, TremBarDive, TremBarDip, TremBarHold,
    /// TremBarPredive, TremBarPrediveDive or TremBarCustom) and sets the BEAT curve cell.WhammyPoints (offset 0..60, quarter-tones;
    /// -24 = -12 semitones). The curve belongs to the whole cell, so use one whammy per cell.
    /// </summary>
    internal static void Whammy(TabCell c, TabNote n, string subtype, params (int off, int val)[] pts)
    {
        if (!subtype.StartsWith("TremBar", StringComparison.Ordinal)) throw new ArgumentException($"Demo song: '{subtype}' is not a whammy sub-type.", nameof(subtype));
        n.Techniques.Add(subtype);
        c.WhammyPoints = pts.Select(p => new BendPointModel { Offset = p.off, Value = p.val }).ToList();
    }

    /// <summary>
    /// Adds a grace note INTO cell <paramref name="c"/> (first in the cell, before the principal notes): string s (1 = top) and fret,
    /// IsGraceNote, GraceBeforeBeat = <paramref name="before"/>, GraceDurationSlots = <paramref name="slots"/>, the technique
    /// <paramref name="tag"/> ("GraceBefore", "GraceOnBeat" or "GraceBend"; add a slide or Bend() on the returned note yourself via
    /// c.Notes[0]). On a drum track <paramref name="s"/> is ignored and <paramref name="fret"/> is the GM note (a flam: 38).
    /// </summary>
    internal static void Grace(TabCell c, TrackModel t, int s, int fret, bool before, string tag, double slots = 0.5, int vel = 62)
    {
        var n = t.Kind == TrackKind.Drums ? Drum(fret, vel) : Fret(t, s, fret, vel);
        n.IsGraceNote = true;
        n.GraceBeforeBeat = before;
        n.GraceDurationSlots = slots;
        if (!string.IsNullOrEmpty(tag)) n.Techniques.Add(tag);
        c.Notes.Insert(0, n);
    }

    /// <summary>A hammer-on/pull-off pair (the importer's convention the renderer draws a slur for): both get "HOPO", the origin
    /// "HOPOOrigin" and the destination "HOPODestination". Use it for pairs on different strings; the riff flag <c>h</c> links same-string pairs.</summary>
    internal static void Hopo(TabNote origin, TabNote dest)
    {
        origin.Techniques.Add("HOPO"); origin.Techniques.Add("HOPOOrigin");
        dest.Techniques.Add("HOPO"); dest.Techniques.Add("HOPODestination");
        PairedOrigin.AddOrUpdate(origin, Mark);
    }

    /// <summary>A keys note of sounding pitch <paramref name="midi"/>: string = the highest octave "string" at or below the pitch
    /// (the importer's PlacePitch rule on {96,84,72,60,48,36,24}), fret = the semitones above it. Throws below the lowest string.</summary>
    internal static TabNote Key(TrackModel keys, int midi, int vel)
    {
        for (var s = 0; s < keys.StringTunings.Count; s++)
            if (midi >= keys.StringTunings[s])
                return new TabNote { StringIndex = s, Fret = midi - keys.StringTunings[s], MidiValue = midi, Velocity = Math.Clamp(vel, 1, 127) };
        throw new ArgumentOutOfRangeException(nameof(midi), $"Demo song: track '{keys.Name}': pitch {midi} is below its lowest string.");
    }

    /// <summary>
    /// Chord-aware keys placement. TabForge itself has no chord rule (the importer's PlacePitch puts every note on the highest octave
    /// "string" at or below its pitch, so a chord inside one octave lands on ONE string and its numbers overprint). This gives each of
    /// the simultaneous pitches its own string: highest pitch first, each takes its natural string (as <see cref="Key"/>) or, when that
    /// or a higher string is already used, the next free lower string (fret = pitch - tuning, so MidiValue is unchanged and the
    /// top-to-bottom order of the rows follows the pitch). <paramref name="midis"/> may be in any order; the result is in the same order.
    /// Returns (StringIndex, Fret) per pitch. Throws when the chord runs out of strings.
    /// </summary>
    internal static (int StringIndex, int Fret)[] Keys(TrackModel keys, IReadOnlyList<int> midis)
    {
        var result = new (int, int)[midis.Count];
        var last = -1;
        foreach (var i in Enumerable.Range(0, midis.Count).OrderByDescending(i => midis[i]).ThenBy(i => i))
        {
            var s = 0;
            while (s < keys.StringTunings.Count && midis[i] < keys.StringTunings[s]) s++;
            if (s >= keys.StringTunings.Count)
                throw new ArgumentOutOfRangeException(nameof(midis), $"Demo song: track '{keys.Name}': pitch {midis[i]} is below its lowest string.");
            s = Math.Max(s, last + 1);
            if (s >= keys.StringTunings.Count)
                throw new InvalidOperationException($"Demo song: track '{keys.Name}': the chord [{string.Join(' ', midis)}] has more notes than free strings.");
            result[i] = (s, midis[i] - keys.StringTunings[s]);
            last = s;
        }
        return result;
    }

    /// <summary>
    /// Finalise step for keys tracks: every beat onset of a bar (both voices together) gets its notes spread over distinct strings with
    /// <see cref="Keys"/>; pitches (MidiValue) never change and single notes keep the <see cref="Key"/> placement.
    /// </summary>
    private static void SpreadKeys(TrackModel t)
    {
        if (t.Kind != TrackKind.Keys) return;
        static void Place(TrackModel t, List<TabNote> notes)
        {
            var placed = Keys(t, notes.Select(n => n.MidiValue).ToList());
            for (var i = 0; i < notes.Count; i++) (notes[i].StringIndex, notes[i].Fret) = placed[i];
        }
        foreach (var m in t.Measures)
        {
            var groups = new SortedDictionary<long, List<List<TabNote>>>();   // onset -> the note lists of each voice's cell
            foreach (var cells in new[] { m.Cells, m.Voice2Cells })
                foreach (var (index, onset) in Beats(cells))
                {
                    var notes = cells[index].Notes.Where(x => !x.IsGraceNote).ToList();
                    if (notes.Count == 0) continue;
                    var key = (long)Math.Round(onset * 1_000_000);
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<List<TabNote>>();
                    list.Add(notes);
                }
            foreach (var voices in groups.Values)
            {
                var all = voices.SelectMany(v => v).ToList();
                if (all.Count < 2) continue;
                // Both hands at once when the 7 octave strings have room (a pitch can only use strings at or below it); else each hand alone.
                try { Place(t, all); }
                catch (InvalidOperationException) { foreach (var v in voices) if (v.Count > 1) Place(t, v); } // Not logged: demo song factory: fallback placement, no failure path
            }
        }
    }

    // ------------------------------------------------------------------ drums (plan 3.9)

    private sealed record DrumChar(int Midi, int Centre, Lane Lane, int Jitter, bool Accent2 = false, bool Ghost = false, bool Flam = false, bool Ramp = false, bool Choke = false);

    private static readonly Dictionary<string, Dictionary<char, DrumChar>> Lanes = new(StringComparer.Ordinal)
    {
        ["K"] = new() { ['X'] = new(36, 120, Lane.Kick, 4), ['x'] = new(36, 110, Lane.Kick, 4), ['d'] = new(36, 100, Lane.KickRun, 4), ['b'] = new(36, 96, Lane.Kick, 4), ['p'] = new(36, 49, Lane.Kick, 4) },
        ["S"] = new()
        {
            ['X'] = new(38, 127, Lane.Snare, 4, Accent2: true), ['x'] = new(38, 122, Lane.Snare, 4), ['f'] = new(38, 122, Lane.Snare, 4, Flam: true),
            ['g'] = new(38, 45, Lane.Ghost, 8, Ghost: true), ['b'] = new(38, 104, Lane.Snare, 4),
        },
        ["SS"] = new() { ['x'] = new(37, 80, Lane.SideStick, 6) },
        ["CL"] = new() { ['x'] = new(39, 96, Lane.Clap, 4) },
        ["H"] = new() { ['X'] = new(42, 90, Lane.HiHat, 6), ['x'] = new(42, 76, Lane.HiHat, 6), ['y'] = new(42, 66, Lane.HiHat, 6), ['o'] = new(46, 102, Lane.HiHat, 6), ['p'] = new(44, 70, Lane.HiHat, 6) },
        ["R"] = new() { ['X'] = new(51, 94, Lane.Ride, 6), ['x'] = new(51, 80, Lane.Ride, 6), ['b'] = new(53, 110, Lane.Ride, 6) },
        ["C"] = new() { ['X'] = new(49, 120, Lane.Crash, 4), ['x'] = new(49, 108, Lane.Crash, 4), ['2'] = new(57, 120, Lane.Crash, 4), ['c'] = new(49, 120, Lane.Crash, 4, Choke: true) },
        ["CH"] = new() { ['X'] = new(52, 118, Lane.China, 4), ['x'] = new(52, 118, Lane.China, 4) },
        ["SP"] = new() { ['X'] = new(55, 104, Lane.Splash, 4), ['x'] = new(55, 104, Lane.Splash, 4) },
        ["T1"] = new() { ['X'] = new(50, 122, Lane.Tom, 8), ['x'] = new(50, 110, Lane.Tom, 8) },
        ["T2"] = new() { ['X'] = new(48, 122, Lane.Tom, 8), ['x'] = new(48, 110, Lane.Tom, 8) },
        ["T3"] = new() { ['X'] = new(45, 122, Lane.Tom, 8), ['x'] = new(45, 110, Lane.Tom, 8) },
        ["T4"] = new() { ['X'] = new(43, 122, Lane.Tom, 8), ['x'] = new(43, 110, Lane.Tom, 8) },
    };

    /// <summary>GM note of a lane's plain hit (used for 'r' ramp hits): K 36, S 38, SS 37, CL 39, H 42, R 51, C 49, CH 52, SP 55, T1..T4 50/48/45/43.</summary>
    private static int LaneMidi(string lane) => lane switch
    {
        "K" => 36, "S" => 38, "SS" => 37, "CL" => 39, "H" => 42, "R" => 51, "C" => 49, "CH" => 52, "SP" => 55,
        "T1" => 50, "T2" => 48, "T3" => 45, "T4" => 43, _ => throw new ArgumentException($"Demo song: unknown drum lane '{lane}'."),
    };

    /// <summary>
    /// Writes drum lanes into one bar (plan 3.9 lane key). Each pattern has one character per slot and must be exactly the bar's slot
    /// count long (16 for 4/4, 14 for 7/8, 12 for 3/4, 8 for 2/4), else it throws naming the bar and lane. '.' = no hit.
    /// <para>Lanes and characters (centre velocity before humanisation): K 36 (X 120, x 110, d 100 double-bass, b 96, p 49);
    /// S 38 (X 127 + cell.Accent 2, x 122, f flam = a 0.5-slot grace 38 before the beat (55..70) + main 122, g 45 ghost, b 104);
    /// SS 37 (x 80); CL 39 (x 96); H (X 90, x 76, y 66 closed 42; o 102 open 46; p 70 pedal 44); R (X 94, x 80 bow 51; b 110 bell 53);
    /// C (X 120, x 108 crash 49; 2 120 crash 57; c 120 choke: the cell is cut to a 16th, rests after); CH 52 (X/x 118); SP 55 (x/X 104);
    /// T1..T4 50/48/45/43 (X 122, x 110). 'r' in ANY lane is a fill-ramp hit: all 'r' hits of this call, in time order (lanes together),
    /// ramp 90 → 120 (±3 jitter).</para>
    /// <para>Velocities go through <see cref="Hv"/> with <paramref name="sec"/> (metric accents on H, R and 'd' kicks; alternation on runs
    /// of adjacent H hits and 'd' kicks). Hits at the same slot share one cell (merged with hits already in the bar; an existing note of the
    /// same GM number is replaced). Afterwards every non-roll cell lasts until the next onset in the bar (unrepresentable gaps are completed
    /// with rests), so Grid, Fill and Roll can be combined on one bar in any order. Voice 1 only.</para>
    /// </summary>
    internal static void Grid(TrackModel drums, int bar, Section sec, params (string lane, string pattern)[] lanes)
    {
        var m = drums.Measures[bar - 1];
        EnsureSlots(m);
        var n = SlotsOf(m);
        var hits = new List<(int Slot, int Order, string Lane, char Ch, int Run)>();
        for (var li = 0; li < lanes.Length; li++)
        {
            var (lane, pattern) = lanes[li];
            if (!Lanes.ContainsKey(lane)) throw new ArgumentException($"Demo song: drums bar {bar}: unknown lane '{lane}'.");
            if (pattern.Length != n)
                throw new InvalidOperationException($"Demo song: drums bar {bar} lane {lane}: pattern '{pattern}' has {pattern.Length} slots, the bar has {n}.");
            for (var i = 0; i < n; i++)
            {
                var ch = pattern[i];
                if (ch == '.') continue;
                if (ch != 'r' && !Lanes[lane].ContainsKey(ch)) throw new ArgumentException($"Demo song: drums bar {bar} lane {lane}: unknown hit '{ch}'.");
                var run = -1;
                if ((lane == "H" && ch != 'r') || (lane == "K" && ch == 'd'))
                {
                    var start = i;
                    while (start > 0 && pattern[start - 1] != '.' && (lane == "H" || pattern[start - 1] == 'd')) start--;
                    var end = i;
                    while (end < n - 1 && pattern[end + 1] != '.' && (lane == "H" || pattern[end + 1] == 'd')) end++;
                    if (end > start) run = i - start;
                }
                hits.Add((i, li, lane, ch, run));
            }
        }
        var ramp = hits.Where(h => h.Ch == 'r').OrderBy(h => h.Slot).ThenBy(h => h.Order).ToList();
        foreach (var h in hits.OrderBy(h => h.Slot).ThenBy(h => h.Order))
        {
            if (h.Ch == 'r')
            {
                var k = ramp.IndexOf(h);
                var centre = ramp.Count == 1 ? 120 : (int)Math.Round(90 + 30.0 * k / (ramp.Count - 1));
                var midi = LaneMidi(h.Lane);
                AddDrumHit(drums, m, bar, h.Slot, midi, Hv(drums.Name, bar, h.Slot, midi, centre, 3, sec, Lane.FillRamp), ghost: false, flam: false, sec);
                continue;
            }
            var dc = Lanes[h.Lane][h.Ch];
            var vel = Hv(drums.Name, bar, h.Slot, dc.Midi, dc.Centre, dc.Jitter, sec, dc.Lane, h.Run);
            var cell = AddDrumHit(drums, m, bar, h.Slot, dc.Midi, vel, dc.Ghost, dc.Flam, sec);
            if (dc.Accent2) cell.Accent = 2;
            if (dc.Choke) Choked.AddOrUpdate(cell, Mark);
        }
        Reduration(m);
    }

    /// <summary>Adds (or replaces) one drum note at an onset; a flam also gets its 0.5-slot grace 38.</summary>
    private static TabCell AddDrumHit(TrackModel drums, MeasureModel m, int bar, double slot, int midi, int vel, bool ghost, bool flam, Section sec)
    {
        var cell = Place(m, slot, 0, out var existed);
        if (!existed) SetValue(cell, 16, 0, 0, 0);
        cell.IsRest = false;
        AutoRest.Remove(cell);   // a hit on a rest the re-duration added: the cell is a real beat now (else Reduration drops it)
        cell.Notes.RemoveAll(x => !x.IsGraceNote && x.MidiValue == midi);
        cell.Notes.Add(Drum(midi, vel, ghost));
        if (flam && !cell.Notes.Any(x => x.IsGraceNote))
            Grace(cell, drums, 0, 38, before: true, "GraceBefore", 0.5, Hv(drums.Name, bar, slot, 1038, 62, 8, sec, Lane.Grace));
        return cell;
    }

    /// <summary>
    /// Writes a fill (plan 3.9 "Fill kinds") into a 4/4 bar, replacing what was there in the fill's range:
    /// P4 = S 16ths on slots 12–15 (the kick and snare on 12–15 are removed; hats/cymbals stay);
    /// P8 = slots 8–15 cleared, then S S T1 T1 T2 T2 T4 T4 (16ths);
    /// F16 = the whole bar: S 0–3, T1 4–7, T2 8–9, T3 10–11, T4 12–14, T4 'X' on 15, K on 0/4/8/12;
    /// Roll39 = the whole bar: S 16ths 0–7, 6 × 16th-triplets on beat 3, 8 × 32nds on beat 4, K on 0/4/8/12, ramp 90 → 127;
    /// F6 = slots 8–15 cleared, then 12 × 16(6:4): S T1 S T2 S T3 T1 T2 T3 T4 T4 T4;
    /// FTrip8 = the whole bar: 12 × 8th-triplets T1×3 T2×3 T3×3 T4×3 (last 'X' 122), K on every beat.
    /// Every fill hit ramps 90 → 120 in time order (Roll39 to 127) with ±3 jitter; kicks are 'x' (110) through Hv; the section comes from
    /// <see cref="SectionOf"/>. <paramref name="flam"/> makes the first snare of the fill a flam (P8 slot 8, F16 slot 0; ignored by the others).
    /// Combine with Grid in either order (a later Grid merges its hits into the fill's cells).
    /// </summary>
    internal static void Fill(TrackModel drums, int bar, FillKind kind, bool flam = false)
    {
        var m = drums.Measures[bar - 1];
        EnsureSlots(m);
        if (SlotsOf(m) != 16) throw new InvalidOperationException($"Demo song: drums bar {bar}: fill {kind} needs a 4/4 bar.");
        var sec = SectionOf(bar);
        // (slot, midi, den, tupN, tupD, rampHit, fixedVel)
        var hits = new List<(double Slot, int Midi, int Den, int TupN, int TupD, bool Ramp, int Vel)>();
        void Seq(double start, int den, int tn, int td, params int[] midis)
        {
            var dur = 16.0 / den * (tn > 0 ? td / (double)tn : 1);
            for (var k = 0; k < midis.Length; k++) hits.Add((start + k * dur, midis[k], den, tn, td, true, 0));
        }
        void Kicks(params int[] slots) { foreach (var s in slots) hits.Add((s, 36, 16, 0, 0, false, 110)); }
        const int S = 38, T1 = 50, T2 = 48, T3 = 45, T4 = 43;
        var rampTop = 120;
        switch (kind)
        {
            case FillKind.P4:
                Clear(m, 12, 16, x => x.MidiValue is 35 or 36 or 38);
                Seq(12, 16, 0, 0, S, S, S, S);
                break;
            case FillKind.P8:
                Clear(m, 8, 16, null);
                Seq(8, 16, 0, 0, S, S, T1, T1, T2, T2, T4, T4);
                break;
            case FillKind.F16:
                Clear(m, 0, 16, null);
                Seq(0, 16, 0, 0, S, S, S, S, T1, T1, T1, T1, T2, T2, T3, T3, T4, T4, T4);
                hits.Add((15, T4, 16, 0, 0, false, 122));
                Kicks(0, 4, 8, 12);
                break;
            case FillKind.Roll39:
                Clear(m, 0, 16, null);
                Seq(0, 16, 0, 0, S, S, S, S, S, S, S, S);
                Seq(8, 16, 3, 2, S, S, S, S, S, S);
                Seq(12, 32, 0, 0, S, S, S, S, S, S, S, S);
                Kicks(0, 4, 8, 12);
                rampTop = 127;
                break;
            case FillKind.F6:
                Clear(m, 8, 16, null);
                Seq(8, 16, 6, 4, S, T1, S, T2, S, T3, T1, T2, T3, T4, T4, T4);
                break;
            case FillKind.FTrip8:
                Clear(m, 0, 16, null);
                Seq(0, 8, 3, 2, T1, T1, T1, T2, T2, T2, T3, T3, T3, T4, T4, T4);
                hits[^1] = hits[^1] with { Ramp = false, Vel = 122 };
                Kicks(0, 4, 8, 12);
                break;
        }
        var ramp = hits.Where(h => h.Ramp).OrderBy(h => h.Slot).ToList();
        var flamDone = !flam || kind is not (FillKind.P8 or FillKind.F16);
        foreach (var h in hits.OrderBy(h => h.Slot))
        {
            int vel;
            if (h.Ramp)
            {
                var k = ramp.IndexOf(h);
                var centre = ramp.Count == 1 ? rampTop : (int)Math.Round(90 + (rampTop - 90.0) * k / (ramp.Count - 1));
                vel = Hv(drums.Name, bar, h.Slot, h.Midi, centre, 3, sec, Lane.FillRamp);
            }
            else vel = Hv(drums.Name, bar, h.Slot, h.Midi, h.Vel, h.Midi == 36 ? 4 : 8, sec, h.Midi == 36 ? Lane.Kick : Lane.Tom);
            var isFlam = !flamDone && h.Midi == S;
            if (isFlam) flamDone = true;
            var cell = AddDrumHit(drums, m, bar, h.Slot, h.Midi, vel, ghost: false, flam: isFlam, sec);
            if (h.TupN > 0 || h.Den > 16) { SetValue(cell, h.Den, 0, h.TupN, h.TupD); FixedValue.AddOrUpdate(cell, Mark); }
        }
        Reduration(m);
    }

    /// <summary>
    /// A roll of <paramref name="count"/> hits of GM note <paramref name="midi"/> from <paramref name="startSlot"/>, each written as
    /// 1/<paramref name="den"/> in a <paramref name="tupN"/>:<paramref name="tupD"/> tuplet (0:0 = none): e.g. (8, 12, 6, 4, 16, 42, 60, 90)
    /// = 6 × 16(6:4) hats on beat 3 ramping 60 → 90; (15, 4, 0, 0, 64, 42, ...) = four 64ths on 15, 15.25, 15.5, 15.75.
    /// Velocity ramps linearly <paramref name="v0"/> → <paramref name="v1"/> ("as written": no metric accent or section contour) with ±3 jitter
    /// from <see cref="Hv"/>, plus the ±3 alternation for hi-hat rolls (42/44/46). Hits merge into cells already at those onsets (the cell takes
    /// the roll's written value); roll cells keep their value when Grid/Fill re-time the bar. Change a hit afterwards through the cell
    /// (e.g. the last 64th to an open hat).
    /// </summary>
    internal static void Roll(TrackModel drums, int bar, double startSlot, int count, int tupN, int tupD, int den, int midi, int v0, int v1)
    {
        var m = drums.Measures[bar - 1];
        EnsureSlots(m);
        var sec = SectionOf(bar);
        var dur = 16.0 / den * (tupN > 0 && tupD > 0 ? tupD / (double)tupN : 1);
        var end = Snap(startSlot + count * dur);
        if (end > SlotsOf(m) + 1e-6) throw new InvalidOperationException($"Demo song: drums bar {bar}: the roll ends at slot {end:0.###}, past the bar ({SlotsOf(m)}).");
        var hat = midi is 42 or 44 or 46;
        for (var k = 0; k < count; k++)
        {
            var slot = Snap(startSlot + k * dur);
            var centre = count == 1 ? v1 : (int)Math.Round(v0 + (v1 - v0) * (double)k / (count - 1));
            var vel = Hv(drums.Name, bar, slot, midi, centre, 3, sec, Lane.Roll, hat ? k : -1);
            var cell = AddDrumHit(drums, m, bar, slot, midi, vel, ghost: false, flam: false, sec);
            SetValue(cell, den, 0, tupN, tupD);
            FixedValue.AddOrUpdate(cell, Mark);
        }
        Reduration(m);
    }

    /// <summary>A drum note: StringIndex = GuitarProImporter.DrumLine(midi), Fret = MidiValue = midi (plan B35).</summary>
    internal static TabNote Drum(int midi, int vel, bool ghost = false) =>
        new() { StringIndex = GuitarProImporter.DrumLine(midi), Fret = midi, MidiValue = midi, Velocity = Math.Clamp(vel, 1, 127), Ghost = ghost };

    /// <summary>Removes notes (all, or those matching <paramref name="which"/>) from beats starting in [from, to); emptied beats are dropped.</summary>
    private static void Clear(MeasureModel m, double from, double to, Func<TabNote, bool>? which)
    {
        var cells = m.Cells;
        var beats = Beats(cells);
        var keep = new List<(double, TabCell)>();
        foreach (var (index, onset) in beats)
        {
            var c = cells[index];
            if (onset >= from - 1e-9 && onset < to - 1e-9)
            {
                var removed = c.Notes.RemoveAll(x => !x.IsGraceNote && (which is null || which(x)));
                if (removed > 0 && c.Notes.All(x => x.IsGraceNote)) c.Notes.Clear();
                if (c.Notes.Count == 0 && !c.HasAnnotation && c.Mix is null) continue;
            }
            keep.Add((onset, c));
        }
        Relayout(cells, keep);
    }

    private static readonly (double Slots, int Den, int Dots)[] NoteValues =
    {
        (16, 1, 0), (14, 2, 2), (12, 2, 1), (8, 2, 0), (7, 4, 2), (6, 4, 1), (4, 4, 0), (3.5, 8, 2), (3, 8, 1), (2, 8, 0),
        (1.75, 16, 2), (1.5, 16, 1), (1, 16, 0), (0.75, 32, 1), (0.5, 32, 0), (0.375, 64, 1), (0.25, 64, 0),
    };

    /// <summary>Drum bar (voice 1): each non-roll hit lasts until the next onset (a choke: at most a 16th); gaps are completed with rests.</summary>
    private static void Reduration(MeasureModel m)
    {
        var cells = m.Cells;
        var n = SlotsOf(m);
        var beats = Beats(cells).Where(b => !AutoRest.TryGetValue(cells[b.Index], out _)).Select(b => (b.Onset, Cell: cells[b.Index])).ToList();
        if (beats.Count == 0) { Relayout(cells, beats); return; }
        var order = new List<(double Onset, TabCell Cell)>();
        void Rests(double from, double to)
        {
            while (to - from > 0.2)
            {
                var v = NoteValues.First(x => x.Slots <= to - from + 1e-9);
                var r = new TabCell { IsRest = true };
                SetValue(r, v.Den, v.Dots, 0, 0);
                AutoRest.AddOrUpdate(r, Mark);
                order.Add((Snap(from), r));
                from += v.Slots;
            }
        }
        Rests(0, beats[0].Onset);
        for (var k = 0; k < beats.Count; k++)
        {
            var (onset, cell) = beats[k];
            var next = k + 1 < beats.Count ? beats[k + 1].Onset : n;
            order.Add((onset, cell));
            double end;
            if (FixedValue.TryGetValue(cell, out _) || cell.Tuplet.Numerator > 0) end = onset + MusicTime.CellSlots(cell);
            else
            {
                var gap = next - onset;
                if (Choked.TryGetValue(cell, out _)) gap = Math.Min(gap, 1);
                var v = NoteValues.FirstOrDefault(x => x.Slots <= gap + 1e-9);
                if (v.Den == 0) v = NoteValues[^1];
                SetValue(cell, v.Den, v.Dots, 0, 0);
                end = onset + v.Slots;
            }
            Rests(end, next);
        }
        Relayout(cells, order.OrderBy(o => o.Onset).ToList());
    }

    // ------------------------------------------------------------------ humanisation (plan 5.2)

    /// <summary>
    /// Deterministic "human" velocity: centre + metric + alternation + section contour + jitter, clamped.
    /// <list type="bullet">
    /// <item>metric (HiHat, Ride, KickRun only, on whole slots): slot 0 +6, slot 8 +3, odd slots −8;</item>
    /// <item>alternation when <paramref name="indexInRun"/> ≥ 0 (continuous 16ths, hat rolls): +3 on even, −3 on odd indices;</item>
    /// <item>contour (drum lanes except Ghost, Grace, FillRamp and Roll): IntroA −25; Pre +3 rising to +5 by the section's last bar;
    /// Chorus +2; Post +4; Interlude −20; Bridge +2; Breakdown: Kick/KickRun/China +6, HiHat −6; Solo2 +3; FinalChorus +4; Outro +3; else 0;</item>
    /// <item>jitter: a uniform integer in [−<paramref name="jitter"/>, +<paramref name="jitter"/>] from SplitMix64(FNV-1a 32 of
    /// "AshenMeridian|track|bar|round(slot×1000)|midiOrFret"): the same inputs always give the same value;</item>
    /// <item>clamp: Ghost 1..60, Grace 55..70, everything else 1..127.</item>
    /// </list>
    /// Chug and Pitched lanes get only the jitter. <paramref name="bar"/> is 1-based.
    /// </summary>
    internal static int Hv(string track, int bar, double slot, int midiOrFret, int centre, int jitter,
                           Section sec, Lane lane, int indexInRun = -1)
    {
        var v = centre;
        if ((lane is Lane.HiHat or Lane.Ride or Lane.KickRun) && IsWhole(slot))
        {
            var s = (int)Math.Round(slot);
            v += s == 0 ? 6 : s == 8 ? 3 : s % 2 == 1 ? -8 : 0;
        }
        if (indexInRun >= 0) v += indexInRun % 2 == 0 ? 3 : -3;
        if (lane is not (Lane.Ghost or Lane.Grace or Lane.FillRamp or Lane.Roll or Lane.Chug or Lane.Pitched))
            v += Contour(sec, lane, bar);
        if (jitter > 0)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"AshenMeridian|{track}|{bar}|{(int)Math.Round(slot * 1000)}|{midiOrFret}");
            var z = SplitMix64(Fnv1a32(key));
            v += (int)(z % (ulong)(2 * jitter + 1)) - jitter;
        }
        return lane switch
        {
            Lane.Ghost => Math.Clamp(v, 1, 60),
            Lane.Grace => Math.Clamp(v, 55, 70),
            _ => Math.Clamp(v, 1, 127),
        };
    }

    private static int Contour(Section sec, Lane lane, int bar) => sec switch
    {
        Section.IntroA => -25,
        Section.Pre => 3 + (int)Math.Round(2.0 * (bar - (bar >= 63 ? 63 : 32)) / 7),
        Section.Chorus => 2,
        Section.Post => 4,
        Section.Interlude => -20,
        Section.Bridge => 2,
        Section.Breakdown => lane is Lane.Kick or Lane.KickRun or Lane.China ? 6 : lane == Lane.HiHat ? -6 : 0,
        Section.Solo2 => 3,
        Section.FinalChorus => 4,
        Section.Outro => 3,
        _ => 0,
    };

    private static uint Fnv1a32(string s)
    {
        var h = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 16777619u; }
        return h;
    }

    private static ulong SplitMix64(ulong seed)
    {
        var z = seed + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
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
