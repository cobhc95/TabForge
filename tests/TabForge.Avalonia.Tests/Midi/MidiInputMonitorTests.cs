using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TabForge.Audio;
using TabForge.Features.Recording.Rules;
using TabForge.LiveInput;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: hearing a MIDI keyboard through a track (MidiInputMonitor): an armed track plays note on and off on its slot and channel, the selected unarmed track plays in the
/// default mode only, one place sounds a note (an armed track beats the selected one and live input's stand-in), Off is silent and holds the MIDI input only for an armed track's meter, and recording is untouched.
/// Does not own: the engine (slots and the note ring), the window hooks (MainWindowMidiMonitor).
/// Tests: this class.
/// </summary>
public class MidiInputMonitorTests
{
    private static TrackModel Armed(string name, int channel, int slot, string? device = null) => new()
    {
        Name = name, MidiChannel = channel, MidiProgram = 40 + channel, RecordArm = true, AudioInput = "",
        MidiInputs = new List<string> { device ?? AudioInputs.AllMidi }
    };

    private sealed class Host : IMidiMonitorHost
    {
        public List<TrackModel> Songs { get; } = new();
        public Dictionary<TrackModel, int> Slots { get; } = new();
        public TrackModel? Pick;
        public string ModeText = MidiMonitorModes.ArmedAndSelected;
        public bool StandIn;
        public readonly List<(int Slot, byte Status, byte D1, byte D2, bool Panic)> Sent = new();
        public IReadOnlyList<TrackModel> Tracks => Songs;
        public TrackModel? Selected => Pick;
        public string Mode => ModeText;
        public bool StandInListening => StandIn;
        public int SlotOf(TrackModel track) => Slots.TryGetValue(track, out var s) ? s : -1;
        public void Write(int slot, byte status, byte data1, byte data2, bool panic) { lock (Sent) Sent.Add((slot, status, data1, data2, panic)); }
        public List<(int Slot, byte Status, byte D1, byte D2, bool Panic)> Notes() { lock (Sent) return Sent.Where(m => !m.Panic && (m.Status & 0xF0) is 0x80 or 0x90).ToList(); }
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
        public void Raise(string device, int status, int data1, int data2) => DeviceMessage?.Invoke(device, status, data1, data2, Stopwatch.GetTimestamp());
        public void Press(string device, int pitch)
        {
            var stamp = Stopwatch.GetTimestamp();
            DeviceMessage?.Invoke(device, 0x90, pitch, 100, stamp);
            DeviceMessage?.Invoke(device, 0x80, pitch, 0, stamp);
        }
    }

    private static (MidiInputMonitor Monitor, Host Host, FakeSet Set, MidiInputHub Hub) Make()
    {
        var set = new FakeSet();
        var hub = new MidiInputHub(set);
        var host = new Host();
        return (new MidiInputMonitor(host, hub, new AvaloniaPlaybackSessionTests.FakeUiThread()), host, set, hub);
    }

    [Fact]
    public void AnArmedTrackHearsNoteOnAndOffOnItsSlotAndChannelAfterItsProgram()
    {
        var (monitor, host, set, hub) = Make();
        using var _ = monitor;
        var track = Armed("Keys", 2, 5);
        host.Songs.Add(track); host.Slots[track] = 5;
        monitor.Refresh();
        Assert.Contains((5, (byte)0xC2, (byte)42, (byte)0, false), host.Sent);   // the program first
        set.Press("Keys", 60);
        Assert.Equal(new[] { (5, (byte)0x92, (byte)60, (byte)100, false), (5, (byte)0x82, (byte)60, (byte)0, false) }, host.Notes());
        Assert.Equal(1, hub.HeldCount);
    }

