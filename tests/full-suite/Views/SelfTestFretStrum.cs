using TabForge.Models;
using TabForge.Playback;
using TabForge.Visualization;

namespace TabForge;

/// <summary>A strummed chord on the fretboard shows one stroke arrow with one merged tag pill; a plain chord keeps its per-note tags.</summary>
public static partial class SelfTest
{
    private static void TestFretStrumArrow()
    {
        TabNote N(int s, params string[] t) { var n = new TabNote { StringIndex = s, Fret = 2 }; foreach (var x in t) n.Techniques.Add(x); return n; }
        var down = new List<TabNote> { N(5, "BrushDown", "PalmMute"), N(4, "PalmMute"), N(3, "LetRing"), N(1) };
        Check("brush down chord is a down-stroke", TechniqueTag.StrumOf(down) == 1);
        Check("pick up chord is an up-stroke", TechniqueTag.StrumOf(new List<TabNote> { N(5, "PickUp"), N(4) }) == -1);
        Check("rasgueado strums down", TechniqueTag.StrumOf(new List<TabNote> { N(5, "Rasgueado"), N(4) }) == 1);
        Check("plain chord is not strummed", TechniqueTag.StrumOf(new List<TabNote> { N(5, "PalmMute"), N(4) }) == 0);
        Check("a single note is not a chord", TechniqueTag.StrumOf(new List<TabNote> { N(5, "BrushDown") }) == 0);

        VisualNote V(TabNote n, sbyte strum) => new() { StringIndex = n.StringIndex, Role = VisualRole.Current, Technique = TechniqueTag.From(n.Techniques), Strum = strum };
        var group = FretboardStrum.Group(down.Select(n => V(n, 1)).ToList());
        Check("one down arrow spans strings 1..5", group is { Down: true, HighString: 1, LowString: 5, Role: VisualRole.Current });
        Check($"tags merged once: '{group?.Tags}'", group?.Tags == "P.M. L.R.");
        Check("plain chord draws no arrow", FretboardStrum.Group(down.Select(n => V(n, 0)).ToList()) is null);
    }
}
