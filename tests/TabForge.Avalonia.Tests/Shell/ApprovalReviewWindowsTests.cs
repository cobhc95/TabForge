using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that the plug-in review ticks and approves only the chosen plug-ins, and that the linked audio review with nothing waiting
/// or allowed says so and disables Allow selected and Revoke selected.
/// Does not own: the trust and approval rules (PluginTrust, MediaAccess) or the notice that opens these windows.
/// Tests: this class.
/// </summary>
public sealed class ApprovalReviewWindowsTests
{
    [AvaloniaFact]
    public void PluginReviewApprovesOnlyTheTickedPluginsWhenAllowSelectedIsPressed()
    {
        using var theme = DialogTestKit.UseTheme(light: false);
        IReadOnlyList<string>? approved = null;
        var untrusted = new[] { (@"C:\Plug-ins\Sample Chorus.dll", "not from your scan or an approved location"), (@"C:\Plug-ins\Sample Delay.vst3", "network or removable location") };
        var window = new PluginReviewWindow(untrusted, paths => approved = paths);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var boxes = window.GetLogicalDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(2, boxes.Count);
        boxes[1].IsChecked = true;
        var allow = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AllowSelectedButton");
        allow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(new[] { @"C:\Plug-ins\Sample Delay.vst3" }, approved);
    }

    [AvaloniaFact]
    public void LinkedAudioReviewWithNothingWaitingOrAllowedDisablesItsActions()
    {
        using var theme = DialogTestKit.UseTheme(light: true);
        var window = new LinkedAudioReviewWindow([], [], [], [], _ => { }, _ => { });
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == LinkedAudioReviewWindow.NoneText);
        Assert.False(window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "AllowSelectedButton").IsEnabled);
        Assert.False(window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "RevokeSelectedButton").IsEnabled);
    }
}
