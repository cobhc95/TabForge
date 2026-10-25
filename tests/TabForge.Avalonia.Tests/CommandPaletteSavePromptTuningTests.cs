using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the command palette, save-changes prompt and global tuning windows (structure, presenter texts, keys, renders, contrast).
/// Does not own: the windows or the presenters; expected values come from CommandPalettePresenter, SavePromptPresenter and GlobalTuningPresenter.
/// Tests: this class.
/// </summary>
public class CommandPaletteSavePromptTuningTests
{
    // WPF command palette, src/TabForge/Views/CommandPalette.cs: the search box, then the matching-commands list (role, automation name).
    private static readonly (string Role, string Automation)[] CommandPaletteControls =
    {
        ("TextBox", "Search commands"),
        ("ListBox", "Matching commands"),
    };

    [AvaloniaFact]
    public void CommandPaletteControlsMatchWpfOrderAndTitle()
    {
        var window = new CommandPaletteWindow(CommandPaletteWindow.SampleEntries);
        window.Show();

        Assert.Equal(("Command palette", 520d, 420d), (window.Title, window.Width, window.Height));
        var found = window.GetLogicalDescendants().OfType<Control>().Where(c => c is TextBox or ListBox)
            .Select(c => (Role: c.GetType().Name, Automation: AutomationProperties.GetName(c) ?? "")).ToList();
        Assert.Equal(CommandPaletteControls, found);
        window.Close();
    }

