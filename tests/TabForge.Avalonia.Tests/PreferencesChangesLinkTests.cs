using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Settings footer's changes link: its count and text, its hover list, the undo arrow of one change (live, the count drops), and the jump
/// from a change's name to its setting.
/// Does not own: which settings count as changed (PreferencesPagePresenter tests) or the window's OK, Apply and Cancel (PreferencesWindowTests).
/// Tests: this class.
/// </summary>
public class PreferencesChangesLinkTests
{
    private const string DynamicsLine = "Score & Notation › Show dynamics: On → Off";
    private const string LimiterLine = "Audio & Plug-ins › Protect rendered files from distortion: On → Off";

    private static Button Link(PreferencesWindow window) => window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == PreferencesChangesLink.LinkName);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void TwoChangesShowTheCountAndListThenUndoingOneLeavesOne()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
        var link = Link(window);
        Assert.False(link.IsVisible);
        window.Write("score.dynamics", false);
        window.Write("vst.renderlimiter", false);
        Assert.True(link.IsVisible);
        Assert.Equal("2 changes · Review", link.Content);
        var lines = ((string)ToolTip.GetTip(link)!).Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains(DynamicsLine, lines);
        Assert.Contains(LimiterLine, lines);
        Click(link);
        var popup = window.GetLogicalDescendants().OfType<Popup>().Single();
        Assert.True(popup.IsOpen);
        Click(window.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Undo Show dynamics"));
        Assert.Equal("1 change · Review", link.Content);
        Assert.Equal(LimiterLine, (string)ToolTip.GetTip(link)!);
        Assert.True(window.Working.Appearance.ShowDynamics);
        Assert.False(window.Working.Render.SafetyLimiter);
        window.Close();
    }

    [AvaloniaFact]
    public void AnEditPutBackToTheSavedValueLeavesApplyDisabled()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
        var apply = window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, SettingsCatalog.Special(SettingsCatalog.ActionApply).Label));
        window.Write("score.dynamics", false);
        Assert.True(apply.IsEnabled);
        window.Write("score.dynamics", true);   // what a cancelled colour pick does: the preview is reverted
        Assert.False(apply.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingASettingNameOpensItsPageAndClosesTheList()
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
        window.Write("score.dynamics", false);
        window.Write("vst.renderlimiter", false);
        Click(Link(window));
        var popup = window.GetLogicalDescendants().OfType<Popup>().Single();
        Click(window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == "Protect rendered files from distortion"));
        Assert.False(popup.IsOpen);
        Assert.Equal(SettingsCatalog.AudioVst, window.Page);
        window.Close();
    }
}
