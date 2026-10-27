#if TABFORGE_TEST_HOOKS
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Plugins.Clap;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the CLAP host (cross-platform Stage 4) through a fake CLAP plug-in written in C# (ClapTestLibrary): native struct layouts, descriptors, create / activate /
//   process with notes, MIDI and transport, state round trip, parameters, latency and tail, reconfigure, the editor handshake and the isolation wire for parameters.
// Does not own: a real third-party .clap (the CLAP self-test runs one when it is installed).
// Tests: itself. One class on purpose: the fake plug-in keeps process-wide counters, so its tests must not run in parallel with each other.
public sealed class ClapHostTests
{
    private static readonly EngineLogWriter Log = new();
    private static readonly TransportInfo Idle = new() { Tempo = 120 };

    private static float[][] Stereo(int frames, float value) => new[] { Enumerable.Repeat(value, frames).ToArray(), Enumerable.Repeat(value, frames).ToArray() };

    private static void Run(IPluginInstance plugin, float[][] input, float[][] output, int frames, BlockMidi[]? midi = null, TransportInfo? transport = null) =>
        plugin.Process(input, output, frames, midi ?? Array.Empty<BlockMidi>(), transport ?? Idle);

    [Fact]
    public void TheNativeStructsMatchTheClapHeaders()
    {
        Assert.Equal(16, Marshal.SizeOf<ClapEventHeader>());
        Assert.Equal(40, Marshal.SizeOf<ClapEventNote>());
        Assert.Equal(32, (int)Marshal.OffsetOf<ClapEventNote>(nameof(ClapEventNote.Velocity)));
        Assert.Equal(22, (int)Marshal.OffsetOf<ClapEventNote>(nameof(ClapEventNote.Channel)));
        Assert.Equal(104, Marshal.SizeOf<ClapEventTransport>());
        Assert.Equal(24, (int)Marshal.OffsetOf<ClapEventTransport>(nameof(ClapEventTransport.SongPosBeats)));
        Assert.Equal(40, (int)Marshal.OffsetOf<ClapEventTransport>(nameof(ClapEventTransport.Tempo)));
        Assert.Equal(88, (int)Marshal.OffsetOf<ClapEventTransport>(nameof(ClapEventTransport.BarStart)));
        Assert.Equal(100, (int)Marshal.OffsetOf<ClapEventTransport>(nameof(ClapEventTransport.TsigNum)));
        Assert.Equal(24, Marshal.SizeOf<ClapEventMidi>());
        Assert.Equal(12, Marshal.SizeOf<ClapVersion>());
    }

    [Fact]
    public void TheFactoryListsBothPlugInsWithTheirRoles()
    {
        using var module = ClapTestLibrary.OpenModule();
        Assert.Equal(2, module.Plugins.Count);
        var synth = module.Plugins.Single(p => p.Id == ClapTestLibrary.SynthId);
        var gain = module.Plugins.Single(p => p.Id == ClapTestLibrary.GainId);
        Assert.True(synth.IsInstrument && !synth.IsEffect);
        Assert.True(gain.IsEffect && !gain.IsInstrument);
        Assert.Equal("TabForge Test Gain", gain.Name);
        Assert.Equal("TabForge Test", gain.Vendor);
        Assert.Contains("utility", gain.Features);
        Assert.Equal(ClapTestLibrary.GainId, module.Pick(wantInstrument: false)!.Id);   // the effect is listed second but still found
        Assert.Equal(ClapTestLibrary.SynthId, module.Pick(wantInstrument: true)!.Id);
    }

    [Fact]
    public void InitAndDeinitArePairedPerInstanceAndThePlugInIsDestroyed()
    {
        var (init, deinit, made, gone) = (ClapTestLibrary.InitCalls, ClapTestLibrary.DeinitCalls, ClapTestLibrary.Created, ClapTestLibrary.Destroyed);
        var plugin = ClapTestLibrary.Create(false, 48000, 256, Log);
        Assert.Equal(init + 1, ClapTestLibrary.InitCalls);
        Assert.Equal(made + 1, ClapTestLibrary.Created);
        Assert.False(plugin.IsInstrument);
        Assert.Equal("TabForge Test Gain", plugin.ProductName);
        plugin.Dispose();
        plugin.Dispose();   // a second dispose is harmless
        Assert.Equal(deinit + 1, ClapTestLibrary.DeinitCalls);
        Assert.Equal(gone + 1, ClapTestLibrary.Destroyed);
    }

