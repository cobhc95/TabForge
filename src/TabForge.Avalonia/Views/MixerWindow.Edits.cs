using System;
using TabForge.Documents;
using TabForge.Platform;

namespace TabForge.Avalonia.Views;

// Owns: the mixer's song edits as undo steps: one step per gesture (a press, a key, a wheel tick, a toggle click, a colour pick), and the dirty mark
//     and playback push of every change inside it (a drag is one step however many values it moves).
// Does not own: the values (the song's Mixer and tracks), the controls (MixerWindow.cs, MixerWindow.Controls.cs and MixerWindow.Interaction.cs), the undo stack (DocumentEdits).
// Tests: TabForge.Avalonia.Tests.MixerWindowTests.
/// <summary>The mixer's undo and dirty handling. UI thread only.</summary>
public sealed partial class MixerWindow
{
    /// <summary>True while the current gesture's undo step is stored; cleared when the dispatcher goes idle, so the next gesture stores its own.</summary>
    private bool _gestureOpen;

    /// <summary>Stores the song's state before the gesture's first change, once per gesture; its label is the step's readable name.</summary>
    private void BeginEdit(string label = "Edit")
    {
        if (_gestureOpen) return;
        DocumentEdits.Checkpoint(_document, label);
        _gestureOpen = true;
        _ui.Post(() => _gestureOpen = false, UiPriority.ApplicationIdle);
    }

    /// <summary>Applies one change of the song: it is dirty, its playback is refreshed, and the live engine takes the new level.</summary>
    private void Mutate(Action apply)
    {
        apply();
        // The dirty mark is immediate (a test and the tab title must see it on this pass); the engine push runs once per frame.
        DocumentEdits.MarkChanged(_document, invalidatesTimeline: false);
        _liveMixChanged?.Request();
    }

    /// <summary>A discrete change (a click, a pick from a menu): its own undo step, then <see cref="Mutate"/>.</summary>
    private void ClickEdit(Action apply)
    {
        BeginEdit();
        Mutate(apply);
    }
}
