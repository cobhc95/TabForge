using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge.Avalonia;

// Owns: the handlers of the Sound menu rows. Test selected track output (Sound.TestOutput) plays a short note at the selected track's
//   channel and program on its MIDI output. MIDI / Audio setup (Sound.AudioSetup) opens the MIDI output window of the selected track with the
//   system's outputs and applies the chosen one to the track. Both show the status texts the WPF build shows.
// Does not own: the menu rows (MainMenuTable in Core), the commands table (MainWindowCommands), the MIDI output and engine (AvaloniaPlaybackSessions,
//   AvaloniaPlaybackComposition), the MIDI output window (Views/TrackOutputWindow), the edit itself (DocumentEdits).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests.
public static class MainWindowTrackOutputMenu
{
    /// <summary>The Sound menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Sound.TestOutput", "Sound.AudioSetup" };

    private const string NoTrackText = "Select a track first";
    private const string TestingText = "Testing MIDI output…";
    private const string TestDoneText = "MIDI test complete";
    private const string TestFailedAction = "Testing the MIDI output";
    private const string AudioSetupText = "MIDI / audio setup: the selected track's MIDI output";

    /// <summary>The output listed when the system reports no MIDI output device, as the WPF build lists it.</summary>
    private const int DefaultOutputDeviceId = -1;
    private const string DefaultOutputName = "MIDI Mapper / Windows default";

    /// <summary>The test note's pitch: a bass track plays a lower note, as in the WPF build.</summary>
    private const int BassTestNote = 40;
    private const int OtherTestNote = 64;

    /// <param name="playback">The playback sessions; null (no engine host, as in the preview) makes the test command do nothing.</param>
    /// <param name="songs">The tabs; the commands act on the shown song's selected track.</param>
    /// <param name="owner">The window the MIDI output window opens over.</param>
    /// <param name="notice">Shows a status text.</param>
    public static void Register(MainWindowCommands commands, AvaloniaPlaybackSessions? playback, MainWindowTabs songs, Window owner, Action<string> notice)
    {
        var tools = new ToolWindows();
        commands.Add("Sound.TestOutput", () => StartTest(playback, songs.Active, notice));
        commands.Add("Sound.AudioSetup", () => ShowOutputWindow(tools, playback, songs.Active, owner, notice));
    }

    private static void StartTest(AvaloniaPlaybackSessions? playback, SongDocument song, Action<string> notice) =>
        Platform.UiTasks.Forget(TestSelectedTrackAsync(playback, song, notice), TestFailedAction, notice);

    private static async Task TestSelectedTrackAsync(AvaloniaPlaybackSessions? playback, SongDocument song, Action<string> notice)
    {
        var tracks = song.Project.Tracks;
        var index = song.TrackIndex;
        if (index < 0 || index >= tracks.Count)
        {
            notice(NoTrackText);
            return;
        }
        if (playback is null) return;
        var track = tracks[index];
        notice(TestingText);
        var note = track.Kind == TrackKind.Bass ? BassTestNote : OtherTestNote;
        await playback.PlayTestNoteAsync(track.MidiOutputDeviceId, track.MidiChannel, track.MidiProgram, note);
        notice(TestDoneText);
    }

    private static void ShowOutputWindow(ToolWindows tools, AvaloniaPlaybackSessions? playback, SongDocument song, Window owner, Action<string> notice)
    {
        var tracks = song.Project.Tracks;
        var index = song.TrackIndex;
        if (index < 0 || index >= tracks.Count)
        {
            notice(NoTrackText);
            return;
        }
        var track = tracks[index];
        tools.OpenOrActivate("Track output", track.Id.ToString("N"), () => new TrackOutputWindow(track.Name, SystemOutputDevices(), track.MidiOutputDeviceId,
            deviceId => SetTrackOutput(song, track, deviceId),
            () => StartTest(playback, song, notice)), owner);
        notice(AudioSetupText);
    }

    /// <summary>Sets the track's MIDI output device as one undoable edit that does not change the timeline.</summary>
    private static void SetTrackOutput(SongDocument song, TrackModel track, int deviceId) =>
        DocumentEdits.Run(song, _ =>
        {
            if (track.MidiOutputDeviceId == deviceId) return false;
            track.MidiOutputDeviceId = deviceId;
            return true;
        }, invalidatesTimeline: false, label: "Track output");

    /// <summary>The system's MIDI output devices, with the Windows default listed when there are none.</summary>
    private static IReadOnlyList<MidiOutputDeviceInfo> SystemOutputDevices()
    {
        using var output = AvaloniaPlaybackComposition.CreateMidiOutput();
        var devices = output.Devices.ToList();
        if (devices.Count == 0) devices.Add(new MidiOutputDeviceInfo { DeviceId = DefaultOutputDeviceId, Name = DefaultOutputName });
        return devices;
    }
}
