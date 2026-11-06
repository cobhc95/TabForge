using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the headless checks of the track-properties string rows with a recording ringer: a click rings the sounding
//   pitch (tuning plus capo), the speaker toggle rings and silences, switching strings rings the new one, and
//   closing while looping silences exactly once, and only the repeating string's speaker is lit in the master accent.
// Does not own: the window, the rows or the sound (FakeRinger); the engine-backed adapter has its own tests.
// Tests: this class.
public class TrackPropertiesStringRingerTests
{
    private sealed class FakeRinger : IStringRinger
    {
        public List<int> Rings { get; } = new();
        public int Silences { get; private set; }
        public void Ring(int midi) => Rings.Add(midi);
        public void Silence() => Silences++;
    }

    private static TrackModel GuitarTrack() => new()
    {
        Name = "Strings", Kind = TrackKind.Guitar, InstrumentName = "Electric Guitar",
        MidiChannel = 0, MidiProgram = 29, MidiOutputDeviceId = -1, NumberOfFrets = 22,
        StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 },
    };

    private static (TrackPropertiesWindow Window, FakeRinger Ringer) Open()
    {
        var ringer = new FakeRinger();
        var window = new TrackPropertiesWindow(GuitarTrack(), ringer: ringer);
        window.Show();
        return (window, ringer);
    }

    // One click on the drawn string, as the row's pointer handler sees it (left button pressed).
    private static void PressString(TrackPropertiesWindow window, int index)
    {
        var hit = window.GetLogicalDescendants().OfType<Panel>().Single(c => c.Name == $"StringHit{index}");
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        hit.RaiseEvent(new PointerPressedEventArgs(hit, pointer, hit, new Point(2, 2), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None)
        { RoutedEvent = InputElement.PointerPressedEvent });
    }

    private static void FlipSpeaker(TrackPropertiesWindow window, int index, bool on) =>
        window.GetLogicalDescendants().OfType<ToggleButton>().Single(c => c.Name == $"SpeakerToggle{index}").IsChecked = on;

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Discard(TrackPropertiesWindow window)
    {
        DialogTestKit.Click(Named<Button>(window, "CancelButton"));
        Dispatcher.UIThread.RunJobs();
        if (window.IsVisible) DialogTestKit.Click(Named<Button>(Prompt(window), "YesButton"));
        Dispatcher.UIThread.RunJobs();
    }

    private static SavePromptWindow Prompt(TrackPropertiesWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        return window.OwnedWindows.OfType<SavePromptWindow>().Single();
    }

    [AvaloniaFact]
    public void AStringShowsAHandLightsUnderThePointerAndGlowsWhenStruck()
    {
        var (window, _) = Open();
        var hit = window.GetLogicalDescendants().OfType<Panel>().Single(c => c.Name == "StringHit1");
        var hover = window.GetLogicalDescendants().OfType<Border>().Single(c => c.Name == "StringHover1");
        var glow = window.GetLogicalDescendants().OfType<Border>().Single(c => c.Name == "StringGlow1");

        Assert.NotNull(hit.Cursor);   // the pointing hand: set on the hit area only, so the speaker and note box keep their own
        Assert.False(hover.IsVisible);
        Assert.Equal(0, glow.Opacity);

        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        hit.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, hit, pointer, hit, new Point(2, 2),
            0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None));
        Assert.True(hover.IsVisible);
        hit.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, hit, pointer, hit, new Point(2, 2),
            0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None));
        Assert.False(hover.IsVisible);

        PressString(window, 1);
        Assert.True(glow.Opacity > 0.5, "the string lights up the moment it is struck");
        window.Close();
        Assert.Equal(0, glow.Opacity);
    }

    [AvaloniaFact]
    public void ClickingAStringRingsItsSoundingPitch()
    {
        var (window, ringer) = Open();

        PressString(window, 0);
        PressString(window, 2);

        Assert.Equal(new[] { 64, 55 }, ringer.Rings);
        Assert.Equal(0, ringer.Silences);
        window.Close();
    }

    [AvaloniaFact]
    public void CapoUpRaisesTheNextRingPitch()
    {
        var (window, ringer) = Open();
        CapoPlacementTests.ClickFret(window, 1);
        Dispatcher.UIThread.RunJobs();

        PressString(window, 2);

        Assert.Equal(new[] { 56 }, ringer.Rings);
        Discard(window);
    }

    [AvaloniaFact]
    public void SpeakerToggleOnRingsAndOffSilencesOnce()
    {
        var (window, ringer) = Open();

        FlipSpeaker(window, 0, true);
        Assert.Equal(new[] { 64 }, ringer.Rings);

        FlipSpeaker(window, 0, false);
        Assert.Equal(new[] { 64 }, ringer.Rings);
        Assert.Equal(1, ringer.Silences);

        FlipSpeaker(window, 0, true);
        FlipSpeaker(window, 0, false);
        Assert.Equal(new[] { 64, 64 }, ringer.Rings);
        Assert.Equal(2, ringer.Silences);
        window.Close();
    }

    [AvaloniaFact]
    public void SwitchingTheLoopRingsTheNewStringAndShowsOnlyItLooping()
    {
        var (window, ringer) = Open();

        FlipSpeaker(window, 0, true);
        FlipSpeaker(window, 2, true);

        Assert.Equal(new[] { 64, 55 }, ringer.Rings);
        var toggles = window.GetLogicalDescendants().OfType<ToggleButton>()
            .Where(c => c.Name is not null && c.Name.StartsWith("SpeakerToggle"))
            .ToDictionary(c => c.Name!, c => c.IsChecked == true);
        Assert.Equal(new Dictionary<string, bool>
        {
            ["SpeakerToggle0"] = false, ["SpeakerToggle1"] = false, ["SpeakerToggle2"] = true,
            ["SpeakerToggle3"] = false, ["SpeakerToggle4"] = false, ["SpeakerToggle5"] = false,
        }, toggles);
        window.Close();
        Assert.Equal(1, ringer.Silences);   // closing while looping cuts the owned ring
    }

    // Only the speaker whose repeat is on shows the master accent (the AccentBrush resource); switching or switching off puts the others back.
    [AvaloniaFact]
    public void OnlyTheRepeatingSpeakerIsLitInTheAccent()
    {
        var (window, _) = Open();
        ToggleButton Speaker(int index) => window.GetLogicalDescendants().OfType<ToggleButton>().Single(c => c.Name == $"SpeakerToggle{index}");
        Assert.True(window.TryFindResource("AccentBrush", window.ActualThemeVariant, out var accent));
        bool Lit(int index) => ReferenceEquals(Speaker(index).Background, accent);
        Assert.False(Lit(0));

        FlipSpeaker(window, 0, true);
        Assert.True(Lit(0));
        FlipSpeaker(window, 0, false);
        Assert.False(Lit(0));
        FlipSpeaker(window, 0, true);
        FlipSpeaker(window, 2, true);
        Assert.Equal(new[] { false, false, true, false, false, false }, Enumerable.Range(0, 6).Select(Lit).ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public void CloseWhileLoopingSilencesExactlyOnce()
    {
        var (window, ringer) = Open();
        FlipSpeaker(window, 1, true);
        Assert.Equal(new[] { 59 }, ringer.Rings);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { 59 }, ringer.Rings);
        Assert.Equal(1, ringer.Silences);
    }

    [AvaloniaFact]
    public void SilentWithoutARinger()
    {
        var window = new TrackPropertiesWindow(GuitarTrack());
        window.Show();

        PressString(window, 0);
        FlipSpeaker(window, 0, true);
        FlipSpeaker(window, 0, false);
        window.Close();

        Assert.False(window.IsVisible);
    }
}
