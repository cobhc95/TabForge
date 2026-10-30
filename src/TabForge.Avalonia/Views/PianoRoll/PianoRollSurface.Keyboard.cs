using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll key strip's state and gestures: which pitches the selection holds (lit softly), the last
//   right-clicked pitch (the Shift-range anchor), the hovered key and its modifier preview (Ctrl: this pitch;
//   Shift with an anchor: the anchor..hover range), and the presses: left only auditions (never selects), right
//   selects the pitch (Ctrl toggles it, Shift adds the anchor..hover range), never a context menu.
// Does not own: the key strip's paint (PianoRollSurface.KeyboardDraw), the edits (PianoRollPresenter) or the menu.
// Tests: TabForge.Avalonia.Tests.PianoRollKeyboardTests.
public sealed partial class PianoRollSurface
{
    /// <summary>Black keys span this share of the key strip's width (white keys span it all).</summary>
    internal const double BlackShare = 0.6;

    /// <summary>A softer accent tint for the keys of selected pitches (accent blended towards the key fill).</summary>
    internal const double SelectedTint = 0.45;

    /// <summary>A lighter accent tint for the notes on the previewed pitches.</summary>
    internal const double PreviewTint = 0.35;

    /// <summary>The hover brightening step (the key fill blended towards white).</summary>
    internal const double HoverStep = 0.22;

    private int _keyAnchor = -1;   // the last right-clicked pitch: Shift+right selects the anchor..key range (-1: none yet)
    private KeyModifiers _keyHoverMods;   // the modifiers held while hovering the key strip (the Ctrl / Shift preview)
    private bool _keyAudition;   // the key gesture in progress sounds (a left press); a right press only selects
    private readonly HashSet<int> _selectedPitches = new();   // the pitches the selection holds (their keys stay lit)
    private readonly HashSet<int> _previewPitches = new();   // the pitches the hovered modifier would touch (dashed outline)

    /// <summary>The pitches holding selected notes (their keys stay lit); refreshed after every selection change.</summary>
    internal IReadOnlySet<int> SelectedPitches => _selectedPitches;

    /// <summary>The pitches a modifier preview touches now: the hovered key (Ctrl), or the anchor..hover range (Shift).</summary>
    internal IReadOnlySet<int> PreviewPitches => _previewPitches;

    /// <summary>The Shift-range anchor (the last right-clicked key, -1 with none): the range preview runs from here.</summary>
    internal int KeyAnchor => _keyAnchor;

    /// <summary>Reads the selection's pitches again (the view calls it after every selection change): the key strip follows live.</summary>
    internal void SyncKeySelection()
    {
        _selectedPitches.Clear();
        foreach (var i in Presenter.Selected)
            if (i >= 0 && i < Presenter.Notes.Count) _selectedPitches.Add(Presenter.Notes[i].Pitch);
        UpdatePreview();
    }

    /// <summary>The anchor..hover range (inclusive), or the hovered key alone with no anchor.</summary>
    internal IReadOnlyList<int> KeyRange(int hover)
    {
        if (_keyAnchor < 0 || hover < 0) return hover < 0 ? new List<int>() : new List<int> { hover };
        var from = System.Math.Min(_keyAnchor, hover);
        var to = System.Math.Max(_keyAnchor, hover);
        var range = new List<int>();
        for (var pitch = from; pitch <= to; pitch++) range.Add(pitch);
        return range;
    }

    private void UpdatePreview()
    {
        _previewPitches.Clear();
        if (_keyHover < 0 || _gesture != Gesture.None) return;
        if (_keyHoverMods.HasPrimary() && !_keyHoverMods.HasFlag(KeyModifiers.Shift)) _previewPitches.Add(_keyHover);
        else if (_keyHoverMods.HasFlag(KeyModifiers.Shift))
            foreach (var pitch in KeyRange(_keyHover)) _previewPitches.Add(pitch);
    }

    /// <summary>The hovered key and its modifier preview follow the pointer (no selection change, one repaint on change).</summary>
    internal void UpdateKeyHover(Point p, KeyModifiers modifiers)
    {
        var key = InKeys(p) ? PitchAt(p.Y) : -1;
        if (key == _keyHover && modifiers == _keyHoverMods) return;
        _keyHover = key;
        _keyHoverMods = modifiers;
        UpdatePreview();
        InvalidateVisual();
        Playhead.InvalidateVisual();
    }

    /// <summary>The pointer left the surface: the key-strip hover and its modifier preview go away.</summary>
    internal void ClearKeyHover()
    {
        if (_keyHover < 0 && _keyHoverMods == KeyModifiers.None) return;
        _keyHover = -1;
        _keyHoverMods = KeyModifiers.None;
        UpdatePreview();
        InvalidateVisual();
        Playhead.InvalidateVisual();
    }

    /// <summary>A left press on the key strip: audition that note only (never a selection change).</summary>
    internal bool PressKeyLeft(Point p)
    {
        _gesture = Gesture.Key;
        _keyAudition = true;
        _keyDown = PitchAt(p.Y);
        if (AuditionOn) Sound(_keyDown);
        if (StepInput) StepAdd(_keyDown);
        return true;
    }

    /// <summary>A right press on the key strip: select that pitch (Ctrl toggles it, Shift adds the anchor..key range).</summary>
    internal bool PressKeyRight(Point p, KeyModifiers modifiers)
    {
        if (!InKeys(p)) return false;
        var pitch = PitchAt(p.Y);
        var ctrl = modifiers.HasPrimary();
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (shift)
        {
            if (_keyAnchor < 0) Presenter.SelectPitchRow(pitch);
            else Presenter.Select(Enumerable.Range(0, Presenter.Notes.Count).Where(i => IsBetween(Presenter.Notes[i].Pitch, _keyAnchor, pitch)), add: true);
        }
        else if (ctrl) TogglePitch(pitch);
        else Presenter.SelectPitchRow(pitch);
        _keyAnchor = pitch;
        _keyAudition = false;
        _gesture = Gesture.Key;
        SuppressMenu = true;
        SyncKeySelection();
        SelectionChanged?.Invoke();
        return true;
    }

    private static bool IsBetween(int pitch, int a, int b) =>
        pitch >= System.Math.Min(a, b) && pitch <= System.Math.Max(a, b);

    private void TogglePitch(int pitch)
    {
        var picked = Enumerable.Range(0, Presenter.Notes.Count).Where(i => Presenter.Notes[i].Pitch == pitch).ToList();
        if (picked.Count == 0) return;
        if (picked.All(Presenter.Selected.Contains))
            foreach (var i in picked) Presenter.Toggle(i);   // the pitch is wholly selected: it goes out
        else Presenter.Select(picked, add: true);   // else the pitch joins (Explorer-style, as the marquee and note clicks)
    }

    /// <summary>The accent colour for a lit key (the held / auditioned key): the Accent token, never a literal or a fixed palette blue.</summary>
    internal UiColor KeyLit() => Colours.Get("AccentBrush");

    /// <summary>The softer accent tint of a key holding selected notes (mostly the accent, softened towards its fill).</summary>
    internal static UiColor KeySelectedTint(UiColor lit, UiColor fill) => UiTokens.Blend(lit, fill, SelectedTint);
}
