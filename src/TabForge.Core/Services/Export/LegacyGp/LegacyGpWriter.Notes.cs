using AlphaTab.Model;

namespace TabForge.Services;

// Owns: the notes of the older binary formats: type, fret, dynamics, fingering and the note effects (bend, grace, slide, harmonic, trill, hammer-on, palm mute, vibrato ...).
// Does not own: the beat the notes sit in (LegacyGpWriter.Beats) or the beat effects (LegacyGpWriter.Effects).
// Tests: TestLegacyGpExport.
internal sealed partial class LegacyGpWriter
{
    /// <summary>A grace note written as the effect of the note it leads into: the grace beat, its note, and the fret on the string of the note it is attached to.</summary>
    private sealed record GraceInfo(Beat Beat, Note Note, int Fret);

    private const double TopFret = 36;

    private void WriteNotes(Beat beat, List<(int String, Note Note)> placed, List<Beat> graces, Staff staff, Track track, BeatFx fx)
    {
        var graceOf = MatchGraces(placed, graces, staff);
        foreach (var (_, note) in placed)
            WriteNote(beat, note, graceOf.GetValueOrDefault(note), staff, track, fx);
    }

    /// <summary>Attaches the notes of the grace beat before this beat to the notes of this beat: the same string when there is one, else the first note without a grace (the fret is moved to keep the pitch).</summary>
    private Dictionary<Note, GraceInfo> MatchGraces(List<(int String, Note Note)> placed, List<Beat> graces, Staff staff)
    {
        var map = new Dictionary<Note, GraceInfo>();
        if (graces.Count == 0) return map;
        if (graces.Count > 1) Hit("More than one grace beat before a note", "only the last grace beat is kept");
        var grace = graces[^1];
        foreach (var g in grace.Notes)
        {
            var from = staff.Tuning.Count - (int)g.String + 1;
            var target = staff.IsPercussion ? placed.FirstOrDefault(x => !map.ContainsKey(x.Note)) : placed.FirstOrDefault(x => x.String == from && !map.ContainsKey(x.Note));
            if (target.Note is null && !staff.IsPercussion) target = placed.FirstOrDefault(x => !map.ContainsKey(x.Note));
            var fret = staff.IsPercussion || target.Note is null ? (int)g.Fret : (int)Math.Round(g.Fret + Tuned(staff, from) - Tuned(staff, target.String));
            if (target.Note is null || staff.IsPercussion || fret < 0 || fret > TopFret) { Hit("Grace note that cannot sit on the note it leads into", "left out"); continue; }
            map[target.Note] = new GraceInfo(grace, g, fret);
        }
        return map;
    }

    private static double Tuned(Staff staff, int guitarString) => guitarString >= 1 && guitarString <= staff.Tuning.Count ? staff.Tuning[guitarString - 1] : 0;

    private static int DrumNumber(Note note, Track track)
    {
        var index = (int)note.PercussionArticulation;
        return index >= 0 && index < track.PercussionArticulations.Count ? (int)track.PercussionArticulations[index].OutputMidiNumber : index;
    }

    private static int DynamicByte(DynamicValue value) => (int)value is >= 0 and <= 7 ? (int)value + 1 : 6;

    private static int FingerByte(Fingers finger) => (int)finger is >= 0 and <= 4 ? (int)finger : 255;

