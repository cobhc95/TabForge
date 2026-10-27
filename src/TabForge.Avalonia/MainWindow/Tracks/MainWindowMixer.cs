using System.Collections.Generic;
using System;
using Avalonia.Controls;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia;

// Owns: the View.Mixer command (the View menu's Mixer row) and the Mixer.MasterFx, MonitorFx, GroupFx and GroupRules commands: each opens
//   the mixer on the shown song and then the window it names.
// Does not own: the menu row (MainMenuTable in Core), the commands table (MainWindowCommands), the mixer and its edits (MixerWindow).
// Tests: TabForge.Avalonia.Tests.MixerWindowTests (the window), TabForge.Avalonia.Tests.MainWindowMenuTests (the row is handled).
/// <summary>The View menu's Mixer command, bound to the shown song.</summary>
public static class MainWindowMixer
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "View.Mixer", MultitrackCommandId, "Mixer.MasterFx", "Mixer.MonitorFx", "Mixer.GroupFx", "Mixer.GroupRules" };

    /// <summary>The View menu row that shows the track list (the mixer with every track).</summary>
    private const string MultitrackCommandId = "View.Multitrack";

    /// <summary>The status notice the track list row shows.</summary>
    private const string MultitrackText = "Multitrack: all tracks in the mixer; click a colour block to jump";

    /// <param name="songs">The tabs; the command reads the shown song.</param>
    /// <param name="owner">The window the mixer opens over.</param>
    /// <param name="playback">The playback sessions the mixer's live changes are pushed to; null (no engine host) pushes nothing.</param>
    /// <param name="settings">The shared settings the master volume is saved to; null saves nothing.</param>
    /// <param name="status">The status notice the track list row shows; null shows nothing (the Compose call leaves it unset).</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, Window owner, TabForge.Avalonia.Transport.AvaloniaPlaybackSessions? playback, MainWindowSettings? settings, Action<string>? status = null)
    {
        var tools = new Views.ToolWindows();
        commands.Add("View.Mixer", () => ShowMixer(tools, songs, owner, playback, settings));
        commands.Add(MultitrackCommandId, () => { ShowMixer(tools, songs, owner, playback, settings); status?.Invoke(MultitrackText); });
        commands.Add("Mixer.MasterFx", () => ShowMixer(tools, songs, owner, playback, settings).OpenMasterFx());
        commands.Add("Mixer.MonitorFx", () => ShowMixer(tools, songs, owner, playback, settings).OpenMonitorFx());
        commands.Add("Mixer.GroupRules", () => ShowMixer(tools, songs, owner, playback, settings).OpenGroupRules());
        commands.Add("Mixer.GroupFx", () =>
        {
            if (MainWindowTrackFxKeys.SelectedTrack(songs.Active) is { } track)
                ShowMixer(tools, songs, owner, playback, settings).OpenGroupFx(MixerGroups.GroupOf(songs.Active.Project, track));
        });
    }

    /// <summary>Opens the mixer over the owner on the shown song and returns it, so a command can open one of its windows.</summary>
    private static MixerWindow ShowMixer(Views.ToolWindows tools, MainWindowTabs songs, Window owner, TabForge.Avalonia.Transport.AvaloniaPlaybackSessions? playback, MainWindowSettings? settings)
    {
        SongDocument document = songs.Active;
        // The Audio & Plug-ins button opens the Settings window on that page, as the other places that open it do; the size is kept as the Settings window keeps its own.
        Action openAudio = () => MainWindowSettings.ShowPreferencesPage(settings, owner, TabForge.Services.SettingsCatalog.AudioVst);
        return tools.OpenOrActivate("Mixer", document, () => playback is null
            ? new MixerWindow(document, openAudioSettings: openAudio, sizing: settings?.Settings, saveSettings: () => settings?.SaveWindowPlacement())
            : new MixerWindow(document, () => playback.PushLiveMix(document), () => playback.MasterPercent, percent =>
            {
                // The Master slider is the master knob: the engines, the transport knobs and the saved audio settings all take the value.
                playback.SetMasterPercent(percent);
                settings?.SaveSettings();
            }, openAudio, settings?.Settings, () => settings?.SaveWindowPlacement()), owner);
    }
}
