using System.Collections.Generic;
using System;
using TabForge.Avalonia.Platform;
using Avalonia.Controls;
using TabForge.Audio;
using TabForge.Avalonia.Views;
using TabForge.Models;

namespace TabForge.Avalonia;

// Owns: the Tools.Tuner command (the Tools menu's Tuner row): opens the live tuner over the window, one window at a time.
// Does not own: the menu row (MainMenuTable in Core), the commands table (MainWindowCommands), the tuner and its reading rules (TunerWindow, TunerPresenter).
// Tests: TabForge.Avalonia.Tests.TunerWindowTests.
/// <summary>The Tools menu's Tuner command.</summary>
public static class MainWindowTuner
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Tools.Tuner" };

    /// <param name="commands">The window's command table that receives the handler.</param>
    /// <param name="owner">The window the tuner opens over.</param>
    /// <param name="engine">The playback engine that detects the pitch (the one the sessions play through); null (no playback host) still registers the command, and the tuner opens in its engine-off state.</param>
    /// <param name="meter">The app's shared meter tick; null gives the window its own.</param>
    /// <param name="selectedTrack">The track the tuner lists the tunings of; read on each poll.</param>
    public static void Register(MainWindowCommands commands, Window owner, AudioEngineClient? engine, Func<TrackModel?> selectedTrack, SharedMeterTick? meter = null)
    {
        var tools = new Views.ToolWindows();
        commands.Add("Tools.Tuner", () => tools.OpenOrActivate("Tuner", null, () => new TunerWindow(selectedTrack, engine, meter), owner));
    }
}
