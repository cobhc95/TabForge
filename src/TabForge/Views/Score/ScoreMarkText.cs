using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Text.RegularExpressions;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

/// <summary>Text and numbers the score editor derives from notes: technique and harmonic labels, bend and whammy amounts, fret label widths, direction text.</summary>
internal static class ScoreMarkText
{
    internal const double HarmonicRowHeight = 10;

    private static readonly Regex CamelWords = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

    /// <summary>Techniques the TAB draws as geometry instead of a text label above the beat.</summary>
    internal static readonly HashSet<string> GeometryTechniques = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bend", "TremBar", "TremBarWide", "TremBarCustom", "TremBarDive", "TremBarDip", "TremBarHold", "TremBarPredive",
        "TremBarPrediveDive", "Trill", "TremoloPick", "Ghost", "Dead", "LetRing", "WahOpen", "WahClose",
        "BrushDown", "BrushUp", "ArpeggioDown", "ArpeggioUp", "Rasgueado", "PickDown", "PickUp",
        "GraceBefore", "GraceOnBeat", "GraceBend", "Harmonic", "ArtificialHarmonic", "PinchHarmonic", "TapHarmonic",
        "SemiHarmonic", "FeedbackHarmonic"
    };

    /// <summary>Text label still printed above a TAB beat (what the geometry does not already show).</summary>
    internal static string DrawnTechniqueLabel(IEnumerable<TabNote> notes, bool harmonics = true) =>
        string.Join(" ", notes.SelectMany(note => note.Techniques
                .Where(t => !IsPalmMute(t) && !GeometryTechniques.Contains(t) && !TabSlideNotation.IsRenderedAsGeometry(t) &&
                            !t.Equals("FadeIn", StringComparison.OrdinalIgnoreCase) && !t.Equals("FadeOut", StringComparison.OrdinalIgnoreCase))
                .Select(ShortTechnique))
            .Where(label => label.Length > 0).Distinct()
            .Concat(harmonics ? notes.Select(n => HarmonicCaption(n.Techniques)).Where(label => label.Length > 0) : Array.Empty<string>())
            .Concat(notes.Any(HasTapTechnique) ? new[] { "T" } : Array.Empty<string>()).Distinct());

    /// <summary>Harmonic caption: "Harm." for a natural harmonic, else the specific kind (never both).</summary>
    internal static string HarmonicCaption(ISet<string> techniques) =>
        techniques.Contains("ArtificialHarmonic") ? "A.H." :
        techniques.Contains("PinchHarmonic") ? "P.H." :
        techniques.Contains("TapHarmonic") ? "T.H." :
        techniques.Contains("SemiHarmonic") ? "S.H." :
        techniques.Contains("FeedbackHarmonic") ? "F.B." :
        techniques.Contains("Harmonic") ? "Harm." : "";

    /// <summary>Conventional amount text for a bend value in quarter-tones: 1/4, 1/2, 3/4, full, 1 1/2, 2 ...</summary>
    internal static string BendAmountLabel(double quarterTones, string fullWord = "full")
    {
        var v = (int)Math.Round(Math.Abs(quarterTones));
        var whole = v / 4;
        var frac = (v % 4) switch { 1 => "1/4", 2 => "1/2", 3 => "3/4", _ => "" };
        if (frac.Length == 0) return whole == 0 ? "" : whole == 1 ? fullWord : whole.ToString(CultureInfo.InvariantCulture);
        return whole == 0 ? frac : $"{whole} {frac}";
    }

    /// <summary>Whammy value text: "-1/2", "-1", "-1 1/2" (dives are negative).</summary>
    internal static string WhammyAmountLabel(double quarterTones)
    {
        var text = BendAmountLabel(quarterTones, "1");
        return text.Length == 0 ? "0" : (quarterTones < 0 ? "-" : "+") + text;
    }

    /// <summary>The bend curve to draw: the imported points, or a plausible default for hand-entered bends.</summary>
    internal static IReadOnlyList<BendPointModel> EffectiveBendPoints(TabNote note)
    {
        if (note.BendPoints.Count > 0) return note.BendPoints.OrderBy(p => p.Offset).ToList();
        BendPointModel P(double o, double v) => new() { Offset = o, Value = v };
        return note.BendTypeName switch
        {
            "Prebend" => new[] { P(0, 4), P(60, 4) },
            "PrebendBend" => new[] { P(0, 4), P(20, 8), P(60, 8) },
            "PrebendRelease" => new[] { P(0, 4), P(60, 0) },
            "Release" => new[] { P(0, 4), P(30, 4), P(60, 0) },
            "BendRelease" => new[] { P(0, 0), P(15, 4), P(35, 4), P(50, 0) },
            _ => new[] { P(0, 0), P(20, 4), P(60, 4) }
        };
    }

    /// <summary>Whammy curve for a beat: the imported points, or a default dip/dive from the technique name.</summary>
    internal static IReadOnlyList<BendPointModel> EffectiveWhammyPoints(TabCell cell)
    {
        if (cell.WhammyPoints.Count > 0) return cell.WhammyPoints.OrderBy(p => p.Offset).ToList();
        BendPointModel P(double o, double v) => new() { Offset = o, Value = v };
        var names = cell.Notes.SelectMany(n => n.Techniques).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Contains("TremBarDive")) return new[] { P(0, 0), P(60, -4) };
        if (names.Contains("TremBarHold")) return new[] { P(0, -4), P(60, -4) };
        if (names.Contains("TremBarPredive")) return new[] { P(0, -4), P(60, 0) };
        return new[] { P(0, 0), P(30, -4), P(60, 0) };
    }

    /// <summary>Slashes on the stem for tremolo picking: 1/8 = 1, 1/16 = 2, 1/32 and faster = 3; 0 for none.</summary>
    internal static int TremoloSlashCount(TabCell cell)
    {
        var picked = cell.TremoloPickDenominator > 0 || cell.Notes.Any(n => n.Techniques.Contains("TremoloPick"));
        if (!picked) return 0;
        var d = cell.TremoloPickDenominator > 0 ? cell.TremoloPickDenominator : 16;
        return d >= 32 ? 3 : d >= 16 ? 2 : 1;
    }

    /// <summary>Fret of an imported trill's upper note on the note's own string, or -1 when unknown.</summary>
    internal static int TrillFret(TrackModel track, TabNote note)
    {
        if (note.TrillTargetMidi <= 0 || note.StringIndex < 0 || note.StringIndex >= track.StringTunings.Count) return -1;
        var fret = note.TrillTargetMidi - track.StringTunings[note.StringIndex];
        return fret is >= 0 and <= 36 ? fret : -1;
    }

    /// <summary>
    /// A double-stop bend (the same bend on several strings of one beat) is one arrow from the upper string with one
    /// label: true for every bent note except the topmost one.
    /// </summary>
    internal static bool IsRepeatedDoubleStopBend(TabCell cell, TabNote note)
    {
        var bent = cell.Notes.Where(n => n.Techniques.Contains("Bend") || n.BendPoints.Count > 0 && !n.IsGraceNote).ToList();
        if (bent.Count < 2 || !bent.Contains(note)) return false;
        var top = bent.OrderBy(n => n.StringIndex).First();
        if (ReferenceEquals(top, note)) return false;
        var a = EffectiveBendPoints(top);
        var b = EffectiveBendPoints(note);
        return a.Count == b.Count && a.Zip(b).All(pair => Math.Abs(pair.First.Offset - pair.Second.Offset) < 0.5 && Math.Abs(pair.First.Value - pair.Second.Value) < 0.05);
    }

    /// <summary>True when an identical amount label already drawn in this bar really overlaps <paramref name="box"/>
    /// (shared area, not just touching edges as <see cref="Rect.IntersectsWith"/> allows): only then is the repeat left out.</summary>
    internal static bool DuplicateBendLabel(IReadOnlyList<(string Text, Rect Box)> drawn, string text, Rect box)
    {
        foreach (var (t, b) in drawn)
            if (t == text && b.Left < box.Right && box.Left < b.Right && b.Top < box.Bottom && box.Top < b.Bottom) return true;
        return false;
    }

    /// <summary>
    /// What the reference prints under the TAB for a harmonic (per its own render): the sounding pitch name for an artificial harmonic
    /// ("E"), the tapped fret for a tap harmonic ("17", one decimal for 5.8 and the like); nothing for natural, pinch and semi.
    /// </summary>
    internal static string HarmonicFretText(TabNote note)
    {
        if (note.Techniques.Contains("ArtificialHarmonic") && note.MidiValue > 0)
            return TabForge.Services.MusicTheoryService.NoteNames[((note.MidiValue % 12) + 12) % 12];
        if (!note.Techniques.Contains("TapHarmonic") || note.HarmonicFret is not { } fret || fret <= 0) return "";
        return Math.Abs(fret - Math.Round(fret)) < 0.05
            ? Math.Round(fret).ToString(CultureInfo.InvariantCulture)
            : fret.ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>Whether a grace note beside a main note gets a transition mark (arc / bend arrow) to it; drum flams never do.</summary>
    internal static bool GraceTransitionShown(TrackModel track, TabCell cell, TabNote grace) =>
        track.Kind != TrackKind.Drums && grace.IsGraceNote &&
        (grace.Techniques.Contains("Slide") || grace.Techniques.Contains("ShiftSlide") || grace.Techniques.Contains("LegatoSlide") ||
         grace.Techniques.Contains("HOPO") || grace.Techniques.Contains("HOPOOrigin") ||
         grace.Techniques.Contains("GraceBend") || cell.Notes.Any(o => o.Techniques.Contains("GraceBend")));

    /// <summary>The fret text as drawn for width purposes: X for a dead note, the number, in brackets for a ghost note (lines and curves stop at its edge, not inside it).</summary>
    internal static string FretLabelWidthText(TabNote note) =>
        note.Dead ? "X" : note.Ghost ? "(" + note.Fret.ToString(CultureInfo.InvariantCulture) + ")" : note.Fret.ToString(CultureInfo.InvariantCulture);

    /// <summary>How far below the last string the beat's harmonic / fingering marks actually reach (the rings end 26 px down, the right-hand letters under them 37 px).</summary>
    /// <summary>Beat text above the staff: a little larger than the old 9 pt, and mixed toward the ink so it reads as well as other secondary text.</summary>
    internal const double BeatTextSize = 10.75;
    internal static System.Windows.Media.Color BeatTextColor(System.Windows.Media.Color ink, System.Windows.Media.Color faint) => System.Windows.Media.Color.FromRgb(
        (byte)((ink.R + faint.R * 2) / 3), (byte)((ink.G + faint.G * 2) / 3), (byte)((ink.B + faint.B * 2) / 3));

    /// <summary>Extra gap between the bottom-string fret number and the pick-stroke mark; marks and lyrics under it move down by this much.</summary>
    internal const double PickStrokeLift = 5;

    internal static double PickStrokeShift(TabCell cell) =>
        cell.Notes.Any(n => n.Techniques.Contains("PickDown") || n.Techniques.Contains("PickUp")) ? PickStrokeLift : 0;

    internal static double FingeringExtent(TabCell cell)
    {
        var left = cell.Notes.Any(n => n.LeftHandFinger is >= 0 and <= 4);
        var right = cell.Notes.Any(n => n.RightHandFinger is >= 0 and <= 4);
        var extent = cell.Notes.Any(n => HarmonicFretText(n).Length > 0) ? 19.0 : 0;
        if (left) extent = Math.Max(extent, 26);
        if (right) extent = Math.Max(extent, left ? 37 : 23);
        return extent > 0 ? extent + PickStrokeShift(cell) : 0;
    }

    /// <summary>Vertical room under the TAB taken by fingering marks (lyrics move down by this much).</summary>
    internal static double FingeringHeight(TabCell cell) => PickStrokeShift(cell) +
        (cell.Notes.Any(n => n.LeftHandFinger is >= 0 and <= 4) ? (cell.Notes.Any(n => n.RightHandFinger is >= 0 and <= 4) ? 22 : 14)
        : cell.Notes.Any(n => n.RightHandFinger is >= 0 and <= 4) ? 11 : 0)
        + (cell.Notes.Any(n => HarmonicFretText(n).Length > 0) ? HarmonicRowHeight : 0);

    /// <summary>The short engraved text of a technique, from the technique table; "" when it has none (a mark drawn as geometry) or the name is unknown.</summary>
    internal static string ShortTechnique(string t) => TechniqueInfo.MarkOf(t);

    internal static bool HasTapTechnique(TabNote note) =>
        note.Techniques.Contains("Tapping") || note.Techniques.Contains("LeftTap");

    internal static string TechniqueLabel(IEnumerable<TabNote> notes, bool includeFade = true)
    {
        var noteList = notes.ToList();
        var labels = noteList.SelectMany(note => note.Techniques.Where(technique => !IsPalmMute(technique) &&
                    !TabSlideNotation.IsRenderedAsGeometry(technique) &&
                    (includeFade || (!technique.Equals("FadeIn", StringComparison.OrdinalIgnoreCase) &&
                                     !technique.Equals("FadeOut", StringComparison.OrdinalIgnoreCase))))
                .Select(technique => technique == "Bend" ? BendLabel(note.BendTypeName) : ShortTechnique(technique)))
            .Where(label => label.Length > 0).Distinct().ToList();
        if (noteList.Any(HasTapTechnique) && !labels.Contains("T")) labels.Add("T");
        return string.Join(" ", labels);
    }

    internal static string BendLabel(string bendType) => bendType switch
    {
        "Prebend" => "P.B.",
        "Release" => "R",
        "BendRelease" => "b/R",
        "PrebendBend" => "P.B./b",
        "PrebendRelease" => "P.B./R",
        "Hold" => "H",
        _ => "b"
    };

    internal static bool IsPalmMute(string technique) => TechniqueNames.IsPalmMute(technique);

    /// <summary>The note shows P.M.: a tied continuation never does (as in the reference), even when it carries the mark.</summary>
    internal static bool ShowsPalmMute(TabNote note) => !note.Tied && note.Techniques.Any(IsPalmMute);

    /// <summary>the standard swing indicator, e.g. "(♫ = ♩♪)".</summary>
    internal static string SwingSymbol(string feel) => feel == TripletFeels.Sixteenth ? "(♬ = ♪♬)" : "(♫ = ♩♪)";

    /// <summary>Navigation directions above a bar: the segno / coda signs and the Fine / D.C. / D.S. texts, right-aligned.</summary>
    /// <summary>the standard wording for a direction name from the file: "TargetSegno" is the sign, "JumpDalSegnoAlFine" is "D.S. al Fine".</summary>
    internal static (string Text, bool IsSign, bool AtLeft) DirectionText(string raw)
    {
        var name = raw.Trim();
        if (name.StartsWith("Target", StringComparison.OrdinalIgnoreCase)) name = name[6..];
        else if (name.StartsWith("Jump", StringComparison.OrdinalIgnoreCase)) name = name[4..];
        return name.ToLowerInvariant() switch
        {
            "segno" => ("\U0001D10B", true, true),
            "segnosegno" => ("\U0001D10B\U0001D10B", true, true),
            "coda" => ("\U0001D10C", true, true),
            "doublecoda" => ("\U0001D10C\U0001D10C", true, true),
            "fine" => ("fine", false, true),
            "dacapo" => ("D.C.", false, false),
            "dacapoalcoda" => ("D.C. al Coda", false, false),
            "dacapoaldoublecoda" => ("D.C. al Double Coda", false, false),
            "dacapoalfine" => ("D.C. al Fine", false, false),
            "dalsegno" => ("D.S.", false, false),
            "dalsegnoalcoda" => ("D.S. al Coda", false, false),
            "dalsegnoaldoublecoda" => ("D.S. al Double Coda", false, false),
            "dalsegnoalfine" => ("D.S. al Fine", false, false),
            "dalsegnosegno" => ("D.S.S.", false, false),
            "dalsegnosegnoalcoda" => ("D.S.S. al Coda", false, false),
            "dalsegnosegnoaldoublecoda" => ("D.S.S. al Double Coda", false, false),
            "dalsegnosegnoalfine" => ("D.S.S. al Fine", false, false),
            "dacoda" => ("To Coda", false, false),
            "dadoublecoda" => ("To Double Coda", false, false),
            _ => (raw.Trim(), false, false)
        };
    }

    /// <summary>"PalmMute" -> "palm mute" (the origin/destination bookkeeping flags of hammer-ons are not spoken).</summary>
    internal static string TechniqueText(TabNote note)
    {
        if (note.Techniques.Count == 0) return "";
        var names = new List<string>();
        foreach (var t in note.Techniques.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (t.Equals(TechniqueNames.HopoOrigin, StringComparison.OrdinalIgnoreCase) ||
                t.Equals(TechniqueNames.HopoDestination, StringComparison.OrdinalIgnoreCase)) continue;
            if (t.Equals(TechniqueNames.PalmMuteLegacy, StringComparison.OrdinalIgnoreCase)) { names.Add("palm mute"); continue; }
            if (t.Equals(TechniqueNames.Hopo, StringComparison.OrdinalIgnoreCase)) { names.Add("hammer-on or pull-off"); continue; }
            names.Add(CamelWords.Replace(t, " ").ToLowerInvariant());
        }
        return string.Join(", ", names.Distinct());
    }

    /// <summary>"quarter note", "dotted eighth note", "eighth triplet" ... for the beat's written duration.</summary>
    internal static string DurationName(TabCell cell)
    {
        var name = cell.DurationDenominator switch
        {
            1 => "whole", 2 => "half", 4 => "quarter", 8 => "eighth", 16 => "sixteenth", 32 => "thirty-second", 64 => "sixty-fourth",
            var d => $"1/{d}"
        };
        var dots = cell.Dots == 1 ? "dotted " : cell.Dots >= 2 ? "double-dotted " : "";
        return dots + name + (cell.IsRest ? " rest" : " note") + (cell.Tuplet.Numerator > 0 ? $" in a {cell.Tuplet.Numerator}-tuplet" : "");
    }
}
