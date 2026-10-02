using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using TabForge.Diagnostics;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;

namespace TabForge;

// R5 (work/night-2026-10-02/R5_CAPABILITY.md): the capability probe. One minimal fixture per loss allowance of the semantic round trip
// (SelfTestRoundTripSemantics.cs) and per tolerance of the GP fix tests (SelfTestGpRoundTripFixes.cs): what the model holds, the gpif XML
// the exporter wrote, what TabForge reads back. `TabForge.exe --gp-capability <out.md> [gp-folder]` writes the record; the class and cause
// of each case live here, next to the fixture, so the record cannot drift from the code.
public static partial class SelfTest
{
    /// <summary>Why an allowance exists. Unknown is never counted as preserved or unavoidable.</summary>
    internal enum GfClass { TabForgeGap, AlphaTabLimitation, FormatLimitation, MeaningPreserving, Unknown }

    private sealed record GfCase(
        string Id, string Allowance, string Field, string Fixture, Func<SongProject> Build, Func<SongProject, string> Show, string[] Xml,
        GfClass Class, string Cause, bool Pair = false, Func<SongProject, SongProject>? Expect = null, string Status = "");

    // ---------------------------------------------------------------- small helpers

    private static SongProject GfSong(int bars, Action<TrackModel>? fill = null, Action<SongProject>? tweak = null, bool imported = false)
    {
        var song = new SongProject { Title = "gf", Tempo = 100, ImportedFrom = imported ? "synthetic.gp5" : null };
        var t = GpFixGuitar(bars);
        song.Tracks.Add(t);
        fill?.Invoke(t);
        tweak?.Invoke(song);
        return song;
    }

    private static TabCell GfPut(TrackModel t, int bar, int slot, int den, params TabNote[] notes) => RtPut(t, bar, slot, den, 0, notes);

    private static List<TabCell> GfBeats(SongProject s, int bar = 0, int track = 0, int voice = 0) =>
        (voice == 0 ? s.Tracks[track].Measures[bar].Cells : s.Tracks[track].Measures[bar].Voice2Cells).Where(c => c.Notes.Count > 0).ToList();

    private static TabNote GfN(SongProject s, int bar = 0, int beat = 0, int note = 0, int track = 0)
    {
        var beats = GfBeats(s, bar, track);
        return beat < beats.Count && note < beats[beat].Notes.Count ? beats[beat].Notes[note] : new TabNote { Fret = -99 };
    }

    private static string GfTech(TabNote n) => string.Join("+", n.Techniques.OrderBy(x => x, StringComparer.Ordinal));
    private static string GfBend(List<BendPointModel> p) => p.Count == 0 ? "-" : string.Join(" ", p.Select(x => $"{RtF(x.Offset)}:{RtF(x.Value)}"));

    private static string GfGpif(byte[] gp)
    {
        var part = GuitarProExporter.ReadZip(gp).FirstOrDefault(p => p.Name.EndsWith("score.gpif", StringComparison.OrdinalIgnoreCase));
        return part.Data is null ? "" : Encoding.UTF8.GetString(part.Data);
    }

    /// <summary>The gpif elements with the given local names, one compact line each (a record of what was written, not a copy of the file).</summary>
    private static string GfSnip(string gpif, string[] names, int limit = 10)
    {
        if (gpif.Length == 0) return "(no gpif)";
        XDocument doc;
        try { doc = XDocument.Parse(gpif); } catch (System.Xml.XmlException) { return "(gpif not XML)"; }
        var lines = new List<string>();
        foreach (var name in names)
            foreach (var el in doc.Descendants(name).Take(limit))
            {
                // Slim: drop the boilerplate every element carries, so the line shows what the case is about.
                var copy = new XElement(el);
                foreach (var drop in copy.Descendants().Where(d => d.Name.LocalName is "Rhythm" or "ConcertPitchStemOrientation" or "XProperties" or "InstrumentSet" or "Staves" or "SystemsLayout"
                    or "SystemsDefautLayout" or "AutoBrush" or "PalmMute" or "PlayingStyle" or "UseOneChannelPerString" or "IconId" or "ShortName" or "InstrumentArticulation"
                    || d.Name.LocalName == "Properties" && !d.HasElements && d.Parent?.Name.LocalName == "Beat"
                    || d.Name.LocalName == "Property" && (string?)d.Attribute("name") is "ConcertPitch" or "TransposedPitch").ToList()) drop.Remove();
                if (name == "Beat" && copy.Element("Notes") is null) continue;
                var text = copy.ToString(SaveOptions.DisableFormatting);
                lines.Add(text.Length > 700 ? text[..700] + "..." : text);
            }
        return lines.Count == 0 ? "(no " + string.Join("/", names) + " element)" : string.Join("\n", lines);
    }

    private static SongProject GfReopen(byte[] gp, string folder, string name)
    {
        var path = Path.Combine(folder, name + ".gp");
        File.WriteAllBytes(path, gp);
        return GuitarProImporter.Import(path);
    }

    // ---------------------------------------------------------------- the cases

