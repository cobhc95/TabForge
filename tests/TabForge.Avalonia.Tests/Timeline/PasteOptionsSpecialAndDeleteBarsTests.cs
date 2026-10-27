using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the paste options, Paste Special and Delete-bars windows (structure, texts, results, renders, contrast)
/// and their Tools menu items.
/// Does not own: the windows or the presenters; expected values come from PasteOptionsPresenter, PasteSpecialPresenter and BarRangePromptPresenter.
/// Tests: this class.
/// </summary>
public class PasteOptionsSpecialAndDeleteBarsTests
{
    private const string SampleText = "Bars 1-2 are selected. What should Delete do?";

    [AvaloniaFact]
    public void PasteOptionsShowsEachQuestionWithItsLabelsAndRemember()
    {
        var presenter = new PasteOptionsPresenter(PasteQuestionInfo.All);
        var window = new PasteOptionsWindow(PasteQuestionInfo.All);
        window.Show();

        Assert.Equal(PasteOptionsPresenter.Title, window.Title);
        var expected = presenter.Questions.SelectMany(presenter.OptionLabels).ToList();
        Assert.Equal(expected, window.GetLogicalDescendants().OfType<RadioButton>().Select(r => (string)r.Content!).ToList());
        Assert.Equal(presenter.Questions.Count, window.GetLogicalDescendants().OfType<CheckBox>().Count());
        Assert.Equal(presenter.Answers(_ => 0, _ => false), window.Current());
        window.Close();
    }

