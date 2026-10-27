using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.LiveInput;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a MIDI-armed track's input meter: a note-on from the track's chosen device jumps the bar to velocity / 127 and then falls, a note from another device or
/// an unarmed track moves nothing, the peak-hold tick stays about a second and then falls, and an idle meter repaints nothing.
/// Does not own: the audio meter (engine peaks), the MIDI devices (MidiInputHub), the sub-row's drawing (ArmedRowPainter).
/// Tests: this class.
/// </summary>
public class ArmedRowMidiMeterTests
{
    private sealed class Host : IMidiMonitorHost
    {
        public List<TrackModel> Songs { get; } = new();
        public IReadOnlyList<TrackModel> Tracks => Songs;
        public TrackModel? Selected => null;
        public string ModeText = MidiMonitorModes.ArmedOnly;
        public string Mode => ModeText;
        public List<byte> Written { get; } = new();
        public bool StandInListening => false;
        public int SlotOf(TrackModel track) => 0;
        public void Write(int slot, byte status, byte data1, byte data2, bool panic) { lock (Written) Written.Add(status); }
    }

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
        public void NoteOn(string device, int velocity) => DeviceMessage?.Invoke(device, 0x90, 60, velocity, 0);
    }

    private static MidiInputMonitor Monitor(FakeSet set, Host host, params TrackModel[] tracks)
    {
        host.Songs.AddRange(tracks);
        var monitor = new MidiInputMonitor(host, new MidiInputHub(set), new AvaloniaPlaybackSessionTests.FakeUiThread());
        monitor.Refresh();
        return monitor;
    }

    private static TrackModel MidiTrack(string device) => new() { Name = "Keys", RecordArm = true, AudioInput = "", MidiInputs = new List<string> { device } };

    [Fact]
    public void ANoteOnJumpsTheBarToVelocityOver127ThenItFalls()
    {
        var slot = new MidiNoteSlot();
        var meter = new MidiVelocityMeter();
        meter.Update(0, 1000);
        Assert.True(meter.IsIdle);

        slot.Record(64, 1000);
        meter.Update(slot.Packed, 1000);
        Assert.Equal(64 / 127.0, meter.Level, 6);

        meter.Update(slot.Packed, 1100);
        Assert.Equal(64 / 127.0 - 0.1 * MidiVelocityMeter.FallPerSecond, meter.Level, 6);
        meter.Update(slot.Packed, 3000);
        Assert.Equal(0, meter.Level);
        Assert.Equal(0, meter.Hold);
        Assert.True(meter.IsIdle);
    }

    [Fact]
    public void ThePeakTickHoldsAboutASecondThenFalls()
    {
        var slot = new MidiNoteSlot();
        var meter = new MidiVelocityMeter();
        slot.Record(100, 0);
        meter.Update(slot.Packed, 0);
        var peak = 100 / 127.0;
        meter.Update(slot.Packed, 500);
        Assert.Equal(peak, meter.Hold, 6);   // held while the bar falls
        Assert.True(meter.Level < peak);
        meter.Update(slot.Packed, 1000);
        Assert.Equal(peak, meter.Hold, 6);
        meter.Update(slot.Packed, 1200);   // past the hold: the tick falls too
        Assert.True(meter.Hold < peak);
        Assert.True(meter.Hold >= meter.Level);
        meter.Update(slot.Packed, 4000);
        Assert.True(meter.IsIdle);
    }

    [Fact]
    public void ASofterNoteInsideTheHoldKeepsTheHigherTick()
    {
        var slot = new MidiNoteSlot();
        var meter = new MidiVelocityMeter();
        slot.Record(120, 0);
        meter.Update(slot.Packed, 0);
        slot.Record(40, 300);
        meter.Update(slot.Packed, 300);
        Assert.Equal(40 / 127.0, meter.Level, 6);   // the bar jumps to the new note
        Assert.Equal(120 / 127.0, meter.Hold, 6);   // the tick keeps the peak
    }

    [Fact]
    public void OnlyNotesFromTheTracksChosenDeviceAreHeard()
    {
        var set = new FakeSet();
        var keys = MidiTrack("Keys");
        var all = MidiTrack(AudioInputs.AllMidi);
        var none = new TrackModel { Name = "Mute", RecordArm = true, AudioInput = "", MidiInputs = new List<string>() };
        var unarmed = MidiTrack("Keys"); unarmed.RecordArm = false;
        using var monitor = Monitor(set, new Host(), keys, all, none, unarmed);

        set.NoteOn("Pad", 90);
        Assert.Equal(0, monitor.LastNoteOf(keys));   // another device: no movement
        Assert.Equal(90, MidiVelocityMeter.VelocityOf(monitor.LastNoteOf(all)));
        Assert.Equal(0, monitor.LastNoteOf(none));
        Assert.Equal(0, monitor.LastNoteOf(unarmed));

        set.NoteOn("Keys", 127);
        Assert.Equal(127, MidiVelocityMeter.VelocityOf(monitor.LastNoteOf(keys)));
    }

    [Fact]
    public void WithMonitoringOffAnArmedTrackStillMetersItsNotesButNothingSounds()
    {
        var set = new FakeSet();
        var host = new Host { ModeText = MidiMonitorModes.Off };
        var keys = MidiTrack("Keys");
        using var monitor = Monitor(set, host, keys);
        var heard = 0;
        monitor.NoteHeard += () => heard++;

        set.NoteOn("Keys", 80);
        set.NoteOn("Pad", 80);
        Assert.Equal(80, MidiVelocityMeter.VelocityOf(monitor.LastNoteOf(keys)));
        Assert.Equal(1, heard);   // only the chosen device's note
        Assert.Empty(host.Written);

        keys.RecordArm = false;
        monitor.Refresh();
        Assert.False(monitor.IsOpen);
    }

    [AvaloniaFact]
    public void TheArmedRowMeterRunsOnDisplayFramesOnlyWhileItFallsAndRepaintsNothingWhenIdle()
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        while (song.Project.Tracks.Count < 3) song.Project.Tracks.Add(NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio));
        var track = song.Project.Tracks[2];
        track.AudioInput = ""; track.MidiInputs = new List<string> { "Keys" };
        var view = new ArrangementView(song.Project, song);
        var window = new global::Avalonia.Controls.Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        DocumentEdits.Run(view.Document, project => { project.Tracks[2].RecordArm = true; return true; });
        window.UpdateLayout();
        var set = new FakeSet();
        var host = new Host { ModeText = MidiMonitorModes.Off };   // monitoring Off: the meter still works
        using var monitor = Monitor(set, host, view.Document.Project.Tracks[2]);
        long offset = 0;
        var pending = new List<Action<TimeSpan>>();
        var requested = 0;
        var layer = view.ArmedRows;
        layer.MidiNoteOf = monitor.LastNoteOf;
        layer.NowMs = () => Environment.TickCount64 + offset;
        layer.RequestFrame = callback => { requested++; pending.Add(callback); };
        layer.NoteSource = wake => { monitor.NoteHeard += wake; return () => monitor.NoteHeard -= wake; };
        var current = view.Document.Project.Tracks[2];
        void Frame()
        {
            var callbacks = pending.ToArray();
            pending.Clear();
            foreach (var callback in callbacks) callback(TimeSpan.Zero);
        }

        Assert.Equal(0, requested);   // idle: no frames
        set.NoteOn("Pad", 127);       // not this track's device
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, requested);
        Assert.Equal(0, view.Header.InputLevelOf(current));

        set.NoteOn("Keys", 127);
        set.NoteOn("Keys", 100);      // a second note while the wake is pending: still one request
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, requested);
        Frame();
        Assert.True(view.Header.InputLevelOf(current) > 0.7);
        Assert.True(view.Header.InputHoldOf(current) > 0.7);
        Assert.Equal(2, requested);   // still falling: the next frame is asked for

        offset = 400;
        Frame();
        Assert.InRange(view.Header.InputLevelOf(current), 0.1, 0.6);   // falling
        Assert.True(view.Header.InputHoldOf(current) > 0.7);           // tick still held

        offset = 5000;
        Frame();
        Assert.Equal(0, view.Header.InputLevelOf(current));
        Assert.Equal(0, view.Header.InputHoldOf(current));
        var after = requested;
        Assert.Empty(pending);   // at rest: no further frame is requested
        Frame();
        Assert.Equal(after, requested);
        window.Close();
    }
}
