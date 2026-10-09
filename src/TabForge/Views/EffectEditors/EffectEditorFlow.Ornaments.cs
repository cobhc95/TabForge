using TabForge.Models;

namespace TabForge.Views.EffectEditors;

// Owns: opening the trill, grace-note and harmonic editors for the selection and turning OK / Clean into one edit (one undo step).
// Does not own: the dialogs (OrnamentEditors), the model writes (OrnamentEdits) or the curve editors (EffectEditorFlow.Open).
// Tests: TestOrnamentEditors.
internal sealed partial class EffectEditorFlow
{
    /// <summary>The editor a command id opens ("Note.BendEditor", "Note.TrillEditor"...), or null.</summary>
    public static EffectEditorKind? KindOf(string commandId) =>
        commandId switch
        {   // the plain technique hotkeys open the same editor the palette button does
            "Note.Bend" => EffectEditorKind.Bend, "Note.TremoloBar" => EffectEditorKind.TremoloBar, "Note.Trill" => EffectEditorKind.Trill,
            "Note.Grace" => EffectEditorKind.Grace, "Note.Harmonic" => EffectEditorKind.Harmonic,
            _ => EditorSuffixKind(commandId),
        };

    private static EffectEditorKind? EditorSuffixKind(string commandId) =>
        commandId.StartsWith("Note.", StringComparison.Ordinal) && commandId.EndsWith("Editor", StringComparison.Ordinal) && commandId.Length > 11
        && Enum.TryParse<EffectEditorKind>(commandId[5..^6], out var kind) ? kind : null;

    /// <summary>The harmonic hotkey on notes that already carry a harmonic removes it (one undo step), like the other effect toggles;
    /// true when it did. Adding a harmonic, the palette button and the editor command open the editor.</summary>
    public bool ToggledOff(string commandId)
    {
        if (commandId != "Note.Harmonic" || _host.Edits is not { } edits) return false;
        var notes = edits.EffectNotes();
        if (!notes.Any(n => OrnamentEdits.HarmonicTypeOf(n) >= 0 || n.Techniques.Contains("FeedbackHarmonic"))) return false;
        if (edits.EditEffect(() => OrnamentEdits.CleanHarmonic(notes))) _host.Say("Harmonic removed.");
        return true;
    }

    /// <summary>Opens the trill, grace-note or harmonic editor; false when nothing changed. <paramref name="ask"/> replaces showing the dialog (tests).</summary>
    public bool OpenOrnament(EffectEditorKind kind, Func<ValuesEffectDialog, EditorAnswer>? ask = null)
    {
        if (_host.Edits is not { } edits || edits.EffectTrack is not { } track) return false;
        var notes = edits.EffectNotes();
        var cells = edits.EffectCells().Where(c => OrnamentEdits.PrincipalOf(c) is not null).ToList();
        var noun = kind switch { EffectEditorKind.Trill => "trill", EffectEditorKind.Grace => "grace note", _ => "harmonic" };
        if (kind == EffectEditorKind.Grace ? cells.Count == 0 : notes.Count == 0) { _host.Say($"Select a note to edit its {noun}."); return false; }
        if (kind == EffectEditorKind.Harmonic && notes.All(n => n.Dead)) { _host.Say("A dead note takes no harmonic."); return false; }   // GP5 k01
        Func<int, int, int> pitchOf = track.PitchOf;
        int? onString = notes.Count == 1 ? notes[0].StringIndex : null;   // the cursor note: a chord's grace note belongs to it
        var presets = new EffectPresetStore(_host.Settings);
        ValuesEffectDialog dialog = kind switch
        {
            EffectEditorKind.Trill => OrnamentEditors.Trill(notes[0], pitchOf, presets, notes.Any(n => n.Techniques.Contains(TechniqueNames.Trill)), _host.SaveSettings),
            EffectEditorKind.Grace => OrnamentEditors.Grace(cells[0], presets, cells.Any(c => OrnamentEdits.GraceOf(c) is not null), _host.SaveSettings, onString),
            _ => OrnamentEditors.Harmonic(notes[0], presets, notes.Any(n => OrnamentEdits.HarmonicTypeOf(n) >= 0 || n.Techniques.Contains("FeedbackHarmonic")), _host.SaveSettings)
        };
        dialog.Dialog.Owner ??= _host.Owner;
        var answer = ask is null ? dialog.Dialog.Ask() : ask(dialog);
        if (answer == EditorAnswer.Cancel) return false;
        var v = dialog.Values;
        var ok = answer == EditorAnswer.Ok;
        var changed = edits.EditEffect(() => kind switch
        {
            EffectEditorKind.Trill => ok ? OrnamentEdits.ApplyTrill(notes, (int)v["Step"], (int)v["Speed"], pitchOf) : OrnamentEdits.CleanTrill(notes),
            EffectEditorKind.Grace => cells.Select(c => ok ? OrnamentEdits.ApplyGrace(c, new GraceSettings((int)v["Step"], v["Dead"] > 0, v["Before"] > 0, (int)v["Speed"], (int)v["Dynamic"], (GraceTransition)(int)v["Transition"]), pitchOf, onString)
                : OrnamentEdits.CleanGrace(c)).ToList().Any(x => x),
            _ => ok ? OrnamentEdits.ApplyHarmonic(notes, (int)v["Type"], (int)v["Fret"]) : OrnamentEdits.CleanHarmonic(notes)
        });
        if (changed) _host.Say(ok ? $"{char.ToUpper(noun[0])}{noun[1..]} set." : $"{char.ToUpper(noun[0])}{noun[1..]} removed.");
        return changed;
    }
}
