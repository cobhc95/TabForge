using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Move notes popup's behaviour: it is a themed Popup (theme brushes, not a white box), the title and count, the Half steps / Stay in the key segments and their captions,
//   the - / + and octave pills (12 or 7), the value in words, Up/Down and Shift+Up/Down, a typed number, the snap tick, the Before / After chips, the range warning, Enter confirming
//   (also while typing), Esc cancelling and the automation names.
// Does not own: the maths (PianoRollTransposeDialogTests) or the edit (PianoRollKeyboardTests).
// Tests: PianoRollTransposePopup.
public sealed class PianoRollTransposePopupTests
{
    private static readonly IReadOnlySet<int> CMajor = new HashSet<int> { 0, 2, 4, 5, 7, 9, 11 };

    private static (Popup Popup, Task<MoveNotesAnswer?> Answer, Window Window) Open(PianoRollTransposeDialog model, System.Action<bool>? modeChanged = null)
    {
        var window = new Window { Content = new Border { Width = 600, Height = 500 } };
        window.Show();
        var popup = PianoRollTransposePopup.Open((Control)window.Content!, model, out var answer, modeChanged);
        Dispatcher.UIThread.RunJobs();
        return (popup, answer, window);
    }

    private static T Find<T>(Popup popup, string name) where T : Control =>
        popup.Child!.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static void Click(Popup popup, string name) => Find<Button>(popup, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Segment(Popup popup, string name) => Find<ToggleButton>(popup, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Press(Popup popup, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        popup.Child!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    private static PianoRollTransposeDialog Model(int count = 3, int[]? pitches = null, int initial = 1, bool steps = false, bool scale = false, bool lockScale = false) =>
        new(count, pitches ?? new[] { 55, 81 }, initial, steps, scale ? CMajor : null, lockScale, scale ? "C major" : null);

    private static string[] Chips(Popup popup, string row) => Find<StackPanel>(popup, row).Children.Select(c => ((TextBlock)((Border)c).Child!).Text!).ToArray();

    [AvaloniaFact]
    public void ItIsAThemedPopupNotAWhiteBox()
    {
        var (popup, _, window) = Open(Model(scale: true));
        try
        {
            Assert.True(popup.IsOpen);
            Assert.True(popup.IsLightDismissEnabled);
            Assert.IsNotType<Window>(popup);
            Assert.Same(window.Content, ((ILogical)popup).LogicalParent);   // attached to the anchor, so it gets the theme and control templates
            var panel = PianoRollTransposePopup.PanelOf(popup);
            var background = Assert.IsAssignableFrom<ISolidColorBrush>(panel.Background);
            Assert.True(window.TryFindResource("Panel2Brush", window.ActualThemeVariant, out var theme));
            Assert.Equal(((ISolidColorBrush)theme!).Color, background.Color);
            Assert.NotEqual(Colors.White, background.Color);
            Assert.True(window.TryFindResource("BorderSoftBrush", window.ActualThemeVariant, out var edge));
            Assert.Equal(((ISolidColorBrush)edge!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(panel.BorderBrush).Color);
            Assert.NotNull(Find<Button>(popup, "TransposeOk").Classes.FirstOrDefault());   // the accent default button
            Assert.NotEmpty(Find<Button>(popup, "TransposePlus").GetVisualDescendants());   // drawn through a template
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ItHasTheTitleCountAndNamedControls()
    {
        var (popup, _, window) = Open(Model(1, new[] { 55 }));
        try
        {
            Assert.Equal("Move notes up or down", Find<TextBlock>(popup, "MoveTitle").Text);
            Assert.Equal("1 note selected", Find<TextBlock>(popup, "TransposeCount").Text);
            Assert.Equal("How should they move?", Find<TextBlock>(popup, "MoveHowLabel").Text);
            Assert.Equal("How far?", Find<TextBlock>(popup, "MoveFarLabel").Text);
            Assert.Equal("up 1", Find<TextBox>(popup, "TransposeValue").Text);
            foreach (var name in new[] { "TransposeMinus", "TransposePlus", "TransposeOctaveDown", "TransposeOctaveUp", "TransposeOk", "TransposeCancel" })
                Assert.False(string.IsNullOrEmpty(global::Avalonia.Automation.AutomationProperties.GetName(Find<Button>(popup, name))), name);
            Assert.Equal("Move notes", (string?)Find<Button>(popup, "TransposeOk").Content);
            Assert.Equal("One octave down", (string?)Find<Button>(popup, "TransposeOctaveDown").Content);
            Assert.Equal("every key on the piano", Find<TextBlock>(popup, "MoveHalfStepsCaption").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ButtonsAndKeysStepTheAmountAndEnterConfirms()
    {
        var model = Model();
        var (popup, answer, window) = Open(model);
        try
        {
            Click(popup, "TransposePlus");
            Assert.Equal("up 2", Find<TextBox>(popup, "TransposeValue").Text);
            Click(popup, "TransposeOctaveUp");
            Assert.Equal(14, model.Delta);   // 12 half steps
            Click(popup, "TransposeOctaveDown");
            Click(popup, "TransposeMinus");
            Assert.Equal(1, model.Delta);
            Press(popup, Key.Up);
            Press(popup, Key.Down, KeyModifiers.Shift);
            Press(popup, Key.Down, KeyModifiers.Shift);
            Assert.Equal(-22, model.Delta);   // Shift = 12
            Assert.Equal("down 22", Find<TextBox>(popup, "TransposeValue").Text);
            Assert.Equal("half steps", Find<TextBlock>(popup, "MoveUnit").Text);
            Assert.Equal("", Find<TextBlock>(popup, "TransposeWarning").Text);
            Press(popup, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new MoveNotesAnswer(-22, false, false), answer.Result);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ATypedNumberPreviewsAndEnterConfirmsWhileTyping()
    {
        var (popup, answer, window) = Open(Model(2, new[] { 60, 64 }));
        try
        {
            Find<TextBox>(popup, "TransposeValue").Text = "-3";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(PianoRollPositionText.PitchName(57), Chips(popup, "MoveAfter"));
            Find<TextBox>(popup, "TransposeValue").Text = "100";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Some notes would go out of range", Find<TextBlock>(popup, "TransposeWarning").Text);
            Find<TextBox>(popup, "TransposeValue").Text = "-3";
            Dispatcher.UIThread.RunJobs();
            Press(popup, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(-3, answer.Result!.Value.Delta);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSegmentsSwitchCaptionsAndWordsAndPersistThroughTheCallback()
    {
        var heard = new List<bool>();
        var (popup, answer, window) = Open(Model(scale: true), heard.Add);
        try
        {
            Assert.Equal("only notes of C major", Find<TextBlock>(popup, "MoveStayInKeyCaption").Text);
            Assert.True(Find<ToggleButton>(popup, "MoveHalfSteps").IsChecked);
            Assert.Equal("half step", Find<TextBlock>(popup, "MoveUnit").Text);
            Segment(popup, "MoveStayInKey");
            Assert.Equal(new[] { true }, heard);
            Assert.True(Find<ToggleButton>(popup, "MoveStayInKey").IsChecked);
            Assert.Equal("step", Find<TextBlock>(popup, "MoveUnit").Text);
            Click(popup, "TransposeOctaveUp");
            Assert.Equal("up 8", Find<TextBox>(popup, "TransposeValue").Text);   // 1 + 7 steps
            Press(popup, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(answer.Result!.Value.ScaleSteps);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void StayInTheKeyIsDisabledWithNoScale()
    {
        var (popup, _, window) = Open(Model());
        try
        {
            Assert.False(Find<ToggleButton>(popup, "MoveStayInKey").IsEnabled);
            Assert.Equal("pick a key first", Find<TextBlock>(popup, "MoveStayInKeyCaption").Text);
            Assert.False(Find<CheckBox>(popup, "MoveSnap").IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSnapTickShowsOnlyInHalfStepsFollowsLockAndPullsTheChipsIntoKey()
    {
        var (popup, answer, window) = Open(Model(1, new[] { 60 }, scale: true, lockScale: false));
        try
        {
            var snap = Find<CheckBox>(popup, "MoveSnap");
            Assert.True(snap.IsEffectivelyVisible);
            Assert.False(snap.IsChecked);   // default = the Lock setting (off here)
            Assert.Equal(new[] { PianoRollPositionText.PitchName(61) }, Chips(popup, "MoveAfter"));
            snap.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { PianoRollPositionText.PitchName(62) }, Chips(popup, "MoveAfter"));
            Assert.Equal(new[] { PianoRollPositionText.PitchName(60) }, Chips(popup, "MoveBefore"));
            Segment(popup, "MoveStayInKey");
            Assert.False(snap.IsEffectivelyVisible);
            Segment(popup, "MoveHalfSteps");
            Press(popup, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(answer.Result!.Value.Snap);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWarningShowsOnlyWhenNotesWouldLeaveTheRangeAndEscapeCancels()
    {
        var (popup, answer, window) = Open(Model(2, new[] { 0, 60 }, initial: 0));
        try
        {
            Assert.False(Find<TextBlock>(popup, "TransposeWarning").IsEffectivelyVisible);
            Click(popup, "TransposeMinus");
            Assert.True(Find<TextBlock>(popup, "TransposeWarning").IsEffectivelyVisible);
            Press(popup, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
            Assert.Null(answer.Result);
        }
        finally { window.Close(); }
    }
}