    private void WriteNote(Beat beat, Note note, GraceInfo? grace, Staff staff, Track track, BeatFx fx)
    {
        var drums = staff.IsPercussion;
        var fret = Math.Clamp(drums ? DrumNumber(note, track) : (int)Math.Round(note.Fret), 0, drums ? 127 : 99);
        var dynamic = note.Dynamics == DynamicValue.F ? 0 : DynamicByte(note.Dynamics);
        var left = FingerByte(note.LeftHandFinger); var right = FingerByte(note.RightHandFinger);
        var fingered = left != 255 || right != 255;
        if (note.Accentuated == AccentuationType.Tenuto) Hit("Tenuto marks", "not written");
        if (note.IsLeftHandTapped) Hit("Left-hand tapping", "not written");
        var effects = PlanNoteEffects(beat, note, grace, fx);
        var flags = (note.Accentuated == AccentuationType.Heavy ? 0x02 : 0) | (note.IsGhost ? 0x04 : 0) | (effects.Any ? 0x08 : 0) | (dynamic != 0 ? 0x10 : 0) | 0x20
            | (note.Accentuated == AccentuationType.Normal ? 0x40 : 0) | (fingered ? 0x80 : 0);
        _w.Byte(flags);
        _w.Byte(note.IsDead ? 3 : note.IsTieDestination ? 2 : 1);
        if (dynamic != 0) _w.Byte(dynamic);
        _w.Byte(fret);
        if (fingered) { _w.Byte(left); _w.Byte(right); }
        if (V5) _w.Byte(0);
        if (effects.Any) WriteNoteEffects(effects, note, grace, fx);
    }

    /// <summary>The effect bits of one note (the single byte of 3.x, or the two of 4.x and 5.x).</summary>
    private sealed record NoteFx(int Flags1, int Flags2, int Slide, int HarmonicCode, bool Vibrato)
    {
        public bool Any => Flags1 != 0 || Flags2 != 0;
    }

    private NoteFx PlanNoteEffects(Beat beat, Note note, GraceInfo? grace, BeatFx fx)
    {
        var flags1 = ((note.BendPoints?.Count ?? 0) >= 2 ? 0x01 : 0) | (note.IsHammerPullOrigin ? 0x02 : 0) | (note.IsLetRing || beat.IsLetRing ? 0x08 : 0) | (grace is not null ? 0x10 : 0);
        var flags2 = 0; var slide = SlideCode(note); var harmonic = 0;
        if (!V4)
        {
            if (slide != 0) flags1 |= 0x04;
            if (note.IsPalmMute || beat.IsPalmMute) Hit("Palm mute", "not written in this format");
            if (note.IsStaccato) Hit("Staccato", "not written in this format");
            if (note.IsTrill) Hit("Trills", "not written in this format");
            if (note.SlideInType != SlideInType.None) Hit("Slides into a note", "not written in this format");
            return new NoteFx(flags1, 0, slide, 0, false);
        }
        if (note.IsStaccato) flags2 |= 0x01;
        if (note.IsPalmMute || beat.IsPalmMute) flags2 |= 0x02;
        if (fx.TremoloMarks > 0) flags2 |= 0x04;
        if (slide != 0) flags2 |= 0x08;
        if (note.HarmonicType != HarmonicType.None) { harmonic = HarmonicCode(note); flags2 |= 0x10; }
        if (note.IsTrill) flags2 |= 0x20;
        if (note.Vibrato != VibratoType.None) flags2 |= 0x40;
        return new NoteFx(flags1, flags2, slide, harmonic, note.Vibrato != VibratoType.None);
    }

    /// <summary>The slide byte: 4.x numbers the kinds (1 shift, 2 legato, 3 out downwards, 4 out upwards); 5.x adds bits for each, with slides into a note as 0x10 and 0x20.</summary>
    private int SlideCode(Note note)
    {
        var code = 0;
        switch (note.SlideOutType)
        {
            case SlideOutType.Shift: code = V5 ? 0x01 : 1; break;
            case SlideOutType.Legato: code = V5 ? 0x02 : 2; if (!V4) Hit("Legato slides", "written as shift slides in this format"); break;
            case SlideOutType.OutDown: code = V5 ? 0x04 : 3; if (!V4) Hit("Slides out downwards and upwards", "written as shift slides in this format"); break;
            case SlideOutType.OutUp: code = V5 ? 0x08 : 4; if (!V4) Hit("Slides out downwards and upwards", "written as shift slides in this format"); break;
            case SlideOutType.PickSlideDown or SlideOutType.PickSlideUp: Hit("Pick slides", "not written"); break;
        }
        if (V5 && note.SlideInType == SlideInType.IntoFromBelow) code |= 0x10;
        else if (V5 && note.SlideInType == SlideInType.IntoFromAbove) code |= 0x20;
        else if (!V5 && note.SlideInType != SlideInType.None) Hit("Slides into a note", "not written in this format");
        return code;
    }

