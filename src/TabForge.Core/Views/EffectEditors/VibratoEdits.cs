using TabForge.Models;

namespace TabForge.Views.EffectEditors;

// Owns: writing the vibrato settings window's result (normal or wide, own depth and speed) into notes, and reading it back (pure model changes).
// Does not own: the undo step (callers run this inside the editor's EditEffect), the window (VibratoEditorWindow) or the playback arithmetic.
// Tests: TabForge.Avalonia.Tests.VibratoSettingsTests.
public static class VibratoEdits
{
    /// <summary>True when the note carries vibrato of either kind.</summary>
    public static bool Has(TabNote note) => note.Techniques.Contains(TechniqueNames.Vibrato) || note.Techniques.Contains(TechniqueNames.WideVibrato);

    /// <summary>Sets the kind on every note and the depth and speed; a value equal to <paramref name="defaultDepth"/> or <paramref name="defaultSpeed"/> is stored as null (follows the settings).</summary>
    public static bool Apply(IEnumerable<TabNote> notes, bool wide, double depth, double speed, double defaultDepth, double defaultSpeed)
    {
        double? ownDepth = Math.Abs(depth - defaultDepth) < 0.005 ? null : Math.Round(depth, 2);
        double? ownSpeed = Math.Abs(speed - defaultSpeed) < 0.05 ? null : Math.Round(speed, 1);
        var changed = false;
        foreach (var note in notes)
        {
            var kind = wide ? TechniqueNames.WideVibrato : TechniqueNames.Vibrato;
            var other = wide ? TechniqueNames.Vibrato : TechniqueNames.WideVibrato;
            if (note.Techniques.Contains(kind) && !note.Techniques.Contains(other) && note.VibratoDepth == ownDepth && note.VibratoSpeed == ownSpeed) continue;
            note.Techniques.Remove(other);
            note.Techniques.Add(kind);
            note.VibratoDepth = ownDepth;
            note.VibratoSpeed = ownSpeed;
            changed = true;
        }
        return changed;
    }

    /// <summary>Removes vibrato (and its own depth and speed) from the notes.</summary>
    public static bool Clean(IEnumerable<TabNote> notes)
    {
        var changed = false;
        foreach (var note in notes)
        {
            if (!Has(note) && note.VibratoDepth is null && note.VibratoSpeed is null) continue;
            note.Techniques.Remove(TechniqueNames.Vibrato);
            note.Techniques.Remove(TechniqueNames.WideVibrato);
            note.VibratoDepth = null;
            note.VibratoSpeed = null;
            changed = true;
        }
        return changed;
    }
}
