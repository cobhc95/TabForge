using TabForge.Models;

namespace TabForge.Views.EffectEditors;

/// <summary>The grace note's transition into the principal note.</summary>
public enum GraceTransition { None, Bend, Slide, Hammer }

/// <summary>What the grace note editor sets. <see cref="Step"/> is the grace fret minus the principal note's fret; <see cref="Speed"/> is 16, 32 or 64.</summary>
public readonly record struct GraceSettings(int Step, bool Dead, bool BeforeBeat, int Speed, int Dynamic, GraceTransition Transition);

// Owns: writing the trill, grace-note and harmonic editors' results into notes and beats, and removing them (pure model changes).
// Does not own: the undo step (callers run these inside the editor's RunEdit), selection, the dialogs (OrnamentEditors) or the file formats.
// Tests: TestOrnamentEditors.
public static class OrnamentEdits
{
    public static readonly int[] TrillSpeeds = { 16, 32, 64 };
    public static readonly string[] HarmonicTags = { TechniqueNames.Harmonic, TechniqueNames.ArtificialHarmonic, "TapHarmonic", "PinchHarmonic", "SemiHarmonic" };
    private static readonly string[] GraceTags = { "GraceBefore", "GraceOnBeat", "GraceBend", TechniqueNames.LegatoSlide, TechniqueNames.Hopo };

    // ---- trill: TrillTargetMidi is the sounding target pitch, TrillDurationDenominator the speed ----

    /// <summary>Trill technique to the pitch <paramref name="step"/> frets above each note (same string), at 1/<paramref name="speed"/>.</summary>
    public static bool ApplyTrill(IEnumerable<TabNote> notes, int step, int speed, Func<int, int, int> pitchOf)
    {
        var changed = false;
        foreach (var note in notes)
        {
            var target = Math.Clamp(pitchOf(note.StringIndex, Math.Max(0, note.Fret + step)), 1, 127);
            if (note.Techniques.Contains(TechniqueNames.Trill) && note.TrillTargetMidi == target && note.TrillDurationDenominator == speed) continue;
            note.Techniques.Add(TechniqueNames.Trill);
            note.TrillTargetMidi = target;
            note.TrillDurationDenominator = speed;
            changed = true;
        }
        return changed;
    }

    public static bool CleanTrill(IEnumerable<TabNote> notes)
    {
        var changed = false;
        foreach (var note in notes)
        {
            if (!note.Techniques.Contains(TechniqueNames.Trill) && note.TrillTargetMidi == 0 && note.TrillDurationDenominator == 0) continue;
            note.Techniques.Remove(TechniqueNames.Trill);
            note.TrillTargetMidi = 0;
            note.TrillDurationDenominator = 0;
            changed = true;
        }
        return changed;
    }

    /// <summary>The trill's fret step above the note and its speed (16 / 32 / 64) as the dialog shows them.</summary>
    public static (int Step, int Speed) TrillOf(TabNote note, Func<int, int, int> pitchOf) =>
        (note.TrillTargetMidi > 0 ? note.TrillTargetMidi - pitchOf(note.StringIndex, note.Fret) : 2,
         TrillSpeeds.OrderBy(s => Math.Abs(s - (note.TrillDurationDenominator > 0 ? note.TrillDurationDenominator : 16))).First());

    // ---- grace note: a grace TabNote beside the beat's principal notes ----

    public static TabNote? GraceOf(TabCell cell) => cell.Notes.FirstOrDefault(n => n.IsGraceNote);
    /// <summary>The note a grace note belongs to: the one on <paramref name="onString"/> (the cursor string) when the beat has it, else the first.</summary>
    public static TabNote? PrincipalOf(TabCell cell, int? onString = null) =>
        cell.Notes.FirstOrDefault(n => !n.IsGraceNote && n.StringIndex == onString) ?? cell.Notes.FirstOrDefault(n => !n.IsGraceNote);
    public static double SlotsOf(int speed) => 16.0 / speed;

    public static GraceSettings GraceSettingsOf(TabCell cell, int? onString = null)
    {
        var principal = PrincipalOf(cell, onString);
        if (GraceOf(cell) is not { } g || principal is null) return new GraceSettings(-2, false, true, 32, Dynamics.NearestIndex(principal?.Velocity ?? Dynamics.Forte), GraceTransition.Hammer);
        var transition = g.Techniques.Contains("GraceBend") ? GraceTransition.Bend : g.Techniques.Contains(TechniqueNames.LegatoSlide) ? GraceTransition.Slide
            : g.Techniques.Contains(TechniqueNames.Hopo) ? GraceTransition.Hammer : GraceTransition.None;
        var speed = TrillSpeeds.OrderBy(s => Math.Abs(SlotsOf(s) - (g.GraceDurationSlots > 0 ? g.GraceDurationSlots : 0.5))).First();
        return new GraceSettings(g.Fret - principal.Fret, g.Dead, g.GraceBeforeBeat, speed, Dynamics.NearestIndex(g.Velocity), transition);
    }