    private static List<GfCase> GfCases()
    {
        var c = new List<GfCase>();
        void Add(GfCase x) => c.Add(x);
        var beat = new[] { "Beat" }; var beatNote = new[] { "Beat", "Note" };

        // ---- loudness
        Add(new("A01", "note.velocity", "note velocity (loudness)", "one quarter note, velocity 100 (between f=95 and ff=112)",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 100))), s => $"velocity {GfN(s).Velocity}", beatNote, GfClass.Unknown,
            "The beat's loudness is one of eight dynamics (ppp..fff): the exporter writes the nearest of the shared table, so a value between two steps is not kept. RECLASSIFIED to Unknown after the Guitar Pro 8 comparison: a real Guitar Pro 7 bass file in the Tabs folder carries a note-level <Velocity>0.625</Velocity> beside the beat's <Dynamic>MF</Dynamic> on all 64 notes, so the format may hold a per-note velocity that alphaTab neither reads nor writes. Whether Guitar Pro 8 writes it when a note's velocity is edited is unverified."));
        Add(new("A02", "note.dynamic", "per-note dynamic inside a chord", "a chord of two notes: p (49) on string 2, ff (112) on string 3",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 49), RtNote(t, 2, 2, 112))), s => $"{Dynamics.Names[Dynamics.NearestIndex(GfN(s, 0, 0, 0).Velocity)]}/{Dynamics.Names[Dynamics.NearestIndex(GfN(s, 0, 0, 1).Velocity)]}", beatNote, GfClass.Unknown,
            "The beat holds one <Dynamic> and the first note's decides (every note the exporter and a Guitar Pro 8 re-save wrote has no dynamic of its own). RECLASSIFIED to Unknown: as A01, a real Guitar Pro 7 file has a note-level <Velocity> float, so a per-note loudness may be storable (through an element alphaTab does not carry); not established for Guitar Pro 8."));

        // ---- track level
        Add(new("A03", "track.volume", "track volume", "one track, volume 100 (of 127)",
            () => GfSong(1, t => { t.Volume = 100; GfPut(t, 0, 0, 4, RtNote(t, 1, 3)); }), s => $"volume {s.Tracks[0].Volume}", new[] { "Track" }, GfClass.AlphaTabLimitation,
            "FIXED: the gpif stores the volume as a float (RSE ChannelStrip Parameters) but alphaTab's model is the 0..16 GP5 scale (a floor), so 0..127 used to be quantised to steps of 8. TabForge.AlphaTab patch 0002 (vendor/alphatab) adds PlaybackInformation.VolumeFraction, which the gpif reader fills and the writer prefers; TabForge maps 0..127 <-> fraction as v/128 in both directions (GpMixerScale), so every value survives exactly. The allowance is removed; a clean .gp compares the effective level (track x mixer group, baked in)."));
        Add(new("A04", "track.pan", "track pan", "one track, pan 50 (of 127)",
            () => GfSong(1, t => { t.Pan = 50; GfPut(t, 0, 0, 4, RtNote(t, 1, 3)); }), s => $"pan {s.Tracks[0].Pan}", new[] { "Track" }, GfClass.AlphaTabLimitation,
            "FIXED: as A03 for the balance (BalanceFraction; the centre, 64, is exactly 0.5). The allowance is removed; a clean .gp compares the effective pan (track pan + group pan + master pan, baked in)."));

        // ---- bend / whammy
        Add(new("A05", "note.bend", "multi-point bend curve", "one note, bend 0:0 15:4 30:2 45:4 60:0",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Bend")).Notes[0].BendPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 15, Value = 4 }, new() { Offset = 30, Value = 2 }, new() { Offset = 45, Value = 4 }, new() { Offset = 60, Value = 0 } }),
            s => $"bend {GfBend(GfN(s).BendPoints)}", beatNote, GfClass.FormatLimitation,
            "gpif keeps a bend as four points: origin, middle1, middle2 (sharing one middle value) and destination (BendOrigin/Middle/Destination Offset+Value note properties). alphaTab's writer puts the middle stretch at the midpoint or the peak and ignores the second offset, so a quick rise that then holds (0:0 15:4 60:4) used to be saved as a slow whole-note ramp (the target reached at 300 ms instead of 150 ms). FIXED: the exporter picks the best four-field fit of the curve (GuitarProBendCurve) and writes it into the note after alphaTab has written the file: a hold at the end becomes the destination offset (0:0 15:4), a flat stretch keeps both offsets (0:0 15:4 45:4 60:0 comes back exactly), and the reader keeps a middle stretch only when its value differs from both the origin and the destination. This fixture, which has more turns than the format holds, now reads back as 0:0 15:4 30:4 60:0 (it used to repeat the point: 0:0 15:4 15:4 60:0); only such a curve is reduced, and only that is listed by the export check. The vocabulary confirms it: all 438 bends of the 28 real Guitar Pro 6-8 files in the Tabs folder, and the 8 bends of the Guitar Pro 8 re-saves, use exactly these eight properties and no other. Drawing a 5-point bend in Guitar Pro 8's own editor is the one thing not tried."));
        Add(new("A06", "beat.whammy", "whammy-bar curve longer than four points", "one beat, whammy 0:0 10:-4 20:2 30:-8 40:0 50:-2 60:0",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)).WhammyPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 10, Value = -4 }, new() { Offset = 20, Value = 2 }, new() { Offset = 30, Value = -8 }, new() { Offset = 40, Value = 0 }, new() { Offset = 50, Value = -2 }, new() { Offset = 60, Value = 0 } }),
            s => $"whammy {GfBend(GfBeats(s)[0].WhammyPoints)}", beat, GfClass.FormatLimitation,
            "gpif <Whammy> holds origin, middle1, middle2 (one middle value) and destination: four points. Longer curves reduce to that shape. The vocabulary confirms it: the 40 whammy elements of the real files and the 2 of the Guitar Pro 8 re-saves carry exactly these seven attributes. Drawing a 7-point whammy in Guitar Pro 8's own editor is not tried."));

        // ---- fingering, tenuto
        Add(new("A07", "note.lhFinger", "left-hand fingering", "one note, left-hand finger 2 (middle)",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)).Notes[0].LeftHandFinger = 2), s => $"lh {GfN(s).LeftHandFinger?.ToString() ?? "-"}", beatNote, GfClass.TabForgeGap,
            "FIXED in this change: the exporter now sets Note.LeftHandFinger; alphaTab writes <LeftFingering> and reads it back. The allowance is removed."));
        Add(new("A08", "note.rhFinger", "right-hand fingering", "one note, right-hand finger 1 (index)",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)).Notes[0].RightHandFinger = 1), s => $"rh {GfN(s).RightHandFinger?.ToString() ?? "-"}", beatNote, GfClass.TabForgeGap,
            "FIXED: <RightFingering>, as A07. Allowance removed."));
        Add(new("A09", "beat.tenuto", "tenuto mark on a beat", "one beat with Tenuto set",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)).Tenuto = true), s => $"tenuto {GfBeats(s)[0].Tenuto}", beatNote, GfClass.TabForgeGap,
            "FIXED: alphaTab has AccentuationType.Tenuto (written as <Accent>16</Accent>); the exporter writes it and the importer maps it onto the beat. The old claim that the model has no tenuto flag was wrong. Allowance removed."));
        Add(new("A10", "note.technique:Tenuto", "Tenuto technique name", "one note tagged with the technique name Tenuto",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Tenuto"))), s => $"technique {GfTech(GfN(s))}", beatNote, GfClass.TabForgeGap, "FIXED with A09: a legacy Tenuto tag is written as tenuto too; the model keeps tenuto on the beat, so the tag name is not read back and no scenario carries it. Allowance removed."));

        // ---- mix table
        Add(new("A11", "beat.mix", "beat mix-table change (program, volume, pan, tempo)", "beat 1: mix program 30, volume 10, pan 2; beat 2: mix tempo 140",
            () => GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3)).Mix = new MixChange { Program = 30, Volume = 10, Pan = 2 }; GfPut(t, 0, 4, 4, RtNote(t, 1, 5)).Mix = new MixChange { Tempo = 140 }; }),
            s => string.Join(" | ", GfBeats(s).Select(b => b.Mix is null ? "no mix" : $"prog {b.Mix.Program} vol {b.Mix.Volume} pan {b.Mix.Pan} tempo {b.Mix.Tempo}")) + $"; bar tempo points {s.Tracks[0].Measures[0].MidBarTempos?.Count ?? 0}", new[] { "Automation", "MasterBar" }, GfClass.AlphaTabLimitation,
            "alphaTab writes track automations of type Tempo and the initial Sound only: Beat.Automations of type Volume, Balance and Instrument set by the exporter produce nothing in the gpif (probed). Mix-table changes on a beat cannot be written through alphaTab."));

        // ---- per-beat flags that spread
        Add(new("A12", "note.technique:PalmMute.extra", "palm mute on one note of a chord", "a chord of two notes, only the first palm-muted",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "PalmMute"), RtNote(t, 2, 2))), s => $"note1 {GfTech(GfN(s, 0, 0, 0))}; note2 {GfTech(GfN(s, 0, 0, 1))}", beatNote, GfClass.TabForgeGap,
            "FIXED: the exporter already wrote PalmMuted per note; the importer OR-ed in alphaTab's beat-level flag (any note muted) and muted the whole chord. It now reads the note flag. Allowance removed."));
        Add(new("A13", "beat.fermata", "fermata on one track only", "two tracks; a fermata on track 1's first beat",
            () => { var s = GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)).Fermata = true); var t2 = GpFixGuitar(1, "Gtr 2"); t2.MidiChannel = 1; GfPut(t2, 0, 0, 4, RtNote(t2, 1, 5)); s.Tracks.Add(t2); return s; },
            s => $"track1 fermata {GfBeats(s, 0, 0)[0].Fermata}, track2 fermata {GfBeats(s, 0, 1)[0].Fermata}", new[] { "MasterBar", "Beat" }, GfClass.FormatLimitation,
            "GP7 keeps a fermata on the master bar (position in the bar), shared by all tracks."));
        Add(new("A14", "track.channel", "MIDI channel", "a guitar track on channel 5",
            () => GfSong(1, t => { t.MidiChannel = 5; GfPut(t, 0, 0, 4, RtNote(t, 1, 3)); }), s => $"channel {s.Tracks[0].MidiChannel}", new[] { "Track" }, GfClass.MeaningPreserving,
            "The importer assigns channels in track order (percussion on 9); the channel number has no musical meaning in a .gp."));

        // ---- derived data
        Add(new("A15", "note.slideTarget", "slide target pitch not set by the source", "legato-slide note, SlideTargetMidi 0, followed by a note a tone higher",
            () => GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "LegatoSlide")); GfPut(t, 0, 4, 4, RtNote(t, 1, 5)); }), s => $"slide target {GfN(s).SlideTargetMidi}", beatNote, GfClass.MeaningPreserving,
            "The importer derives the target from the next note when the source set none; the slide itself is kept (SlideOutType). A value the source did not set is filled in."));
        Add(new("A16", "note.trillTarget", "trill target not set by the source", "a trill note with TrillTargetMidi 0",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Trill"))), s => $"trill target {GfN(s).TrillTargetMidi}", beatNote, GfClass.MeaningPreserving,
            "The exporter writes a trill of two semitones when no target is set (note.TrillValue = pitch + 2); reading it back fills the target. Same musical meaning as the default playback."));
        Add(new("A17", "note.trillDur", "trill speed", "three trills with speed 1/16 (set), 1/32 (set) and none",
            () => GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Trill")).Notes[0].TrillDurationDenominator = 16; GfPut(t, 0, 4, 4, RtNote(t, 1, 3, 95, "Trill")).Notes[0].TrillDurationDenominator = 32; GfPut(t, 0, 8, 4, RtNote(t, 1, 3, 95, "Trill")); }),
            s => string.Join(" / ", GfBeats(s).Select(b => b.Notes[0].TrillDurationDenominator)), beatNote, GfClass.AlphaTabLimitation,
            "FIXED: alphaTab's writer wrote the trill target pitch only (<Trill>64</Trill>) and no speed, and its reader gave every trill 1/16, so a 1/32 trill reopened as 1/16. Guitar Pro itself stores the speed in a note XProperty (id 688062467, an Int: the note value in ticks of a 960-tick quarter; a real Guitar Pro 7 file holds 471-474 for an 8th and 240 for a 16th). TabForge.AlphaTab patch 0003 writes it (480 / 240 / 120 / 60 for 1/8, 1/16, 1/32, 1/64) and reads it back as the nearest note value. A source without the XProperty still reads as 1/16. The allowance is now only: no speed in the source."));
        Add(new("A18", "beat.tremoloPick", "tremolo picking speed", "four beats with tremolo picking 1/8, 1/16, 1/32 and 1/64",
            () => GfSong(1, t => { var i = 0; foreach (var d in new[] { 8, 16, 32, 64 }) { GfPut(t, 0, i * 4, 4, RtNote(t, 1, 3, 95, "TremoloPick")).TremoloPickDenominator = d; i++; } }),
            s => string.Join(" / ", GfBeats(s).Select(b => b.TremoloPickDenominator)), beat, GfClass.FormatLimitation,
            "GP7 has three speeds (1, 2 or 3 slashes = 1/8, 1/16, 1/32); 1/64 is written as 1/32. Narrow allowance: only 64 -> 32."));

        // ---- fades and flags that spread over a beat
        Add(new("A19", "note.technique:FadeIn.extra", "fade-in on one note of a chord", "a chord of two notes, only the first faded in",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "FadeIn"), RtNote(t, 2, 2))), s => $"note1 {GfTech(GfN(s, 0, 0, 0))}; note2 {GfTech(GfN(s, 0, 0, 1))}", beat, GfClass.FormatLimitation,
            "GP stores a volume swell as a Beat property (Fadding); there is no per-note fade."));
        Add(new("A20", "note.technique:FadeOut.extra", "fade-out on one note of a chord", "a chord of two notes, only the first faded out",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "FadeOut"), RtNote(t, 2, 2))), s => $"note1 {GfTech(GfN(s, 0, 0, 0))}; note2 {GfTech(GfN(s, 0, 0, 1))}", beat, GfClass.FormatLimitation, "As A19."));

        // ---- bar level
        Add(new("A21", "bar.directions", "navigation marks", "bar 2: Segno, ToCoda; bar 3: Coda; bar 4: DaCapoAlCoda",
            () => GfSong(4, t => { for (var b = 0; b < 4; b++) GfPut(t, b, 0, 1, RtNote(t, 1, b)); t.Measures[1].Directions = "Segno,ToCoda"; t.Measures[2].Directions = "Coda"; t.Measures[3].Directions = "DaCapoAlCoda"; }),
            s => string.Join(" | ", Enumerable.Range(0, 4).Select(b => s.Tracks[0].Measures[b].Directions)), new[] { "MasterBar" }, GfClass.MeaningPreserving,
            "The importer names marks the way alphaTab does (TargetSegno, JumpDaCoda...); the same marks under GP's own names. The test narrows the allowance to exactly this spelling map."));
        Add(new("A22", "bar.tempoChange", "tempo marking that restates the running tempo", "bar 2 sets tempo 100 in a song already at 100",
            () => GfSong(2, t => { GfPut(t, 0, 0, 1, RtNote(t, 1, 3)); GfPut(t, 1, 0, 1, RtNote(t, 1, 5)); t.Measures[1].TempoChange = 100; }), s => $"bar 2 TempoChange {s.Tracks[0].Measures[1].TempoChange?.ToString() ?? "none"}", new[] { "MasterBar", "Automation" }, GfClass.MeaningPreserving,
            "A tempo automation equal to the running tempo is not kept as a change on import; the tempo actually played is compared strictly."));
        Add(new("A23", "note.technique:TremBar*", "whammy sub-type name", "a beat with a dive curve and the tag TremBarDive",
            () => GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "TremBarDive")).WhammyPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = -8 } }; }),
            s => $"technique {GfTech(GfN(s))}; whammy {GfBend(GfBeats(s)[0].WhammyPoints)}", beat, GfClass.MeaningPreserving, "alphaTab re-derives the sub-type from the curve; the curve is compared strictly."));

        // ---- tag mirroring by the importer
        Add(new("A24", "note.technique:Harmonic.extra", "generic Harmonic tag on a specific harmonic", "a pinch harmonic",
            () => GfSong(1, t => { var n = RtNote(t, 1, 5, 95, "PinchHarmonic"); n.HarmonicFret = 12; GfPut(t, 0, 0, 4, n); }), s => $"technique {GfTech(GfN(s))}; harmonic fret {GfN(s).HarmonicFret}", beatNote, GfClass.MeaningPreserving, "The importer tags every harmonic kind also with the generic name."));
        Add(new("A25", "note.technique:Dead.extra", "Dead technique mirror", "a dead note", () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)).Notes[0].Dead = true), s => $"dead {GfN(s).Dead}; technique {GfTech(GfN(s))}", beatNote, GfClass.MeaningPreserving, "The importer mirrors the flag as a name; the flag is compared strictly."));
        Add(new("A26", "note.technique:Ghost.extra", "Ghost technique mirror", "a ghost note", () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)).Notes[0].Ghost = true), s => $"ghost {GfN(s).Ghost}; technique {GfTech(GfN(s))}", beatNote, GfClass.MeaningPreserving, "As A25."));
        Add(new("A27", "note.technique:HOPOOrigin.extra", "hammer-on origin tag split", "a hammer-on: origin note then destination note",
            () => GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "HOPO")); GfPut(t, 0, 4, 4, RtNote(t, 1, 5)); }), s => $"origin {GfTech(GfN(s, 0, 0))}; destination {GfTech(GfN(s, 0, 1))}", beatNote, GfClass.MeaningPreserving, "The importer splits one HOPO tag into origin and destination tags."));
        Add(new("A28", "note.technique:HOPODestination.extra", "hammer-on destination tag", "as A27", () => GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "HOPO")); GfPut(t, 0, 4, 4, RtNote(t, 1, 5)); }), s => $"destination {GfTech(GfN(s, 0, 1))}", beatNote, GfClass.MeaningPreserving, "As A27."));
        Add(new("A29", "note.technique:Legato.missing", "legato slur (Legato tag)", "a note tagged Legato followed by a note",
            () => GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Legato")); GfPut(t, 0, 4, 4, RtNote(t, 1, 5)); }), s => $"technique {GfTech(GfN(s, 0, 0))}", beatNote, GfClass.TabForgeGap,
            "FIXED: Guitar Pro's legato is a slur over two beats (<Legato origin=\"true\" destination=\"false\"/> on a beat; 24 in the real GP6/7/8 files of the Tabs folder). A note tagged Legato now marks its beat as the slur origin (alphaTab's Beat.IsLegatoOrigin) and the next beat of the same voice, over the bar line too, as the destination; the importer reads the origin flag back as the Legato tag on the notes of that beat. The allowance and the save-question item are removed."));
        Add(new("A30", "note.technique:Rasgueado.missing", "rasgueado", "a beat tagged Rasgueado",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "Rasgueado"))), s => $"technique {GfTech(GfN(s))}", beat, GfClass.TabForgeGap,
            "FIXED: alphaTab's writer emits a beat Property \"Rasgueado\" (Rasgueado.Ii writes <Rasgueado>ii_1</Rasgueado>; 19 patterns ii_1, mi_1, pmp_1 ... peami). A Rasgueado tag is written as the first pattern, or as the pattern named by a Rasgueado<pattern> tag (for example RasgueadoPeami); the importer reads the property back as Rasgueado, plus Rasgueado<pattern> for any pattern but the first. The allowance and the save-question item are removed. No real file of the 45 uses one."));
        Add(new("A31", "note.technique:PickSlideUp.missing", "pick slide up", "a note tagged PickSlideUp", () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "PickSlideUp"))), s => $"technique {GfTech(GfN(s))}", beatNote, GfClass.TabForgeGap,
            "FIXED (was Unknown): alphaTab's SlideOutType has PickSlideUp / PickSlideDown and its writer emits them as <Property name=\"Slide\"><Flags>128</Flags> / 64; the importer already read them back as the tags. The exporter simply never set them. Now written; the allowance is removed. Confirmed by a Guitar Pro 8 re-save of gp-capability A31/A32 (round 2): 0 differences, drawn as P.S. with an up / down line (the 45 real files have no pick slide, so the flags 64/128 come from alphaTab, not from a Guitar Pro file)."));
        Add(new("A32", "note.technique:PickSlideDown.missing", "pick slide down", "a note tagged PickSlideDown", () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "PickSlideDown"))), s => $"technique {GfTech(GfN(s))}", beatNote, GfClass.TabForgeGap, "FIXED: as A31 (Flags 64)."));
        Add(new("A33", "note.technique:LeftTap.missing", "left-hand tap", "a note tagged LeftTap",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "LeftTap"))), s => $"technique {GfTech(GfN(s))}", beatNote, GfClass.TabForgeGap, "The exporter writes LeftHandTapped and the importer reads it back as the LeftTap tag (probed): the allowance was stale. Removed."));

        // ---- grace notes
        Add(new("A34", "note.graceSlots", "grace-note length", "three before-beat grace notes of 0.5, 1 and 2 slots (a 32nd, 16th, 8th) on quarter notes",
            () => GfSong(1, t => { var k = 0; foreach (var len in new[] { 0.5, 1.0, 2.0 }) { var cell = GfPut(t, 0, k * 4, 4, RtNote(t, 1, 5)); cell.Notes.Insert(0, new TabNote { StringIndex = 1, Fret = 3, MidiValue = t.PitchOf(1, 3), IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = len, Velocity = 95 }); k++; } }),
            s => "grace slots " + string.Join(" / ", GfBeats(s).Select(b => b.Notes.Where(n => n.IsGraceNote).Select(n => RtF(n.GraceDurationSlots)).FirstOrDefault() ?? "-")), new[] { "Beat", "Rhythm" }, GfClass.AlphaTabLimitation,
            "alphaTab normalises a grace beat: every grace is written with an eighth-note Rhythm (see the Rhythm elements) whatever the model held, and read back as 2 slots. Playback does not use the value. Allowance narrowed to the read-back value 2."));
        Add(new("A35", "note.technique:GraceBefore.extra", "GraceBefore technique mirror", "a before-beat grace", () => GfSong(1, t => { var cell = GfPut(t, 0, 0, 4, RtNote(t, 1, 5)); cell.Notes.Insert(0, new TabNote { StringIndex = 1, Fret = 3, MidiValue = t.PitchOf(1, 3), IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = 1, Velocity = 95 }); }),
            s => string.Join(" ; ", GfBeats(s)[0].Notes.Select(n => $"{(n.IsGraceNote ? "grace" : "note")} {GfTech(n)}")), beatNote, GfClass.MeaningPreserving, "The importer mirrors the flag as a name; the flag is compared strictly."));
        Add(new("A36", "note.technique:GraceOnBeat.extra", "GraceOnBeat technique mirror", "an on-beat grace", () => GfSong(1, t => { var cell = GfPut(t, 0, 0, 4, RtNote(t, 1, 5)); cell.Notes.Insert(0, new TabNote { StringIndex = 1, Fret = 3, MidiValue = t.PitchOf(1, 3), IsGraceNote = true, GraceBeforeBeat = false, GraceDurationSlots = 1, Velocity = 95 }); }),
            s => string.Join(" ; ", GfBeats(s)[0].Notes.Select(n => $"{(n.IsGraceNote ? "grace" : "note")} {GfTech(n)}")), beatNote, GfClass.MeaningPreserving, "As A35."));
        Add(new("A37", "note.technique:BrushDown.extra", "Brush tag on an arpeggio down", "a chord with an ArpeggioDown stroke",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "ArpeggioDown", "BrushDown"), RtNote(t, 2, 2, 95, "ArpeggioDown", "BrushDown"))), s => $"technique {GfTech(GfN(s))}", beat, GfClass.MeaningPreserving, "The importer tags an arpeggio stroke also as a brush stroke."));
        Add(new("A38", "note.technique:BrushUp.extra", "Brush tag on an arpeggio up", "a chord with an ArpeggioUp stroke",
            () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3, 95, "ArpeggioUp", "BrushUp"), RtNote(t, 2, 2, 95, "ArpeggioUp", "BrushUp"))), s => $"technique {GfTech(GfN(s))}", beat, GfClass.MeaningPreserving, "As A37."));
        Add(new("A39", "note.technique:HOPO.extra", "hammer-on origin ending a bar", "a hammer-on origin as the last note of bar 1, the destination first in bar 2",
            () => GfSong(2, t => { GfPut(t, 0, 12, 4, RtNote(t, 1, 3, 95, "HOPO")); GfPut(t, 1, 0, 4, RtNote(t, 1, 5)); }), s => $"bar1 last {GfTech(GfN(s, 0, 0))}; bar2 first {GfTech(GfN(s, 1, 0))}", beatNote, GfClass.MeaningPreserving, "The importer marks the destination across the bar line with the HOPO tag."));
        Add(new("A40", "note.technique:GraceBend.missing", "bend grace note", "a grace note tagged GraceBend with a bend",
            () => GfSong(1, t => { var cell = GfPut(t, 0, 0, 4, RtNote(t, 1, 5)); var g = new TabNote { StringIndex = 1, Fret = 3, MidiValue = t.PitchOf(1, 3), IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = 1, Velocity = 95 }; g.Techniques.Add("GraceBend"); g.BendPoints = new() { new() { Offset = 0, Value = 0 }, new() { Offset = 60, Value = 4 } }; cell.Notes.Insert(0, g); }),
            s => string.Join(" ; ", GfBeats(s)[0].Notes.Select(n => $"{(n.IsGraceNote ? "grace" : "note")} {GfTech(n)} bend {GfBend(n.BendPoints)}")), beatNote, GfClass.AlphaTabLimitation, "alphaTab's GP7 writer drops a BendGrace beat entirely (noted in the exporter); the exporter writes a before-beat grace with its bend and loses only the tag."));

        // ---- embedded profile
        Add(new("A41", "song.title (embedded profile)", "title", "a titled song saved with the embedded project", () => GfSong(1, t => GfPut(t, 0, 0, 4, RtNote(t, 1, 3)), s => s.Title = "Title ä"), s => $"title '{s.Title}'", new[] { "Title" }, GfClass.MeaningPreserving,
            "Not a loss: the allowance exists only to document that the embedded profile has an empty allow-list. Candidate for removal (nothing needs it)."));

        // ---- .tfaudio profile
        void Pair(string id, string key, string field, Action<TrackModel> set, Func<TrackModel, string> show, GfClass cls, string cause) =>
            Add(new(id, key + " (clean .gp + .tfaudio)", field, "one track with " + field + " set to a non-default value", () => GfSong(1, t => { GfPut(t, 0, 0, 4, RtNote(t, 1, 3)); set(t); }), s => show(s.Tracks[0]), new[] { "Track" }, cls, cause, Pair: true));
        Pair("A42", "track.midiDevice", "MIDI output device", t => t.MidiOutputDeviceId = 77, t => $"device {t.MidiOutputDeviceId}", GfClass.FormatLimitation, "A device number is machine-specific; neither the .gp nor the .tfaudio stores it. Deliberate: it would point at hardware that is not there.");
        Pair("A43", "track.audioInput", "record input choice", t => t.AudioInput = AudioInputs.Input2, t => $"input {t.AudioInput}", GfClass.TabForgeGap, "A live setting kept in .tforge only. The .tfaudio could carry it; it is machine-specific like A42, so leaving it out is a decision, not a limit.");
        Pair("A44", "track.monitor", "input monitoring", t => t.MonitorInput = false, t => $"monitor {t.MonitorInput}", GfClass.TabForgeGap, "As A43.");
        Pair("A45", "track.tint", "row tint", t => t.TintRow = false, t => $"tint {t.TintRow}", GfClass.TabForgeGap, "A view preference (.tforge only); the .tfaudio does not store view settings.");
        Pair("A46", "track.reverb", "reverb send", t => t.Reverb = 50, t => $"reverb {t.Reverb}", GfClass.FormatLimitation, "RESOLVED (was Unknown): a per-track reverb or chorus send is a Guitar Pro 3-5 mixer value (a MIDI channel's reverb and chorus controllers); the gpif of Guitar Pro 6-8 has no such element. Across the 142 tracks of 45 real and Guitar Pro 8 re-saved .gp files the track's RSE ChannelStrip holds 16 floats: indexes 0-9 and 11-12 vary (volume is 12, balance 11, the rest are the strip's own controls), but 10 and 13-15 are 0.5 in every track, and the words reverb / chorus appear only as effect ids inside the sound's EffectChain (e.g. M03_StudioReverbRoomStudioA, E15_ChorusEnsemble: a pedal or master effect, not a send). alphaTab's PlaybackInformation has no such field either. So a clean .gp cannot hold the send; the preflight already lists it. Inferred from the file contents, not from Guitar Pro 8's mixer window.");
        Pair("A47", "track.chorus", "chorus send", t => t.Chorus = 20, t => $"chorus {t.Chorus}", GfClass.FormatLimitation, "RESOLVED: as A46.");
        Pair("A48", "track.transpose", "per-track playback transpose", t => t.Transpose = -2, t => $"transpose {t.Transpose}; tuning {string.Join(",", t.StringTunings)}", GfClass.MeaningPreserving, "A .gp has no playback transposition: the exporter bakes it into the tuning (or string + fret), so the sounding pitches survive and the field reads 0. The verifier already compares the baked song.");
        Pair("A49", "track.performer", "performer name", t => t.Performer = "Test player", t => $"performer '{t.Performer}'", GfClass.TabForgeGap, "Display-only, .tforge only.");
        Pair("A50", "track.trackNotes", "track notes", t => t.TrackNotes = "note", t => $"notes '{t.TrackNotes}'", GfClass.TabForgeGap, "Display-only, .tforge only.");
        Pair("A51", "track.instrumentName", "instrument name", t => t.InstrumentName = "My Guitar", t => $"instrument '{t.InstrumentName}'", GfClass.MeaningPreserving, "The importer names the instrument from the GM program.");
        Pair("A52", "track.drumMap", "drum-map preset", t => t.DrumMapPreset = "Custom", t => $"drum map '{t.DrumMapPreset}'", GfClass.TabForgeGap, "TabForge-only notation preset (.tforge only).");
        Pair("A53", "track.color", "track colour", t => t.ColorHex = "#12AB34", t => $"colour {t.ColorHex}", GfClass.TabForgeGap, "The colour survives (probed): the allowance was stale. Removed.");

        // ---- found by the Guitar Pro 8 comparison (fixture 05)
        Add(new("A54", "note.ghost + beat accent (found by the Guitar Pro 8 re-save)", "ghost note in an accented beat", "a chord of two notes, the first a ghost note, the beat accented",
            () => GfSong(1, t => { var chord = GfPut(t, 0, 0, 4, RtNote(t, 1, 3), RtNote(t, 2, 2)); chord.Notes[0].Ghost = true; chord.Accent = 1; }),
            s => { var b = GfBeats(s)[0]; return $"ghost {string.Join("/", b.Notes.OrderBy(n => n.StringIndex).Select(n => n.Ghost ? "yes" : "no"))}; accent {b.Accent}"; }, beatNote, GfClass.FormatLimitation,
            "Guitar Pro 8 keeps a ghost mark only on a note that has no <Accent> element at all. Measured in two Guitar Pro 8 re-saves: the file TabForge used to write (<AntiAccent>normal</AntiAccent> and <Accent>8</Accent> on the same note) came back with the accent and no ghost mark, and the probe ghost-combos.gp (--write-gp-probes: a chord of a ghost note and a plain note, the ghost note also carrying a mark) shows every <Accent> value replacing the ghost mark: staccato (1), heavy accent (4), accent (8), tenuto (16), staccato+accent (9) and staccato+tenuto (17) all kept their mark and lost the ghost mark; only the control beat, the ghost mark alone, kept it. All five marks are tried. The exporter now writes accent, heavy accent, tenuto and staccato on the other notes of the chord only; the ghost note keeps its mark. The one narrow loss: a beat whose notes are ALL ghost notes has nothing to carry the mark, so it reopens without it (the preflight lists it)",
            Status: "FIXED in the exporter (ghost wins on the ghost note, against accent, heavy accent, tenuto and staccato); loss narrowed to a beat of ghost notes only"));
        return c;
    }

    // ---------------------------------------------------------------- the record

    /// <summary>`TabForge.exe --gp-capability &lt;out.md&gt; [gp-folder]`: runs every case and writes the capability record. The "Guitar Pro 8 result" column is left empty on purpose.</summary>
    internal static int RunGpCapability(string outPath, string? gpFolder)
    {
        var folder = RtFolder();
        var sb = new StringBuilder();
        var cases = GfCases();
        var rows = new List<(GfCase Case, string Expected, string Xml, string Reimport, string? WithAudio, string? Error)>();
        if (gpFolder is not null) Directory.CreateDirectory(gpFolder);
        try
        {
            foreach (var cs in cases)
            {
                try
                {
                    var song = cs.Build();
                    var expectedSong = cs.Expect is null ? RtCopy(song) : cs.Expect(RtCopy(song));
                    var bytes = GuitarProExporter.ToBytes(song, embedProject: false);
                    if (gpFolder is not null) File.WriteAllBytes(Path.Combine(gpFolder, cs.Id + ".gp"), bytes);
                    var xml = GfSnip(GfGpif(bytes), cs.Xml);
                    var back = GfReopen(bytes, folder, cs.Id);
                    string? paired = null;
                    if (cs.Pair) paired = cs.Show(RtViaGpPair(song, folder, cs.Id));
                    rows.Add((cs, cs.Show(RtBakedTranspose(expectedSong)), xml, cs.Show(back), paired, null));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { rows.Add((cs, "", "", "", null, $"{ex.GetType().Name}: {ex.Message}")); }
            }
        }
        finally { RtCleanup(folder); }

        sb.AppendLine("<!-- generated by `TabForge.exe --gp-capability`; the class and cause of every row are in SelfTestGpFidelityCapability.cs next to its fixture -->");
        sb.AppendLine();
        sb.AppendLine("| ID | Allowance | Field | Class | Guitar Pro 8 result |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var r in rows) sb.AppendLine($"| {r.Case.Id} | `{r.Case.Allowance}` | {r.Case.Field} | {r.Case.Class} | {(Gp8Results.TryGetValue(r.Case.Id, out var g8) ? g8.Short : "")} |");
        sb.AppendLine();
        foreach (var r in rows)
        {
            sb.AppendLine($"### {r.Case.Id} `{r.Case.Allowance}`");
            sb.AppendLine();
            sb.AppendLine($"- Field or technique: {r.Case.Field}");
            sb.AppendLine($"- Minimal fixture: {r.Case.Fixture}");
            if (r.Error is not null) { sb.AppendLine($"- ERROR while probing: {r.Error}"); sb.AppendLine(); continue; }
            sb.AppendLine($"- Expected value: {r.Expected}");
            sb.AppendLine("- Exported representation (gpif):");
            sb.AppendLine("  ```xml");
            foreach (var line in r.Xml.Split('\n')) sb.AppendLine("  " + line);
            sb.AppendLine("  ```");
            sb.AppendLine($"- TabForge re-import result: {r.Reimport}" + (r.WithAudio is not null ? $" (with the .tfaudio beside it: {r.WithAudio})" : ""));
            sb.AppendLine($"- Cause: {r.Case.Cause}");
            sb.AppendLine($"- Class: {r.Case.Class}");
            if (r.Case.Status.Length > 0) sb.AppendLine($"- Status: {r.Case.Status}");
            sb.AppendLine("- Guitar Pro 8 result: " + (Gp8Results.TryGetValue(r.Case.Id, out var detail) ? detail.Detail : ""));
            sb.AppendLine();
        }
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Console.Out.WriteLine($"Wrote {outPath}: {rows.Count} cases, {rows.Count(r => r.Error is not null)} errors");
        return rows.Any(r => r.Error is not null) ? 1 : 0;
    }
}
