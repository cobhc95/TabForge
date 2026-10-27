using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Synth;
using Xunit;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: that every instrument keeps its sound when the audio device or its mix is rebuilt (all instruments turned to acoustic piano after
///     another program took the device): the synth keeps its programs across a rate change, the session channel memory reaches every new mix, and
///     a lost output (device unplugged or invalidated, ASIO reset request) is reopened with the programs replayed into the chains.
/// Does not own: the resend after an engine restart (EngineRestartResendsSetupTests), the sound level of the synth (AudioSmokeTests).
/// Tests: GmSynth.Reconfigure, ChannelStateMemory, EngineSession.ReopenOutput.
/// </summary>
[Collection("EngineHeadless")]
public class PianoAfterDeviceRestartTests
{
    private const int Block = 4410;
    private const int Slot = 0;
    private const int Program = 30;

    private static bool Available => OperatingSystem.IsWindows() && File.Exists(DlsToSoundFont.WindowsBankPath);

    private static BlockMidi Msg(int status, int d1, int d2) => new() { Status = (byte)status, Data1 = (byte)d1, Data2 = (byte)d2 };

    private static float[] Render(GmSynth synth, params BlockMidi[] midi)
    {
        var output = new[] { new float[Block], new float[Block] };
        synth.Process(Array.Empty<float[]>(), output, Block, midi, default);
        return output[0];
    }

    [Fact]
    public void A_synth_that_follows_a_new_sample_rate_keeps_the_programs()
    {
        if (!Available) return;
        var note = Msg(0x90, 60, 100);
        using var reference = new GmSynth(48000, Block);
        var expected = Render(reference, Msg(0xC0, Program, 0), note);
        using var live = new GmSynth(44100, Block);
        Render(live, Msg(0xC0, Program, 0));
        live.Reconfigure(48000, Block);
        Assert.Equal(expected, Render(live, note));
    }

    [Fact]
    public void A_synth_reconfigured_at_the_same_sample_rate_keeps_the_programs()
    {
        if (!Available) return;
        var note = Msg(0x90, 60, 100);
        using var reference = new GmSynth(48000, Block);
        Render(reference, Msg(0xC0, Program, 0));
        var expected = Render(reference, note);
        using var live = new GmSynth(48000, Block);
        Render(live, Msg(0xC0, Program, 0));
        live.Reconfigure(48000, Block);
        Assert.Equal(expected, Render(live, note));
    }

    [Fact]
    public void A_synth_made_after_an_engine_restart_is_a_piano_until_it_is_told_otherwise()
    {
        if (!Available) return;
        var note = Msg(0x90, 60, 100);
        using var withProgram = new GmSynth(48000, Block);
        var expected = Render(withProgram, Msg(0xC0, Program, 0), note);
        using var restarted = new GmSynth(48000, Block);
        Assert.NotEqual(expected, Render(restarted, note));
    }

    [Fact]
    public void The_channel_memory_replays_bank_select_before_the_program_then_the_controllers_and_the_bend()
    {
        var memory = new ChannelStateMemory();
        foreach (var (status, d1, d2) in new[] { (0xB0, 7, 90), (0xC0, Program, 0), (0xB0, 32, 2), (0xB0, 0, 1), (0xE0, 5, 70) })
            memory.Remember(Slot, (byte)status, (byte)d1, (byte)d2);
        var instrument = new RecordingInstrument();
        using var chain = new TrackChain(Slot, -1, instrument, Array.Empty<TrackChain.Effect>(), 256);

        memory.Replay(Slot, chain);
        RenderChain(chain);

        var kinds = instrument.Heard().Select(e => (e.Status & 0xF0, (int)e.Data1)).ToList();
        var program = kinds.IndexOf((0xC0, Program));
        Assert.True(program >= 0);
        Assert.InRange(kinds.IndexOf((0xB0, 0)), 0, program - 1);    // bank select (CC0) before the program change
        Assert.InRange(kinds.IndexOf((0xB0, 32)), 0, program - 1);   // bank select (CC32) before the program change
        Assert.Contains((0xE0, 5), kinds);
    }

    [Fact]
    public void A_slot_that_was_unloaded_is_forgotten_before_its_next_chain_is_primed()
    {
        var memory = new ChannelStateMemory();
        memory.Remember(Slot, 0xC0, Program, 0);
        memory.Forget(Slot);
        memory.ApplyForget(Slot);
        var instrument = new RecordingInstrument();
        using var chain = new TrackChain(Slot, -1, instrument, Array.Empty<TrackChain.Effect>(), 256);

        memory.Replay(Slot, chain);
        RenderChain(chain);

        Assert.Empty(instrument.Heard());
    }

    [Theory]
    [InlineData("reconfigure")]   // the device settings were applied again, the rate unchanged
    [InlineData("lost")]          // WASAPI: the endpoint was invalidated or unplugged and the stream stopped with an error
    [InlineData("reset")]         // ASIO: the driver asked to be reset
    public void A_new_mix_gets_the_programs_the_song_set(string how)
    {
        using var shared = SharedBlock.Create($"tf-piano-{Guid.NewGuid():N}");
        var instrument = new RecordingInstrument();
        EH.Attach(shared, (_, _, _) => instrument);
        try
        {
            var config = new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, 256, false);
            EH.Configure(config);
            EH.LoadChain(Slot, "t", false, new List<PluginSpec> { new("recording", "VST2", true, true, 100, null, Id: "i", LocationTrusted: true) });
            Assert.True(shared.TryWrite(new TimedMidi { Timestamp = Stopwatch.GetTimestamp() - Stopwatch.Frequency, Slot = Slot, Status = 0xC0, Data1 = Program }));
            WaitUntil(() => instrument.Heard().Any(e => e.Status == 0xC0 && e.Data1 == Program), "the program change did not reach the instrument");
            var firstOutput = EH.Output;
            instrument.Forget();

            switch (how)
            {
                case "reconfigure": EH.Configure(config); break;
                case "lost": firstOutput!.LoseDevice(new IOException("AUDCLNT_E_DEVICE_INVALIDATED")); EH.Collect(); break;
                default: firstOutput!.RequestReset(); EH.Collect(); break;
            }

            if (how != "reconfigure") Assert.NotSame(firstOutput, EH.Output);   // the output was opened again
            WaitUntil(() => instrument.Heard().Any(e => e.Status == 0xC0 && e.Data1 == Program), "the new mix did not replay the program change");
        }
        finally { EH.Detach(); }
    }

    private static void WaitUntil(Func<bool> done, string failure)
    {
        var timer = Stopwatch.StartNew();
        while (!done() && timer.ElapsedMilliseconds < 5000) { EH.Pump(2); Thread.Sleep(5); }
        Assert.True(done(), failure);
    }

    private static void RenderChain(TrackChain chain)
    {
        using var shared = SharedBlock.Create($"tf-piano-{Guid.NewGuid():N}");
        var left = new float[256]; var right = new float[256];
        chain.Render(left, right, 0, 256, default, shared);
    }

    /// <summary>A VST-style instrument that records the MIDI it is given.</summary>
    private sealed class RecordingInstrument : IPluginInstance
    {
        private readonly List<BlockMidi> _heard = new();
        public string Path => "recording";
        public bool IsInstrument => true;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public List<BlockMidi> Heard() { lock (_heard) return _heard.ToList(); }
        public void Forget() { lock (_heard) _heard.Clear(); }

        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
        {
            lock (_heard) foreach (var e in midi) _heard.Add(e);
        }

        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }
}
