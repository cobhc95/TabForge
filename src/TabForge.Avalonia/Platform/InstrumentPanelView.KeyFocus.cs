using Avalonia.Input;
using TabForge.Platform;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

// Owns: the keyboard route of the keyboard and drum views: the focused key or pad, the arrow keys moving it, Enter or Space writing its note (with the same
//   pressed shade and flash a click gives), and the ring drawn round it.
// Does not own: the fretboard (it has no per-fret keyboard route), the shades (InstrumentPanelView.Feedback.cs) or the note writing (NotePicked's owner).
// Tests: TabForge.Avalonia.Tests.InstrumentPanelKeyboardFocusTests.
// Threads: UI thread only.
public sealed partial class InstrumentPanelView
{
    private int? _focusKey;

    /// <summary>The key or pad the arrow keys are on, or null before the first arrow press.</summary>
    internal int? FocusedKey => _focusKey;

    /// <summary>The keys and pads this view lets the arrow keys visit: the shown key range, or the GM drum map.</summary>
    private (int First, int Last)? FocusRange()
    {
        if (_track is null) return null;
        return _kind switch
        {
            InstrumentPanelKind.Keyboard => KeyRange(),
            InstrumentPanelKind.Drums => (PercussionKeyMapRules.FirstNote, PercussionKeyMapRules.FirstNote + PercussionKeyMapRules.Names.Count - 1),
            _ => null,
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsFocused) return;   // only an explicitly focused panel (Tab or click) takes the arrows; otherwise the score's navigation gets them
        if (FocusRange() is not { } range) return;
        var current = _focusKey is { } k && k >= range.First && k <= range.Last ? k : (int?)null;
        if (e.Key is Key.Enter or Key.Space)
        {
            if (current is not { } note) return;
            e.Handled = true;
            if (_pressedKey is not null) return;   // a held key does not repeat the note (a lost focus ends the press, so a missed key-up cannot stall later writes)
            var pick = new InstrumentNotePick(note, null, null);
            BeginKeyPress(pick);
            NotePicked?.Invoke(pick);
            return;
        }
        var step = StepOf(e.Key);
        if (step == 0) return;
        e.Handled = true;
        _focusKey = current is { } at ? System.Math.Clamp(at + step, range.First, range.Last) : range.First;
        _keyboardFocus = true;
        InvalidateVisual();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (IsFocused && e.Key is Key.Enter or Key.Space) EndKeyPress();
    }

    // Left and Right move one key (the keyboard) or one pad down its column; Up and Down move a pad within its column.
    private int StepOf(Key key)
    {
        var rows = _kind == InstrumentPanelKind.Drums ? PercussionKeyMapRules.GridFor(Bounds.Width, Bounds.Height).Rows : 1;
        return key switch
        {
            Key.Left => _kind == InstrumentPanelKind.Drums ? -rows : -1,
            Key.Right => _kind == InstrumentPanelKind.Drums ? rows : 1,
            Key.Up when _kind == InstrumentPanelKind.Drums => -1,
            Key.Down when _kind == InstrumentPanelKind.Drums => 1,
            _ => 0,
        };
    }

    /// <summary>The panel rectangle of a key or pad, or null when it is not shown.</summary>
    private UiRect? KeyRectOf(int midi)
    {
        if (_kind == InstrumentPanelKind.Drums)
        {
            var index = midi - PercussionKeyMapRules.FirstNote;
            return index < 0 || index >= PercussionKeyMapRules.Names.Count ? null : PercussionKeyMapRules.CellRect(PercussionKeyMapRules.GridFor(Bounds.Width, Bounds.Height), index);
        }
        var area = KeysArea();
        var (lowest, highest) = KeyRange();
        if (midi < lowest || midi > highest) return null;
        var keyWidth = area.Width / System.Math.Max(1, KeyboardGeometryRules.WhiteKeysIn(lowest, highest));
        var whites = 0;
        for (var m = lowest; m < midi; m++) if (KeyboardGeometryRules.IsWhite(m)) whites++;
        return KeyboardGeometryRules.IsWhite(midi) ? KeyboardGeometryRules.WhiteKeyRect(area, keyWidth, whites) : KeyboardGeometryRules.BlackKeyRect(area, keyWidth, whites);
    }

    private void DrawKeyFocusRing(IDrawContext d, System.Func<string, UiColor> ink)
    {
        if (!_keyboardFocus || _focusKey is not { } key || FocusRange() is null || KeyRectOf(key) is not { } r) return;
        d.Rect(null, new UiStroke(ink("InstrumentAccent"), 2), r.X, r.Y, r.Width, r.Height);
    }
}
