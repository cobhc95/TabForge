using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the small track and confirm prompts in Avalonia: texts, buttons, the Enter default, Esc, Y and N, the choices
/// and remember box, the Tools submenu, and contrast on the window.
/// Does not own: the window or the wording; expected texts come from PromptTextsPresenter.
/// Tests: this class.
/// </summary>
public class PromptWindowTests
{
    // The catalogue's default Undo key: the sentences must name the bound gesture, not a typed one.
    private static readonly string Undo = HotkeyCatalog.DisplayAll(new HotkeySettings(), "Edit.Undo");

    [AvaloniaFact]
    public void DeletePromptShowsPresenterTextsAndCancelIsTheDefault()
    {
        var window = new PromptWindow(PromptTextsPresenter.DeleteTrack("Guitar", Undo));
        window.Show();
        Assert.Equal("Delete track", window.Title);
        Assert.Equal($"Delete the track 'Guitar'? You can undo this with {Undo}.", Named<TextBlock>(window, "PromptMessage").Text);
        Assert.Equal(new[] { "Delete", "Cancel" }, ButtonTexts(window));
        Assert.True(ButtonNamed(window, "Cancel").IsDefault);
        Assert.True(ButtonNamed(window, "Cancel").IsCancel);
        window.Close();
    }

    [AvaloniaFact]
    public void AddTrackPromptOffersTheTwoKindsAndCancel()
    {
        var window = new PromptWindow(PromptTextsPresenter.AddTrack());
        window.Show();
        Assert.Equal("What kind of track do you want to add?", Named<TextBlock>(window, "PromptMessage").Text);
        Assert.Equal(new[] { "Audio track", "Instrument…", "Cancel" }, ButtonTexts(window));
        Assert.True(ButtonNamed(window, "Audio track").IsDefault);
        Assert.True(ButtonNamed(window, "Cancel").IsCancel);
        window.Close();
    }

    [AvaloniaFact]
    public async Task EnterPressesTheDefaultAnswer()
    {
        var owner = new Window();
        owner.Show();
        var addTrack = new PromptWindow(PromptTextsPresenter.AddTrack());
        var pendingAdd = addTrack.ShowDialog(owner);
        Press(addTrack, Key.Enter);
        await pendingAdd;
        Assert.Equal(PromptAnswer.Yes, addTrack.Outcome!.Value.Answer);

        var delete = new PromptWindow(PromptTextsPresenter.DeleteTrack("Guitar", Undo));
        var pendingDelete = delete.ShowDialog(owner);
        Press(delete, Key.Enter);
        await pendingDelete;
        Assert.Equal(PromptAnswer.No, delete.Outcome!.Value.Answer);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task EscCancelsEveryPrompt()
    {
        var owner = new Window();
        owner.Show();
        var specs = new[]
        {
            PromptTextsPresenter.AddTrack(),
            PromptTextsPresenter.DeleteTrack("Guitar", Undo),
            PromptTextsPresenter.Confirm("TabForge", "Smoke check prompt", "Yes", false, Undo),
        };
        foreach (var spec in specs)
        {
            var window = new PromptWindow(spec);
            var pending = window.ShowDialog(owner);
            Press(window, Key.Escape);
            await pending;
            Assert.Equal(PromptAnswer.Cancel, window.Outcome!.Value.Answer);
        }
        owner.Close();
    }

    [AvaloniaFact]
    public async Task YAnswersYesOnlyWhereYesIsNotTheDefaultAndNAnswersNo()
    {
        var owner = new Window();
        owner.Show();
        var addTrack = new PromptWindow(PromptTextsPresenter.AddTrack());
        var pendingAdd = addTrack.ShowDialog(owner);
        Press(addTrack, Key.Y);
        await pendingAdd;
        Assert.Equal(PromptAnswer.Yes, addTrack.Outcome!.Value.Answer);

        var delete = new PromptWindow(PromptTextsPresenter.DeleteTrack("Guitar", Undo));
        var pendingDelete = delete.ShowDialog(owner);
        Press(delete, Key.Y);
        Assert.Null(delete.Outcome);
        Press(delete, Key.N);
        await pendingDelete;
        Assert.Equal(PromptAnswer.No, delete.Outcome!.Value.Answer);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task ClipPromptReturnsTheChosenOptionAndTheRememberBox()
    {
        var owner = new Window();
        owner.Show();
        var window = new PromptWindow(PromptTextsPresenter.ConvertTrackToInstrumentClips("Guitar", hasMidi: true));
        Assert.Equal(new[] { "Write them as notation", "Keep them as MIDI on a second lane" }, RadioTexts(window));
        Assert.Equal("Remember my choice", Named<CheckBox>(window, "PromptRemember").Content);
        var pending = window.ShowDialog(owner);
        window.GetLogicalDescendants().OfType<RadioButton>().ElementAt(1).IsChecked = true;
        Named<CheckBox>(window, "PromptRemember").IsChecked = true;
        Click(ButtonNamed(window, "Convert"));
        await pending;
        var outcome = window.Outcome!.Value;
        Assert.Equal((ClipAnswer.KeepClips, true),
            PromptTextsPresenter.ConvertClipsAnswer(outcome.Answer, true, outcome.SelectedChoice, outcome.RememberTicked));
        owner.Close();
    }

    [AvaloniaFact]
    public void TrackPromptsSubmenuListsEverySmallPrompt()
    {
        var menu = PreviewPromptsMenu.Build(new Window());
        Assert.Equal("Track prompts", menu.Header?.ToString());
        Assert.Equal(7, menu.Items.Count);
    }

    [AvaloniaFact]
    public void PromptTextReadsAtLeastFourAndAHalfToOneInTheDarkTheme() => AssertPromptContrast(light: false);

    [AvaloniaFact]
    public void PromptTextReadsAtLeastFourAndAHalfToOneInTheLightTheme() => AssertPromptContrast(light: true);

    private static void AssertPromptContrast(bool light)
    {
        using var theme = UseTheme(light);
        var window = new PromptWindow(PromptTextsPresenter.ConvertTrackToInstrumentClips("Guitar", hasMidi: true));
        window.Show();
        foreach (var text in window.GetLogicalDescendants().OfType<TextBlock>())
            Assert.True(Contrast(text.Foreground, SurfaceBehind(text, window), $"prompt text '{text.Text}' (light: {light})") >= 4.5);
        window.Close();
    }

    private static void Press(Window window, Key key) =>
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    private static List<string> ButtonTexts(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Select(b => (string)b.Content!).ToList();

    private static List<string> RadioTexts(Window window) =>
        window.GetLogicalDescendants().OfType<RadioButton>().Select(r => (string)r.Content!).ToList();

    private static Button ButtonNamed(Window window, string text) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text);
}
