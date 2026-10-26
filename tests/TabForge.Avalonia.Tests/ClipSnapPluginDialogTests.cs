using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the audio clip properties, snap settings, plug-in save and plug-in preset windows and the track input picker (structure, texts, results, renders in each theme, contrast).
/// Does not own: the windows or the presenters; expected values come from ClipPropertiesPresenter SnapSettingsPresenter, PluginSaveChoicePresenter and PluginPresetNamePresenter.
/// Tests: this class.
/// </summary>
public class ClipSnapPluginDialogTests
{
    [AvaloniaFact]
    public void ClipPropertiesShowsTheRowsValuesAndButtons()
    {
        var clip = ClipPropertiesWindow.SampleClip();
        var window = new ClipPropertiesWindow(clip);
        window.Show();
        Assert.Equal(ClipPropertiesPresenter.Title, window.Title);
        var texts = Texts(window);
        Assert.All(new[] { "Name", "Volume", "Pitch", "Speed", "-3.0 dB", "+2 st", ClipPropertiesPresenter.Note(clip) }, t => Assert.Contains(t, texts));
        Assert.Equal(new[] { (-36.0, 12.0), (-12.0, 12.0), (0.5, 2.0) }, window.GetLogicalDescendants().OfType<Slider>().Select(s => (s.Minimum, s.Maximum)));
        Assert.Equal(new[] { "OK", "Cancel" }, ButtonTexts(window));
        window.Close();
    }

    [AvaloniaFact]
    public async Task ClipPropertiesOkWritesTheValuesAndCancelDoesNot()
    {
        var owner = new Window();
        owner.Show();
        var clip = ClipPropertiesWindow.SampleClip();
        var window = new ClipPropertiesWindow(clip);
        var pending = window.ShowDialog<bool>(owner);
        window.GetLogicalDescendants().OfType<TextBox>().Single().Text = "  Riff ";
        window.GetLogicalDescendants().OfType<Slider>().First().Value = 6;
        window.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
        Click(ButtonNamed(window, "OK"));
        Assert.True(await pending);
        Assert.Equal(("Riff", 6.0, true), (clip.Name, clip.GainDb, clip.Muted));

        var cancelled = new ClipPropertiesWindow(clip);
        var second = cancelled.ShowDialog<bool>(owner);
        cancelled.GetLogicalDescendants().OfType<TextBox>().Single().Text = "Other";
        Click(ButtonNamed(cancelled, "Cancel"));
        Assert.False(await second);
        Assert.Equal("Riff", clip.Name);
        owner.Close();
    }

    [AvaloniaFact]
    public void SnapSettingsShowsTheSwitchesAndAppliesAtOnce()
    {
        var snap = new SnapSettings { DistancePx = 8 };
        var changes = 0;
        var window = new SnapSettingsWindow(snap, () => changes++);
        window.Show();
        Assert.Equal(SnapSettingsPresenter.Title, window.Title);
        var boxes = window.GetLogicalDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(new[] { SnapSettingsPresenter.EnableLabel }.Concat(SnapSettingsPresenter.SnapTo.Select(s => s.Label)), boxes.Select(b => (string)b.Content!));
        Assert.All(new[] { "Grid size", "Snap to", "Snap distance", "pixels" }, t => Assert.Contains(t, Texts(window)));
        Assert.Equal(SnapSettings.Grids, window.GetLogicalDescendants().OfType<ComboBox>().Single().Items.Cast<string>());
        Assert.Equal(new[] { "Close" }, ButtonTexts(window));
        boxes[4].IsChecked = false;
        Assert.False(snap.ToPlayhead);
        Assert.Equal(1, changes);
        window.GetLogicalDescendants().OfType<TextBox>().Single().Text = "99";
        Click(ButtonNamed(window, "Close"));
        Assert.Equal((40, 2), (snap.DistancePx, changes));
    }

