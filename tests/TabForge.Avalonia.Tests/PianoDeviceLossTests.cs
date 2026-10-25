using System;
using System.IO;
using System.Linq;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using TabForge.AudioEngine.Plugins;
using TabForge.AudioEngine.Synth;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the output-loss signal (debounced, silent after dispose), the order the channel memory replays a registered parameter in,
///     and a synth whose programs were never set keeping its defaults (drum kit) across the replay.
/// Does not own: the reopen itself (PianoAfterDeviceRestartTests).
/// Tests: OutputLossWatch, ChannelStateMemory.Replay, GmSynth.SetOfflineMode.
/// </summary>
public class PianoDeviceLossTests
{
    private static NullOutput NewOutput() => new(new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)).ToSampleProvider(), 256, true);

    [Fact]
    public void A_burst_of_loss_signals_raises_one_loss()
    {
        using var output = NewOutput();
        var raised = 0;
        using var watch = new OutputLossWatch(output, false, _ => raised++);
        output.RequestReset();
        output.LoseDevice(new IOException("invalidated"));
        output.RequestReset();
        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_disposed_watch_ignores_a_late_event_from_its_output()
    {
        using var output = NewOutput();
        var raised = 0;
        var watch = new OutputLossWatch(output, false, _ => raised++);
        watch.Dispose();
        output.RequestReset();
        output.LoseDevice(new IOException("late"));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_registered_parameter_is_replayed_number_first_then_the_data_entry()
    {
        var memory = new ChannelStateMemory();
        foreach (var (cc, value) in new[] { (38, 1), (6, 12), (100, 0), (101, 0), (11, 90) })
            memory.Remember(0, 0xB0, (byte)cc, (byte)value);
        var instrument = new Recorder();
        using var chain = new TrackChain(0, -1, instrument, Array.Empty<TrackChain.Effect>(), 256);

        memory.Replay(0, chain);
        using var shared = SharedBlock.Create($"tf-rpn-{Guid.NewGuid():N}");
        chain.Render(new float[256], new float[256], 0, 256, default, shared);

        var order = instrument.Controllers;
        Assert.Equal(new[] { 101, 100, 6, 38 }, order.Where(c => c is 6 or 38 or 100 or 101).ToArray());
        Assert.Contains(11, order);
    }

    [Fact]
    public void A_synth_that_never_got_a_program_keeps_its_drum_kit_after_an_offline_round_trip()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(DlsToSoundFont.WindowsBankPath)) return;
        var hit = new BlockMidi { Status = 0x99, Data1 = 38, Data2 = 100 };
        using var reference = new GmSynth(48000, 4410);
        using var trip = new GmSynth(48000, 4410);
        trip.SetOfflineMode(true);
        trip.SetOfflineMode(false);
        Assert.Equal(Render(reference, hit), Render(trip, hit));
    }

    private static float[] Render(GmSynth synth, BlockMidi note)
    {
        var output = new[] { new float[4410], new float[4410] };
        synth.Process(Array.Empty<float[]>(), output, 4410, new[] { note }, default);
        return output[0];
    }

    private sealed class Recorder : IPluginInstance
    {
        private readonly System.Collections.Generic.List<int> _controllers = new();
        public int[] Controllers { get { lock (_controllers) return _controllers.ToArray(); } }
        public string Path => "recorder";
        public bool IsInstrument => true;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
        {
            lock (_controllers) foreach (var e in midi) if ((e.Status & 0xF0) == 0xB0) _controllers.Add(e.Data1);
        }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }
}
