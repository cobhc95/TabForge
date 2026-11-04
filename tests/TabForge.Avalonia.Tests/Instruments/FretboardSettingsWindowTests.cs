using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Fretboard settings window checks: five sections in the sidebar, a pick writes its setting and saves once, a preset writes the whole
/// look, a slider moves the setting while it drags but saves only when it is let go, Reset section writes the defaults back, and the link to
/// Preferences calls its door.
/// Tests: FretboardSettingsWindow.
/// </summary>
public class FretboardSettingsWindowTests
{
    private sealed class Doors { public int Saves, Preferences; }

    private static (FretboardSettingsWindow Window, AppSettings Settings, Doors Doors) Make()
    {
        var settings = new AppSettings();
        var doors = new Doors();
        return (new FretboardSettingsWindow(settings, () => doors.Saves++, () => doors.Preferences++), settings, doors);
    }

    private static T Find<T>(FretboardSettingsWindow window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void TheSidebarListsTheFiveSectionsAndShowsEachOne()
    {
        var (window, _, _) = Make();
        Assert.Equal(new[] { "Look", "Notes and markers", "Layout and size", "Strings and capo", "Scale highlight" },
            Enumerable.Range(0, FretboardSettingsWindow.SectionCount).Select(FretboardSettingsWindow.SectionName).ToArray());
        for (var i = 0; i < FretboardSettingsWindow.SectionCount; i++)
        {
            window.Select(i);
            Assert.Equal(i, window.SelectedSection);
            Assert.NotNull(window.Page);
        }
    }

    [AvaloniaFact]
    public void APickWritesItsSettingOnceAndAPresetWritesTheWholeLook()
    {
        var (window, settings, doors) = Make();
        Click(Find<ToggleButton>(window, "FretboardWoodEbony"));
        Assert.Equal("Ebony", settings.Editing.FretboardWood);
        Assert.Equal(1, doors.Saves);
        Dispatcher.UIThread.RunJobs();
        Click(Find<ToggleButton>(window, "FretboardLookPearlfins"));
        Assert.Equal(FretboardStyles.OfPreset("Pearl fins"), FretboardStyles.LookOf(settings.Editing));
        Assert.Equal(2, doors.Saves);
    }

    [AvaloniaFact]
    public void NotesLayoutAndStringsPagesWriteTheirSettings()
    {
        var (window, settings, doors) = Make();
        window.Select(1);
        var previous = Find<CheckBox>(window, "FretboardPreviewPrevious");
        previous.IsChecked = true;
        Assert.True(settings.Editing.PreviewPreviousNotesEnabled);
        Click(Find<ToggleButton>(window, "FretboardDotSaysNotename"));
        Assert.True(settings.Editing.ShowNoteNames);
        Dispatcher.UIThread.RunJobs();
        window.Select(2);
        Click(Find<ToggleButton>(window, "FretboardDockBottom"));
        Assert.True(settings.Appearance.FretboardAtBottom);
        Click(Find<ToggleButton>(window, "FretboardPositionRight"));
        Assert.Equal("Right", settings.Appearance.FretboardPosition);
        Dispatcher.UIThread.RunJobs();
        window.Select(3);
        Click(Find<ToggleButton>(window, "FretboardVibrationOff"));
        Assert.Equal("Off", settings.Editing.FretboardVibration);
        Assert.True(doors.Saves >= 4);
    }

    [AvaloniaFact]
    public void ResetSectionWritesThatPagesDefaultsAndTheLinkCallsPreferences()
    {
        var (window, settings, doors) = Make();
        settings.Editing.FretboardWood = "Walnut";
        settings.Editing.FretboardVibration = "Strong";
        window.ResetSection(0);
        Assert.Equal(FretboardStyles.DefaultLook, FretboardStyles.LookOf(settings.Editing));
        Assert.Equal("Strong", settings.Editing.FretboardVibration);
        window.ResetSection(3);
        Assert.Equal("Mild", settings.Editing.FretboardVibration);
        Click(Find<Button>(window, "FretboardMoreSettings"));
        Assert.Equal(1, doors.Preferences);
    }

    [AvaloniaFact]
    public void TheSidebarListsFiveIconRowsAndMarksTheOpenOne()
    {
        var (window, _, _) = Make();
        var rows = window.GetLogicalDescendants().OfType<ListBoxItem>().Where(i => i.Tag is int).OrderBy(i => (int)(i.Tag ?? 0)).ToList();
        Assert.Equal(FretboardSettingsWindow.SectionCount, rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.Contains(AvaloniaFormControlStyles.PrefsNavRowClass, rows[i].Classes);
            var grid = Assert.IsType<Grid>(rows[i].Content);
            var glyph = Assert.IsType<SettingsNavigationIconView>(grid.Children[0]);
            Assert.Equal(FretboardSettingsWindow.SectionName(i), Assert.IsType<TextBlock>(grid.Children[1]).Text);
            Assert.Equal(i == 0, glyph.IsSelected);
        }
        window.Select(3);
        Assert.Equal(3, window.SelectedSection);
        Assert.True(GlyphSelected(rows[3]));
        Assert.False(GlyphSelected(rows[0]));
    }

    private static bool GlyphSelected(ListBoxItem row) =>
        row.Content is Grid grid && grid.Children[0] is SettingsNavigationIconView glyph && glyph.IsSelected;

    [AvaloniaFact]
    public void TheWindowSizesToItsContent()
    {
        var (window, _, _) = Make();
        Assert.Equal(SizeToContent.Height, window.SizeToContent);
    }

    [AvaloniaFact]
    public void TheFooterAndStepperMatchTheProgramStyle()
    {
        var (window, _, _) = Make();
        foreach (var name in new[] { "FretboardMoreSettings", "FretboardResetSection", "FretboardSettingsDone" })
            Assert.Contains("PrefsActionButton", Find<Button>(window, name).Classes);
        var done = Find<Button>(window, "FretboardSettingsDone");
        Assert.Contains(AvaloniaDefaultLooks.AccentLook, done.Classes);
        Assert.True(done.IsDefault);
        window.Select(1);
        Assert.Contains(AvaloniaFormControlStyles.PrefsFieldClass, Find<TextBox>(window, "FretboardHorizonBox").Classes);
        Assert.Contains(AvaloniaFormControlStyles.StepButtonClass, Find<Button>(window, "FretboardHorizonMinus").Classes);
        Assert.Contains(AvaloniaFormControlStyles.StepButtonClass, Find<Button>(window, "FretboardHorizonPlus").Classes);
    }

    [AvaloniaFact]
    public void TheStepperStepsAndSavesAndTypingAppliesOrReverts()
    {
        var (window, settings, doors) = Make();
        window.Select(1);
        var start = settings.Editing.PreviewHorizon;
        Click(Find<Button>(window, "FretboardHorizonPlus"));
        Assert.Equal(Math.Min(10, start + 1), settings.Editing.PreviewHorizon);
        Click(Find<Button>(window, "FretboardHorizonMinus"));
        Click(Find<Button>(window, "FretboardHorizonMinus"));
        Assert.Equal(Math.Max(1, start - 1), settings.Editing.PreviewHorizon);
        Assert.True(doors.Saves >= 3);
        var box = Find<TextBox>(window, "FretboardHorizonBox");
        box.Text = "8";
        box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
        Assert.Equal(8, settings.Editing.PreviewHorizon);
        box.Text = "99";
        box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
        Assert.Equal(10, settings.Editing.PreviewHorizon);
        box.Text = "abc";
        box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
        Assert.Equal(10, settings.Editing.PreviewHorizon);
        Assert.Equal("10", box.Text);
    }
}
