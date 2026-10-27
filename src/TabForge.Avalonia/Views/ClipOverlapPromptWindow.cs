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
using TabForge.Documents;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The "Clips overlap" prompt: the question, two clickable radio cards (RadioCard; move the existing clip to a free lane, default; keep them overlapping),
/// "Remember my choice" with its hint, and Cancel / OK. Enter is OK, Esc cancels. Close leaves <see cref="Answer"/> null on cancel.
/// Owns: the window's controls and reading them back. Does not own: the words (ClipOverlapPromptPresenter), finding or moving the clips
/// (ClipLaneOverlap) or the stored setting.
/// Tests: TabForge.Avalonia.Tests.ClipLaneOverlapTests.
/// </summary>
public sealed class ClipOverlapPromptWindow : Window
{
    private readonly RadioCard _move;
    private readonly CheckBox _remember;

    /// <summary>The answer the window closed with; null on Cancel / Esc / close.</summary>
    public ClipOverlapAnswer? Answer { get; private set; }

    public ClipOverlapPromptWindow(ClipOverlapQuestion question)
    {
        PromptChrome.Setup(this, ClipOverlapPromptPresenter.Title, DialogMetrics.SectionAudioWidth);

        var body = PromptChrome.Column(ClipOverlapPromptPresenter.Title, PromptChrome.Muted("MessageText", ClipOverlapPromptPresenter.Body(question)));
        var group = new RadioCardGroup("ClipOverlapChoice");
        _move = new RadioCard(group, "Move", ClipOverlapPromptPresenter.MoveTitle(question), ClipOverlapPromptPresenter.MoveHint, selected: true);
        body.Children.Add(_move);
        body.Children.Add(new RadioCard(group, "Keep", ClipOverlapPromptPresenter.KeepTitle, ClipOverlapPromptPresenter.KeepHint, selected: false));

        _remember = new CheckBox { Name = "RememberBox", Content = ClipOverlapPromptPresenter.RememberText };
        var hint = PromptChrome.Muted("RememberHint", ClipOverlapPromptPresenter.RememberHint);

        var cancel = PromptChrome.Action("CancelButton", ClipOverlapPromptPresenter.CancelText, primary: false, isCancel: true);
        ToolTip.SetTip(cancel, ClipOverlapPromptPresenter.CancelToolTip);
        cancel.Click += (_, _) => Close();
        var ok = PromptChrome.Action("OkButton", ClipOverlapPromptPresenter.OkText, primary: true);
        ToolTip.SetTip(ok, ClipOverlapPromptPresenter.OkToolTip);
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
        Opened += (_, _) => _move.Focus();
    }

    /// <summary>Shows the prompt over <paramref name="owner"/>; null when the user cancelled.</summary>
    public static async Task<ClipOverlapAnswer?> AskAsync(Window? owner, ClipOverlapQuestion question) =>
        await DialogLauncher.AskFrom<ClipOverlapPromptWindow, ClipOverlapAnswer?>(owner, () => new ClipOverlapPromptWindow(question), window => window.Answer);

    private void Accept()
    {
        Answer = new ClipOverlapAnswer(_move.IsSelected, _remember.IsChecked == true);
        Close();
    }
}
