using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia;

// Owns: Track.FxChain (the FX chain window of the selected track), Track.Wiring (that window, with the wiring window of its focused plug-in on top, as
//   MixerHost.OpenWiring) and Track.MidiProcessing (the same with the MIDI processing window), and the window registry of the main window (FxChainWindows).
// Does not own: the windows themselves (FxChainWindow, WiringWindow, MidiProcessingWindow) or their edits (SongFxChainHost), the mixer's FX buttons (MixerWindow),
//   the key table (MainWindowKeyboard).
// Tests: TabForge.Avalonia.Tests.MainWindowTrackFxKeysTests, TabForge.Avalonia.Tests.MainWindowMenuTests (every Commands id is handled).
public static class MainWindowTrackFxKeys
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Track.FxChain", "Track.Wiring", "Track.MidiProcessing" };

    /// <param name="commands">The window's command table.</param>
    /// <param name="songs">The tabs; the commands act on the shown song's selected track.</param>
    /// <param name="owner">The window that owns the opened windows.</param>
    /// <param name="playback">The playback sessions the engine sync goes through; null: no engine.</param>
    /// <param name="settings">The shared settings; null: defaults that are not saved.</param>
    /// <param name="trust">The app's plug-in trust state; null makes one (tests).</param>
    /// <returns>The registry of chain windows of this main window (the FX cells and the mixer open windows through it).</returns>
    public static FxChainWindows Register(MainWindowCommands commands, MainWindowTabs songs, Window owner, AvaloniaPlaybackSessions? playback, MainWindowSettings? settings, TabForge.Plugins.PluginTrust trust)
    {
        var windows = new FxChainWindows(owner, () => songs.Active, playback, settings, trust);
        commands.Add("Track.FxChain", () => { if (SelectedTrack(songs.Active) is { } track) windows.Open(track); });
        commands.Add("Track.Wiring", () => { if (SelectedTrack(songs.Active) is { } track) windows.OpenWiring(track); });
        commands.Add("Track.MidiProcessing", () => { if (SelectedTrack(songs.Active) is { } track) windows.OpenMidiProcessing(track); });
        return windows;
    }

    /// <summary>The song's selected track; null when the song has no tracks.</summary>
    public static TrackModel? SelectedTrack(SongDocument song)
    {
        var index = song.TrackIndex;
        return index >= 0 && index < song.Project.Tracks.Count ? song.Project.Tracks[index] : null;
    }

    /// <summary>Opens the FX chain window of <paramref name="track"/> over <paramref name="owner"/> (the FX button, the key and the track row cell).</summary>
    public static void OpenChain(TrackModel track, Window owner) => FxChainWindows.OpenFor(owner, track);
}