    private int HarmonicCode(Note note)
    {
        switch (note.HarmonicType)
        {
            case HarmonicType.Natural: return 1;
            case HarmonicType.Tap: return 3;
            case HarmonicType.Pinch: return 4;
            case HarmonicType.Semi: return 5;
            case HarmonicType.Feedback: if (V4) Hit("Feedback harmonics", "written as artificial harmonics"); break;
        }
        if (V5) return 2;
        return Math.Abs(note.HarmonicValue - 19) < 1.5 ? 17 : Math.Abs(note.HarmonicValue - 24) < 1.5 ? 22 : 15;   // 4.x names the interval above the fretted note
    }

    private void WriteNoteEffects(NoteFx effects, Note note, GraceInfo? grace, BeatFx fx)
    {
        _w.Byte(effects.Flags1);
        if (V4) _w.Byte(effects.Flags2);
        if ((effects.Flags1 & 0x01) != 0) WriteBendStructure(note.BendPoints ?? new List<BendPoint>(), BendKind(note.BendType));
        if (grace is not null) WriteGrace(grace);
        if (!V4) return;
        if ((effects.Flags2 & 0x04) != 0) _w.Byte(fx.TremoloMarks);
        if ((effects.Flags2 & 0x08) != 0) _w.Byte(effects.Slide);
        if ((effects.Flags2 & 0x10) != 0) WriteHarmonic(effects.HarmonicCode, note);
        if ((effects.Flags2 & 0x20) != 0) { _w.Byte((int)Math.Clamp(Math.Round(note.TrillFret), 0, 99)); _w.Byte(TrillPeriod(note.TrillSpeed)); }
    }

    private static int TrillPeriod(Duration speed) => speed switch { Duration.ThirtySecond => 2, Duration.SixtyFourth => 3, _ => 1 };

    private static int BendKind(BendType type) => type switch
    {
        BendType.BendRelease => 2, BendType.Prebend or BendType.PrebendBend => 4, BendType.PrebendRelease or BendType.Release => 5, _ => 1,
    };

    private void WriteHarmonic(int code, Note note)
    {
        _w.Byte(code);
        if (!V5) return;
        if (code == 3) _w.Byte((int)Math.Clamp(Math.Round(note.Fret + note.HarmonicValue), 0, 99));   // tapped: the fret that is tapped
        else if (code == 2)
        {
            var sounding = (int)Math.Round(note.RealValueWithoutHarmonic + note.HarmonicPitch);   // artificial: the pitch of the harmonic as note, accidental and octave
            _w.Byte(((sounding % 12) + 12) % 12); _w.Byte(0); _w.Byte(Math.Max(0, sounding / 12));
        }
    }

    private void WriteGrace(GraceInfo grace)
    {
        var note = grace.Note;
        _w.Byte(grace.Fret);
        _w.Byte(note.Dynamics == DynamicValue.F ? 6 : DynamicByte(note.Dynamics));
        _w.Byte(note.SlideOutType != SlideOutType.None ? 1 : note.IsHammerPullOrigin ? 3 : (note.BendPoints?.Count ?? 0) >= 2 ? 2 : 0);   // how it moves into the note
        _w.Byte(grace.Beat.Duration switch { Duration.SixtyFourth => 1, Duration.Sixteenth => 3, _ => 2 });                    // 1 sixty-fourth, 2 thirty-second, 3 sixteenth
        if (V5) _w.Byte((note.IsDead ? 0x01 : 0) | (grace.Beat.GraceType == GraceType.OnBeat ? 0x02 : 0));
        if (grace.Beat.Duration is not (Duration.SixtyFourth or Duration.ThirtySecond or Duration.Sixteenth)) Hit("Grace notes of other lengths", "written as a thirty-second");
    }
}