    [AvaloniaFact]
    public void FilterListsPresenterRowsAndEnterChoosesTheSelectedCommand()
    {
        var expected = new CommandPalettePresenter(CommandPaletteWindow.SampleEntries);
        var window = new CommandPaletteWindow(CommandPaletteWindow.SampleEntries);
        window.Show();
        window.GetLogicalDescendants().OfType<TextBox>().Single().Text = "sav";
        expected.SetQuery("sav");
        var list = window.GetLogicalDescendants().OfType<ListBox>().Single();
        Assert.Equal(expected.Shown, ((IEnumerable<CommandPaletteEntry>)list.ItemsSource!).ToList());

        Press(window, Key.Down);
        expected.Move(1);
        Assert.Equal(expected.SelectedIndex, list.SelectedIndex);
        Press(window, Key.Enter);
        expected.Choose();
        Assert.Equal(expected.ChosenId, window.ChosenId);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void NoMatchLeavesEnterWithNothingToChoose()
    {
        var window = new CommandPaletteWindow(CommandPaletteWindow.SampleEntries);
        window.Show();
        window.GetLogicalDescendants().OfType<TextBox>().Single().Text = "zzqx";
        Press(window, Key.Enter);

        Assert.Null(window.ChosenId);
        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void EscClosesTheCommandPaletteWithoutChoosing()
    {
        var window = new CommandPaletteWindow(CommandPaletteWindow.SampleEntries);
        window.Show();
        Press(window, Key.Escape);

        Assert.Null(window.ChosenId);
        Assert.False(window.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommandPaletteRendersPngInEachTheme(bool light) =>
        RenderPng(new CommandPaletteWindow(CommandPaletteWindow.SampleEntries), "command-palette", light);

    [AvaloniaFact]
    public void SavePromptTextsAndTooltipsComeFromThePresenter()
    {
        var prompt = SavePromptPresenter.SaveChanges("Song.tf");
        var window = new SavePromptWindow(prompt);
        window.Show();

        Assert.Equal(prompt.Title, window.Title);
        Assert.Equal(prompt.Message, Named<TextBlock>(window, "MessageText").Text);
        Assert.Equal(prompt.YesToolTip + "  (Y / Enter)", ToolTip.GetTip(Named<Button>(window, "YesButton")));
        Assert.Equal(prompt.NoToolTip + "  (N)", ToolTip.GetTip(Named<Button>(window, "NoButton")));
        Assert.Equal(new[] { "Yes", "No", "Cancel" }, Buttons(window));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(Key.Y, PromptButton.Yes)]
    [InlineData(Key.N, PromptButton.No)]
    [InlineData(Key.Escape, PromptButton.Cancel)]
    public void KeysAnswerAsTheirButtonsDo(Key key, PromptButton answer)
    {
        var window = new SavePromptWindow(SavePromptPresenter.SaveChanges("Song.tf"));
        window.Show();
        Press(window, key);

        Assert.Equal(answer, window.Result);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void DiscardQuestionHasNoCancelListsChangesAndOffersRemember()
    {
        var prompt = new SavePrompt("TabForge", "Close without applying the changes?", "Close without applying", "Keep editing");
        var window = new SavePromptWindow(prompt, showCancel: false, details: new[] { "Changed the tempo", "Renamed track 2" }, rememberText: "Don't ask me again");
        window.Show();

        Assert.Equal(new[] { "Yes", "No" }, Buttons(window));
        Assert.Equal("▸ Show changes (2)", Named<TextBlock>(window, "DetailsLink").Text);
        Assert.False(Named<ScrollViewer>(window, "DetailsList").IsVisible);
        Assert.False(window.RememberChoice);
        Named<CheckBox>(window, "RememberBox").IsChecked = true;
        Assert.True(window.RememberChoice);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavePromptRendersPngInEachTheme(bool light) =>
        RenderPng(new SavePromptWindow(SavePromptPresenter.SaveChanges("Song.tf")), "save-prompt", light);

    [AvaloniaFact]
    public void TuningRowsShowPresenterNotesAndOffsets()
    {
        var presenter = new GlobalTuningPresenter(GlobalTuningPresenter.StandardE, TabForge.Services.TuningPresets.SixStringPresets);
        var window = new GlobalTuningWindow(GlobalTuningPresenter.StandardE, TabForge.Services.TuningPresets.SixStringPresets);
        window.Show();

        for (var s = 0; s < 6; s++)
        {
            Assert.Equal(presenter.NoteOf(s), Named<TextBlock>(window, $"NoteLabel{s}").Text);
            Assert.Equal(presenter.OffsetText(s), Named<TextBlock>(window, $"OffsetLabel{s}").Text);
        }
        Assert.Equal(presenter.PresetChoices, ((IEnumerable<string>)Named<ComboBox>(window, "PresetBox").ItemsSource!).ToList());
        Assert.Equal(new[] { "Apply", "Cancel" }, Buttons(window).Where(b => b is "Apply" or "Cancel"));
        window.Close();
    }

    [AvaloniaFact]
    public async Task StringStepThenApplyReturnsTheNudgedTuning()
    {
        var owner = new Window();
        owner.Show();
        var window = new GlobalTuningWindow(GlobalTuningPresenter.StandardE, TabForge.Services.TuningPresets.SixStringPresets);
        var pending = window.ShowDialog<int[]?>(owner);

        Click(window, "StringUp0");
        var expected = new GlobalTuningPresenter(GlobalTuningPresenter.StandardE, TabForge.Services.TuningPresets.SixStringPresets);
        expected.NudgeString(0, 1);
        Assert.Equal(expected.NoteOf(0), Named<TextBlock>(window, "NoteLabel0").Text);
        Click(window, "ApplyButton");

        Assert.Equal(expected.Tuning, await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task EscCancelsTheTuningWindowWithNull()
    {
        var owner = new Window();
        owner.Show();
        var window = new GlobalTuningWindow(GlobalTuningPresenter.StandardE, TabForge.Services.TuningPresets.SixStringPresets);
        var pending = window.ShowDialog<int[]?>(owner);
        Press(window, Key.Escape);

        Assert.Null(await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public void EnterInTheShiftBoxAppliesTheSemitonesOnly()
    {
        var expected = new GlobalTuningPresenter(GlobalTuningPresenter.StandardE, TabForge.Services.TuningPresets.SixStringPresets);
        expected.TryShiftAll("+2");
        var window = new GlobalTuningWindow(GlobalTuningPresenter.StandardE, TabForge.Services.TuningPresets.SixStringPresets);
        window.Show();
        var shift = Named<TextBox>(window, "ShiftBox");
        shift.Text = "+2";
        shift.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = shift });

        Assert.Equal(expected.NoteOf(0), Named<TextBlock>(window, "NoteLabel0").Text);
        Assert.Equal("0", shift.Text);
        Assert.True(window.IsVisible);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalTuningRendersPngInEachTheme(bool light) =>
        RenderPng(new GlobalTuningWindow(GlobalTuningPresenter.StandardE, TabForge.Services.TuningPresets.SixStringPresets), "global-tuning", light);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOneOnThePanel(bool light)
    {
        var panel = AvaloniaPalette.Get("PanelBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), panel) >= 4.5, $"{text} on PanelBackground (light: {light})");
        Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get("TextStrong", light), AvaloniaPalette.Get("TuningPillBackground", light)) >= 4.5,
            $"TextStrong on TuningPillBackground (light: {light})");
    }

    private static void RenderPng(Window window, string name, bool light)
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.");
            var path = DialogTestKit.RenderPath(nameof(CommandPaletteSavePromptTuningTests), $"{name}-{(light ? "light" : "dark")}.png");
            frame.Save(path);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            window.Close();
            app.RequestedThemeVariant = null;
        }
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static IEnumerable<string> Buttons(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Where(b => b is not global::Avalonia.Controls.Primitives.ToggleButton).Select(b => (string)b.Content!).ToList();

    private static void Press(Window window, Key key) =>
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = window });

    private static void Click(Window window, string buttonName) =>
        Named<Button>(window, buttonName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
