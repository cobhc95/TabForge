using System;
using System.Diagnostics;
using System.Linq;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the MIDI takes of Transport Record, driven through a fake MIDI device behind the real MIDI input hub: the notes played while an armed
/// MIDI track records land on it as a MIDI clip, and recording again over the same bars opens a second lane that plays.
/// Does not own: the audio takes (TransportRecordingTests), the MIDI devices of the machine (none are opened).
/// Tests: this class.
/// </summary>
public class MidiTakeCaptureTests
{
    private const int NoteOn = 0x90, NoteOff = 0x80;

    [Fact]
    public void NotesPlayedWhileAnArmedMidiTrackRecordsBecomeAMidiClipOnIt()
    {
        var (song, track) = ArmedMidiSong();
        var device = new FakeMidiDevice();
        var recording = new TransportRecording(new AudioEngineClient(new TabForge.Plugins.PluginTrust()), new MidiInputHub(device).CreateClient());
        RecordOneNote(recording, song, device, pitch: 60);

        Assert.Single(track.AudioClips);
        var clip = track.AudioClips[0];
        Assert.True(clip.IsMidi);
        Assert.Equal(0, clip.Lane);
        Assert.Equal(60, Assert.Single(clip.Notes!).Pitch);
    }

    [Fact]
    public void RecordingAgainOverTheSameBarsOpensASecondLaneThatPlays()
    {
        var (song, track) = ArmedMidiSong();
        var device = new FakeMidiDevice();
        var recording = new TransportRecording(new AudioEngineClient(new TabForge.Plugins.PluginTrust()), new MidiInputHub(device).CreateClient());
        RecordOneNote(recording, song, device, pitch: 60);
        RecordOneNote(recording, song, device, pitch: 64);

        Assert.Equal(2, track.AudioClips.Count);
        Assert.Equal(64, Assert.Single(track.AudioClips.Single(c => c.Lane == 1).Notes!).Pitch);
        Assert.True(ClipLanes.Plays(track, 1));
        Assert.False(ClipLanes.Plays(track, 0));
    }

    [Fact]
    public void ClickingAnEarlierTakeMakesItsLaneThePlayingOne()
    {
        var (song, track) = ArmedMidiSong();
        var device = new FakeMidiDevice();
        var recording = new TransportRecording(new AudioEngineClient(new TabForge.Plugins.PluginTrust()), new MidiInputHub(device).CreateClient());
        RecordOneNote(recording, song, device, pitch: 60);
        RecordOneNote(recording, song, device, pitch: 64);

        Assert.True(ClipLanes.SelectTake(track, 0, midi: true, add: false));
        Assert.True(ClipLanes.Plays(track, 0));
        Assert.False(ClipLanes.Plays(track, 1));
    }

    // Starts Transport Record, plays one note on the device while it records, stops, and lands the take as the stop does in the window.
    private static void RecordOneNote(TransportRecording recording, SongDocument song, FakeMidiDevice device, int pitch)
    {
        recording.Toggle(song.Project.Tracks, "unused");
        device.Play(NoteOn, pitch, 100, Stopwatch.GetTimestamp());
        device.Play(NoteOff, pitch, 0, Stopwatch.GetTimestamp());
        recording.Toggle(song.Project.Tracks, "unused");
        MainWindowTransportRecord.LandMidiTakes(recording, song);
    }

    private static (SongDocument Song, TrackModel Track) ArmedMidiSong()
    {
        var song = new SongDocument(() => null);
        var track = new TrackModel { Name = "Keys", AudioInput = AudioInputs.Midi, RecordArm = true };
        song.Project.Tracks.Add(track);
        return (song, track);
    }

    /// <summary>A MIDI input device the test plays by hand: it opens when a client holds it and passes each message on as the driver would.</summary>
    private sealed class FakeMidiDevice : IMidiInputSource
    {
        private bool _open;

        public event Action<int, int, int, long>? Message;
        public bool IsOpen => _open;

        public int Open()
        {
            _open = true;
            return 1;
        }

        public void Close() => _open = false;

        public void Play(int status, int pitch, int velocity, long stamp) => Message?.Invoke(status, pitch, velocity, stamp);

        public void Dispose() => Close();
    }
}
