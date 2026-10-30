using System;
using TabForge.Audio;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Meters;

// Owns: the glue that makes the track-row meters live: the shared TrackMeterSettings, the engine reading at the chosen tap, and saving after a menu choice.
// Does not own: the meters and their tick (TrackMeterColumn), the numbers (MasterMeterModel), the measuring (the engine).
// Tests: TabForge.Avalonia.Tests.TrackMeterTests.
// Thread: UI thread.
public static class TrackMeterWiring
{
    /// <summary>Hooks <paramref name="meters"/> to <paramref name="engine"/> (null: silent bars) and the app settings.</summary>
    public static void Attach(TrackMeterColumn meters, AudioEngineClient? engine, Func<AppSettings> settings, Action save)
    {
        meters.Settings = () => settings().TrackMeter ??= new TrackMeterSettings();
        meters.Source = track => engine?.ReadTrackMeter(track, meters.Settings().IsPreFader);
        meters.Changed = () => { save(); meters.Refresh(); };
    }

    /// <summary>Connects the header's meter gestures and hover state to the row meters; a double-click runs the same mixer command as the master meter.</summary>
    public static void AttachInput(TrackMeterColumn meters, TrackHeaderColumn header, ContextMenuHost? contextMenus)
    {
        meters.OpenMixer = contextMenus is null ? null : () => TransportBarView.RunIfHandled(contextMenus, TransportBarView.MixerItem.CommandId!);
        header.MeterResetRequested = meters.ResetClip;
        header.MeterMixerRequested = _ => meters.OpenMixer?.Invoke();
        header.CellPaintChanged += () => meters.UpdatePointer(header.HoverCell, header.PressedCell);
    }
}
