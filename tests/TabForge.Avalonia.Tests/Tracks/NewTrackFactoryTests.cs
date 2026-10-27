using TabForge.Models;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the check that the Add track strip's Audio answer names a new audio track after the existing ones.
// Does not own: the prompt, the Track properties window or the document edit (ArrangementView and DocumentEdits).
// Tests: this file.
public class NewTrackFactoryTests
{
    [Fact]
    public void AudioTrackIsNumberedAfterExistingAudioTracks()
    {
        var project = new SongProject();
        project.Tracks.Add(NewTrackFactory.CreateTrack(project, TrackKind.Audio));
        var second = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        Assert.Equal("Audio 2", second.Name);
        Assert.True(second.IsAudio);
    }

    [Theory]
    [InlineData(TrackKind.Drums, "Blue")]
    [InlineData(TrackKind.Bass, "Yellow")]
    [InlineData(TrackKind.Guitar, "Red")]
    [InlineData(TrackKind.Keys, "Green")]
    public void NewTrackTakesItsInstrumentGroupColour(TrackKind kind, string swatch)
    {
        var track = NewTrackFactory.CreateTrack(new SongProject(), kind);
        Assert.Equal(TabForge.Platform.Theme.NamedColourSwatches.HexOf(swatch), track.ColorHex);
        Assert.True(track.ColorFollowsInstrument);
    }

    [Theory]
    [InlineData(TrackKind.Guitar, true)]
    [InlineData(TrackKind.Keys, true)]
    [InlineData(TrackKind.Drums, true)]
    [InlineData(TrackKind.Audio, false)]
    public void NewTrackHearsAllMidiInputsUnlessItIsAudio(TrackKind kind, bool hearsAll)
    {
        var track = NewTrackFactory.CreateTrack(new SongProject(), kind);
        Assert.Equal(hearsAll, track.MidiInputs.Contains(AudioInputs.AllMidi));
        Assert.Equal(hearsAll, AudioInputs.TakesMidi(track));
        Assert.Empty(AudioInputs.MidiDevicesOf(track));
    }

    [Fact]
    public void FreshDefaultTrackOpensAFakeDeviceThroughTheHub()
    {
        var track = NewTrackFactory.CreateTrack(new SongProject(), TrackKind.Keys);
        track.RecordArm = true;
        var device = new TabForge.Audio.MidiInputHub(new OneDevice());
        using var capture = new TabForge.Features.Recording.Rules.MidiTakeCapture(device.CreateClient());
        Assert.Equal(1, capture.Start(new[] { track }, 0, System.Diagnostics.Stopwatch.GetTimestamp(), 1, 0));
        Assert.Equal(new[] { "Fake keys" }, device.OpenDeviceNames);
        capture.Stop(System.Diagnostics.Stopwatch.GetTimestamp());
    }

    private sealed class OneDevice : TabForge.Audio.IMidiDeviceSet
    {
        public event System.Action<int, int, int, long>? Message { add { } remove { } }
        public event System.Action<string, int, int, int, long>? DeviceMessage { add { } remove { } }
        public System.Collections.Generic.IReadOnlyList<string> OpenNames => IsOpen ? new[] { "Fake keys" } : System.Array.Empty<string>();
        public bool IsOpen { get; private set; }
        public System.Collections.Generic.IReadOnlyList<string> ListNames() => new[] { "Fake keys" };
        public bool Rescan() => false;
        public int Open() { IsOpen = true; return 1; }
        public void Close() => IsOpen = false;
        public void Dispose() => Close();
    }

    [Fact]
    public void ColourFollowsInstrumentChangeUntilPickedByHand()
    {
        var project = new SongProject();
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Guitar);
        project.Tracks.Add(track);
        Assert.True(AudioToInstrumentConversion.ApplyInstrument(project, track, "Drum Kit (Standard)"));
        Assert.Equal(TabForge.Platform.Theme.NamedColourSwatches.HexOf("Blue"), track.ColorHex);
        TrackColouring.SetColour(track, "#123456");
        Assert.False(track.ColorFollowsInstrument);
        AudioToInstrumentConversion.ApplyInstrument(project, track, "Electric Guitar");
        Assert.Equal("#123456", track.ColorHex);
    }

    [Fact]
    public void LoadedTrackKeepsItsColourOnInstrumentChange()
    {
        var project = new SongProject();
        var track = new TrackModel { ColorHex = "#F61A16", Kind = TrackKind.Guitar };
        project.Tracks.Add(track);
        AudioToInstrumentConversion.ApplyInstrument(project, track, "Drum Kit (Standard)");
        Assert.Equal("#F61A16", track.ColorHex);
    }
}
