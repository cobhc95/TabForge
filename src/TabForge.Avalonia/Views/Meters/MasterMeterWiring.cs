using System;
using Avalonia;
using Avalonia.VisualTree;
using TabForge.Audio;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Meters;

// Owns: the glue that makes a transport bar master meter live: the settings it reads and saves, the engine reading and the shared 33 ms meter tick,
//   which runs only while the bar is on screen.
// Does not own: the meter (MasterMeterView), its numbers (MasterMeterModel), the measuring (the engine) or the mixer command (TransportBarView.BindCommands).
// Tests: TabForge.Avalonia.Tests.MasterMeterTests.
// Thread: UI thread.
public static class MasterMeterWiring
{
    /// <summary>Hooks the meter of <paramref name="bar"/> to <paramref name="engine"/> (null: no engine, silent bars) and the settings; the tick ends when the bar leaves the screen.</summary>
    /// <param name="settings">The window settings; the meter reads MasterMeter from it at each tick.</param>
    /// <param name="save">Saves the settings after a menu choice.</param>
    /// <param name="tick">The app shared meter tick; null: the meter does not move.</param>
    public static void Attach(TransportBarView bar, AudioEngineClient? engine, Func<AppSettings> settings, Action save, SharedMeterTick? tick)
    {
        var meter = bar.MasterMeter;
        meter.Settings = () => settings().MasterMeter ??= new MasterMeterSettings();
        meter.Source = () => engine?.ReadMasterMeter();
        meter.Changed = save;
        if (tick is null) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Action onTick = () => meter.Tick(clock.Elapsed.TotalMilliseconds);
        meter.AttachedToVisualTree += (_, _) => tick.Subscribe(onTick);
        meter.DetachedFromVisualTree += (_, _) => tick.Unsubscribe(onTick);
        if (meter.GetVisualRoot() is not null) tick.Subscribe(onTick);
    }
}
