using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the MIDI output device binding checks (name-first resolution, index fallback, the missing mark,
//   old index-only projects).
// Does not own: the device list (WinMmMidiOutput.Devices), the track model or the windows.
// Tests: this class. Headless.
public class MidiOutputRoutingTests
{
    private static readonly MidiOutputDeviceInfo[] Devices =
    {
        new() { DeviceId = 0, Name = "Speakers" },
        new() { DeviceId = 1, Name = "US-2x2" },
    };

    private static readonly MidiOutputDeviceInfo[] Reordered =
    {
        new() { DeviceId = 0, Name = "US-2x2" },
        new() { DeviceId = 1, Name = "Speakers" },
    };

    [Fact]
    public void ResolveFindsTheSavedDeviceByNameAfterAReorder()
    {
        Assert.Equal(1, MidiOutputRouting.Resolve(Devices, 1, "US-2x2"));
        Assert.Equal(0, MidiOutputRouting.Resolve(Reordered, 1, "US-2x2"));
    }

    [Fact]
    public void ResolveFallsBackToTheSavedIndex()
    {
        Assert.Equal(5, MidiOutputRouting.Resolve(Devices, 5, ""));   // an old project saved no name
        Assert.Equal(5, MidiOutputRouting.Resolve(Devices, 5, null));
        Assert.Equal(1, MidiOutputRouting.Resolve(Devices, 1, "Unplugged"));   // the name is gone: the index stays
    }

    [Fact]
    public void MissingMarksOnlyAnUnlistedSavedName()
    {
        Assert.False(MidiOutputRouting.IsMissing(Devices, ""));
        Assert.False(MidiOutputRouting.IsMissing(Devices, (string?)null));
        Assert.False(MidiOutputRouting.IsMissing(Devices, "US-2x2"));
        Assert.True(MidiOutputRouting.IsMissing(Devices, "Unplugged"));
    }

    [Fact]
    public void ResolveUsesTheTrackModel()
    {
        var track = new TrackModel { MidiOutputDeviceId = 1, MidiOutputDeviceName = "US-2x2" };
        Assert.Equal(0, MidiOutputRouting.Resolve(Reordered, track));
        Assert.True(MidiOutputRouting.IsMissing(Reordered[1..], track));
    }

    [Fact]
    public void OldIndexOnlyProjectLoadsWithoutAName()
    {
        var track = JsonSerializer.Deserialize<TrackModel>(
            """{"Name":"Guitar","MidiOutputDeviceId":2}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("", track.MidiOutputDeviceName);
        Assert.Equal(2, MidiOutputRouting.Resolve(Devices, track));
        Assert.False(MidiOutputRouting.IsMissing(Devices, track));
    }

    [Fact]
    public void StatusTextShowsTheResolvedNameAndTheMissingMark()
    {
        Assert.Equal("US-2x2", MidiOutputStatusPresenter.OutputText(Reordered, 1, "US-2x2"));
        Assert.Equal("Speakers", MidiOutputStatusPresenter.OutputText(Reordered, 1, ""));
        Assert.Equal("Unplugged (missing)", MidiOutputStatusPresenter.OutputText(Reordered, 1, "Unplugged"));
        Assert.Equal(MidiOutputStatusPresenter.UnknownOutputText, MidiOutputStatusPresenter.OutputText(Reordered, 7, ""));
    }

    [Fact]
    public void MissingMarkText()
    {
        Assert.Equal("Unplugged (missing)", TrackOutputPresenter.MissingDeviceText("Unplugged"));
    }
}
