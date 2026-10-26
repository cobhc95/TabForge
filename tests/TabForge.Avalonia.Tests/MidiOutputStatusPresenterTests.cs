using System;
using TabForge.Playback;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks of the status bar's MIDI output name (the rule the status bar used before it moved to Core): the name of the
/// track's device, the mapper's name, and the fallback text for a device the list does not hold.
/// Does not own: the device list (WinMmMidiOutput.Devices) or the status bar view.
/// Tests: this class.
/// </summary>
public class MidiOutputStatusPresenterTests
{
    private static readonly MidiOutputDeviceInfo[] Devices =
    {
        new() { DeviceId = -1, Name = WinMmMidiOutput.MapperName },
        new() { DeviceId = 2, Name = "Synth A" },
    };

    [Fact]
    public void OutputTextIsTheNameOfTheTracksDevice()
    {
        Assert.Equal("Synth A", MidiOutputStatusPresenter.OutputText(Devices, 2));
        Assert.Equal("Microsoft MIDI Mapper (default)", MidiOutputStatusPresenter.OutputText(Devices, -1));
    }

    [Fact]
    public void OutputTextFallsBackWhenTheDeviceIsNotListed()
    {
        Assert.Equal("MIDI output", MidiOutputStatusPresenter.OutputText(Devices, 7));
        Assert.Equal("MIDI output", MidiOutputStatusPresenter.OutputText(Array.Empty<MidiOutputDeviceInfo>(), -1));
    }
}
