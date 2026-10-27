using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TabForge.Audio;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: which MIDI devices an armed track records: a track with devices ticked hears only those, a track with none ticked hears every device.
/// Does not own: the picker (ArmedRowTests), the MIDI clip building (MidiTakeCaptureTests).
/// Tests: this class.
/// </summary>
public class MidiTrackDeviceFilterTests
{
    [Fact]
    public void ATrackRecordsOnlyItsTickedDevicesAndAnUntickedTrackHearsAll()
    {
        var set = new FakeSet();
        var ticked = new TrackModel { Name = "Ticked", AudioInput = AudioInputs.Midi, RecordArm = true, MidiInputs = new List<string> { "Pad" } };
        var all = new TrackModel { Name = "All", AudioInput = AudioInputs.Midi, RecordArm = true };
        using var capture = new MidiTakeCapture(new MidiInputHub(set).CreateClient());
        Assert.Equal(2, capture.Start(new[] { ticked, all }, 0, Stopwatch.GetTimestamp(), 1, 0));

        set.Play("Keys", 60);
        set.Play("Pad", 64);
        var taken = capture.Stop(Stopwatch.GetTimestamp());

        Assert.Equal(new[] { 64 }, Pitches(taken, ticked));
        Assert.Equal(new[] { 60, 64 }, Pitches(taken, all).OrderBy(p => p));
    }

    [Fact]
    public void AnAudioTrackWithAMidiDeviceTickedAlsoTakesMidi()
    {
        var track = new TrackModel { AudioInput = AudioInputs.Input1, MidiInputs = new List<string> { "Pad" } };
        Assert.True(AudioInputs.TakesMidi(track));
        Assert.False(AudioInputs.TakesMidi(new TrackModel { AudioInput = AudioInputs.Input1 }));
    }

    private static int[] Pitches(List<(TrackModel Track, List<AudioClip> Clips)> taken, TrackModel track) =>
        taken.Where(t => ReferenceEquals(t.Track, track)).SelectMany(t => t.Clips).SelectMany(c => c.Notes ?? new()).Select(n => (int)n.Pitch).ToArray();

    private sealed class FakeSet : IMidiDeviceSet
    {
        public event Action<int, int, int, long>? Message { add { } remove { } }
        public event Action<string, int, int, int, long>? DeviceMessage;
        public IReadOnlyList<string> OpenNames => IsOpen ? new[] { "Keys", "Pad" } : Array.Empty<string>();
        public bool IsOpen { get; private set; }
        public IReadOnlyList<string> ListNames() => new[] { "Keys", "Pad" };
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
