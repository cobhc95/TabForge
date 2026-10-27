using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the track input choice: one audio input or None and one MIDI input (None, All, or a device), as the menu shows it, as the button words it,
/// what recording takes from each of the four combinations, and how older selections are read.
/// Does not own: the sub-row layout (ArmedRowTests), the MIDI clip building (MidiTakeCaptureTests).
/// Tests: this class.
/// </summary>
public class TrackInputPickerTests
{
    [AvaloniaFact]
    public void TheMenuHasANoneRowFirstInBothListsAndOnlyRadioItems()
    {
        var picker = new TrackInputPicker(() => AudioInputs.Input1, () => new[] { "Keys" }, _ => { }, _ => { },
            new AvaloniaPlaybackSessionTests.FakeUiThread(), () => Array.Empty<string>());
        var rows = Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(Assert.IsType<Flyout>(picker.Flyout).Content).Content).Children;

        Assert.Empty(rows.OfType<CheckBox>());
        var texts = rows.Select(r => r is TextBlock t ? "# " + t.Text : r is RadioButton b ? (b.IsChecked == true ? "(x) " : "( ) ") + b.Content : "?").ToList();
        var audio = texts.IndexOf("# Audio input");
        var midi = texts.IndexOf("# MIDI input");
        Assert.Equal(0, audio);
        Assert.Equal("( ) None", texts[audio + 1]);
        Assert.Equal("(x) " + AudioInputs.Label(AudioInputs.Input1), texts[audio + 2]);
        Assert.Equal("( ) None", texts[midi + 1]);
        Assert.Equal("( ) All MIDI inputs", texts[midi + 2]);
        Assert.Equal("(x) Keys" + AudioInputs.NotAvailable, texts[midi + 3]);   // chosen, and not plugged in
    }

    [Theory]
    [InlineData("", "", "No input")]
    [InlineData("Input 1", "", "Input 1")]
    [InlineData("Input 1", "SZ-CONTROLKEY49S", "Input 1 + SZ-CONTROLKEY49S")]
    [InlineData("", "SZ-CONTROLKEY49S", "MIDI: SZ-CONTROLKEY49S")]
    [InlineData("", AudioInputs.AllMidi, "MIDI: All")]
    public void TheButtonNamesTheChoice(string audio, string midi, string expected)
    {
        var (a, m) = TrackInputPicker.Normalise(audio, midi.Length == 0 ? Array.Empty<string>() : new[] { midi });
        Assert.Equal(expected, TrackInputPicker.Summary(a, m).Replace(AudioInputs.Label("Input 1"), "Input 1"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EachCombinationTakesTheRightKinds(bool audio, bool midi)
    {
        var track = new TrackModel { Name = "T", RecordArm = true, AudioInput = audio ? AudioInputs.Input1 : "", MidiInputs = midi ? new List<string> { "Keys" } : new List<string>() };
        Assert.Equal(audio, AudioInputs.HasAudio(track.AudioInput));
        Assert.Equal(midi, AudioInputs.TakesMidi(track));
        Assert.Equal(audio || midi, AudioInputs.HasInput(track));

        // The MIDI side: a take is opened only for a track with a MIDI input, and it hears just that device.
        var set = new FakeSet();
        using var capture = new MidiTakeCapture(new MidiInputHub(set).CreateClient());
        Assert.Equal(midi ? 2 : 0, capture.Start(new[] { track }, 0, Stopwatch.GetTimestamp(), 1, 0));
        set.Play("Keys", 60);
        set.Play("Other", 62);
        var taken = capture.Stop(Stopwatch.GetTimestamp());
        if (!midi) Assert.Empty(taken);
        else Assert.Equal(new[] { 60 }, taken.Single().Clips.SelectMany(c => c.Notes ?? new()).Select(n => (int)n.Pitch));
    }

    [Fact]
    public void AllMidiInputsHearsEveryDevice()
    {
        var track = new TrackModel { RecordArm = true, AudioInput = "", MidiInputs = new List<string> { AudioInputs.AllMidi } };
        var set = new FakeSet();
        using var capture = new MidiTakeCapture(new MidiInputHub(set).CreateClient());
        capture.Start(new[] { track }, 0, Stopwatch.GetTimestamp(), 1, 0);
        set.Play("Keys", 60);
        set.Play("Other", 62);
        var pitches = capture.Stop(Stopwatch.GetTimestamp()).Single().Clips.SelectMany(c => c.Notes ?? new()).Select(n => (int)n.Pitch).OrderBy(p => p);
        Assert.Equal(new[] { 60, 62 }, pitches);
    }

    [Fact]
    public void RecordingMakesAMidiTakeFromAMidiOnlyTrackAndRefusesNoInput()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var set = new FakeSet();
        using var recording = new TransportRecording(client, new MidiInputHub(set).CreateClient());
        var none = new TrackModel { RecordArm = true, AudioInput = "" };
        Assert.Equal(AudioInputs.NoInputText, recording.Toggle(new[] { none }, "unused"));
        Assert.False(recording.IsRecording);

        var midiOnly = new TrackModel { RecordArm = true, AudioInput = "", MidiInputs = new List<string> { "Keys" } };
        Assert.Equal(TransportRecording.RecordingText, recording.Toggle(new[] { midiOnly }, "unused"));   // no audio take: the engine is not needed
        set.Play("Keys", 64);
        recording.Toggle(new[] { midiOnly }, "unused");
        Assert.Single(recording.TakeFinishedMidi());

        var withAudio = new TrackModel { RecordArm = true, AudioInput = AudioInputs.Input1, MidiInputs = new List<string> { "Keys" } };
        Assert.Equal(TransportRecording.CouldNotStartText, recording.Toggle(new[] { withAudio }, "unused"));   // an audio take is wanted: the engine is down here
    }

    [Fact]
    public void ArmingATrackWithNoInputTakesNoStepAndSaysSo()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        song.Project.Tracks[0].AudioInput = "";
        song.TrackIndex = 0;
        string? said = null;

        TabForge.Avalonia.MainWindowTrackArmKeys.ToggleArm(song, refused: text => said = text);

        Assert.False(song.Project.Tracks[0].RecordArm);
        Assert.Equal(AudioInputs.NoInputText, said);
    }

    [Theory]
    [InlineData("Input 2", new string[0], "Input 2", new string[0])]
    [InlineData("Input 2", new[] { "Keys" }, "Input 2", new[] { "Keys" })]
    [InlineData("Input 2", new[] { "Keys", "Pad" }, "Input 2", new[] { AudioInputs.AllMidi })]
    [InlineData(AudioInputs.Midi, new string[0], "", new[] { AudioInputs.AllMidi })]
    [InlineData(AudioInputs.Midi, new[] { "Keys" }, "", new[] { "Keys" })]
    [InlineData(AudioInputs.Midi, new[] { "Keys", "Pad" }, "", new[] { AudioInputs.AllMidi })]
    [InlineData("", new[] { AudioInputs.AllMidi }, "", new[] { AudioInputs.AllMidi })]
    public void OlderSelectionsMigrate(string audio, string[] midi, string expectedAudio, string[] expectedMidi)
    {
        var track = new TrackModel { AudioInput = audio, MidiInputs = midi.ToList() };

        AudioInputs.MigrateMidi(track);
        AudioInputs.MigrateMidi(track);   // safe to repeat

        Assert.Equal(expectedAudio, track.AudioInput);
        Assert.Equal(expectedMidi, track.MidiInputs);
    }

    private sealed class FakeSet : IMidiDeviceSet
    {
        public event Action<int, int, int, long>? Message { add { } remove { } }
        public event Action<string, int, int, int, long>? DeviceMessage;
        public IReadOnlyList<string> OpenNames => IsOpen ? new[] { "Keys", "Other" } : Array.Empty<string>();
        public bool IsOpen { get; private set; }
        public IReadOnlyList<string> ListNames() => new[] { "Keys", "Other" };
        public bool Rescan() => false;
        public int Open() { IsOpen = true; return 2; }
        public void Close() => IsOpen = false;
        public void Dispose() => Close();
        public void Play(string device, int pitch)
        {
            var stamp = Stopwatch.GetTimestamp();
            DeviceMessage?.Invoke(device, 0x90, pitch, 100, stamp);
            DeviceMessage?.Invoke(device, 0x80, pitch, 0, stamp);
        }
    }
}
