using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Repeat selection text prompt in Avalonia: the presenter's count rules, the window's title, label and default, OK and Cancel results,
/// Repeat selection with a typed count, and the look of the prompt and the paste questions in both themes (PNG files under the temp tf-avalonia-tests folder).
/// Tests: RepeatSelectionPromptPresenter, TextPromptWindow, MainWindowEdit.
/// </summary>
public class TextPromptWindowTests
{
    [Theory]
    [InlineData("3", 3)]
    [InlineData("16", 16)]
    [InlineData("99", 16)]
    [InlineData("1", 1)]
    [InlineData("0", null)]
    [InlineData("-2", null)]
    [InlineData("two", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TypedCountIsClampedOrRefused(string? typed, int? expected) => Assert.Equal(expected, RepeatSelectionPromptPresenter.Times(typed));

    [AvaloniaFact]
    public void WindowShowsTheWpfTitleLabelAndDefault()
    {
        var window = new TextPromptWindow(RepeatSelectionPromptPresenter.Title, RepeatSelectionPromptPresenter.Label, RepeatSelectionPromptPresenter.Default);
        window.Show();
        Assert.Equal("Repeat selection", window.Title);
        Assert.Equal("How many times should the selected bars be repeated?", Named<TextBlock>(window, "PromptLabel").Text);
        Assert.Equal("1", Named<TextBox>(window, "PromptText").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task OkReturnsTheTypedTextAndCancelReturnsNull()
    {
        var owner = new Window();
        owner.Show();
        var ok = new TextPromptWindow("t", "l", "1");
        var okResult = ok.ShowDialog<string?>(owner);
        Named<TextBox>(ok, "PromptText").Text = "4";
        Click(Named<Button>(ok, "OkButton"));
        Assert.Equal("4", await okResult);

        var cancel = new TextPromptWindow("t", "l", "1");
        var cancelResult = cancel.ShowDialog<string?>(owner);
        Click(Named<Button>(cancel, "CancelButton"));
        Assert.Null(await cancelResult);
    }

    [AvaloniaFact]
    public async Task RepeatSelectionRepeatsTheSelectedBarsByTheTypedCount()
    {
        var view = new ScorePreviewView();
        var owner = new Window { Content = view };
        owner.Show();
        var commands = new MainWindowCommands(owner, () => { });
        MainWindowEdit.Register(commands, view, owner, new ContextMenuHost(), () => false, _ => { });
        var bars = view.Editor.Project!.Tracks[0].Measures.Count;
        view.Editor.SelectRange(0, 0, 0, 0);
        commands.Run("Edit.RepeatSelection");
        Dispatcher.UIThread.RunJobs();
        var prompt = owner.OwnedWindows.OfType<TextPromptWindow>().Single();
        Named<TextBox>(prompt, "PromptText").Text = "3";
        Click(Named<Button>(prompt, "OkButton"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(bars + 3, view.Editor.Project!.Tracks[0].Measures.Count);
        owner.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PromptAndPasteQuestionsAreReadableInBothThemes(bool light)
    {
        using var theme = UseTheme(light);
        foreach (var window in new Window[]
        {
            new TextPromptWindow(RepeatSelectionPromptPresenter.Title, RepeatSelectionPromptPresenter.Label, RepeatSelectionPromptPresenter.Default),
            new PasteOptionsWindow(new[] { TabForge.Services.PasteQuestion.BeatsOntoNotes, TabForge.Services.PasteQuestion.Octave }),
        })
        {
            SavePng(window, nameof(TextPromptWindowTests), $"{window.GetType().Name}-{(light ? "light" : "dark")}.png");
            foreach (var text in window.GetLogicalDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)))
                Assert.True(Contrast(text.Foreground, SurfaceBehind(text, window), $"{window.GetType().Name} text '{text.Text}' (light: {light})") >= 4.5);
            window.Close();
        }
    }
}
