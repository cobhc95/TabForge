using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Avalonia "update available" window: its texts, buttons and defaults for a newer release and for the up-to-date case, the status
/// line of the download button, the dark and light renders and their contrast. Tests: this file, plus the Tools menu item in PreviewToolsMenu.
/// </summary>
public sealed class UpdateAvailableWindowTests
{
    private static UpdateAvailableWindow Open(bool light, UpdateAvailablePresenter? presenter = null)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new UpdateAvailableWindow(presenter ?? new UpdateAvailablePresenter(UpdateAvailableWindow.SampleRelease, "0.1.0-beta.1", checkAutomatically: true));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static string[] TextsOf(UpdateAvailableWindow window) =>
        window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();

    [AvaloniaFact]
    public void ShowsTheHeadingDetailAndOptOutOfAnAvailableUpdate()
    {
        var window = Open(light: false);
        Assert.Equal("Update available", window.Title);
        var texts = TextsOf(window);
        Assert.Contains("TabForge 0.2.0 is available", texts);
        Assert.Contains("You have 0.1.0-beta.1. The download page on GitHub has the installer, the portable zip and what changed.", texts);
        Assert.Contains("One anonymous request to GitHub; nothing is downloaded or installed by TabForge.", texts);
        Assert.Equal("Check for updates automatically (once a day)", DialogTestKit.Named<CheckBox>(window, "CheckAutomaticallyBox").Content);
        Assert.True(window.CheckAutomatically);
        var buttons = window.GetLogicalDescendants().OfType<Button>().Where(b => b is not CheckBox).ToList();
        Assert.Equal(new[] { "Open download page", "Later" }, buttons.Select(b => b.Content as string).ToArray());
        Assert.True(buttons[0].IsDefault);
        Assert.True(buttons[1].IsCancel && !buttons[1].IsDefault);
        window.Close();
    }

    [AvaloniaFact]
    public void UpToDateWindowHasOnlyOkAndTheSettingBox()
    {
        var window = Open(light: false, new UpdateAvailablePresenter(null, "0.1.0-beta.1", checkAutomatically: false));
        Assert.Equal("No update available", window.Title);
        var texts = TextsOf(window);
        Assert.Contains("TabForge is up to date", texts);
        Assert.Contains("You have the latest version (0.1.0-beta.1).", texts);
        var buttons = window.GetLogicalDescendants().OfType<Button>().Where(b => b is not CheckBox).ToList();
        Assert.Single(buttons);
        Assert.Equal("OK", buttons[0].Content);
        Assert.True(buttons[0].IsDefault && buttons[0].IsCancel);
        Assert.False(window.CheckAutomatically);
        window.Close();
    }

    [AvaloniaFact]
    public void TheOptOutBoxAnswerFollowsTheBox()
    {
        var window = Open(light: false);
        DialogTestKit.Named<CheckBox>(window, "CheckAutomaticallyBox").IsChecked = false;
        Assert.False(window.CheckAutomatically);
        window.Close();
    }

    [AvaloniaFact]
    public void TheDownloadButtonSaysItNeedsTheFullAppOnTheStatusLine()
    {
        var window = Open(light: false);
        Assert.Equal("", window.StatusText);
        DialogTestKit.Click(DialogTestKit.Named<Button>(window, "DownloadButton"));
        Assert.StartsWith("Opening the download page needs the full app", window.StatusText);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "update-available-dark.png")]
    [InlineData(true, "update-available-light.png")]
    public void RendersPngAndKeepsTextContrastAtLeast45(bool light, string fileName)
    {
        var window = Open(light);
        var path = DialogTestKit.SavePng(window, nameof(UpdateAvailableWindowTests), fileName);
        Assert.Equal(fileName, Path.GetFileName(path));
        Assert.True(new FileInfo(path).Length > 0);

        var background = AvaloniaPalette.Get("WindowBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), background) >= 4.5, $"{text} on WindowBackground (light: {light})");

        window.Close();
    }

    [AvaloniaFact]
    public void PreviewToolsMenuOpensTheUpdateWindow()
    {
        var main = new MainWindow();
        main.Show();
        // The development preview menus are not in the release menu: the entry list opens the window.
        PreviewToolsMenu.Entries(main).Single(e => e.Header == "Update available…").Open();
        Dispatcher.UIThread.RunJobs();

        var opened = main.OwnedWindows.OfType<UpdateAvailableWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }
}
