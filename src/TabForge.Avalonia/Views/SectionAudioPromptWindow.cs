using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The "Audio crosses this section" prompt: the question, two clickable radio cards (RadioCard; split and move with the section, default; leave the audio where it is),
/// "Remember my choice" with its hint, and Cancel move / Move. Enter moves, Esc cancels. Close leaves <see cref="Answer"/> null on cancel.
/// Owns: the window's controls and reading them back. Does not own: the words and answer (SectionAudioPromptPresenter), finding or cutting the
/// clips (SectionAudioCrossings) or the stored setting.
/// Tests: TabForge.Avalonia.Tests.SectionAudioMoveTests.
/// </summary>
public sealed class SectionAudioPromptWindow : Window
{
    private readonly RadioCard _split;
    private readonly RadioCard _leave;
    private readonly CheckBox _remember;

    /// <summary>The answer the window closed with; null on Cancel / Esc / close.</summary>
    public SectionAudioAnswer? Answer { get; private set; }

    public SectionAudioPromptWindow(SongProject project, SectionAudioCrossing crossing)
    {
        PromptChrome.Setup(this, SectionAudioPromptPresenter.Title, DialogMetrics.SectionAudioWidth);

        var body = PromptChrome.Column(SectionAudioPromptPresenter.Title, PromptChrome.Muted("MessageText", SectionAudioPromptPresenter.Body(project, crossing)));
        var group = new RadioCardGroup("SectionAudioChoice");
        _split = new RadioCard(group, "Split", SectionAudioPromptPresenter.SplitTitle, SectionAudioPromptPresenter.SplitHint, selected: true);
        _leave = new RadioCard(group, "Leave", SectionAudioPromptPresenter.LeaveTitle, SectionAudioPromptPresenter.LeaveHint, selected: false);
        body.Children.Add(_split);
        body.Children.Add(_leave);

        _remember = new CheckBox { Name = "RememberBox", Content = SectionAudioPromptPresenter.RememberText };
        var hint = PromptChrome.Muted("RememberHint", SectionAudioPromptPresenter.RememberHint);

        var cancel = PromptChrome.Action("CancelButton", SectionAudioPromptPresenter.CancelText, primary: false, isCancel: true);
        ToolTip.SetTip(cancel, SectionAudioPromptPresenter.CancelToolTip);
        cancel.Click += (_, _) => Close();
        var move = PromptChrome.Action("MoveButton", SectionAudioPromptPresenter.MoveText, primary: true);
        ToolTip.SetTip(move, SectionAudioPromptPresenter.MoveToolTip);
        move.Click += (_, _) => Move();
        body.Children.Add(PromptChrome.Footer(_remember, hint, cancel, move));
        Content = body;

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) Move();
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) => _split.Focus();
    }

    /// <summary>Shows the prompt over <paramref name="owner"/>; null when the user cancelled the move.</summary>
    public static async Task<SectionAudioAnswer?> AskAsync(Window? owner, SongProject project, SectionAudioCrossing crossing) =>
        await DialogLauncher.AskFrom<SectionAudioPromptWindow, SectionAudioAnswer?>(owner, () => new SectionAudioPromptWindow(project, crossing), window => window.Answer);

    private void Move()
    {
        Answer = new SectionAudioAnswer(_split.IsSelected, _remember.IsChecked == true);
        Close();
    }
}