    [Fact]
    public void AnEffectGainsItsInputAndAParameterChangeApplies()
    {
        using var plugin = ClapTestLibrary.Create(false, 48000, 256, Log);
        var output = Stereo(128, 0f);
        Run(plugin, Stereo(128, 0.5f), output, 128);
        Assert.All(output[0].Concat(output[1]), v => Assert.Equal(0.5f, v));
        var snapshot = ClapTestLibrary.Snapshot()!;
        Assert.True(snapshot.Active && snapshot.Processing);
        Assert.Equal(48000, snapshot.SampleRate);
        Assert.Equal(256u, snapshot.MaxFrames);

        Assert.True(plugin.SetParameter(ClapTestLibrary.GainParam, 0.5));
        Run(plugin, Stereo(128, 0.5f), output, 128);
        Assert.All(output[1], v => Assert.Equal(0.25f, v));
        Assert.Equal(1, ClapTestLibrary.Snapshot()!.ParamEvents);

        Assert.True(plugin.SetParameter(ClapTestLibrary.GainParam, 50));   // clamped to the maximum the plug-in reports
        Run(plugin, Stereo(128, 0.5f), output, 128);
        Assert.All(output[0], v => Assert.Equal(1.0f, v));
        Assert.False(plugin.SetParameter(999, 1));   // an unknown id
    }

    [Fact]
    public void ParametersListVisibleOnesWithTheirRangeAndValue()
    {
        using var plugin = ClapTestLibrary.Create(false, 48000, 256, Log);
        var list = plugin.GetParameters();
        var gain = Assert.Single(list);   // the hidden parameter is left out
        Assert.Equal((ClapTestLibrary.GainParam, "Gain", 0.0, 2.0, 1.0, 1.0), (gain.Id, gain.Name, gain.Min, gain.Max, gain.Default, gain.Value));
        Assert.False(gain.ReadOnly);
        plugin.SetParameter(ClapTestLibrary.GainParam, 0.75);
        Run(plugin, Stereo(32, 0f), Stereo(32, 0f), 32);
        Assert.Equal(0.75, plugin.GetParameters().Single().Value);
    }