    [Fact]
    public void AMutedMonitorIsSilentLetsGoOfHeldNotesAndHandsBackCleanly()
    {
        var (monitor, host, set, hub) = Make();
        using var _ = monitor;
        var track = Armed("Keys", 0, 1);
        host.Songs.Add(track); host.Slots[track] = 1;
        monitor.Refresh();
        Assert.True(monitor.Sounds(track));
        host.Sent.Clear();
        monitor.Muted = true;
        Assert.Contains(host.Sent, m => m.Panic);   // held notes let go
        Assert.False(monitor.Sounds(track));
        Assert.False(monitor.ArmedClaims("Keys"));
        Assert.False(monitor.IsOpen);
        host.Sent.Clear();
        set.Press("Keys", 60);
        Assert.Empty(host.Notes());
        monitor.Muted = false;
        set.Press("Keys", 62);
        Assert.Equal(2, host.Notes().Count);
    }

    [Fact]
    public void PedalBendAndModulationPassThrough()
    {
        var (monitor, host, set, _) = Make();
        using var _ = monitor;
        var track = Armed("Keys", 0, 1);
        host.Songs.Add(track); host.Slots[track] = 1;
        monitor.Refresh();
        host.Sent.Clear();
        set.Raise("Keys", 0xB0, 64, 127); set.Raise("Keys", 0xE0, 0, 100); set.Raise("Keys", 0xB0, 1, 50);
        Assert.Equal(3, host.Sent.Count);
        Assert.Equal(new[] { (byte)0xB0, (byte)0xE0, (byte)0xB0 }, host.Sent.Select(m => m.Status));
    }

    [Fact]
    public void TheSelectedUnarmedTrackHearsInTheDefaultModeButNotInArmedOnly()
    {
        var (monitor, host, set, hub) = Make();
        using var _ = monitor;
        var track = new TrackModel { Name = "Piano", MidiChannel = 1 };
        host.Songs.Add(track); host.Slots[track] = 3; host.Pick = track;
        monitor.Refresh();
        set.Press("Keys", 64);
        Assert.Equal(2, host.Notes().Count);
        Assert.All(host.Notes(), m => Assert.Equal(3, m.Slot));

        host.ModeText = MidiMonitorModes.ArmedOnly;
        monitor.Refresh();
        host.Sent.Clear();
        set.Press("Keys", 64);
        Assert.Empty(host.Notes());
        Assert.Equal(0, hub.HeldCount);   // nothing needs the inputs
    }

    [Fact]
    public void OnlyOnePlaceSoundsANote()
    {
        var (monitor, host, set, _) = Make();
        using var _ = monitor;
        var armed = Armed("Armed", 0, 1);
        var selected = new TrackModel { Name = "Selected", MidiChannel = 1 };
        host.Songs.Add(armed); host.Songs.Add(selected);
        host.Slots[armed] = 1; host.Slots[selected] = 2; host.Pick = selected;
        monitor.Refresh();
        Assert.True(monitor.ArmedClaims("Keys"));   // live input's stand-in yields to it
        set.Press("Keys", 60);
        Assert.All(host.Notes(), m => Assert.Equal(1, m.Slot));   // the armed track, not also the selected one
        Assert.Equal(2, host.Notes().Count);

        armed.RecordArm = false;
        host.StandIn = true;   // live input's stand-in listens: the selected track stays silent
        monitor.Refresh();
        Assert.False(monitor.ArmedClaims("Keys"));
        host.Sent.Clear();
        set.Press("Keys", 60);
        Assert.Empty(host.Notes());
    }

    [Fact]
    public void AnArmedTrackWithOneDeviceIgnoresTheOthers()
    {
        var (monitor, host, set, _) = Make();
        using var _ = monitor;
        var track = Armed("Pad", 0, 1, "Pad");
        host.Songs.Add(track); host.Slots[track] = 1;
        monitor.Refresh();
        set.Press("Keys", 60);
        Assert.Empty(host.Notes());
        Assert.False(monitor.ArmedClaims("Keys"));
        set.Press("Pad", 60);
        Assert.Equal(2, host.Notes().Count);
    }

