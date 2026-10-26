using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using Avalonia.Threading;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the project settings window (structure, presenter texts and clamping, renders, contrast).
/// Does not own: the window or ProjectSettingsPresenter; expected values come from the presenter and the window's texts.
/// Tests: this class.
/// </summary>
public class ProjectSettingsWindowTests
{
    private static readonly ProjectSettingsResult Original = new("Song", "Sub", "Artist", "Album", "Music", "Words", "Tab", "(c)",
        120, 4, 4, 0, false, false, "", "", "");

    private static ProjectSettingsWindow Open(ProjectSettingsResult? original = null) => new(original ?? Original, 3, 40, 2);

    [AvaloniaFact]
    public void ControlsMatchWpfTitleSizeTabsAndButtons()
    {
        var window = Open();
        window.Show();

        Assert.Equal(("Project settings", 660d, 600d), (window.Title, window.Width, window.Height));
        Assert.Equal(new[] { "Song", "Credits", "Music", "Notes & lyrics", "Summary" },
            window.GetLogicalDescendants().OfType<TabItem>().Select(t => (string)t.Header!).ToList());
        Assert.Equal(new[] { "Cancel", "OK" }, Buttons(window));
        var tabItems = window.GetLogicalDescendants().OfType<TabItem>().ToList();
        Assert.All(tabItems, tab => Assert.True(tab.FontSize <= 15 && tab.Height <= 40));
        Assert.All(tabItems, tab => Assert.Equal(new Thickness(0, 2, 0, 0), tab.BorderThickness));
        var texts = window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Starting tempo, 20–400. Tempo changes inside the song are kept.", texts);
        Assert.Contains("Performance notes shown with the score.", texts);
        Assert.Contains("Length assumes the starting tempo and time signature, without repeats.", texts);
        window.Close();
    }

    [AvaloniaFact]
    public void SizesAndDefaultButtonsMatchWpf()
    {
        var window = Open();
        window.Show();

        Assert.Equal((660d, 600d, 540d, 460d), (window.Width, window.Height, window.MinWidth, window.MinHeight));
        Assert.Equal((ChromeMetrics.SongInfoWindowWidth, ChromeMetrics.SongInfoWindowHeight), (window.Width, window.Height));
        var buttons = window.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.True(buttons.Single(b => (string?)b.Content == "OK").IsDefault);
        Assert.True(buttons.Single(b => (string?)b.Content == "Cancel").IsCancel);
        window.Close();
    }

    [AvaloniaFact]
    public void SummaryFiguresComeFromThePresenter()
    {
        var presenter = new ProjectSettingsPresenter(Original, 3, 40, 2);
        var window = Open();
        window.Show();

        var texts = window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(presenter.LengthText, texts);
        Assert.Contains(presenter.TrackCount.ToString(), texts);
        Assert.Contains(presenter.BarCount.ToString(), texts);
        Assert.Contains(presenter.SectionCount.ToString(), texts);
        window.Close();
    }

    [AvaloniaFact]
    public void KeyBoxIsClampedThroughThePresenterAndOkRejectsTempoOutOfRange()
    {
        var song = Original with { KeySignature = 99 };
        var window = Open(song);
        window.Show();

        var keyBox = window.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Items.Count == SignatureChoices.KeyNames.Count);
        Assert.Equal(ProjectSettingsPresenter.KeyIndexOf(99), keyBox.SelectedIndex);
        Assert.Equal(SignatureChoices.KeyNames.Count - 1, keyBox.SelectedIndex);

        var tempo = window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Text == "120");
        tempo.Text = "500";
        Click(window, "OK");

        var problem = new ProjectSettingsPresenter(song, 3, 40, 2).FindProblem(new ProjectSettingsDraft(
            "Song", "Sub", "Artist", "Album", "Music", "Words", "Tab", "(c)", "500", "4", 2, keyBox.SelectedIndex, false, false, "", "", ""));
        Assert.NotNull(problem);
        Assert.Equal(problem!.Message, window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Foreground is not null && t.Text == problem.Message).Text);
        Assert.True(window.IsVisible);

        // The draft has changes, so closing asks first; answering the question lets the window close (an unanswered modal hangs the run).
        window.Close();
        Dispatcher.UIThread.RunJobs();
        var prompt = window.OwnedWindows.OfType<SavePromptWindow>().Single();
        prompt.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "YesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task OkWithoutEditsReturnsTheOpenedValues()
    {
        var owner = new Window();
        owner.Show();
        var window = Open();
        var pending = window.ShowDialog<ProjectSettingsResult?>(owner);
        Click(window, "OK");

        Assert.Equal(Original, await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task EscWithoutEditsCancelsWithNull()
    {
        var owner = new Window();
        owner.Show();
        var window = Open();
        var pending = window.ShowDialog<ProjectSettingsResult?>(owner);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = window });

        Assert.Null(await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public void SecondCloseWhileTheQuestionIsOpenDoesNotOpenASecondPrompt()
    {
        var window = Open();
        window.Show();
        window.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Text == "120").Text = "130";
        window.Close();
        window.Close();
        Dispatcher.UIThread.RunJobs();
        var prompt = window.OwnedWindows.OfType<SavePromptWindow>().Single();
        prompt.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "YesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RendersPngInEachTheme(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = Open();
        var path = DialogTestKit.SavePng(window, nameof(ProjectSettingsWindowTests), light ? "project-settings-light.png" : "project-settings-dark.png");
        window.Close();
        Assert.True(new FileInfo(path).Length > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnThePanel(bool light)
    {
        var panel = AvaloniaPalette.Get("PanelBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), panel) >= 4.5, $"{text} on PanelBackground (light: {light})");
    }

    private static IEnumerable<string> Buttons(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Where(b => b is not global::Avalonia.Controls.Primitives.ToggleButton).Select(b => (string)b.Content!).ToList();

    private static void Click(Window window, string content) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => (string?)b.Content == content)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
