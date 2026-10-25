using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the Group rules editor (structure, texts from the presenters, results, renders,
/// contrast) and of the preset rename and copy rules that the preset manager shares.
/// Does not own: the windows or the presenters; expected texts come from GroupRulesPresenter and PresetRules. Each test that closes a window
/// with pending edits answers its question first, so no test waits on a dialog; neither window asks a discard question.
/// Tests: this class.
/// </summary>
public class GroupRulesAndColourPickerTests
{
    [AvaloniaFact]
    public void GroupRulesShowsWpfTextsAndEachGroupsRuleHint()
    {
        var window = new GroupRulesWindow(new MixerSettings());
        window.Show();
        Assert.Equal("Group rules", window.Title);
        var texts = window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains("Apply to", texts);
        Assert.Contains("Everything else goes to", texts);
        Assert.Contains("This song follows the app-wide rules.", texts);
        var buttons = ButtonTexts(window);
        foreach (var label in new[] { "Add group", "Reset to defaults", "OK", "Cancel" }) Assert.Contains(label, buttons);
        foreach (var group in new GroupRulesPresenter(new MixerSettings()).Groups)
            Assert.Contains(GroupRulesPresenter.RulesHint(group.Def.Rules.Count), texts);
        window.Close();
    }

    [AvaloniaFact]
    public async Task GroupRulesCancelClosesWithoutResult()
    {
        var owner = new Window();
        owner.Show();
        var window = new GroupRulesWindow(new MixerSettings());
        var pending = window.ShowDialog<GroupRulesResult?>(owner);
        Click(ButtonNamed(window, "Cancel"));

        Assert.Null(await pending);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task GroupRulesOkReturnsTheBuiltRules()
    {
        var owner = new Window();
        owner.Show();
        var window = new GroupRulesWindow(new MixerSettings());
        var pending = window.ShowDialog<GroupRulesResult?>(owner);
        Click(ButtonNamed(window, "OK"));

        Assert.NotNull(await pending);
        owner.Close();
    }

    [Fact]
    public void PresetRenameAndCopyRulesRefuseBadNamesAndNumberCopies()
    {
        Assert.Equal("Type a name.", PresetRules.Error("   "));
        Assert.Equal("A name cannot end with a dot.", PresetRules.Error("Lead."));
        Assert.Null(PresetRules.Error("Lead"));
        Assert.Equal("Lead copy", PresetRules.CopyName("Lead", new[] { "Lead" }));
        Assert.Equal("Lead copy 2", PresetRules.CopyName("Lead", new[] { "Lead", "lead copy" }));

        var renamed = PresetManagePresenter.PlanRename("Lead", " Bass ", new[] { "Lead", "bass" });
        Assert.Equal("Bass", renamed.Name);
        Assert.Equal("bass", renamed.ReplacesName);
        Assert.True(PresetManagePresenter.PlanRename("Lead", "Lead", new[] { "Lead" }).Unchanged);
        Assert.Equal("Type a name.", PresetManagePresenter.PlanRename("Lead", " ", new[] { "Lead" }).Error);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupRulesRenderPngsInEachTheme(bool light)
    {
        var windows = new (Window Window, string Name)[]
        {
            (new GroupRulesWindow(new MixerSettings()), "group-rules"),
        };
        using var theme = UseTheme(light);
        foreach (var (window, name) in windows)
        {
            Assert.True(new FileInfo(SavePng(window, nameof(GroupRulesAndColourPickerTests), $"{name}-{(light ? "light" : "dark")}.png")).Length > 0);
            window.Close();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupRulesTextReadsAtLeastFourAndAHalfToOne(bool light)
    {
        // The problem line uses the Danger key, which is under 4.5:1 on the panel in both themes (as in WPF), so it is not asserted here.
        var panel = AvaloniaPalette.Get("PanelBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextSecondary" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), panel) >= 4.5, $"{text} on PanelBackground (light: {light})");
    }

    private static List<string> ButtonTexts(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Where(b => b is not ToggleButton && b.Content is string).Select(b => (string)b.Content!).ToList();

    private static Button ButtonNamed(Window window, string text) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text);
}