    /// <summary>Adds or updates the beat's grace note (on the cursor string's note, else the first principal note's string); false when the beat already holds exactly it.</summary>
    public static bool ApplyGrace(TabCell cell, GraceSettings s, Func<int, int, int> pitchOf, int? onString = null)
    {
        if (PrincipalOf(cell, onString) is not { } principal) return false;
        var fret = Math.Max(0, principal.Fret + s.Step);
        var midi = pitchOf(principal.StringIndex, fret);
        var tags = new HashSet<string> { s.BeforeBeat ? "GraceBefore" : "GraceOnBeat" };
        if (s.Transition == GraceTransition.Bend) tags.Add("GraceBend");
        else if (s.Transition == GraceTransition.Slide) tags.Add(TechniqueNames.LegatoSlide);
        else if (s.Transition == GraceTransition.Hammer) tags.Add(TechniqueNames.Hopo);
        var velocity = Dynamics.Velocities[Math.Clamp(s.Dynamic, 0, Dynamics.Velocities.Length - 1)];
        var existing = GraceOf(cell);
        if (existing is not null && existing.StringIndex == principal.StringIndex && existing.Fret == fret && existing.MidiValue == midi && existing.Dead == s.Dead
            && existing.GraceBeforeBeat == s.BeforeBeat && Math.Abs(existing.GraceDurationSlots - SlotsOf(s.Speed)) < 1e-9 && existing.Velocity == velocity
            && GraceTags.Where(existing.Techniques.Contains).OrderBy(x => x).SequenceEqual(tags.OrderBy(x => x))) return false;
        var grace = existing ?? new TabNote();
        if (existing is null) cell.Notes.Add(grace);
        grace.IsGraceNote = true;
        grace.StringIndex = principal.StringIndex;
        grace.Fret = fret;
        grace.MidiValue = midi;
        grace.Dead = s.Dead;
        grace.GraceBeforeBeat = s.BeforeBeat;
        grace.GraceDurationSlots = SlotsOf(s.Speed);
        grace.Velocity = velocity;
        grace.Techniques.RemoveWhere(t => GraceTags.Contains(t));
        foreach (var tag in tags) grace.Techniques.Add(tag);
        return true;
    }

    public static bool CleanGrace(TabCell cell) => cell.Notes.RemoveAll(n => n.IsGraceNote) > 0;

    // ---- harmonic: one tag per note, plus HarmonicFret for artificial and tapped ----

    /// <summary>0 natural, 1 artificial, 2 tapped, 3 pinch, 4 semi; -1 when the note has no harmonic.</summary>
    public static int HarmonicTypeOf(TabNote note)
    {
        for (var i = 0; i < HarmonicTags.Length; i++) if (note.Techniques.Contains(HarmonicTags[i])) return i;
        return -1;
    }

    public static bool UsesHarmonicFret(int type) => type is 1 or 2;

    public static bool ApplyHarmonic(IEnumerable<TabNote> notes, int type, int fret)
    {
        type = Math.Clamp(type, 0, HarmonicTags.Length - 1);
        double? harmonicFret = UsesHarmonicFret(type) ? fret : null;
        var changed = false;
        foreach (var note in notes.Where(n => !n.Dead))   // a dead note takes no harmonic (GP5 k01); a harmonic note made dead keeps it (k02)
        {
            var only = HarmonicTags.Count(note.Techniques.Contains) == 1 && !note.Techniques.Contains("FeedbackHarmonic");
            if (only && HarmonicTypeOf(note) == type && note.HarmonicFret == harmonicFret) continue;
            RemoveHarmonic(note);
            note.Techniques.Add(HarmonicTags[type]);
            note.HarmonicFret = harmonicFret;
            changed = true;
        }
        return changed;
    }

    public static bool CleanHarmonic(IEnumerable<TabNote> notes)
    {
        var changed = false;
        foreach (var note in notes)
        {
            if (HarmonicTypeOf(note) < 0 && !note.Techniques.Contains("FeedbackHarmonic") && note.HarmonicFret is null) continue;
            RemoveHarmonic(note);
            changed = true;
        }
        return changed;
    }

    private static void RemoveHarmonic(TabNote note)
    {
        note.Techniques.RemoveWhere(t => HarmonicTags.Contains(t) || t == "FeedbackHarmonic");
        note.HarmonicFret = null;
    }
}
