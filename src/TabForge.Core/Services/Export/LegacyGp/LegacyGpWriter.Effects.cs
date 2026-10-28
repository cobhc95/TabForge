using AlphaTab.Model;

namespace TabForge.Services;

// Owns: the beat effects of the older binary formats (tap, slap, pop, fade in, strokes, pick direction, tremolo bar) and which string each note of a beat sits on.
// Does not own: the notes themselves (LegacyGpWriter.Notes) or the beats (LegacyGpWriter.Beats).
// Tests: TestLegacyGpExport.
internal sealed partial class LegacyGpWriter
{
    /// <summary>The effects of one beat in the shape the file stores them.</summary>
    private sealed class BeatFx
    {
        public int Flags1, Flags2;
        /// <summary>1 tap, 2 slap, 3 pop; 0 none.</summary>
        public int Slap;
        public int StrokeDown, StrokeUp, Pick;
        public List<BendPoint> Whammy = new();
        /// <summary>Tremolo picking marks (1 eighth, 2 sixteenth, 3 thirty-second) written on every note of the beat; 0 none.</summary>
        public int TremoloMarks;
        public bool Any => Flags1 != 0 || Flags2 != 0;
    }

    private const int StrokeSlot = 0x40, SlapSlot = 0x20, FadeSlot = 0x10;

    private BeatFx PlanEffects(Beat beat, List<(int String, Note Note)> placed)
    {
        var fx = new BeatFx();
        if (beat.Fade == FadeType.FadeIn || beat.FadeIn) fx.Flags1 |= FadeSlot;
        else if (beat.Fade is FadeType.FadeOut or FadeType.VolumeSwell) Hit("Fade out and volume swell", "not written");
        fx.Slap = beat.Tap ? 1 : beat.Slap ? 2 : beat.Pop ? 3 : 0;
        if (beat.DeadSlapped) Hit("Dead slapped notes", "not written");
        if (fx.Slap != 0) fx.Flags1 |= SlapSlot;
        if (beat.HasWhammyBar)
        {
            fx.Whammy = beat.WhammyBarPoints?.ToList() ?? new();
            if (V4) fx.Flags2 |= 0x04;
            else if (fx.Slap == 0) { fx.Flags1 |= SlapSlot; Hit("Tremolo bar shape", "only the depth of the dive is written in this format"); }
            else Hit("Tremolo bar together with tap, slap or pop", "the tremolo bar is left out in this format");
        }
        if (beat.BrushType != BrushType.None)
        {
            var speed = StrokeSpeed(beat.BrushDuration);
            if (beat.BrushType is BrushType.BrushUp or BrushType.ArpeggioUp) fx.StrokeUp = speed; else fx.StrokeDown = speed;
            fx.Flags1 |= StrokeSlot;
            if (beat.BrushType is BrushType.ArpeggioUp or BrushType.ArpeggioDown) Hit("Arpeggio strokes", "written as brush strokes");
        }
        if (beat.PickStroke != PickStroke.None)
        {
            if (V4) { fx.Pick = beat.PickStroke == PickStroke.Up ? 1 : 2; fx.Flags2 |= 0x02; }
            else Hit("Pick direction marks", "not written in this format");
        }
        if (beat.IsTremolo)
        {
            fx.TremoloMarks = (int)Math.Clamp(beat.TremoloPicking!.Marks, 1, 3);
            if (!V4) { fx.TremoloMarks = 0; Hit("Tremolo picking", "not written in this format"); }
        }
        if (!V4) PlanVersion3Marks(fx, placed);
        else if (placed.Any(x => x.Note.Vibrato == VibratoType.Wide)) fx.Flags1 |= 0x02;   // wide vibrato is a beat mark from 4.x on
        return fx;
    }

    /// <summary>3.x keeps vibrato and harmonics as beat marks that apply to every note of the beat.</summary>
    private void PlanVersion3Marks(BeatFx fx, List<(int String, Note Note)> placed)
    {
        var vibrato = placed.Select(x => x.Note.Vibrato).DefaultIfEmpty(VibratoType.None).Max();
        if (vibrato != VibratoType.None) fx.Flags1 |= vibrato == VibratoType.Wide ? 0x02 : 0x01;
        if (placed.Any(x => x.Note.Vibrato != vibrato)) Hit("Vibrato on part of a chord", "the vibrato applies to the whole beat in this format");
        var harmonics = placed.Where(x => x.Note.HarmonicType != HarmonicType.None).ToList();
        if (harmonics.Count == 0) return;
        fx.Flags1 |= harmonics[0].Note.HarmonicType == HarmonicType.Natural ? 0x04 : 0x08;
        if (harmonics.Count != placed.Count) Hit("Harmonics on part of a chord", "the harmonic applies to the whole beat in this format");
        if (harmonics.Any(x => x.Note.HarmonicType != harmonics[0].Note.HarmonicType)) Hit("Mixed harmonic types in one beat", "all notes get the first harmonic type");
        if (harmonics.Any(x => x.Note.HarmonicType is not (HarmonicType.Natural or HarmonicType.Artificial))) Hit("Pinch, tapped, semi and feedback harmonics", "written as artificial harmonics in this format");
    }

