using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.EffectEditors;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Opens the bend or tremolo-bar curve editor for the score's selection (or the cursor's note) and applies its answer as one undo step.
/// Owns: the open rules (which notes and beats, the start curve) and mapping the dialog's answer to the Core EffectEdits writes.
/// Does not own: the dialog (CurveEffectWindow), the curve rules (EffectCurve), the writes (EffectEdits), or the undo step (ScorePreviewEditor.RunEdit).
/// Tests: TabForge.Avalonia.Tests.EffectCurveEditorTests.
/// </summary>
public static class EffectCurveFlow
{
    /// <summary>Opens the editor of <paramref name="kind"/> (Bend or TremoloBar); false when nothing changed or nothing is selected.</summary>
    public static async Task<bool> Open(ScorePreviewView score, Window owner, AvaloniaDialogHost host, Func<AppSettings> settings,
        Action saveSettings, EffectEditorKind kind)
    {
        var edits = score.Editor.Effects;
        var notes = edits.EffectNotes();
        var cells = edits.EffectCells();
        if (notes.Count == 0) return false;
        var bend = kind == EffectEditorKind.Bend;
        var existing = bend ? notes.Select(n => n.BendPoints).FirstOrDefault(p => p.Count > 0) : cells.Select(c => c.WhammyPoints).FirstOrDefault(p => p.Count > 0);
        var hasEffect = bend
            ? notes.Any(n => n.BendPoints.Count > 0 || n.Techniques.Contains(TechniqueNames.Bend))
            : cells.Any(c => c.WhammyPoints.Count > 0 || c.Notes.Any(n => n.Techniques.Contains(TechniqueNames.TremoloBar)));
        var start = existing ?? EffectPresetStore.BuiltIn(kind)[0].Points;
        var dialog = new CurveEffectWindow(kind, bend ? "Bend editor" : "Tremolo bar editor", bend ? 0 : EffectCurve.TremoloMinValue,
            bend ? EffectCurve.BendMaxValue : EffectCurve.TremoloMaxValue, start, new EffectPresetStore(settings()), hasEffect, saveSettings,
            bend ? edits.PreviousBendEnd() : edits.PreviousWhammyEnd());
        CurveEffectAnswer? answer = await host.ShowModal<CurveEffectAnswer?>(dialog, owner);
        if (answer is null or CurveEffectAnswer.Cancel) return false;
        var points = dialog.Curve.Snapshot();
        var apply = answer.Value;
        return score.Edit(editor => editor.Effects.EditEffect(() => (bend, apply) switch
        {
            (true, CurveEffectAnswer.Ok) => EffectEdits.ApplyBend(notes, points),
            (true, _) => EffectEdits.CleanBend(notes),
            (false, CurveEffectAnswer.Ok) => EffectEdits.ApplyTremolo(cells, points),
            _ => EffectEdits.CleanTremolo(cells),
        }));
    }
}
