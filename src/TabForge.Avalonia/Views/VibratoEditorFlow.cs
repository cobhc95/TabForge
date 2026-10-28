using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.EffectEditors;

namespace TabForge.Avalonia.Views;

// Owns: opening the vibrato settings window on the cursor notes and turning its answer into one undoable edit (and the vibrato button/hotkey rule:
// a note that already has vibrato opens the window, else the plain toggle runs).
// Does not own: the fields (VibratoEditorWindow), the model writes (Core VibratoEdits), or the undo step (ScoreEditCommands.EditEffect).
// Tests: TabForge.Avalonia.Tests.VibratoSettingsTests.
public sealed class VibratoEditorFlow
{
    private readonly ScorePreviewView _score;
    private readonly Func<EffectValuesWindow, Task<EffectValuesAnswer?>> _ask;
    private readonly Action<string> _notice;
    private readonly Func<AppSettings> _settings;

    public VibratoEditorFlow(ScorePreviewView score, Func<EffectValuesWindow, Task<EffectValuesAnswer?>> ask, Action<string>? notice = null, Func<AppSettings>? settings = null)
    {
        var own = new AppSettings();
        _score = score;
        _ask = ask;
        _notice = notice ?? (_ => { });
        _settings = settings ?? (() => own);
    }

    /// <summary>The vibrato button or hotkey: opens the window when a cursor note has vibrato, else toggles it on as usual.</summary>
    public Task<bool> OpenOrToggle()
    {
        if (!_score.Editor.Effects.EffectNotes().Any(VibratoEdits.Has)) return Task.FromResult(_score.Run("Note.Vibrato"));
        return Open();
    }

    /// <summary>The window on the cursor notes (a note without vibrato gets it on OK); false when no note or nothing changed.</summary>
    public async Task<bool> Open()
    {
        var notes = _score.Editor.Effects.EffectNotes();
        if (notes.Count == 0) { _notice("Select a note to edit its vibrato."); return false; }
        var audio = _settings().Audio;
        var first = notes.FirstOrDefault(VibratoEdits.Has) ?? notes[0];
        var wide = first.Techniques.Contains(TechniqueNames.WideVibrato);
        var window = VibratoEditorWindow.Create(wide, first.VibratoDepth ?? (wide ? audio.WideVibratoDepth : audio.NormalVibratoDepth),
            first.VibratoSpeed ?? audio.VibratoSpeedHz, audio.NormalVibratoDepth, audio.WideVibratoDepth, audio.VibratoSpeedHz, notes.Any(VibratoEdits.Has));
        var answer = await _ask(window) ?? EffectValuesAnswer.Cancel;
        return Apply(answer, window.Values, notes);
    }

    /// <summary>Applies an answered window (values Type, Depth, Speed) as one undo step.</summary>
    public bool Apply(EffectValuesAnswer answer, IReadOnlyDictionary<string, double> values, IReadOnlyList<TabNote> notes)
    {
        if (answer == EffectValuesAnswer.Cancel) return false;
        var audio = _settings().Audio;
        var isWide = values["Type"] > 0;
        var changed = _score.Edit(editor => editor.Effects.EditEffect(answer == EffectValuesAnswer.Ok
            ? () => VibratoEdits.Apply(notes, isWide, values["Depth"], values["Speed"], isWide ? audio.WideVibratoDepth : audio.NormalVibratoDepth, audio.VibratoSpeedHz)
            : () => VibratoEdits.Clean(notes)));
        if (changed) _notice(answer == EffectValuesAnswer.Ok ? "Vibrato set." : "Vibrato removed.");
        return changed;
    }
}
