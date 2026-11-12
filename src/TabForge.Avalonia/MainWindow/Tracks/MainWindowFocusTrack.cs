using System;
using System.Collections.Generic;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: Focus Track (the Transport.FocusTrack command, the left strip icon and the View menu row): the opt-in practice aid that plays every track except the
//   selected one quieter, kept on the selected track as the selection or the shown song changes, its saved on / off (Audio settings, off by default) and its check.
// Does not own: the levels it changes (PracticeFocus, EngineMixLevels, PlaybackEngine live state), the icon (LeftStripItems), the menu row (MainMenuTable).
// Tests: TabForge.Avalonia.Tests.PracticeFocusTests.
// Threads: UI thread only.
/// <summary>Focus Track, bound to the shown song's selected track.</summary>
public sealed class MainWindowFocusTrack
{
    /// <summary>The command id; the menu row, the strip icon and the tests use it.</summary>
    public const string CommandId = "Transport.FocusTrack", SettingsId = "Transport.FocusTrackSettings";

    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { CommandId, SettingsId };

    private readonly MainWindowTabs _songs;
    private readonly Transport.AvaloniaPlaybackSessions? _playback;
    private readonly MainWindowSettings? _settings;
    private readonly PracticeFocus _focus;
    private SongDocument? _watched;
    private bool _on;

    private MainWindowFocusTrack(MainWindowTabs songs, Transport.AvaloniaPlaybackSessions? playback, MainWindowSettings? settings)
    {
        _songs = songs;
        _playback = playback;
        _settings = settings;
        _focus = playback?.Focus ?? new PracticeFocus();
        _on = settings?.Settings.Audio.FocusTrack ?? false;
    }

    /// <summary>Registers the command and starts following the shown song; Focus Track starts as it was saved (off the first time).</summary>
    public static MainWindowFocusTrack Register(MainWindowCommands commands, global::Avalonia.Controls.Window owner, MainWindowTabs songs, Transport.AvaloniaPlaybackSessions? playback, MainWindowSettings? settings)
    {
        var focus = new MainWindowFocusTrack(songs, playback, settings);
        commands.Add(CommandId, focus.Toggle);
        commands.Add(SettingsId, () => focus.OpenSettings(owner));
        commands.AddCheckState(CommandId, () => focus._on);
        songs.ActiveChanged += focus.Rebind;
        focus.Rebind();
        return focus;
    }

    /// <summary>True while Focus Track is on.</summary>
    public bool IsOn => _on;

    private FocusTrackSettings Rules => _settings?.Settings.Audio.FocusTrackRules ?? _fallbackRules;
    private readonly FocusTrackSettings _fallbackRules = new();

    private bool HasShownSong => _songs.ActiveIndex >= 0 && _songs.ActiveIndex < _songs.Songs.Count;

    // Right-click the icon (or View > Focus track settings): the rules window; Apply works the levels out again at once.
    private void OpenSettings(global::Avalonia.Controls.Window owner)
    {
        var window = new Features.LeftStrip.FocusTrackSettingsWindow(Rules, _on, on => { if (on != _on) Toggle(); }, () => { _settings?.SaveSettingsDeferred(); Apply(true); });
        window.Show(owner);
    }

    private void Toggle()
    {
        _on = !_on;
        if (_settings is not null) { _settings.Settings.Audio.FocusTrack = _on; _settings.SaveSettingsDeferred(); }
        Apply();
    }

    private void Rebind()
    {
        if (_watched is not null) _watched.TrackIndexChanged -= Apply;
        _watched = HasShownSong ? _songs.Active : null;
        if (_watched is not null) _watched.TrackIndexChanged += Apply;
        Apply();
    }

    // Sets the focused track (the shown song's selected one, or none while off) and pushes the levels to the running playback at once.
    private void Apply() => Apply(false);

    private void Apply(bool forceRefresh)
    {
        if (!HasShownSong) return;   // no song is shown (the last tab closed)
        var song = _songs.Active;
        var index = song.TrackIndex;
        TrackModel? selected = index >= 0 && index < song.Project.Tracks.Count ? song.Project.Tracks[index] : null;
        var changed = _focus.Set(_on, song.Project, selected, Rules);
        if (changed || forceRefresh) _playback?.PushLiveMix(song);
    }
}
