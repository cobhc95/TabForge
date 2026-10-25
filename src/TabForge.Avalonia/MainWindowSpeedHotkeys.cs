using System;
using System.Collections.Generic;
using TabForge.Avalonia.Transport;
using TabForge.Playback;

namespace TabForge.Avalonia;

// Owns: the Playback speed hotkeys (Playback.SpeedUp and Playback.SpeedDown step the shown song's speed to the next or previous preset,
//   Playback.SpeedReset returns it to the song's own tempo) and the status line text each one shows.
// Does not own: the speed the transport plays at (PlaybackTransport), the playback sessions (AvaloniaPlaybackSessions), the transport
//   bar's speed box, the key table (MainWindowKeyboard).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (reads Commands by reflection and adds each id to a command table).
public static class MainWindowSpeedHotkeys
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Playback.SpeedUp", "Playback.SpeedDown", "Playback.SpeedReset" };

    /// <summary>The presets the up and down hotkeys step through, lowest first (50, 75, 100, 125, 150 and 200 %).</summary>
    private static readonly IReadOnlyList<double> Presets = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

    /// <summary>The slack when a speed is compared with a preset, so a preset reached by arithmetic still counts as that preset.</summary>
    private const double PresetTolerance = 1e-6;

    /// <param name="commands">The window's command table; each id is added once.</param>
    /// <param name="songs">The tabs; the hotkeys act on the shown song.</param>
    /// <param name="playback">The playback sessions the speed is set on; null (no engine host) makes the hotkeys do nothing.</param>
    /// <param name="notice">Shows the status line text after a change.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, AvaloniaPlaybackSessions? playback, Action<string> notice)
    {
        commands.Add("Playback.SpeedUp", () => ChangeSpeed(songs, playback, notice, current => NextPreset(current, 1)));
        commands.Add("Playback.SpeedDown", () => ChangeSpeed(songs, playback, notice, current => NextPreset(current, -1)));
        commands.Add("Playback.SpeedReset", () => ChangeSpeed(songs, playback, notice, _ => PlaybackTransport.DefaultSpeed));
    }

    /// <summary>Sets the shown song's speed to the value the change function returns from its current speed, and shows the result.</summary>
    private static void ChangeSpeed(MainWindowTabs songs, AvaloniaPlaybackSessions? playback, Action<string> notice, Func<double, double> speedFor)
    {
        if (playback?.For(songs.Active) is not { } session) return;
        session.SetSpeed(speedFor(session.Speed));
        notice($"Speed {session.Speed:0.00}×");
    }

    /// <summary>The next preset above (direction 1) or below (direction -1) the speed; the speed itself when no preset lies that way.</summary>
    private static double NextPreset(double current, int direction)
    {
        if (direction > 0)
        {
            foreach (var preset in Presets)
                if (preset > current + PresetTolerance) return preset;
        }
        else
        {
            for (var i = Presets.Count - 1; i >= 0; i--)
                if (Presets[i] < current - PresetTolerance) return Presets[i];
        }
        return current;
    }
}
