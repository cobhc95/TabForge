using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

/// <summary>
/// "Insert several bars": a count and where the new bars go (before the selected bar, after it, or at the end of the song); OK closes with the
/// answer, Cancel / Esc with null.
/// Owns: the window's controls and reading the answer back. Does not own: the wording and the count rules (InsertBarsPresenter) or the insert itself (BarStructureEdits.InsertBars).
/// Tests: TabForge.Avalonia.Tests.InsertMenuTests.
/// </summary>
public sealed class InsertBarsWindow : BarDialogWindow<InsertBarsAnswer>
{
    private readonly TextBox _count;
    private readonly RadioButton[] _places;
    private readonly int _songBars;

    /// <param name="songBars">The bars the song has now (the count is held to the room left under the song limit).</param>
    public InsertBarsWindow(int songBars) : base(InsertBarsPresenter.Title, DialogMetrics.InsertBarsWidth, "Insert")
    {
        _songBars = songBars;
        _count = new TextBox { Name = "BarCount", Text = InsertBarsPresenter.DefaultCount, Width = DialogMetrics.InsertBarsCountWidth, HorizontalAlignment = HorizontalAlignment.Left };
        _places = new[] { InsertBarsPresenter.BeforeText, InsertBarsPresenter.AfterText, InsertBarsPresenter.AtEndText }
            .Select((text, i) => new RadioButton { Name = "Place" + i, Content = text, GroupName = "place", IsChecked = i == 1 }).ToArray();
        SetBody(new Control[] { PromptChrome.Heading(InsertBarsPresenter.Title), Label(InsertBarsPresenter.CountLabel), _count }.Concat(_places).ToArray());
        Opened += (_, _) => { _count.Focus(); _count.SelectAll(); };
    }

    protected override bool CanAccept() => InsertBarsPresenter.Count(_count.Text, _songBars) is not null;

    protected override InsertBarsAnswer? Answer() =>
        InsertBarsPresenter.Count(_count.Text, _songBars) is { } count
            ? new InsertBarsAnswer(count, (InsertBarsPlace)System.Array.FindIndex(_places, p => p.IsChecked == true))
            : null;
}
