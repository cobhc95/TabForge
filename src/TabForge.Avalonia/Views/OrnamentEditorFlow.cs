using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.ScoreEditing;
using TabForge.Views.EffectEditors;

namespace TabForge.Avalonia.Views;

// Owns: opening the trill, grace-note and harmonic editors on the score's cursor notes, turning the answer into one undoable edit, and the
// status line the editors show (no note to edit, a dead note, set, removed).
// Does not own: the fields (OrnamentEditorWindows), the dialog frame (EffectValuesWindow), the model writes (Core OrnamentEdits), the undo step
//   (ScoreEditCommands.EditEffect) the built-in presets (Core OrnamentPresets) or the preset storage rules (Core EffectPresetStore over AppSettings.EffectPresets).
// Tests: TabForge.Avalonia.Tests.OrnamentEditorFlowTests.
public sealed class OrnamentEditorFlow
{
    private readonly ScorePreviewView _score;
    private readonly Func<EffectValuesWindow, Task<EffectValuesAnswer?>> _ask;
    private readonly Action<string> _notice;
    private readonly Func<AppSettings> _settings;   // the settings whose EffectPresets keep the person's own presets (EffectPresetStore)
    private readonly Action _saveSettings;          // writes the settings file after a preset is saved or deleted

    /// <summary>One opened editor: its window, and the change its answer makes (false when nothing changed).</summary>
    public sealed record OrnamentDialog(EffectValuesWindow Window, Func<EffectValuesAnswer, bool> Apply);

    /// <param name="ask">Shows an editor window and returns its answer (null when closed without one). The app shows it modally; tests answer directly.</param>
    /// <param name="notice">Shows a status line such as "Select a note to edit its trill."; null shows nothing.</param>
    /// <param name="settings">The settings the presets are saved in; null keeps them in this flow only.</param>
    /// <param name="saveSettings">Writes the settings file after a preset change; null writes nothing.</param>
    public OrnamentEditorFlow(ScorePreviewView score, Func<EffectValuesWindow, Task<EffectValuesAnswer?>> ask, Action<string>? notice = null,
        Func<AppSettings>? settings = null, Action? saveSettings = null)
    {
        var ownSettings = new AppSettings();
        _settings = settings ?? (() => ownSettings);
        _saveSettings = saveSettings ?? (() => { });
        _score = score;
        _ask = ask;
        _notice = notice ?? (_ => { });
    }

    /// <summary>The trill editor on the cursor note; false when no note was there or nothing changed.</summary>
    public Task<bool> OpenTrill() => Run(BuildTrill());

    /// <summary>The grace-note editor on the cursor beat; false when no beat had a principal note or nothing changed.</summary>
    public Task<bool> OpenGrace() => Run(BuildGrace());

    /// <summary>The harmonic editor on the cursor notes; false when they are all dead or nothing changed.</summary>
    public Task<bool> OpenHarmonic() => Run(BuildHarmonic());

    /// <summary>The harmonic hotkey and palette button: a note that has a harmonic loses it (one undo step), else the editor opens.</summary>
    public Task<bool> OpenOrRemoveHarmonic()
    {
        var notes = _score.Editor.Effects.EffectNotes();
        if (!notes.Any(HasHarmonic)) return OpenHarmonic();
        var removed = _score.Edit(editor => editor.Effects.EditEffect(() => OrnamentEdits.CleanHarmonic(notes)));
        if (removed) _notice("Harmonic removed.");
        return Task.FromResult(removed);
    }

    /// <summary>The trill editor for the cursor note, not yet shown; null when there is no note.</summary>
    public OrnamentDialog? BuildTrill()
    {
        var effects = _score.Editor.Effects;
        if (effects.EffectTrack is not { } track) return null;
        if (effects.EffectNotes() is not { Count: > 0 } notes) return SelectANote("trill");
        var first = notes[0];
        Func<int, int, int> pitchOf = track.PitchOf;
        var (step, speed) = first.Techniques.Contains(TechniqueNames.Trill) ? OrnamentEdits.TrillOf(first, pitchOf) : (2, 16);
        var window = OrnamentEditorWindows.Trill(first.Fret, step, speed, Presets(EffectEditorKind.Trill), notes.Any(n => n.Techniques.Contains(TechniqueNames.Trill)),
            values => SaveTo(EffectEditorKind.Trill, values));
        window.PresetDeleted += preset => Delete(EffectEditorKind.Trill, preset);
        return new OrnamentDialog(window, answer => Apply(answer, "Trill",
            () => OrnamentEdits.ApplyTrill(notes, (int)window.Values["Step"], (int)window.Values["Speed"], pitchOf),
            () => OrnamentEdits.CleanTrill(notes)));
    }

