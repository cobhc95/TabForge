using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the headless checks of the Mix Table, colour chooser and value-based effect windows (structure, texts, clamping, renders, contrast).
/// Does not own: the windows or the presenters; expected values come from MixTablePresenter, ColourChooserPresenter and EffectValuesPresenter.
/// Tests: this class.
/// </summary>
public class MixTableColourChooserEffectValuesTests
{
    private static readonly EffectValuesPreset[] Presets =
        { new("Default", true, new Dictionary<string, double>()), new("Wide", false, new Dictionary<string, double> { ["Step"] = 3 }) };

    [AvaloniaFact]
    public void MixTableShowsPresenterTextsInWpfOrder()
    {
        var window = new MixTableWindow(MixTableWindow.SamplePresenter());
        window.Show();
        Assert.Equal(MixTablePresenter.Title, window.Title);
        var checks = window.GetLogicalDescendants().OfType<CheckBox>().Select(c => (string)c.Content!).ToList();
        Assert.Equal(new[] { MixTablePresenter.InstrumentLabel, MixTablePresenter.VolumeLabel, MixTablePresenter.PanLabel, MixTablePresenter.ChorusLabel,
            MixTablePresenter.ReverbLabel, MixTablePresenter.PhaserLabel, MixTablePresenter.TremoloLabel, MixTablePresenter.TempoLabel,
            MixTablePresenter.AllTracksLabel }, checks);
        Assert.Equal(new[] { MixTablePresenter.ClearButton, MixTablePresenter.OkButton, MixTablePresenter.CancelButton }, ButtonTexts(window));
        window.Close();
    }

    [AvaloniaFact]
    public async Task MixTableClampsTheTypedTempo()
    {
        var owner = new Window();
        owner.Show();
        var window = new MixTableWindow(MixTableWindow.SamplePresenter());
        var pending = window.ShowDialog<MixTablePresenter.Result?>(owner);
        window.GetLogicalDescendants().OfType<CheckBox>().Single(c => (string)c.Content! == MixTablePresenter.TempoLabel).IsChecked = true;
        window.GetLogicalDescendants().OfType<TextBox>().Single().Text = "999";
        Click(ButtonNamed(window, MixTablePresenter.OkButton));

        Assert.Equal(MixTablePresenter.TempoMax, (await pending)!.Tempo);
        owner.Close();
    }

    [AvaloniaFact]
    public void MixTableRowTogglesEnableTheirControls()
    {
        var window = new MixTableWindow(MixTableWindow.SamplePresenter());
        window.Show();
        var pairs = window.GetLogicalDescendants().OfType<Grid>().Where(g => g.Children.OfType<CheckBox>().Any() && g.Children.Count == 5)
            .Select(g => (Check: g.Children.OfType<CheckBox>().Single(), Editor: (Control)g.Children[3], Readout: (TextBlock)g.Children[4])).ToList();
        Assert.Equal(8, pairs.Count);
        foreach (var (check, editor, readout) in pairs)
        {
            check.IsChecked = false;
            Assert.False(editor.IsEnabled, (string)check.Content!);
            check.IsChecked = true;
            Assert.True(editor.IsEnabled, (string)check.Content!);
            Assert.False(string.IsNullOrEmpty(readout.Text) && editor is Slider);
        }
        window.Close();
    }

    [AvaloniaFact]
    public void MixTableReadoutFollowsTheSliderAndPanReadsLeftCentreRight()
    {
        var window = new MixTableWindow(MixTableWindow.SamplePresenter());
        window.Show();
        var pan = window.GetLogicalDescendants().OfType<Grid>().Single(g => g.Children.OfType<CheckBox>().Any(c => (string?)c.Content == MixTablePresenter.PanLabel) && g.Children.Count == 5);
        var slider = (Slider)pan.Children[3];
        var readout = (TextBlock)pan.Children[4];
        slider.Value = -3; Assert.Equal("L3", readout.Text);
        slider.Value = 0; Assert.Equal("C", readout.Text);
        slider.Value = 5; Assert.Equal("R5", readout.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task MixTableOkWritesWhatThePresenterBuildsFromTheSameEdits()
    {
        var presenter = MixTableWindow.SamplePresenter();
        var owner = new Window();
        owner.Show();
        var window = new MixTableWindow(presenter);
        var pending = window.ShowDialog<MixTablePresenter.Result?>(owner);
        foreach (var c in window.GetLogicalDescendants().OfType<CheckBox>()) c.IsChecked = true;
        var sliders = window.GetLogicalDescendants().OfType<Slider>().ToList();
        for (var i = 0; i < sliders.Count; i++) sliders[i].Value = i == 1 ? -2 : i + 3;
        window.GetLogicalDescendants().OfType<TextBox>().Single().Text = "133";
        Click(ButtonNamed(window, MixTablePresenter.OkButton));

        MixFieldValue F(int i) => new(true, (int)sliders[i].Value);
        var expected = presenter.Build(new MixTableEdits(true, presenter.InstrumentStartIndex, F(0), F(1), F(2), F(3), F(4), F(5), true, "133", presenter.TransitionStartIndex, true));
        var actual = (await pending)!;
        Assert.Equal(expected.Tempo, actual.Tempo);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected.Mix), System.Text.Json.JsonSerializer.Serialize(actual.Mix));
        owner.Close();
    }