    [Fact]
    public void ASynthTurnsNotesAndMidiIntoClapEvents()
    {
        using var plugin = ClapTestLibrary.Create(true, 48000, 256, Log);
        Assert.True(plugin.IsInstrument);
        var output = Stereo(64, 0f);
        Run(plugin, Stereo(64, 0f), output, 64, new[] { new BlockMidi { Frame = 10, Status = 0x90, Data1 = 60, Data2 = 127 } });
        var s = ClapTestLibrary.Snapshot()!;
        Assert.Equal((1, 60), (s.NoteOns, s.LastKey));
        Assert.Equal(1.0, s.LastVelocity, 3);
        Assert.All(output[0], v => Assert.Equal(0.5f, v));

        Run(plugin, Stereo(64, 0f), output, 64, new[] { new BlockMidi { Frame = 0, Status = 0x80, Data1 = 60, Data2 = 0 }, new BlockMidi { Frame = 1, Status = 0xB0, Data1 = 1, Data2 = 64 } });
        s = ClapTestLibrary.Snapshot()!;
        Assert.Equal((1, 1), (s.NoteOffs, s.MidiEvents));   // the controller change is a CLAP MIDI event
        Assert.All(output[0], v => Assert.Equal(0f, v));

        Run(plugin, Stereo(64, 0f), output, 64, new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 62, Data2 = 0 } });
        Assert.Equal(2, ClapTestLibrary.Snapshot()!.NoteOffs);   // a note on with velocity 0 is a note off
    }

    [Fact]
    public void MoreEventsThanTheListHoldsAreDroppedNotOverrun()
    {
        using var plugin = ClapTestLibrary.Create(true, 48000, 256, Log);
        var many = Enumerable.Range(0, 2000).Select(i => new BlockMidi { Frame = i % 64, Status = 0x90, Data1 = (byte)(i % 100), Data2 = 90 }).ToArray();
        Run(plugin, Stereo(64, 0f), Stereo(64, 0f), 64, many);
        Assert.InRange(ClapTestLibrary.Snapshot()!.NoteOns, 500, 600);
    }

    [Fact]
    public void TheTransportCarriesTempoPositionAndTimeSignature()
    {
        using var plugin = ClapTestLibrary.Create(false, 48000, 256, Log);
        var transport = new TransportInfo { Tempo = 133, PpqPosition = 4.5, SongSec = 2, Playing = true, Meter = new TransportMeter { Numerator = 3, Denominator = 4, BarStartPpq = 3 } };
        Run(plugin, Stereo(32, 0f), Stereo(32, 0f), 32, null, transport);
        var s = ClapTestLibrary.Snapshot()!;
        Assert.Equal(1u | 2u | 4u | 8u | 16u, s.TransportFlags);   // tempo, beats, seconds, time signature, playing
        Assert.Equal(133, s.Tempo);
        Assert.Equal(4.5, s.BeatsPosition, 6);
        Assert.Equal((3, 4), (s.TsigNum, s.TsigDenom));
        Run(plugin, Stereo(32, 0f), Stereo(32, 0f), 32, null, new TransportInfo { Tempo = 90 });
        s = ClapTestLibrary.Snapshot()!;
        Assert.Equal(1u | 2u | 4u, s.TransportFlags);   // stopped, no meter known: no playing and no time-signature flags
    }

    [Fact]
    public void LatencyAndTailComeFromTheExtensions()
    {
        using var plugin = ClapTestLibrary.Create(false, 48000, 256, Log);
        Assert.Equal(ClapTestLibrary.LatencyFrames, plugin.LatencySamples);
        Assert.Equal(ClapTestLibrary.TailFrames, plugin.TailSamples);
        Assert.Equal(2, plugin.InputChannels);
    }

    [Fact]
    public void StateRoundTripsAndAForeignStateIsRejected()
    {
        byte[] saved;
        using (var first = ClapTestLibrary.Create(false, 48000, 256, Log))
        {
            first.SetParameter(ClapTestLibrary.GainParam, 0.25);
            Run(first, Stereo(16, 0f), Stereo(16, 0f), 16);
            saved = first.GetState()!;
            Assert.Equal(12, saved.Length);
        }
        using (var second = ClapTestLibrary.Create(false, 48000, 256, Log))
        {
            second.SetState(saved);
            var output = Stereo(16, 0f);
            Run(second, Stereo(16, 1f), output, 16);
            Assert.All(output[0], v => Assert.Equal(0.25f, v));
            Assert.Throws<InvalidDataException>(() => second.SetState(new byte[12]));   // wrong magic: the plug-in refuses
            second.SetState(Array.Empty<byte>());   // an empty state is ignored
        }
    }

    [Fact]
    public void ARejectedStateDisposesTheNewInstance()
    {
        var gone = ClapTestLibrary.Destroyed;
        Assert.Throws<InvalidDataException>(() => PluginLoading.CreateWithState(() => ClapTestLibrary.Create(false, 48000, 256, Log), new byte[12], Log, out _));
        Assert.Equal(gone + 1, ClapTestLibrary.Destroyed);
    }

    [Fact]
    public void ReconfigureFollowsTheNewRateAndAnImpossibleRateLeavesItSilent()
    {
        using var plugin = ClapTestLibrary.Create(false, 48000, 256, Log);
        plugin.Reconfigure(96000, 512);
        var s = ClapTestLibrary.Snapshot()!;
        Assert.Equal((96000.0, 512u, true), (s.SampleRate, s.MaxFrames, s.Active));
        var output = Stereo(512, 0f);
        Run(plugin, Stereo(512, 1f), output, 512);
        Assert.Equal(1f, output[0][511]);
        Assert.Throws<InvalidOperationException>(() => plugin.Reconfigure(4000, 256));   // the fake refuses it
        output = Stereo(64, 9f);
        Run(plugin, Stereo(64, 1f), output, 64);
        Assert.All(output[0], v => Assert.Equal(0f, v));   // loaded but silent, as IPluginInstance.Reconfigure promises
    }

    [Fact]
    public void TheEditorOpensInTheGivenWindowResizesAndCloses()
    {
        using var plugin = ClapTestLibrary.Create(false, 48000, 256, Log);
        Assert.True(plugin.HasEditor);
        Assert.Null(plugin.OpenEditor(IntPtr.Zero));   // no window to put it in
        var size = plugin.OpenEditor(new IntPtr(1234));
        Assert.Equal((400, 300), size);
        var s = ClapTestLibrary.Snapshot()!;
        Assert.True(s.GuiCreated && s.GuiShown);
        Assert.Equal(new IntPtr(1234), s.Parent);
        Assert.Null(plugin.OpenEditor(new IntPtr(1234)));   // already open

        var asked = new System.Collections.Generic.List<(int, int)>();
        plugin.ResizeRequested += (w, h) => asked.Add((w, h));
        ClapTestLibrary.RequestHostResize(640, 480);   // the plug-in asks, possibly from another thread: the host reports it on the main thread
        Assert.Empty(asked);
        plugin.EditorIdle();
        plugin.EditorIdle();
        Assert.Equal(new[] { (640, 480) }, asked);

        plugin.SetEditorSize(500, 400);   // the window got that size: the plug-in is told
        Assert.Equal(500 * 10000 + 400, ClapTestLibrary.Snapshot()!.SizeSet);
        plugin.CloseEditor();
        s = ClapTestLibrary.Snapshot()!;
        Assert.False(s.GuiCreated || s.GuiShown);
        plugin.CloseEditor();   // closing twice is harmless
    }

    [Fact]
    public void ARequestedMainThreadCallbackRunsOnTheMainThread()
    {
        using var plugin = ClapTestLibrary.Create(false, 48000, 256, Log);
        ClapTestLibrary.RequestHostCallback();
        Assert.Equal(0, ClapTestLibrary.Snapshot()!.MainThreadCalls);
        plugin.PumpMainThread();
        plugin.PumpMainThread();
        Assert.Equal(1, ClapTestLibrary.Snapshot()!.MainThreadCalls);
    }

    [Fact]
    public void ADisposedPlugInProcessesSilenceAndAnswersNothing()
    {
        var plugin = ClapTestLibrary.Create(false, 48000, 256, Log);
        plugin.Dispose();
        var output = Stereo(32, 7f);
        Run(plugin, Stereo(32, 1f), output, 32);
        Assert.All(output[0], v => Assert.Equal(0f, v));
        Assert.Null(plugin.GetState());
        Assert.Empty(plugin.GetParameters());
        Assert.False(plugin.SetParameter(ClapTestLibrary.GainParam, 1));
        Assert.False(plugin.HasEditor);
        Assert.Equal(0, plugin.LatencySamples);
    }

    [Fact]
    public void ClapIsChosenByExtensionOrFormatAndNeverStealsAVst3()
    {
        Assert.True(PluginFactory.IsClap(@"C:\a\Synth.clap", ""));
        Assert.True(PluginFactory.IsClap("/a/Synth.clap/", ""));
        Assert.True(PluginFactory.IsClap(@"C:\a\Whatever", "CLAP"));
        Assert.False(PluginFactory.IsClap(@"C:\a\Amp.vst3", "VST3"));
        Assert.False(PluginFactory.IsClap(@"C:\a\Old.dll", "VST2"));
        Assert.True(PluginFactory.IsVst3(@"C:\a\Amp.vst3", ""));
    }

    [Fact]
    public void AMissingClapFileFailsWithAReadableMessage()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => new ClapPlugin(Path.Combine(Path.GetTempPath(), "tf-missing-" + Guid.NewGuid().ToString("N") + ".clap"), false, 48000, 256, Log));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void AFileThatIsNotAClapLibraryIsRefused()
    {
        var file = Path.Combine(Path.GetTempPath(), "tf-notclap-" + Guid.NewGuid().ToString("N") + ".clap");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4 });
        try { Assert.ThrowsAny<Exception>(() => new ClapPlugin(file, false, 48000, 256, Log)); }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TheParameterListSurvivesTheIsolationWire()
    {
        var list = new[]
        {
            new PluginParameterInfo(7, "Gain", "Main", 0, 2, 1, 1.5, false, false),
            new PluginParameterInfo(9, new string('x', 400), "", -1, 1, 0, 0.25, true, true),
        };
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) RemotePlugin.WriteParameters(w, list);
        var back = RemotePlugin.ReadParameters(ms.ToArray());
        Assert.Equal(2, back.Count);
        Assert.Equal(list[0], back[0]);
        Assert.Equal(128, back[1].Name.Length);   // long names are cut so a reply stays small
        Assert.True(back[1].Stepped && back[1].ReadOnly);
        Assert.Throws<InvalidDataException>(() => RemotePlugin.ReadParameters(BitConverter.GetBytes(999999)));
        Assert.Equal(6, RemotePlugin.CmdListParams);   // the additive message numbers stay put: old hosts answer "unknown command"
        Assert.Equal((byte)7, RemotePlugin.CmdSetParam);
        Assert.Equal((byte)107, RemotePlugin.MsgParams);
    }
}
#endif
