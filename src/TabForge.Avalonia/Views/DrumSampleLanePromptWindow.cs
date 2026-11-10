using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// The drum-sample lane prompt: the question, two clickable radio cards (the tab lane, default; the MIDI lane), "Remember my choice" with its hint,
// and Cancel / OK. Enter is OK, Esc cancels. Close leaves Answer null on cancel.
// Owns: the window's controls and reading them back. Does not own: the words (DrumSampleLanePromptPresenter), the drop (DrumSampleDrop) or the stored setting.
// Tests: TabForge.Avalonia.Tests.DrumSampleDropTests.
public sealed class DrumSampleLanePromptWindow : Window
{
    private readonly RadioCard _tab;
    private readonly CheckBox _remember;

    /// <summary>The answer the window closed with; null on Cancel / Esc / close.</summary>
    public DrumSampleLaneAnswer? Answer { get; private set; }

    public DrumSampleLanePromptWindow(DrumSampleDropQuestion question)
    {
        PromptChrome.Setup(this, DrumSampleLanePromptPresenter.Title, DialogMetrics.SectionAudioWidth);

        var body = PromptChrome.Column(DrumSampleLanePromptPresenter.Title, PromptChrome.Muted("MessageText", DrumSampleLanePromptPresenter.Body(question)));
        var group = new RadioCardGroup("DrumSampleLaneChoice");
        _tab = new RadioCard(group, "Tab", DrumSampleLanePromptPresenter.TabTitle,
            question.TabAvailable ? DrumSampleLanePromptPresenter.TabHint : DrumSampleLanePromptPresenter.TabUnavailableHint, selected: question.TabAvailable);
        var midi = new RadioCard(group, "Midi", DrumSampleLanePromptPresenter.MidiTitle, DrumSampleLanePromptPresenter.MidiHint, selected: !question.TabAvailable);
        if (!question.TabAvailable) _tab.IsEnabled = false;
        body.Children.Add(_tab);
        body.Children.Add(midi);

        _remember = new CheckBox { Name = "RememberBox", Content = DrumSampleLanePromptPresenter.RememberText };
        var hint = PromptChrome.Muted("RememberHint", DrumSampleLanePromptPresenter.RememberHint);

        var cancel = PromptChrome.Action("CancelButton", DrumSampleLanePromptPresenter.CancelText, primary: false, isCancel: true);
        ToolTip.SetTip(cancel, DrumSampleLanePromptPresenter.CancelToolTip);
        cancel.Click += (_, _) => Close();
        var ok = PromptChrome.Action("OkButton", DrumSampleLanePromptPresenter.OkText, primary: true);
        ToolTip.SetTip(ok, DrumSampleLanePromptPresenter.OkToolTip);
        ok.Click += (_, _) => Accept();
        body.Children.Add(PromptChrome.Footer(_remember, hint, cancel, ok));
        Content = body;

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) Accept();
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) => (question.TabAvailable ? _tab : midi).Focus();
    }

    /// <summary>Shows the prompt over <paramref name="owner"/>; null when the user cancelled.</summary>
    public static async Task<DrumSampleLaneAnswer?> AskAsync(Window? owner, DrumSampleDropQuestion question) =>
        await DialogLauncher.AskFrom<DrumSampleLanePromptWindow, DrumSampleLaneAnswer?>(owner, () => new DrumSampleLanePromptWindow(question), window => window.Answer);

    private void Accept()
    {
        Answer = new DrumSampleLaneAnswer(_tab.IsSelected ? DrumSampleLane.Tab : DrumSampleLane.Midi, _remember.IsChecked == true);
        Close();
    }
}
