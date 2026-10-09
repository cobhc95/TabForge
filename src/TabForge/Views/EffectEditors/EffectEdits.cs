using TabForge.Models;

namespace TabForge.Views.EffectEditors;

// Owns: writing an edited curve into notes (bend) or beats (tremolo bar) and removing the effect (pure model changes).
// Does not own: the undo step (callers run these inside the editor's RunEdit, which is DocumentEdits.Run), selection, or the dialog.
// Tests: TestEffectEditors.
public static class EffectEdits
{
    /// <summary>Bend technique plus the curve; false when every note already holds exactly this.</summary>
    public static bool ApplyBend(IEnumerable<TabNote> notes, IReadOnlyList<BendPointModel> points)
    {
        var changed = false;
        foreach (var note in notes)
        {
            if (note.Techniques.Contains(TechniqueNames.Bend) && note.BendTypeName == "Custom" && SameCurve(note.BendPoints, points)) continue;
            note.Techniques.Add(TechniqueNames.Bend);
            note.BendPoints = points.Select(p => p.Clone()).ToList();
            note.BendTypeName = "Custom";
            note.BendStyleName = "";
            changed = true;
        }
        return changed;
    }

    public static bool CleanBend(IEnumerable<TabNote> notes)
    {
        var changed = false;
        foreach (var note in notes)
        {
            if (note.BendPoints.Count == 0 && !note.Techniques.Contains(TechniqueNames.Bend) && note.BendTypeName.Length == 0) continue;
            note.Techniques.Remove(TechniqueNames.Bend);
            note.BendPoints = new List<BendPointModel>();
            note.BendTypeName = "";
            note.BendStyleName = "";
            changed = true;
        }
        return changed;
    }

    /// <summary>The whammy-bar curve of each beat plus the tremolo-bar technique on its notes.</summary>
    public static bool ApplyTremolo(IEnumerable<TabCell> cells, IReadOnlyList<BendPointModel> points)
    {
        var changed = false;
        foreach (var cell in cells.Where(c => c.Notes.Count > 0))
        {
            var same = SameCurve(cell.WhammyPoints, points) && cell.Notes.All(n => n.Techniques.Contains(TechniqueNames.TremoloBar) && !HasShapeName(n));
            if (same) continue;
            cell.WhammyPoints = points.Select(p => p.Clone()).ToList();
            foreach (var note in cell.Notes) { RemoveTremolo(note); note.Techniques.Add(TechniqueNames.TremoloBar); }
            changed = true;
        }
        return changed;
    }

    public static bool CleanTremolo(IEnumerable<TabCell> cells)
    {
        var changed = false;
        foreach (var cell in cells)
        {
            if (cell.WhammyPoints.Count == 0 && !cell.Notes.Any(n => n.Techniques.Any(IsTremoloName))) continue;
            cell.WhammyPoints = new List<BendPointModel>();
            foreach (var note in cell.Notes) RemoveTremolo(note);
            changed = true;
        }
        return changed;
    }

    public static bool SameCurve(IReadOnlyList<BendPointModel> a, IReadOnlyList<BendPointModel> b) =>
        a.Count == b.Count && a.Zip(b).All(p => Math.Abs(p.First.Offset - p.Second.Offset) < 1e-6 && Math.Abs(p.First.Value - p.Second.Value) < 1e-6);

    private static bool IsTremoloName(string name) => name.StartsWith("TremBar", StringComparison.Ordinal);
    private static bool HasShapeName(TabNote note) => note.Techniques.Any(t => IsTremoloName(t) && t != TechniqueNames.TremoloBar);
    private static void RemoveTremolo(TabNote note) => note.Techniques.RemoveWhere(IsTremoloName);
}