    /// <summary>The grace-note editor for the cursor beat, not yet shown; null when no beat had a principal note.</summary>
    public OrnamentDialog? BuildGrace()
    {
        var effects = _score.Editor.Effects;
        if (effects.EffectTrack is not { } track) return null;
        var notes = effects.EffectNotes();
        var cells = effects.EffectCells().Where(c => OrnamentEdits.PrincipalOf(c) is not null).ToList();
        if (cells.Count == 0) return SelectANote("grace note");
        Func<int, int, int> pitchOf = track.PitchOf;
        int? onString = notes.Count == 1 ? notes[0].StringIndex : null;   // the cursor note: a chord's grace note belongs to it
        var cell = cells[0];
        var start = OrnamentEdits.GraceSettingsOf(cell, onString);
        var principalFret = OrnamentEdits.PrincipalOf(cell, onString)?.Fret ?? 0;
        var window = OrnamentEditorWindows.Grace(principalFret, start.Step, start.Dead, start.BeforeBeat, start.Speed, start.Dynamic,
            (int)start.Transition, Presets(EffectEditorKind.Grace), cells.Any(c => OrnamentEdits.GraceOf(c) is not null), values => SaveTo(EffectEditorKind.Grace, values));
        window.PresetDeleted += preset => Delete(EffectEditorKind.Grace, preset);
        return new OrnamentDialog(window, answer => Apply(answer, "Grace note",
            () =>
            {
                var v = window.Values;
                var grace = new GraceSettings((int)v["Step"], v["Dead"] > 0, v["Before"] > 0, (int)v["Speed"], (int)v["Dynamic"], (GraceTransition)(int)v["Transition"]);
                return cells.Select(c => OrnamentEdits.ApplyGrace(c, grace, pitchOf, onString)).ToList().Any(x => x);
            },
            () => cells.Select(OrnamentEdits.CleanGrace).ToList().Any(x => x)));
    }

    /// <summary>The harmonic editor for the cursor notes, not yet shown; null when there are none or they are all dead.</summary>
    public OrnamentDialog? BuildHarmonic()
    {
        var notes = _score.Editor.Effects.EffectNotes();
        if (notes.Count == 0) return SelectANote("harmonic");
        if (notes.All(n => n.Dead))
        {
            _notice("A dead note takes no harmonic.");   // GP5 k01
            return null;
        }
        var first = notes[0];
        var type = Math.Max(0, OrnamentEdits.HarmonicTypeOf(first));
        var window = OrnamentEditorWindows.Harmonic(type, (int)(first.HarmonicFret ?? 12), Presets(EffectEditorKind.Harmonic), notes.Any(HasHarmonic),
            values => SaveTo(EffectEditorKind.Harmonic, values));
        window.PresetDeleted += preset => Delete(EffectEditorKind.Harmonic, preset);
        return new OrnamentDialog(window, answer => Apply(answer, "Harmonic",
            () => OrnamentEdits.ApplyHarmonic(notes, (int)window.Values["Type"], (int)window.Values["Fret"]),
            () => OrnamentEdits.CleanHarmonic(notes)));
    }

    private async Task<bool> Run(OrnamentDialog? dialog)
    {
        if (dialog is null) return false;
        var answer = await _ask(dialog.Window) ?? EffectValuesAnswer.Cancel;
        return dialog.Apply(answer);
    }

    /// <summary>Shows "Select a note to edit its noun." and returns null, so no editor opens.</summary>
    private OrnamentDialog? SelectANote(string noun)
    {
        _notice($"Select a note to edit its {noun}.");
        return null;
    }

    /// <summary>Runs the OK or Clean change as one undo step on the score, then shows "label set." or "label removed."; false on Cancel or when nothing changed.</summary>
    private bool Apply(EffectValuesAnswer answer, string label, Func<bool> ok, Func<bool> clean)
    {
        if (answer == EffectValuesAnswer.Cancel) return false;
        var applyOk = answer == EffectValuesAnswer.Ok;
        var changed = _score.Edit(editor => editor.Effects.EditEffect(applyOk ? ok : clean));
        if (changed) _notice(applyOk ? $"{label} set." : $"{label} removed.");
        return changed;
    }

    private static bool HasHarmonic(TabNote note) => OrnamentEdits.HarmonicTypeOf(note) >= 0 || note.Techniques.Contains("FeedbackHarmonic");

    /// <summary>The built-in presets of <paramref name="kind"/>, then the person's own saved in the settings.</summary>
    private List<EffectValuesPreset> Presets(EffectEditorKind kind)
    {
        var builtIn = kind switch { EffectEditorKind.Trill => OrnamentPresets.Trill, EffectEditorKind.Grace => OrnamentPresets.Grace, _ => OrnamentPresets.Harmonic };
        var own = new EffectPresetStore(_settings()).List(kind).Where(p => !p.BuiltIn).Select(p => new EffectValuesPreset(p.Preset.Name, false, p.Preset.Values));
        return builtIn.Select(p => new EffectValuesPreset(p.Name, true, p.Values)).Concat(own).ToList();
    }

    private EffectValuesPreset? SaveTo(EffectEditorKind kind, IReadOnlyDictionary<string, double> values)
    {
        var name = "My preset " + (Presets(kind).Count(p => !p.BuiltIn) + 1);
        if (!new EffectPresetStore(_settings()).Save(kind, name, Array.Empty<BendPointModel>(), values.ToDictionary(v => v.Key, v => v.Value))) return null;
        _saveSettings();
        return new EffectValuesPreset(name, false, values);
    }

    private void Delete(EffectEditorKind kind, EffectValuesPreset preset)
    {
        if (new EffectPresetStore(_settings()).Delete(kind, preset.Name)) _saveSettings();
    }
}
