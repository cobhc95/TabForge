using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: that a plug-in gets exactly the MIDI its Wiring window selects. MIDI input Source None means no events at all (notes, CC, bend,
///     program change), while the stream still passes on to the next plug-in when "Pass incoming MIDI" is on; a channel filter blocks other channels;
///     the choice reaches the engine as the wiring flags of the plug-in and also holds for live (monitored) MIDI.
/// Does not own: the Wiring window (WiringWindowTests), the instrument routing from another track.
/// Tests: TrackChain.RunEffect / SetPluginWiring, AudioEngineClient wiring sync, the headless engine live MIDI ring.
/// </summary>
[Collection("EngineHeadless")]
public class PluginMidiSourceTests
{
    private const int None = 8;
    private const int Defaults = 3;   // pass incoming MIDI on, MIDI output on

    private static BlockMidi Note(int channel = 0) => new() { Status = (byte)(0x90 | channel), Data1 = 60, Data2 = 100 };

    private static void Render(TrackChain chain, params BlockMidi[] events)
    {
        using var shared = SharedBlock.Create($"tf-midisrc-{Guid.NewGuid():N}");
        foreach (var e in events) chain.AddEvent(0, e.Status, e.Data1, e.Data2);
        chain.Render(new float[256], new float[256], 0, 256, default, shared);
    }

    private static TrackChain Chain(Recorder first, Recorder second, int instrumentIndex)
    {
        var effects = new List<TrackChain.Effect>
        {
            new(first, 1f, 0, IsInstrument: first.IsInstrument),
            new(second, 1f, 1, IsInstrument: second.IsInstrument),
        };
        return new TrackChain(0, instrumentIndex, null, effects, 256);
    }

    [Fact]
    public void An_effect_with_source_none_hears_nothing_while_the_instrument_before_it_plays()
    {
        var inst = new Recorder(true); var fx = new Recorder(false);
        using var chain = Chain(inst, fx, 0);
        chain.SetPluginWiring(1, Defaults | None);
        Render(chain, Note());
        Assert.Single(inst.Heard);
        Assert.Empty(fx.Heard);
    }

    [Fact]
    public void An_effect_with_source_none_before_the_instrument_hears_nothing_and_the_instrument_still_plays()
    {
        var fx = new Recorder(false); var inst = new Recorder(true);
        using var chain = Chain(fx, inst, 1);
        chain.SetPluginWiring(0, Defaults | None);
        Render(chain, Note());
        Assert.Empty(fx.Heard);
        Assert.Single(inst.Heard);
    }

    [Fact]
    public void Source_none_hears_no_controller_bend_or_program_change_either()
    {
        var inst = new Recorder(true); var fx = new Recorder(false);
        using var chain = Chain(inst, fx, 0);
        chain.SetPluginWiring(1, Defaults | None);
        Render(chain, new BlockMidi { Status = 0xB1, Data1 = 1, Data2 = 5 }, new BlockMidi { Status = 0xE0, Data1 = 0, Data2 = 70 }, new BlockMidi { Status = 0xC0, Data1 = 9 }, Note());
        Assert.Equal(4, inst.Heard.Count);
        Assert.Empty(fx.Heard);
    }

    [Fact]
    public void An_effect_with_the_own_track_as_source_hears_the_notes()
    {
        var inst = new Recorder(true); var fx = new Recorder(false);
        using var chain = Chain(inst, fx, 0);
        chain.SetPluginWiring(1, Defaults);
        Render(chain, Note());
        Assert.Single(inst.Heard);
        Assert.Single(fx.Heard);
    }

    [Fact]
    public void A_plugin_that_does_not_pass_incoming_midi_on_leaves_the_next_one_without_events()
    {
        var inst = new Recorder(true); var fx = new Recorder(false);
        using var chain = Chain(inst, fx, 0);
        chain.SetPluginWiring(0, 2);   // pass off, output on
        Render(chain, Note());
        Assert.Single(inst.Heard);
        Assert.Empty(fx.Heard);
    }

    [Fact]
    public void Source_none_with_pass_on_lets_the_stream_reach_a_later_plugin()
    {
        var a = new Recorder(false); var b = new Recorder(false);
        using var chain = Chain(a, b, -1);
        chain.SetPluginWiring(0, Defaults | None);
        Render(chain, Note());
        Assert.Empty(a.Heard);
        Assert.Single(b.Heard);
    }

    [Fact]
    public void A_channel_filter_blocks_the_other_channels_for_that_plugin_only()
    {
        var inst = new Recorder(true); var fx = new Recorder(false);
        using var chain = Chain(inst, fx, 0);
        chain.SetPluginWiring(1, Defaults | ((~(1 << 2) & 0xFFFF) << 16));   // only channel 3
        Render(chain, Note(0), Note(2));
        Assert.Equal(2, inst.Heard.Count);
        Assert.Equal(new[] { 0x92 }, fx.Heard.Select(e => (int)e.Status));
    }

    [Fact]
    public void Live_monitored_midi_respects_source_none_through_the_engine()
    {
        using var shared = SharedBlock.Create($"tf-midisrc-{Guid.NewGuid():N}");
        var plugins = new Dictionary<string, Recorder> { ["inst"] = new(true), ["fx"] = new(false) };
        EH.Attach(shared, (spec, _, _) => plugins[spec.Path]);
        try
        {
            EH.Configure(new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, 256, false));
            EH.LoadChain(0, "t", false, new List<PluginSpec>
            {
                new("inst", "VST2", true, true, 100, null, Id: "i", LocationTrusted: true),
                new("fx", "VST2", false, true, 100, null, Id: "f", LocationTrusted: true),
            });
            Assert.True(EH.Command(EngineCommand.SetPluginWiring, new SetPluginWiringMessage(0, 1, Defaults | None).Write));
            EH.Collect();
            Assert.True(shared.TryWrite(new TimedMidi { Timestamp = Stopwatch.GetTimestamp() - Stopwatch.Frequency, Slot = 0, Status = 0x90, Data1 = 64, Data2 = 90 }));
            var timer = Stopwatch.StartNew();
            while (plugins["inst"].Heard.Count == 0 && timer.ElapsedMilliseconds < 5000) { EH.Pump(2); Thread.Sleep(5); }
            Assert.NotEmpty(plugins["inst"].Heard);
            EH.Pump(4);
            Assert.Empty(plugins["fx"].Heard);
        }
        finally { EH.Detach(); }
    }

    [Fact]
    public void Changing_the_midi_source_of_a_plugin_to_none_sends_the_wiring_to_the_engine()
    {
        var client = new AudioEngineClient(new PluginTrust());
        client.AttachFakeForTest();
        var song = TemplateFactory.Blank();
        var track = NewTrackFactory.CreateTrack(song, TrackKind.Guitar);
        track.SoundSource = SoundSources.Plugins;
        track.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = @"C:\NoSuch\Synth.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
        var fx = new PluginSlot { Name = "Limiter", Path = @"C:\NoSuch\Limiter.vst3", Format = "VST3", Type = PluginSlotType.Effect };
        track.Rig.Plugins.Add(fx);
        song.Tracks.Add(track);
        client.Sync(song.Tracks, new PluginSettings(), null, song, new object());
        var sent = new List<EngineCommand>();
        client.SentForTest = sent.Add;
        try
        {
            client.Sync(song.Tracks, new PluginSettings(), null, song, new object());
            Assert.DoesNotContain(EngineCommand.SetPluginWiring, sent);

            fx.MidiIn = new PluginMidiIn { Source = PluginMidiIn.None };
            client.Sync(song.Tracks, new PluginSettings(), null, song, new object());
            Assert.Contains(EngineCommand.SetPluginWiring, sent);
        }
        finally { client.SentForTest = null; }
    }

    /// <summary>A plug-in that records the MIDI it is given.</summary>
    private sealed class Recorder : IPluginInstance
    {
        private readonly bool _instrument;
        public Recorder(bool instrument) => _instrument = instrument;
        public List<BlockMidi> Heard { get; } = new();
        public string Path => _instrument ? "inst" : "fx";
        public bool IsInstrument => _instrument;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
        {
            lock (Heard) foreach (var e in midi) Heard.Add(e);
        }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }
}
