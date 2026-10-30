using TabForge.Platform;
using TabForge.Presenters;
using TabForge.ScoreEditing;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the preview score's key handling: the editor key map (cursor moves, digits, durations) run through
//   ScoreCursorNavigator and ScoreEditCommands, and the catalogue fallback for bound key names (HotkeyCatalog).
// Does not own: the key rules (EditorKeyMap, HotkeyCatalog), the edit commands (ScoreEditCommands) or the selection (probe's EditorSelectionState).
// Tests: TabForge.Avalonia.Tests.ScorePreviewEditorTests.
internal sealed partial class ScorePreviewEditor
{
    /// <summary>
    /// Runs the key's editor command, else the catalogue command bound to <paramref name="keyName"/> (the toolkit's key name, e.g. "R",
    /// "OemPeriod"); false when the preview gives the key no meaning (the caller then skips the repaint).
    /// </summary>
    internal bool Key(EditorKey key, string keyName, bool ctrl, bool shift, bool alt)
    {
        if (_probe.Cursor is not var (measure, cell, stringIndex)) return false;
        var command = EditorKeyMap.Resolve(key, ctrl, shift, alt, AppendKeyState(measure, cell), out var digit);
        if (command == EditorCommand.None) return RunCatalogued(keyName, ctrl, shift, alt);
        if (command is EditorCommand.ExtendSelectionLeft or EditorCommand.ExtendSelectionRight) return ExtendSelection(command == EditorCommand.ExtendSelectionRight ? 1 : -1);
        if (command is not (EditorCommand.EnterDigit or EditorCommand.DeleteNote or EditorCommand.DeleteBeat
            or EditorCommand.LongerDuration or EditorCommand.ShorterDuration or EditorCommand.ToggleTriplet)) Sel.Clear();   // moving the cursor ends the range
        var stops = Stops;
        CursorTarget? target = command switch
        {
            EditorCommand.PreviousBeat => ScoreCursorNavigator.BackBeat(stops, measure, cell),
            EditorCommand.NextBeat => ScoreCursorNavigator.ForwardBeat(stops, measure, cell),
            EditorCommand.PreviousBar => ScoreCursorNavigator.Bar(stops, measure, cell, -1),
            EditorCommand.NextBar => ScoreCursorNavigator.Bar(stops, measure, cell, 1),
            EditorCommand.BarStart => new CursorTarget(measure, stops.Snap(measure, 0)),
            EditorCommand.BarEnd => new CursorTarget(measure, ScoreCursorNavigator.BarEnd(stops, measure, cell)),
            EditorCommand.FirstBar => new CursorTarget(0, stops.Snap(0, 0)),
            EditorCommand.LastBar => ScoreCursorNavigator.LastBar(stops),
            _ => null,
        };
        if (target is { } t)
        {
            _probe.Cursor = (t.Measure, t.Cell, stringIndex);
            if (command == EditorCommand.NextBeat) TakeNewSlotDuration(measure, cell, t);
            return true;
        }
        switch (command)
        {
            case EditorCommand.AppendBar: AppendBar(moveCursorToNewBar: true); return true;
            case EditorCommand.StringUp or EditorCommand.StringDown:
                _probe.Cursor = (measure, cell, ScoreCursorNavigator.WrapString(stringIndex, command == EditorCommand.StringUp ? -1 : 1, TrackNow.StringTunings.Count));
                return true;
            case EditorCommand.EnterDigit:
                Sel.Clear();   // typing a fret ends the selection (GP5)
                _commands.EnterFret(digit);
                return true;
            case EditorCommand.DeleteNote: _commands.DeleteNote(); return true;
            case EditorCommand.DeleteBeat: _commands.DeleteBeat(); return true;
            case EditorCommand.LongerDuration: _commands.Longer(); return true;
            case EditorCommand.ShorterDuration: _commands.Shorter(); return true;
            case EditorCommand.ToggleTriplet: _commands.ToggleTriplet(); return true;
            default:
                return false;
        }
    }

    // The append-key state at the cursor: a bar may be added while the song is below the bar limit, and the cursor's own place then decides
    // between the last-bar rule (Ctrl+Right) and the song-end rule (Right), as GP5.
    private EditorKeyState AppendKeyState(int measure, int cell)
    {
        var plusLengthens = Tools.PlusKeyLengthensNotes;
        if (_document.Project.Tracks.Count == 0 || BarRangeEditor.MaxMeasures(_document.Project) >= InputLimits.MaxMeasuresPerTrack)
            return new EditorKeyState(false, false, false, plusLengthens);
        var stops = Stops;
        var lastBar = stops.MeasureCount == 0 || measure >= stops.MeasureCount - 1;
        return new EditorKeyState(lastBar, ScoreCursorNavigator.AtSongEnd(stops, measure, cell), false, plusLengthens);
    }

    private bool RunCatalogued(string keyName, bool ctrl, bool shift, bool alt)
    {
        var mods = (ctrl ? HotkeyModifiers.Control : 0) | (shift ? HotkeyModifiers.Shift : 0) | (alt ? HotkeyModifiers.Alt : 0);
        if (!_hotkeys.TryGetValue(HotkeyCatalog.Canonical(HotkeyCatalog.Format(keyName, mods)), out var id)) return false;
        // A bare editor (the test harness) that never received a settings map still gets the + and - polarity right.
        if (!_hotkeysFromSettings && !ctrl && !alt && id is HotkeyCatalog.NoteLonger or HotkeyCatalog.NoteShorter)
        {
            var plus = keyName == "OemPlus" && !shift;
            if (plus) id = Tools.PlusKeyLengthensNotes ? HotkeyCatalog.NoteLonger : HotkeyCatalog.NoteShorter;
            else if (keyName == "OemMinus") id = Tools.PlusKeyLengthensNotes ? HotkeyCatalog.NoteShorter : HotkeyCatalog.NoteLonger;
        }
        return RunCommand(id);
    }
}
