using System.Collections.Generic;
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

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The "Unsaved changes" question of the Preferences window: its wording and buttons, a theme change folded into one line, individual
/// changes kept beside it, colours as swatches and the eight-line cap.
/// Tests: this class.
/// </summary>
public class PreferencesDiscardWindowTests
{
    private static PreferencesWindow Edited(out List<PreferencesDiscardWindow> questions, params (string Key, object Value)[] edits)
    {
        var asked = new List<PreferencesDiscardWindow>();
        questions = asked;
        var host = new AvaloniaDialogHost { Capture = dialog => { asked.Add((PreferencesDiscardWindow)dialog); return MessageBoxAnswer.No; } };
        var window = new PreferencesWindow(host, new TabForge.Services.AppSettings());
        foreach (var (key, value) in edits) window.Write(key, value);
        return window;
    }

    private static IReadOnlyList<Control> Lines(PreferencesDiscardWindow dialog) =>
        dialog.GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "ChangeList").Children
            .Where(c => c.Name != "MoreText").ToList();

    private static string Text(Control line) => string.Join(" ", line.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

    [AvaloniaFact]
    public async Task ThemeChangeWithOnlyPresetColoursIsOneLine()
    {
        var window = Edited(out var asked, ("appearance.thememode", "Light"));
        Assert.False(await window.ConfirmDiscardAsync());
        var lines = Lines(asked.Single());
        Assert.Equal("Theme: Dark → Light", Text(Assert.Single(lines)));
    }

    [AvaloniaFact]
    public async Task KeepEditingLeavesTheChangeListWhole()
    {
        var window = Edited(out _, ("appearance.thememode", "Light"));
        var before = window.PendingChanges();
        Assert.False(await window.ConfirmDiscardAsync());
        Assert.NotEmpty(before);
        Assert.Equal(before, window.PendingChanges());
    }

    [AvaloniaFact]
    public async Task CancelAfterResetAllPutsTheBaselineLayoutBack()
    {
        var host = new AvaloniaDialogHost { Capture = _ => MessageBoxAnswer.Yes };
        var saved = new TabForge.Services.AppSettings();
        Assert.Null(saved.Workspace);
        var window = new PreferencesWindow(host, saved);
        window.Show();
        await window.ResetAllAsync();
        Assert.NotNull(saved.Workspace);   // the reset previews its layout
        window.Close();
        Assert.Null(saved.Workspace);      // Cancel puts the opening layout back
    }

    [AvaloniaFact]
    public async Task ClosingByTheOsAsksTheCancelQuestionAndKeepEditingStaysOpen()
    {
        var asked = 0;
        var answer = MessageBoxAnswer.No;
        var host = new AvaloniaDialogHost { Capture = _ => { asked++; return answer; } };
        var saved = new TabForge.Services.AppSettings();
        var window = new PreferencesWindow(host, saved);
        window.Show();
        window.Write("score.dynamics", false);
        await window.CloseAskingAsync();
        Assert.Equal(1, asked);
        Assert.True(window.IsVisible);
        Assert.False(saved.Appearance.ShowDynamics);
        answer = MessageBoxAnswer.Yes;
        await window.CloseAskingAsync();
        Assert.False(window.IsVisible);
        Assert.True(saved.Appearance.ShowDynamics);
    }

    [AvaloniaFact]
    public async Task ACustomColourBesideAThemeChangeKeepsItsOwnLine()
    {
        var window = Edited(out var asked, ("appearance.thememode", "Light"), ("appearance.accent", "#123456"));
        await window.ConfirmDiscardAsync();
        var lines = Lines(asked.Single());
        Assert.Equal(2, lines.Count);
        Assert.Equal("Theme: Dark → Light", Text(lines[0]));
        Assert.StartsWith("Appearance · Accent colour:", Text(lines[1]));
    }

    [AvaloniaFact]
    public async Task AColourLineShowsSwatchesNotHexCodes()
    {
        var window = Edited(out var asked, ("appearance.accent", "#123456"));
        await window.ConfirmDiscardAsync();
        var line = Assert.Single(Lines(asked.Single()));
        var swatches = line.GetLogicalDescendants().OfType<Border>().Select(b => b.Name).ToList();
        Assert.Equal(new[] { "OldSwatch", "NewSwatch" }, swatches);
        Assert.DoesNotContain("#", Text(line));
        Assert.Contains("#123456", ToolTip.GetTip(line)!.ToString());
    }

    [AvaloniaFact]
    public async Task TheQuestionUsesThePlainWordingAndKeepEditingIsTheSafeButton()
    {
        var window = Edited(out var asked, ("general.restorewindow", false));
        await window.ConfirmDiscardAsync();
        var dialog = asked.Single();
        var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToDictionary(b => b.Name!);
        Assert.Equal("Unsaved changes", dialog.Title);
        Assert.Contains("You changed some preferences but haven't applied them. Discard these changes?", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Equal("Discard changes", buttons["DiscardButton"].Content);
        Assert.Equal("Keep editing", buttons["KeepButton"].Content);
        Assert.True(buttons["KeepButton"].IsDefault);
        Assert.True(buttons["KeepButton"].IsCancel);
        Assert.False(buttons["DiscardButton"].IsDefault);
    }

    [AvaloniaFact]
    public async Task KeyboardFocusStartsOnKeepEditing()
    {
        var window = Edited(out var asked, ("general.restorewindow", false));
        await window.ConfirmDiscardAsync();
        var dialog = asked.Single();
        var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToDictionary(b => b.Name!);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(buttons["KeepButton"].IsFocused);
        Assert.True(buttons["KeepButton"].TabIndex < buttons["DiscardButton"].TabIndex);
        dialog.Close();
    }

    [Fact]
    public void MoreThanEightChangesShowEightLinesAndAnAndMoreLine()
    {
        var changes = Enumerable.Range(1, 11)
            .Select(i => new PreferencesPagePresenter.PreferenceChange($"k{i}", "General", $"Setting {i}", "On", "Off")).ToList();
        var summary = DiscardPreferencesPresenter.Summarize(changes, new List<IPreferenceRow>());
        Assert.Equal(8, summary.Lines.Count);
        Assert.Equal("…and 3 more changes", summary.MoreText);
    }
}
