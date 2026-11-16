using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Features.LeftStrip;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Focus track settings window: its plain-language line, that Apply writes the boost and the group template, that a ready-made template fills the groups
//   and that the switch acts at once.
public sealed class FocusTrackSettingsWindowTests
{
    [Fact]
    public void TheLineSaysWhatTheSettingsDoToALevelOf100()
    {
        var spotlight = FocusTrackSettings.ForPreset(FocusTrackSettings.Spotlight);
        Assert.Equal("The selected track at level 100 plays at 120. Others: guitars -20%, basses -20%, drums -20%, other instruments -20%.",
            FocusTrackSettingsWindow.Describe(new FocusBoost(FocusBoostMode.Percent, 20), spotlight));
        var keep = FocusTrackSettings.ForPreset(FocusTrackSettings.KeepRhythm);
        Assert.Equal("The selected track at level 100 plays at 127, as loud as it can go. Others: guitars -6 dB, basses unchanged, drums unchanged, other instruments -6 dB.",
            FocusTrackSettingsWindow.Describe(new FocusBoost(FocusBoostMode.Decibels, 6), keep));
        Assert.Equal("+6 dB", FocusTrackSettingsWindow.Summary(new FocusBoost()));
        Assert.Equal("-30%", FocusTrackSettingsWindow.LowerSummary(new FocusBoost(FocusBoostMode.Percent, 30)));
    }

    [AvaloniaFact]
    public void ApplyWritesTheBoostTheTemplateNameAndEveryGroup()
    {
        var settings = new FocusTrackSettings { Boost = new FocusBoost(FocusBoostMode.Decibels, 4) };
        var applied = false;
        var window = new FocusTrackSettingsWindow(settings, false, _ => { }, () => applied = true);
        window.Show();
        window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ApplyButton").RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(applied);
        Assert.Equal(4, settings.Boost.Amount);
        Assert.Equal(FocusTrackSettings.Spotlight, settings.Preset);
        Assert.Equal(FocusTrackSettings.Groups, settings.Others.Keys);
        Assert.All(settings.Others.Values, o => Assert.Equal(FocusOtherMode.SameAsBoost, o.Mode));
    }

    [AvaloniaFact]
    public void ChoosingATemplateFillsTheGroups()
    {
        var settings = new FocusTrackSettings();
        var window = new FocusTrackSettingsWindow(settings, false, _ => { }, () => { });
        window.Show();
        window.GetVisualDescendants().OfType<ComboBox>().First(c => c.Name == "PresetBox").SelectedItem = FocusTrackSettings.KeepRhythm;
        window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ApplyButton").RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(FocusTrackSettings.KeepRhythm, settings.Preset);
        Assert.Equal(FocusOtherMode.LeaveAlone, settings.Others[FocusTrackSettings.Drums].Mode);
        Assert.Equal(FocusOtherMode.SameAsBoost, settings.Others[FocusTrackSettings.Guitars].Mode);
    }

    [AvaloniaFact]
    public void TheSwitchActsAtOnce()
    {
        bool? seen = null;
        var window = new FocusTrackSettingsWindow(new FocusTrackSettings(), false, on => seen = on, () => { });
        window.Show();
        window.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Name == "FocusSwitch").IsChecked = true;
        Assert.True(seen);
        window.Close();
    }

    [AvaloniaFact]
    public void SwitchMovesThumbAndSyncsFromOutside()
    {
        var calls = 0;
        var window = new FocusTrackSettingsWindow(new FocusTrackSettings(), true, _ => calls++, () => { });
        window.Show();
        window.UpdateLayout();
        var sw = window.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Name == "FocusSwitch");
        var thumb = sw.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Thumb");
        var onX = thumb.Bounds.X;

        window.SyncSwitch(false);
        window.UpdateLayout();
        var offX = thumb.Bounds.X;

        Assert.Equal(0, calls);
        Assert.True(offX < onX, $"Expected thumb to move left when turned off (onX={onX}, offX={offX}).");
        window.Close();
    }

    [AvaloniaFact]
    public void RightClickOnFocusTrackButtonRunsSettingsAndSuppressesContextMenu()
    {
        var window = new Window { Width = 400, Height = 600 };
        var commands = new MainWindowCommands(window, () => { });
        var toggled = 0;
        var settingsOpened = 0;
        commands.Add(MainWindowFocusTrack.CommandId, () => toggled++);
        commands.Add(MainWindowFocusTrack.SettingsId, () => settingsOpened++);
        var feature = new LeftStripFeature(commands, null);
        var menuRow = new Border { Child = new DockPanel() };
        var modeBar = new TabForge.Avalonia.Platform.Controls.CompactTrackStrip();
        feature.Attach(menuRow, modeBar);
        window.Content = new DockPanel { Children = { feature.Left, modeBar } };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var btn = feature.Left.GetVisualDescendants().OfType<ToggleButton>().First(b => Equals(b.Tag, MainWindowFocusTrack.CommandId));
        var center = btn.TranslatePoint(new Point(btn.Bounds.Width / 2, btn.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, global::Avalonia.Input.MouseButton.Right);
        window.MouseUp(center, global::Avalonia.Input.MouseButton.Right);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, settingsOpened);
        Assert.Equal(0, toggled);
        window.Close();
    }
}
