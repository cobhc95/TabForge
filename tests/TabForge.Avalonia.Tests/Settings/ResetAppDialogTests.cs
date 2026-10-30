using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;
using TabForge.Services.Reset;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Reset entire app dialog: all eight ticks on by default, the button count and disabled state at zero, the typed RESET
/// gate, Back, and the owner rule that the destructive row is searchable but only ever opens the Advanced page (search never runs the wipe).
/// Tests: this class.
/// </summary>
public sealed class ResetAppDialogTests : IDisposable
{
    private readonly TempFolders _folders = new();

    public void Dispose() => _folders.Dispose();

    [AvaloniaFact]
    public void AllTicksStartOnAndTheButtonDisablesAtZero()
    {
        var window = new ResetAppWindow(ResetAppPlan.Create(_folders));
        window.Show();
        var wipe = FindButton(window, "WipeButton");
        Assert.Equal("Wipe 8 items", wipe.Content);
        Assert.True(wipe.IsEnabled);

        Click(window, "ResetAppSelectNone");
        Assert.Equal("Wipe 0 items", wipe.Content);
        Assert.False(wipe.IsEnabled);

        Click(window, "ResetAppSelectAll");
        Assert.Equal("Wipe 8 items", wipe.Content);
        Assert.True(wipe.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void SecondStepNeedsTheExactWordAndBackReturns()
    {
        var window = new ResetAppWindow(ResetAppPlan.Create(_folders));
        window.Show();
        Click(window, "WipeButton");
        var confirm = FindButton(window, "ResetAppConfirm");
        var typed = window.GetLogicalDescendants().OfType<TextBox>().Single(box => box.Name == "ResetConfirmBox");
        Assert.False(confirm.IsEnabled);

        typed.Text = "reset";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(confirm.IsEnabled);
        typed.Text = "RESET";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(confirm.IsEnabled);

        Click(window, "ResetAppBack");
        var stepOne = window.GetLogicalDescendants().OfType<StackPanel>().Single(panel => panel.Name == "ResetAppStepOne");
        Assert.True(stepOne.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ResetEntireAppIsSearchableButOnlyOpensItsPage()
    {
        var entry = SettingsCatalog.Special(SettingsCatalog.ActionResetApp);
        Assert.True(SettingsCatalog.Matches(entry, "reset entire app"));
        Assert.True(SettingsCatalog.Matches(entry, "wipe"));
        Assert.DoesNotContain(SettingsCatalog.ActionResetApp, SettingsCatalog.FooterActions);

        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
        window.ShowPage(SettingsCatalog.Advanced);
        var button = window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == entry.Label);
        Assert.Contains(AvaloniaFormControlStyles.DangerButtonClass, button.Classes);

        window.Search(entry.Label);
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.IsVisible && b.Content as string == entry.Label)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(SettingsCatalog.Advanced, window.Page);
        window.Close();
    }

    private static Button FindButton(Window window, string name) =>
        window.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == name);

    private static void Click(Window window, string name) =>
        FindButton(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