    private void WriteBeatEffects(BeatFx fx)
    {
        _w.Byte(fx.Flags1);
        if (V4) _w.Byte(fx.Flags2);
        if ((fx.Flags1 & SlapSlot) != 0)
        {
            if (V4) _w.Byte(fx.Slap);
            else
            {
                _w.Byte(fx.Slap);
                _w.Int(fx.Slap == 0 ? (int)Math.Round(-fx.Whammy.Select(p => p.Value).DefaultIfEmpty(0).Min() * 25) : 0);   // 3.x: the dive depth, or nothing for tap, slap and pop
            }
        }
        if (V4 && (fx.Flags2 & 0x04) != 0) WriteBendStructure(fx.Whammy, fx.Whammy.Any(p => p.Value < 0) ? 1 : 3);
        if ((fx.Flags1 & StrokeSlot) != 0) { if (V5) { _w.Byte(fx.StrokeUp); _w.Byte(fx.StrokeDown); } else { _w.Byte(fx.StrokeDown); _w.Byte(fx.StrokeUp); } }   // 5.x lists the up stroke first
        if (V4 && (fx.Flags2 & 0x02) != 0) _w.Byte(fx.Pick);
    }

    /// <summary>The stroke speed code (1 fastest ... 6 slowest) closest to the stroke length in ticks (960 to the quarter note); 4 when none is set.</summary>
    private static int StrokeSpeed(double ticks)
    {
        if (ticks <= 0) return 4;
        var best = 4;
        foreach (var (code, length) in new[] { (1, 30.0), (3, 60.0), (4, 120.0), (5, 240.0), (6, 480.0) })
            if (Math.Abs(ticks - length) < Math.Abs(ticks - StrokeLength(best))) best = code;
        return best;
    }

    private static double StrokeLength(int code) => code switch { 1 => 30, 2 => 30, 3 => 60, 4 => 120, 5 => 240, _ => 480 };

    /// <summary>A bend or tremolo-bar curve: a kind, its depth, and its points (position 0..12, value in 1/100 tone steps, vibrato off).</summary>
    private void WriteBendStructure(IList<BendPoint> points, int kind)
    {
        var peak = points.Select(p => Math.Abs(p.Value)).DefaultIfEmpty(0).Max();
        _w.Byte(kind); _w.Int((int)Math.Round(peak * 25)); _w.Int(points.Count);
        foreach (var point in points)
        {
            _w.Int((int)Math.Clamp(Math.Round(point.Offset / 5.0), 0, 12));
            _w.Int((int)Math.Round(point.Value * 25));
            _w.Bool(false);
        }
    }

    /// <summary>
    /// The notes of a beat with the string each is written on (1 = highest). A drum beat puts each sound on its own line. Notes on a string past the seventh,
    /// or a second note on a string already used in the beat, cannot be written and are reported.
    /// </summary>
    private List<(int String, Note Note)> Place(Beat beat, Track track)
    {
        var staff = StaffOf(track);
        var placed = new List<(int, Note)>();
        var used = new Dictionary<int, double>();
        foreach (var note in beat.Notes)
        {
            int gs;
            if (staff.IsPercussion)
            {
                gs = placed.Count + 1;
                if (gs > DrumStrings) { Hit("More than six drum sounds in one beat", "the extra sounds are left out"); continue; }
            }
            else
            {
                gs = staff.Tuning.Count - (int)note.String + 1;
                if (gs < 1) continue;
                if (gs > MaxStrings) { Hit("Notes on strings past the seventh", "left out"); continue; }
                if (!used.TryAdd(gs, note.Fret)) { if (used[gs] != note.Fret) Hit("Two notes on one string in a beat", "the second is left out"); continue; }   // an exact repeat of the same fret is one note
            }
            placed.Add((gs, note));
        }
        placed.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return placed;
    }
}