    [Fact]
    public void OffIsSilentButAnArmedMidiTrackStillListensForItsMeterAndNothingArmedClosesTheInputs()
    {
        var (monitor, host, set, hub) = Make();
        using var _ = monitor;
        var track = Armed("Keys", 0, 1);
        host.Songs.Add(track); host.Slots[track] = 1;
        monitor.Refresh();
        Assert.Equal(1, hub.HeldCount);
        host.ModeText = MidiMonitorModes.Off;
        monitor.Refresh();
        Assert.Equal(1, hub.HeldCount);   // held for metering only
        Assert.Contains(host.Sent, m => m.Panic && m.Slot == 1);   // held notes are let go
        Assert.False(monitor.Sounds(track));
        Assert.False(monitor.ArmedClaims("Keys"));   // live input's stand-in is not held back by a silent listener
        host.Sent.Clear();
        set.Press("Keys", 60);
        Assert.Empty(host.Sent);   // no sound, no program
        Assert.Equal(100, MidiVelocityMeter.VelocityOf(monitor.LastNoteOf(track)));
        track.RecordArm = false;
        monitor.Refresh();
        Assert.Equal(0, hub.HeldCount);   // nothing armed and nothing monitors: no input open
    }

    [Fact]
    public void RecordingStillCapturesWhileMonitoring()
    {
        var (monitor, host, set, hub) = Make();
        using var _ = monitor;
        var track = Armed("Keys", 0, 1);
        host.Songs.Add(track); host.Slots[track] = 1;
        monitor.Refresh();
        using var capture = new MidiTakeCapture(hub.CreateClient());
        Assert.True(capture.Start(new[] { track }, 0, Stopwatch.GetTimestamp(), 1, 0) > 0);
        set.Press("Keys", 62);
        var taken = capture.Stop(Stopwatch.GetTimestamp());
        var pitches = taken.SelectMany(t => t.Clips).SelectMany(c => c.Notes ?? new()).Select(n => (int)n.Pitch).ToArray();
        Assert.Equal(new[] { 62 }, pitches);
        Assert.Equal(2, host.Notes().Count);   // and it was heard
        Assert.Equal(1, hub.HeldCount);        // the monitor's hold stays after the recording's goes
    }

    [Fact]
    public void ANoteTakesNoUiHop()
    {
        var set = new FakeSet();
        var host = new Host();
        var queue = new CountingUi();
        using var monitor = new MidiInputMonitor(host, new MidiInputHub(set), queue);
        var track = Armed("Keys", 0, 1);
        host.Songs.Add(track); host.Slots[track] = 1;
        monitor.Refresh();
        var posts = queue.Posts;
        set.Press("Keys", 60);
        Assert.Equal(2, host.Notes().Count);
        Assert.Equal(posts, queue.Posts);
    }

    private sealed class CountingUi : TabForge.Platform.IUiThread
    {
        private readonly AvaloniaPlaybackSessionTests.FakeUiThread _inner = new();
        public int Posts;
        public bool CheckAccess() => true;
        public bool IsShuttingDown => false;
        public void Post(Action work, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Normal) { Posts++; work(); }
        public System.Threading.Tasks.Task InvokeAsync(Action work, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Normal) => _inner.InvokeAsync(work, priority);
        public System.Threading.Tasks.Task<T> InvokeAsync<T>(Func<T> work, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Normal) => _inner.InvokeAsync(work, priority);
        public System.Threading.Tasks.Task YieldAsync(TabForge.Platform.UiPriority priority) => _inner.YieldAsync(priority);
        public void Invoke(Action work, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Send) => work();
        public T Invoke<T>(Func<T> work, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Send) => work();
        public TabForge.Platform.IUiTimer CreateTimer(TimeSpan interval, TabForge.Platform.UiPriority priority = TabForge.Platform.UiPriority.Background) => _inner.CreateTimer(interval, priority);
    }
}
