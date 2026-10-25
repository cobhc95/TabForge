using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Presets;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the mixer's edits as the song sees them: a fader gesture is one undo step and marks the song dirty (IN-11), and the Master slider drives the
//     process's master volume, which the transport knobs follow (IN-12).
// Does not own: the mixer's layout (MixerWindowTests) or the audio engine (AvaloniaPlaybackSessionTests).
public class MixerSongEditTests
{
    private const string MasterPanTip = "Master pan";
    private const string MasterVolumeTip = "Master volume";

    private static SongDocument CleanDocument()
    {
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        document.MarkClean();
        return document;
    }

    /// <summary>The master row's slider whose tip starts with <paramref name="tip"/> (the tip is the slider's automation name).</summary>
    private static Slider MasterSlider(MixerWindow window, string tip) =>
        window.GetVisualDescendants().OfType<Slider>()
            .First(slider => slider.Name == "Fader" && (AutomationProperties.GetName(slider) ?? "").StartsWith(tip, System.StringComparison.Ordinal));

    /// <summary>A key press on the slider: the gesture that opens an undo step, as the keyboard makes it.</summary>
    private static void PressKey(Slider slider, Key key) =>
        slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = slider });

    [AvaloniaFact]
    public void AMasterPanGestureIsOneUndoStepAndMarksTheSongDirty()
    {
        var document = CleanDocument();
        var before = document.Project.Mixer.MasterPan;
        var window = new MixerWindow(document);
        window.Show();
        var slider = MasterSlider(window, MasterPanTip);

        PressKey(slider, Key.Right);
        slider.Value = before + 3;

        Assert.True(document.HasUnsavedChanges);
        Assert.Equal(before + 3, document.Project.Mixer.MasterPan);
        Assert.NotNull(DocumentEdits.Undo(document));
        Assert.Equal(before, document.Project.Mixer.MasterPan);
    }

    [AvaloniaFact]
    public void TheMasterVolumeSliderSetsTheProcessVolumeAndLeavesTheSongClean()
    {
        var document = CleanDocument();
        var sets = new List<int>();
        var level = 100;
        var window = new MixerWindow(document, null, () => level, value => { level = value; sets.Add(value); });
        window.Show();
        Assert.False(document.HasUnsavedChanges, "opening the mixer does not change the song");
        var slider = MasterSlider(window, MasterVolumeTip);
        Assert.Equal(100, slider.Value);

        slider.Value = 42;

        Assert.Equal(new[] { 42 }, sets);
        Assert.Equal(42, level);
        Assert.False(document.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public void TheMasterVolumeSliderShowsTheProcessVolume()
    {
        var window = new MixerWindow(CleanDocument(), null, () => 70, _ => { });
        window.Show();

        Assert.Equal(70, MasterSlider(window, MasterVolumeTip).Value);
    }

    [AvaloniaFact]
    public void ASetMasterPercentRaisesTheChangeOnlyWhenItChanges()
    {
        var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(Dispatcher.UIThread), null);
        sessions.AttachSettings(new AudioSettings(), new PluginSettings());
        var raised = new List<int>();
        sessions.MasterPercentChanged += raised.Add;

        sessions.SetMasterPercent(55);
        sessions.SetMasterPercent(55);

        Assert.Equal(new[] { 55 }, raised);
    }
}
