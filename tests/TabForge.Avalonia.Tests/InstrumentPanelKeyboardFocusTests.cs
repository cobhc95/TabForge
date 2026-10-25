using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the keyboard route of the drum pads: arrow keys move the focused pad, Enter writes its note with the pressed shade, and the focus ring is painted.
/// Tests: InstrumentPanelView.OnKeyDown, OnKeyUp and Draw.
/// </summary>
public class InstrumentPanelKeyboardFocusTests
{
    private static InstrumentPanelView Drums(bool focused = true)
    {
        var track = new TrackModel { Kind = TrackKind.Drums, MidiChannel = 9, Measures = TemplateFactory.Measures(1) };
        var view = new InstrumentPanelView { Kind = InstrumentPanelKind.Drums, Width = 900, Height = 168 };
        view.Measure(new global::Avalonia.Size(900, 168));
        view.Arrange(new global::Avalonia.Rect(0, 0, 900, 168));
        view.Show(track, System.Array.Empty<InstrumentLitNotes.Lit>());
        if (!focused) return view;   // not in any window: it cannot hold the focus
        var window = new global::Avalonia.Controls.Window { Width = 900, Height = 168, Content = view };
        window.Show();
        view.Focus();
        return view;
    }

    private static void Press(InstrumentPanelView view, Key key, bool up = false) =>
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = up ? InputElement.KeyUpEvent : InputElement.KeyDownEvent, Key = key });

    [AvaloniaFact]
    public void ArrowsMoveThePadAndEnterWritesItsNote()
    {
        var view = Drums();
        var picks = new List<InstrumentNotePick>();
        view.NotePicked += picks.Add;

        Press(view, Key.Down);
        var first = view.FocusedKey;
        Assert.Equal(PercussionKeyMapRules.FirstNote, first);
        Press(view, Key.Down);
        Assert.Equal(first + 1, view.FocusedKey);

        Press(view, Key.Enter);
        Assert.Equal(new[] { first!.Value + 1 }, picks.Select(p => p.Midi));
        Assert.Equal(first + 1, view.PressedKey);
        Press(view, Key.Enter, up: true);
        Assert.Null(view.PressedKey);
    }

    [AvaloniaFact]
    public void TwoEnterPressesWithoutAKeyUpWriteTwoNotes()
    {
        var view = Drums();
        var picks = new List<InstrumentNotePick>();
        view.NotePicked += picks.Add;
        Press(view, Key.Down);
        Press(view, Key.Enter);
        global::Avalonia.Controls.TopLevel.GetTopLevel(view)!.FocusManager!.ClearFocus();   // the score took the focus, so no key-up arrived
        view.Focus();
        Press(view, Key.Enter);
        Assert.Equal(2, picks.Count);
    }

    [AvaloniaFact]
    public void AHeldEnterWritesOneNoteAndAReleaseOutsideThePanelEndsThePress()
    {
        var view = Drums();
        var picks = new List<InstrumentNotePick>();
        view.NotePicked += picks.Add;
        Press(view, Key.Down);
        Press(view, Key.Enter);
        Press(view, Key.Enter);   // auto-repeat: no key-up between
        Assert.Single(picks);
        Press(view, Key.Enter, up: true);

        var at = new global::Avalonia.Point(10, 10);
        view.SetFeedbackForTest(null, 40);
        view.RaiseEvent(new global::Avalonia.Input.PointerEventArgs(InputElement.PointerExitedEvent, view, new global::Avalonia.Input.Pointer(0, global::Avalonia.Input.PointerType.Mouse, true), (global::Avalonia.Visual)view, at, 0, new global::Avalonia.Input.PointerPointProperties(), global::Avalonia.Input.KeyModifiers.None));
        Assert.Null(view.PressedKey);
    }

    [AvaloniaFact]
    public void KeyboardFocusPaintsARingRoundTheFocusedPad()
    {
        var view = Drums();
        var plain = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(plain, false);
        Press(view, Key.Down);
        var ringed = new InstrumentFretNumberTests.RecordingDrawContext();
        view.Draw(ringed, false);
        Assert.True(ringed.Rects.Count(r => r.Fill is null) > plain.Rects.Count(r => r.Fill is null));
    }

    [AvaloniaFact]
    public void ArrowsPassThroughToTheScoreWhenThePanelIsNotFocused()
    {
        var view = Drums(focused: false);
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right };
        view.RaiseEvent(args);
        Assert.False(args.Handled);
        Assert.Null(view.FocusedKey);
    }
}
