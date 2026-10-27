using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The "Close gap (bars A-B)?" prompt for a gap that is not empty: what is in the bars (notes per track, clips crossing), three radio cards (remove the bars and what is in them, default;
/// make it a section instead; remove empty bars only) and Cancel / OK. Enter confirms, Esc cancels. <see cref="Answer"/> is null on cancel.
/// Owns: the window's controls and reading them back. Does not own: the edit (SectionGapEdits) or the undo step.
/// Tests: TabForge.Avalonia.Tests.SectionGapCloseTests.
/// </summary>
public sealed class GapClosePromptWindow : Window
{
    private readonly RadioCard _remove, _section, _emptyOnly;

    public GapCloseMode? Answer { get; private set; }

    /// <summary>The window's title for bars first..last (0-based).</summary>
    public static string TitleFor(int first, int last) => $"Close gap (bars {first + 1}\u2013{last + 1})?";

    /// <summary>The "These bars aren't empty:" list: notes per track, then the clips crossing.</summary>
    public static string BodyFor(GapContents contents) =>
        "These bars aren't empty:\n" + string.Join("\n", contents.Notes.Select(n => $"\u2022 {n.Track}: {n.Notes} note{(n.Notes == 1 ? "" : "s")}")
            .Concat(contents.Clips.Select(c => $"\u2022 clip {c}")));

    /// <summary>The compact "Not empty: Track (n) · ... · clip ..." line (full list in <see cref="BodyFor"/>, shown as its tooltip).</summary>
    public static string SummaryFor(GapContents contents) =>
        "Not empty: " + string.Join(" · ", contents.Notes.Select(n => $"{n.Track} ({n.Notes})").Concat(contents.Clips.Select(c => $"clip {c}")))
        + $" ({contents.Notes.Sum(n => n.Notes)} note{(contents.Notes.Sum(n => n.Notes) == 1 ? "" : "s")})";

    public GapClosePromptWindow(int first, int last, GapContents contents)
    {
        PromptChrome.Setup(this, TitleFor(first, last), DialogMetrics.SectionAudioWidth);

        var range = $"{first + 1}–{last + 1}";
        var body = PromptChrome.Column(TitleFor(first, last), PromptChrome.Muted("MessageText", SummaryFor(contents), maxLines: 2, tip: BodyFor(contents)));
        var group = new RadioCardGroup("GapCloseChoice");
        _remove = new RadioCard(group, "Remove", "Remove the bars and what's in them", $"Notes in bars {range} go; clips crossing the gap are trimmed to the gap edges.", selected: true);
        _section = new RadioCard(group, "Section", "Make it a section instead", $"Keeps everything; adds a section named 'Bars {range}' so nothing is lost.", selected: false);
        _emptyOnly = new RadioCard(group, "EmptyOnly", "Remove empty bars only", "Only bars with nothing in any track are removed; the rest stay.", selected: false);
        body.Children.Add(_remove);
        body.Children.Add(_section);
        body.Children.Add(_emptyOnly);

        var cancel = PromptChrome.Action("CancelButton", "Cancel", primary: false, isCancel: true);
        cancel.Click += (_, _) => Close();
        var ok = PromptChrome.Action("OkButton", "OK", primary: true);
        ok.Click += (_, _) => Confirm();
        body.Children.Add(PromptChrome.Footer(null, null, cancel, ok));
        Content = body;

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) Confirm();
            else if (e.Key == Key.Escape) Close();
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) => _remove.Focus();
    }

    /// <summary>Shows the prompt over <paramref name="owner"/>; null when cancelled.</summary>
    public static async Task<GapCloseMode?> AskAsync(Window? owner, int first, int last, GapContents contents) =>
        await DialogLauncher.AskFrom<GapClosePromptWindow, GapCloseMode?>(owner, () => new GapClosePromptWindow(first, last, contents), window => window.Answer);

    private void Confirm()
    {
        Answer = _section.IsSelected ? GapCloseMode.MakeSection : _emptyOnly.IsSelected ? GapCloseMode.RemoveEmptyOnly : GapCloseMode.RemoveAll;
        Close();
    }
}
