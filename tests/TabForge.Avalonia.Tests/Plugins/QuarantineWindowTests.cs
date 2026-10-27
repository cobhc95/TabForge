using System;
using System.Collections.Generic;
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
/// The Avalonia "Plug-ins switched off after a crash" window on its sample paths: the caption, rows with file names and full-path tool
/// tips, the Allow again buttons (which need the full app and say so on the status line), the empty list, the dark and light renders and
/// their contrast. Tests: this file, plus the Tools menu item in PreviewToolsMenu.
/// </summary>
public sealed class QuarantineWindowTests
{
    private static QuarantineWindow Open(bool light, IReadOnlyList<string>? paths = null)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new QuarantineWindow(paths ?? QuarantineWindow.SamplePaths);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void ShowsTheCaptionAndOneRowPerPluginWithItsFileNameAndFullPath()
    {
        var window = Open(light: false);
        Assert.Equal("Plug-ins switched off after a crash", window.Title);
        var texts = window.GetLogicalDescendants().OfType<TextBlock>().ToList();
        Assert.Contains(texts, t => t.Text == "These plug-ins crashed and are not loaded. Allow again loads one on the next playback; trust and approval stay as they are.");
        foreach (var (name, path) in new[] { ("Sample Chorus.dll", @"C:\Plug-ins\Sample Chorus.dll"), ("Sample Delay.vst3", @"C:\Plug-ins\Sample Delay.vst3"), ("Sample Reverb", @"C:\Plug-ins\Sample Reverb\") })
            Assert.Contains(texts, t => t.Text == name && ToolTip.GetTip(t) as string == path);
        var allow = window.GetLogicalDescendants().OfType<Button>().Where(b => b.Name == "AllowAgainButton").ToList();
        Assert.Equal(3, allow.Count);
        Assert.All(allow, b => Assert.Equal("Allow again", b.Content));
        // The preview has no plug-in settings, so Allow again is shown disabled with the "Not available yet" tip (ToolWindowParts.NotAvailable).
        Assert.Equal(ToolWindowParts.NotAvailableTip, ToolTip.GetTip(allow[0]));
        var close = DialogTestKit.Named<Button>(window, "CloseButton");
        Assert.True(close.IsDefault && close.IsCancel && close.Content as string == "Close");
        window.Close();
    }

    [AvaloniaFact]
    public void AllowAgainNeedsTheFullAppAndSaysSoOnTheStatusLine()
    {
        var window = Open(light: false);
        Assert.Equal("", window.StatusText);
        var allow = window.GetLogicalDescendants().OfType<Button>().Where(b => b.Name == "AllowAgainButton").ToList();
        DialogTestKit.Click(allow[0]);
        // The preview has no plug-in settings to save, so the status line says the work is not available yet (ToolWindowParts.NeedsFullApp wording).
        Assert.Equal("Allowing a plug-in again isn't available yet.", window.StatusText);
        Assert.Equal(3, window.GetLogicalDescendants().OfType<Button>().Count(b => b.Name == "AllowAgainButton"));
        window.Close();
    }

    [AvaloniaFact]
    public void AnEmptyListSaysNoPluginIsSwitchedOff()
    {
        var window = Open(light: false, Array.Empty<string>());
        Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "No plug-ins are switched off.");
        Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), b => b.Name == "AllowAgainButton");
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "quarantine-window-dark.png")]
    [InlineData(true, "quarantine-window-light.png")]
    public void RendersPngAndKeepsTextContrastAtLeast45(bool light, string fileName)
    {
        var window = Open(light);
        var path = DialogTestKit.SavePng(window, nameof(QuarantineWindowTests), fileName);
        Assert.Equal(fileName, Path.GetFileName(path));
        Assert.True(new FileInfo(path).Length > 0);

        var background = AvaloniaPalette.Get("WindowBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), background) >= 4.5, $"{text} on WindowBackground (light: {light})");

        window.Close();
    }

    [AvaloniaFact]
    public void PreviewToolsMenuOpensTheQuarantineWindow()
    {
        var main = new MainWindow();
        main.Show();
        // The development preview menus are not in the release menu: the entry list opens the window.
        PreviewToolsMenu.Entries(main).Single(e => e.Header == "Plug-ins switched off…").Open();
        Dispatcher.UIThread.RunJobs();

        var opened = main.OwnedWindows.OfType<QuarantineWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }
}
