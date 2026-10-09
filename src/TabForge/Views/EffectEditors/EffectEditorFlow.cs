using System.Windows;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Views.EffectEditors;

/// <summary>What the effect editors need from their window.</summary>
internal interface IEffectEditorHost
{
    Window Owner { get; }
    AppSettings Settings { get; }
    /// <summary>The displayed editor's commands, or null with no editor.</summary>
    ScoreEditCommands? Edits { get; }
    void SaveSettings();
    void Say(string text);
}

// Owns: opening an effect editor for the selection, and turning OK / Clean into one edit (one undo step through the editor's RunEdit).
// Does not own: the dialog controls (CurveEffectDialog and ThemedEditorDialog), the point rules (EffectCurve), the model writes (EffectEdits).
// Tests: TestEffectEditors.
internal sealed partial class EffectEditorFlow
{
    // Bend: 0..+3 semitones. Tremolo bar: -6..+6 semitones. Values are quarter-tones.
    private const double BendMax = 6, TremoloRange = 12;

    private readonly IEffectEditorHost _host;

    public EffectEditorFlow(IEffectEditorHost host) => _host = host;

    /// <summary>Opens the editor of <paramref name="kind"/> for the selected notes; false when nothing changed. <paramref name="ask"/> replaces showing the dialog (tests).</summary>
    public bool Open(EffectEditorKind kind, Func<CurveEffectDialog, EditorAnswer>? ask = null)
    {
        if (_host.Edits is not { } edits) return false;
        var notes = edits.EffectNotes();
        var cells = edits.EffectCells();
        if (notes.Count == 0) { _host.Say(kind == EffectEditorKind.Bend ? "Select a note to edit its bend." : "Select a note to edit its tremolo bar."); return false; }
        var bend = kind == EffectEditorKind.Bend;
        var presets = new EffectPresetStore(_host.Settings);
        var existing = bend ? notes.Select(n => n.BendPoints).FirstOrDefault(p => p.Count > 0) : cells.Select(c => c.WhammyPoints).FirstOrDefault(p => p.Count > 0);
        var hasEffect = bend ? notes.Any(n => n.BendPoints.Count > 0 || n.Techniques.Contains(TechniqueNames.Bend))
            : cells.Any(c => c.WhammyPoints.Count > 0 || c.Notes.Any(n => n.Techniques.Contains(TechniqueNames.TremoloBar)));
        var start = existing ?? EffectPresetStore.BuiltIn(kind)[0].Points;
        var dialog = new CurveEffectDialog(kind, bend ? "Bend editor" : "Tremolo bar editor", bend ? 0 : -TremoloRange, bend ? BendMax : TremoloRange,
            start, presets, hasEffect, _host.SaveSettings);
        dialog.Dialog.Owner ??= _host.Owner;
        var answer = ask is null ? dialog.Dialog.Ask() : ask(dialog);
        if (answer == EditorAnswer.Cancel) return false;
        var points = dialog.Curve.Snapshot();
        var changed = edits.EditEffect(() => (bend, answer) switch
        {
            (true, EditorAnswer.Ok) => EffectEdits.ApplyBend(notes, points),
            (true, _) => EffectEdits.CleanBend(notes),
            (false, EditorAnswer.Ok) => EffectEdits.ApplyTremolo(cells, points),
            _ => EffectEdits.CleanTremolo(cells)
        });
        if (changed) _host.Say(answer == EditorAnswer.Ok ? (bend ? "Bend set." : "Tremolo bar set.") : (bend ? "Bend removed." : "Tremolo bar removed."));
        return changed;
    }
}
