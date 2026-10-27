using System;
using System.Threading;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the engine tuner staying on while any consumer (Tools > Tuner, the live input strip) wants it, and going off when the last one lets go.
// Does not own: the tuner readout (LiveInputStripTests) or the windows.
// Tests: this file.
public class TunerSharedUseTests
{
    [Fact]
    public void ClosingOneTunerConsumerKeepsTheTunerOnForTheOther()
    {
        using var shared = SharedBlock.Create($"tf-tuner-{Guid.NewGuid():N}");
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachForTest(shared, new SynchronizationContext(), 0);
        var window = new object();
        var strip = new object();
        client.SetTunerWanted(window, true);
        client.SetTunerWanted(strip, true);
        Assert.True(shared.TunerOn);
        client.SetTunerWanted(window, false);
        Assert.True(shared.TunerOn);   // the strip still wants it
        client.SetTunerWanted(strip, false);
        Assert.False(shared.TunerOn);
        client.SetTunerWanted(strip, false);   // a second release changes nothing
        Assert.False(shared.TunerOn);
        client.SetTunerWanted(window, true);
        client.SetTunerWanted(window, true);   // asking twice still counts once
        client.SetTunerWanted(window, false);
        Assert.False(shared.TunerOn);
    }

    [Fact]
    public void ANewEngineBlockGetsTheTunerBackWhileSomeoneStillWantsIt()
    {
        using var first = SharedBlock.Create($"tf-tuner-{Guid.NewGuid():N}");
        using var second = SharedBlock.Create($"tf-tuner-{Guid.NewGuid():N}");
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.SetTunerWanted(new object(), true);   // asked while no engine ran
        client.AttachForTest(first, new SynchronizationContext(), 0);
        Assert.True(first.TunerOn);
        client.AttachForTest(second, new SynchronizationContext(), 0);   // the engine restarted: a fresh block
        Assert.True(second.TunerOn);
    }
}