    [AvaloniaFact]
    public async Task PluginSaveShowsTheOptionsAndClosesWithTheChosenValue()
    {
        var owner = new Window();
        owner.Show();
        var window = new PluginSaveWindow("song.gp");
        var pending = window.ShowDialog<string?>(owner);
        Assert.Equal(PluginSaveChoicePresenter.Title, window.Title);
        var texts = Texts(window);
        Assert.Contains(PluginSaveChoicePresenter.Headline, texts);
        Assert.Contains(PluginSaveChoicePresenter.Detail("song.gp"), texts);
        Assert.All(PluginSaveChoicePresenter.Options, o => { Assert.Contains(o.Title, texts); Assert.Contains(o.Text, texts); });
        var buttons = window.GetLogicalDescendants().OfType<Button>().ToList();
        Assert.Equal(4, buttons.Count);
        Assert.Equal(new[] { true, false, false }, buttons.Take(3).Select(b => b.IsDefault));
        Assert.Equal("Cancel", buttons[3].Content);
        Click(buttons[1]);
        Assert.Equal(PluginSaveChoicePresenter.GpPlusDataFile, await pending);

        var cancelled = new PluginSaveWindow("song.gp");
        var second = cancelled.ShowDialog<string?>(owner);
        Click(ButtonNamed(cancelled, "Cancel"));
        Assert.Null(await second);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task PluginPresetShowsThePromptDeletesAndAsksBeforeReplacing()
    {
        var owner = new Window();
        owner.Show();
        var names = new List<string> { "Warm", "Bright" };
        var questions = new List<string>();
        var host = new AvaloniaDialogHost { Capture = dialog => { questions.Add(string.Join("\n", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text))); return MessageBoxAnswer.Yes; } };
        var window = new PluginPresetWindow(host, "Sample Reverb", () => names.ToList(), name => names.Remove(name));
        var pending = window.ShowDialog<string?>(owner);
        Assert.Equal(PluginPresetNamePresenter.Title, window.Title);
        Assert.Contains("Preset name for Sample Reverb:", Texts(window));
        Assert.Equal(new[] { "Delete", "OK", "Cancel" }, ButtonTexts(window));
        var box = window.GetLogicalDescendants().OfType<ComboBox>().Single();
        Assert.Equal("My preset", box.Text);
        Assert.Equal(new[] { "Warm", "Bright" }, box.Items.Cast<string>());
        var delete = ButtonNamed(window, "Delete");
        Assert.False(delete.IsEnabled);
        box.Text = " warm ";
        Assert.True(delete.IsEnabled);
        Click(delete);
        await Task.Yield();
        Assert.Equal(new[] { "Bright" }, names);
        Assert.Contains("Delete the preset \"Warm\"?", questions.Last());
        box.Text = "bright";
        Click(ButtonNamed(window, "OK"));
        Assert.Equal("bright", await pending);
        Assert.Contains(PluginPresetNamePresenter.ReplaceQuestion("bright"), questions.Last());
        owner.Close();
    }

    [AvaloniaFact]
    public void AudioInputPickerShowsTheSelectedInput()
    {
        var window = AudioInputPicker.SampleWindow(TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication(), () => System.Array.Empty<string>());
        window.Show();
        var picker = window.GetLogicalDescendants().OfType<TrackInputPicker>().Single();
        Assert.Contains(AudioInputs.Label(AudioInputs.Input1), Assert.IsType<TextBlock>(picker.Content).Text);

        window.Close();
    }

    public static IEnumerable<object[]> Windows() => new[] { new object[] { "clip-properties" }, new object[] { "snap-settings" }, new object[] { "plugin-save" }, new object[] { "plugin-preset" }, new object[] { "audio-input" } };

    private static Window Build(string name) => name switch
    {
        "clip-properties" => new ClipPropertiesWindow(ClipPropertiesWindow.SampleClip()),
        "snap-settings" => new SnapSettingsWindow(new SnapSettings(), () => { }),
        "plugin-save" => new PluginSaveWindow("song.gp"),
        "plugin-preset" => new PluginPresetWindow(new AvaloniaDialogHost(), "Sample Reverb", () => new[] { "Warm" }, _ => { }),
        "audio-input" => AudioInputPicker.SampleWindow(TabForge.Avalonia.Platform.AvaloniaUiThread.ForApplication(), () => System.Array.Empty<string>()),
        _ => throw new System.ArgumentException(name),
    };

    [AvaloniaTheory]
    [MemberData(nameof(Windows))]
    public void RendersPngsAndReadsAtLeastFourAndAHalfToOneInEachTheme(string name)
    {
        foreach (var light in new[] { false, true })
        {
            using var theme = UseTheme(light);
            var window = Build(name);
            Assert.True(new FileInfo(SavePng(window, nameof(ClipSnapPluginDialogTests), $"k2-{name}-{(light ? "light" : "dark")}.png")).Length > 0);
            Assert.True(Contrast(window.Foreground, window.Background, name) >= 4.5, $"{name} text (light: {light})");
            var page = SolidColour(window.Background, name);
            foreach (var text in window.GetLogicalDescendants().OfType<TextBlock>())
            {
                // A button or check box paints a see-through fill over the window, so the text sits on the blend of the two.
                var fill = text.GetLogicalAncestors().OfType<Button>().FirstOrDefault()?.Background;
                var surface = fill is null ? page : Over(SolidColour(fill, name), page);
                var ratio = ThemeColours.Contrast(SolidColour(text.Foreground, name), surface);
                Assert.True(ratio >= 4.5, $"{name} \"{text.Text}\" reads {ratio:0.00}:1 (light: {light})");
            }
            window.Close();
        }
    }

    private static UiColor Over(UiColor top, UiColor bottom)
    {
        byte Mix(byte t, byte b) => (byte)((t * top.A + b * (255 - top.A)) / 255);
        return new UiColor(255, Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static List<string?> Texts(Window window) => window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

    private static List<string> ButtonTexts(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Where(b => b is not global::Avalonia.Controls.Primitives.ToggleButton && b.Content is string).Select(b => (string)b.Content!).ToList();

    private static Button ButtonNamed(Window window, string text) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text);
}