    [AvaloniaFact]
    public void GraceEditorHasWpfFieldsPresetsAndButtons()
    {
        var window = OrnamentEditorWindows.Grace(5, -2, false, true, 32, 4, 0, Presets, false, _ => null);
        window.Show();
        Assert.Equal("Grace note editor", window.Title);
        var labels = window.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Equal(new[] { "Fret", "Position", "Duration", "Dynamic", "Transition", "Presets" },
            labels.Where(t => t is "Fret" or "Position" or "Duration" or "Dynamic" or "Transition" or "Presets"));
        Assert.Equal(new[] { "Save as preset…", "Delete preset", "OK", "Clean", "Cancel" }, ButtonTexts(window).Where(t => t is not ("-" or "+")));
        Assert.Equal(new[] { "Default", EffectValuesPresenter.PresetLabel("Wide", false) }, window.PresetRows);
        Assert.False(ButtonNamed(window, "Clean").IsEnabled);
        Assert.False(ButtonNamed(window, "Delete preset").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void TrillFretClampsAndPickingAPresetFillsIt()
    {
        var window = OrnamentEditorWindows.Trill(5, 2, 16, Presets, true, _ => null);
        window.Show();
        var box = window.GetLogicalDescendants().OfType<TextBox>().Single();
        box.Text = "99";
        Assert.Equal(36 - 5, window.Values["Step"]);
        box.Text = "-4";
        Assert.Equal(-5, window.Values["Step"]);
        window.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex = 1;
        Assert.Equal(3, window.Values["Step"]);
        Assert.True(ButtonNamed(window, "Delete preset").IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void FailedSaveShowsThePresenterMessage()
    {
        var window = OrnamentEditorWindows.Trill(5, 2, 16, Presets, true, _ => null);
        window.Show();
        Click(ButtonNamed(window, "Save as preset…"));
        Assert.Equal(EffectValuesPresenter.SaveFailedMessage, window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "StatusText").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void HarmonicFretIsOnlyForArtificialAndTapped()
    {
        var window = OrnamentEditorWindows.Harmonic(0, 12, Presets, false, _ => null);
        window.Show();
        var fret = window.GetLogicalDescendants().OfType<OrnamentEditorWindows.NumberField>().Single();
        Assert.False(fret.IsEnabled);
        window.GetLogicalDescendants().OfType<RadioButton>().Single(r => (string)r.Content! == "Tapped").IsChecked = true;
        Assert.True(fret.IsEnabled);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsRenderPngsInEachTheme(bool light)
    {
        var windows = new (Window Window, string Name)[]
        {
            (new MixTableWindow(MixTableWindow.SamplePresenter()), "mix-table"),
            (OrnamentEditorWindows.Grace(5, -2, false, true, 32, 4, 0, Presets, false, _ => null), "grace-editor"),
            (OrnamentEditorWindows.Harmonic(1, 12, Presets, true, _ => null), "harmonic-editor"),
        };
        using var theme = UseTheme(light);
        foreach (var (window, name) in windows)
        {
            var png = SavePng(window, nameof(MixTableColourChooserEffectValuesTests), $"{name}-{(light ? "light" : "dark")}.png");
            Assert.True(new FileInfo(png).Length > 0);
            if (name == "mix-table" && Environment.GetEnvironmentVariable("MT_SHOTS") is { Length: > 0 } shots)
            {
                Directory.CreateDirectory(shots);
                File.Copy(png, Path.Combine(shots, $"mix-table-{(light ? "light" : "dark")}.png"), true);
            }
            window.Close();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SliderTrackAndThumbReadAtLeastThreeToOneOnEverySurface(bool light)
    {
        foreach (var surface in new[] { "PanelBackground", "WindowBackground", "CardBackground", "RaisedBackground" })
            foreach (var part in new[] { "SystemSliderTrack", "SystemSliderThumbBorder" })
                Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(part, light), AvaloniaPalette.Get(surface, light)) >= 3, $"{part} on {surface} (light: {light})");
    }

    [AvaloniaFact]
    public void DisabledSlidersComboBoxesAndTextBoxesAreDimmed()
    {
        var window = new MixTableWindow(MixTableWindow.SamplePresenter());
        window.Show();
        foreach (var control in new Control[] { window.GetLogicalDescendants().OfType<Slider>().First(), window.GetLogicalDescendants().OfType<ComboBox>().First(), window.GetLogicalDescendants().OfType<TextBox>().Single() })
        {
            control.IsEnabled = false;
            Assert.Equal(AvaloniaStateStyles.DisabledOpacity, control.Opacity);
        }
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixTableWithRowsTickedRendersPng(bool light)
    {
        var window = new MixTableWindow(MixTableWindow.SamplePresenter());
        window.Show();
        foreach (var label in new[] { MixTablePresenter.VolumeLabel, MixTablePresenter.ReverbLabel, MixTablePresenter.TempoLabel })
            window.GetLogicalDescendants().OfType<CheckBox>().Single(c => (string)c.Content! == label).IsChecked = true;
        using var theme = UseTheme(light);
        var png = SavePng(window, nameof(MixTableColourChooserEffectValuesTests), $"mix-table-ticked-{(light ? "light" : "dark")}.png");
        if (Environment.GetEnvironmentVariable("MT_SHOTS") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            File.Copy(png, Path.Combine(shots, $"mix-table-ticked-{(light ? "light" : "dark")}.png"), true);
        }
        window.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextReadsAtLeastFourAndAHalfToOne(bool light)
    {
        var panel = AvaloniaPalette.Get("PanelBackground", light);
        foreach (var text in new[] { "TextPrimary", "TextMuted" })
            Assert.True(ThemeColours.Contrast(AvaloniaPalette.Get(text, light), panel) >= 4.5, $"{text} on PanelBackground (light: {light})");
    }

    private static List<string> ButtonTexts(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Where(b => b is not global::Avalonia.Controls.Primitives.ToggleButton && b.Content is string).Select(b => (string)b.Content!).ToList();

    private static Button ButtonNamed(Window window, string text) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == text);

    private static void Leave(Control target) => target.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
}
