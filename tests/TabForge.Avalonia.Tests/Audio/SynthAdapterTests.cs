using System;
using System.IO;
using System.Linq;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Synth;
using TabForge.Services;
using TabForge.Sf2Synth.Tests;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the adapter seam between the app and the TabForge Synth library: the experimental engine choice plays its SoundFont, and the
//   settings reach the engine (ActivePath) while a changed amp keeps the loaded SoundFont (only the rig swaps).
// Does not own: the layer itself (the engine's GuitarLayerTests), the SoundFont player (Sf2SynthTests), the humanizer model (HumanizerTests).
// Tests: GmSynth, GmBank, SoundEngines, SoundFontStore.
public sealed class SynthAdapterTests
{
    private const int Rate = 48000;

    [Fact]
    public void TheEngineChoicePlaysTheLayerAndItsSoundFont()
    {
        var sf2 = Path.Combine(Path.GetTempPath(), "tf-sf2synth-test.sf2");
        File.WriteAllBytes(sf2, TestSoundFont.Build());
        var bank = new GmBank(windowsHost: false, bundledPath: Path.Combine(Path.GetTempPath(), "tf-no-such-bank.sf2"));
        Assert.True(bank.Select(SoundEngines.TabForgeSynthPath + "|" + sf2, out var error), error);
        Assert.True(bank.Current.Experimental && bank.Current.Sf2 is not null);
        using var synth = new GmSynth(Rate, 512, bank);
        BlockMidi[] notes = [new() { Status = 0xC0, Data1 = 30 }, new() { Frame = 10, Status = 0x90, Data1 = 40, Data2 = 100 }, new() { Frame = 20, Status = 0x91, Data1 = 69, Data2 = 100 }];
        var input = new[] { new float[4800], new float[4800] };
        var outBuf = new[] { new float[4800], new float[4800] };
        synth.Process(input, outBuf, 4800, notes, new TransportInfo());
        Assert.True(outBuf[0].Max(MathF.Abs) > 1e-2);
        synth.Process(input, outBuf, 4800, ReadOnlySpan<BlockMidi>.Empty, new TransportInfo());
        var before = GC.GetAllocatedBytesForCurrentThread();
        synth.Process(input, outBuf, 4800, ReadOnlySpan<BlockMidi>.Empty, new TransportInfo());
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void TheSettingsReachTheEngineAndARigSwapKeepsTheFont()
    {
        var nam = Path.Combine(Path.GetTempPath(), "dummy.nam");
        var plugins = new PluginSettings { SoundEngine = SoundEngines.TabForgeSynth, SynthAmpPath = nam };
        Assert.EndsWith("|" + nam, SoundFontStore.ActivePath(plugins, windowsHost: false));
        var sf2 = Path.Combine(Path.GetTempPath(), "tf-sf2synth-test.sf2");
        File.WriteAllBytes(sf2, TestSoundFont.Build());
        var bank = new GmBank(windowsHost: false, bundledPath: sf2);
        Assert.True(bank.Select(SoundEngines.TabForgeSynthPath + "|" + sf2, out _));
        var font = bank.Current.Sf2;
        Assert.True(bank.Select(SoundEngines.TabForgeSynthPath + "|" + sf2 + "|" + nam, out _));
        Assert.True(bank.Current.RigOnly && ReferenceEquals(font, bank.Current.Sf2) && bank.Current.Amp == nam);
    }
}