    [AvaloniaFact]
    public async Task PasteOptionsPasteReturnsThePickedAnswers()
    {
        var owner = new Window();
        owner.Show();
        var window = new PasteOptionsWindow(new[] { PasteQuestion.Octave, PasteQuestion.Drums });
        var pending = window.ShowDialog<PasteAnswers?>(owner);
        Named<RadioButton>(window, $"Q{(int)PasteQuestion.Octave}Option1").IsChecked = true;
        Named<CheckBox>(window, $"Q{(int)PasteQuestion.Octave}Remember").IsChecked = true;
        Click(Named<Button>(window, "PasteButton"));

        var expected = new PasteOptionsPresenter(new[] { PasteQuestion.Octave, PasteQuestion.Drums })
            .Answers(q => q == PasteQuestion.Octave ? 1 : 0, q => q == PasteQuestion.Octave);
        Assert.Equal(expected, await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task PasteOptionsCancelReturnsNull()
    {
        var owner = new Window();
        owner.Show();
        var window = new PasteOptionsWindow(new[] { PasteQuestion.Drums });
        var pending = window.ShowDialog<PasteAnswers?>(owner);
        Click(Named<Button>(window, "CancelButton"));
        Assert.Null(await pending);
        owner.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PasteSpecialModesAndBarSettingsFollowTheClipKind(bool bars)
    {
        var presenter = new PasteSpecialPresenter(bars);
        var window = new PasteSpecialWindow(bars);
        window.Show();

        Assert.Equal(presenter.ModeLabels, window.GetLogicalDescendants().OfType<RadioButton>().Select(r => (string)r.Content!).ToList());
        Assert.Equal(bars, window.GetLogicalDescendants().OfType<CheckBox>().Any(c => c.Name == "BarSettingsBox"));
        Assert.Equal(presenter.Result(), window.Current());
        Assert.Equal("none", Named<TextBlock>(window, "OctaveText").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void PasteSpecialStepsClampAndKeepFingeringTurnsTheOctaveOff()
    {
        var window = new PasteSpecialWindow(bars: true);
        window.Show();
        for (var i = 0; i < 4; i++) Click(Named<Button>(window, "OctaveUp"));
        Assert.Equal("+2 octaves", Named<TextBlock>(window, "OctaveText").Text);
        Assert.False(Named<Button>(window, "OctaveUp").IsEnabled);
        Named<TextBox>(window, "RepeatBox").Text = "250";
        Named<RadioButton>(window, "Mode2").IsChecked = true;
        Assert.Equal(new PasteSpecialOptions(99, BeatPasteMode.Replace, BarsOntoNotesAnswer.InsertAfter, 2, false, true), window.Current());

        Named<CheckBox>(window, "KeepFingeringBox").IsChecked = true;
        Assert.False(Named<Button>(window, "OctaveDown").IsEnabled);
        Assert.Equal(0, window.Current().OctaveShift);
        Click(Named<Button>(window, "RepeatDown"));
        Assert.Equal("98", Named<TextBox>(window, "RepeatBox").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void DeleteBarsShowsLabelsScopesAndDefaults()
    {
        var window = new BarRangePromptWindow(SampleText, BarRangeAction.Remove, allTracks: false, id => id == "Range.Remove" ? "Shift+Del" : "");
        window.Show();

        Assert.Equal(BarRangePromptPresenter.Title, window.Title);
        Assert.Equal(SampleText, Named<TextBlock>(window, "MessageText").Text);
        var labels = window.GetLogicalDescendants().OfType<RadioButton>().Select(r => (string)r.Content!).ToList();
        Assert.Equal(BarRangePromptPresenter.Labels(id => id == "Range.Remove" ? "Shift+Del" : "").Concat(BarRangePromptPresenter.ClipLabels).Concat(BarRangePromptPresenter.Scopes), labels);
        Assert.Equal(new BarRangeAnswer(BarRangeAction.Remove, false, false), window.Current());
        Assert.Equal(new[] { "OK", "Cancel" }, window.GetLogicalDescendants().OfType<Button>().Where(b => b is not global::Avalonia.Controls.Primitives.ToggleButton).Select(b => (string)b.Content!));
        window.Close();
    }

    [AvaloniaFact]
    public async Task DeleteBarsOkReturnsTheAnswerAndEscapeCancels()
    {
        var owner = new Window();
        owner.Show();
        var window = new BarRangePromptWindow(SampleText, BarRangeAction.Clear, true, _ => "");
        var pending = window.ShowDialog<BarRangeAnswer?>(owner);
        Named<RadioButton>(window, "Choice1").IsChecked = true;
        Named<RadioButton>(window, "Scope1").IsChecked = true;
        Named<CheckBox>(window, "RememberBox").IsChecked = true;
        Click(Named<Button>(window, "OkButton"));
        Assert.Equal(new BarRangeAnswer(BarRangeAction.Remove, false, true), await pending);

        var second = new BarRangePromptWindow(SampleText, BarRangeAction.Clear, true, _ => "");
        var cancelled = second.ShowDialog<BarRangeAnswer?>(owner);
        second.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = second });
        Assert.Null(await cancelled);
        owner.Close();
    }

    [AvaloniaTheory]
    [InlineData("Delete bars prompt…", typeof(BarRangePromptWindow))]
    public void PreviewToolsMenuOpensEachWindow(string header, System.Type type)
    {
        var main = new MainWindow();
        main.Show();
        // The development preview menus are not in the release menu: the entry list opens the window.
        PreviewToolsMenu.Entries(main).Single(e => e.Header == header).Open();
        Dispatcher.UIThread.RunJobs();

        var opened = main.OwnedWindows.Where(type.IsInstanceOfType).ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsRenderPngsInEachTheme(bool light)
    {
        var windows = new (Window Window, string Name)[]
        {
            (new PasteOptionsWindow(PasteQuestionInfo.All), "paste-options"),
            (new PasteSpecialWindow(bars: true), "paste-special"),
            (new BarRangePromptWindow(SampleText, BarRangeAction.Clear, true, _ => ""), "delete-bars"),
        };
        using var theme = UseTheme(light);
        foreach (var (window, name) in windows)
        {
            Assert.True(new FileInfo(SavePng(window, nameof(PasteOptionsSpecialAndDeleteBarsTests), $"{name}-{(light ? "light" : "dark")}.png")).Length > 0);
            window.Close();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOne(bool light)
    {
        var panel = AvaloniaPalette.Get("PanelBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), panel) >= 4.5, $"{text} on PanelBackground (light: {light})");
    }
}
