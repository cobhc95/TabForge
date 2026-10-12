using System.IO;
using TabForge.Audio.Contracts;
using TabForge.Models;
using EA = TabForge.AudioEngine.Audio;
using EMix = TabForge.AudioEngine.Mixing;
using EP = TabForge.AudioEngine.Plugins;

namespace TabForge;

/// <summary>Every input of the device can be addressed: wire modes, input ids, the monitor mix, the capture ring and the recorder, on a fake 8-input device.</summary>
public static partial class SelfTest
{
    private static void TestAudioInputChannels()
    {
        // Wire: the three original modes keep their meaning; any channel and any pair round-trip.
        Check("input modes 0, 1, 2 are input 1, input 2 and the pair 1+2",
            AudioInputAddress.Decode(0) == (0, 0) && AudioInputAddress.Decode(1) == (1, 1) && AudioInputAddress.Decode(2) == (0, 1)
            && AudioInputAddress.Encode(0, false) == 0 && AudioInputAddress.Encode(1, false) == 1 && AudioInputAddress.Encode(0, true) == 2);
        var roundTrips = true;
        for (var c = 0; c < AudioInputAddress.MaxChannels; c++)
            foreach (var stereo in new[] { false, true })
            {
                var mode = AudioInputAddress.Encode(c, stereo);
                roundTrips &= mode <= AudioInputAddress.MaxMode && AudioInputAddress.Decode(mode) == (c, stereo ? c + 1 : c);
            }
        Check("every mono input and every pair round-trips through the input mode", roundTrips);
        Check("mono input 5 and the pair 7+8 decode to their channels",
            AudioInputAddress.Decode(AudioInputAddress.Encode(4, false)) == (4, 4) && AudioInputAddress.Decode(AudioInputAddress.Encode(6, true)) == (6, 7));
        Check("an out-of-range mode decodes inside the channel limit", AudioInputAddress.Decode(int.MaxValue).Left < AudioInputAddress.MaxChannels && AudioInputAddress.Decode(-5) == (0, 0));

        // Ids: the stored names keep working and new ones follow the same scheme.
        Check("the stored ids map to the original modes (an old song with Input 2 arms channel 2)",
            AudioInputs.WireMode(AudioInputs.Input1) == 0 && AudioInputs.WireMode(AudioInputs.Input2) == 1 && AudioInputs.WireMode(AudioInputs.Stereo) == 2 && AudioInputs.WireMode(AudioInputs.Midi) == -1);
        Check("Input 5 and Inputs 7+8 (stereo) address channel 5 and the pair 7+8",
            AudioInputAddress.Decode(AudioInputs.WireMode("Input 5")) == (4, 4) && AudioInputAddress.Decode(AudioInputs.WireMode("Inputs 7+8 (stereo)")) == (6, 7));
        Check("malformed ids are rejected", !AudioInputs.IsValid("Input 0") && !AudioInputs.IsValid("Input 65") && !AudioInputs.IsValid("Input 03") && !AudioInputs.IsValid("Inputs 3+5 (stereo)")
            && !AudioInputs.IsValid("Inputs 64+65 (stereo)") && !AudioInputs.IsValid("") && AudioInputs.IsValid("Input 64") && AudioInputs.IsValid(AudioInputs.Midi));

        var (device, names) = (AudioInputs.Device, AudioInputs.Names.ToArray());
        try
        {
            AudioInputs.SetDevice("", Array.Empty<string>());
            Check("no device report: Input 1, Input 2 and the pair", AudioInputs.Audio.SequenceEqual(new[] { AudioInputs.Input1, AudioInputs.Input2, AudioInputs.Stereo }) && AudioInputs.All.Last() == AudioInputs.Midi);
            AudioInputs.SetDevice("Fake 8", new[] { "Mic 1", "Mic 2", "Line 3", "Line 4", "", "", "", "" });
            Check("an 8-input device lists 8 inputs and 4 pairs", AudioInputs.Audio.Length == 12 && AudioInputs.Audio[4] == "Input 5" && AudioInputs.Audio[8] == AudioInputs.Stereo && AudioInputs.Audio[11] == "Inputs 7+8 (stereo)",
                string.Join(", ", AudioInputs.Audio));
            Check("inputs show the device's names", AudioInputs.Label("Input 3") == "3: Line 3" && AudioInputs.Label("Inputs 3+4 (stereo)") == "3+4: Line 3 + Line 4" && AudioInputs.Label("Input 6") == "Input 6");
            var plugins = new TabForge.Services.PluginSettings();
            TabForge.Views.InputsShownDialog.Apply(plugins, "Fake 8", new[] { "Input 3", "Input 4" }, new[] { "Input 8" });
            var recording = AudioInputs.For(AudioInputUse.Recording, "Input 4");
            Check("an unticked input is hidden from its picker only; the selected one stays",
                !recording.Contains("Input 3") && recording.Contains("Input 4") && recording.Contains("Input 8")
                && AudioInputs.For(AudioInputUse.LiveInput).Contains("Input 3") && !AudioInputs.For(AudioInputUse.LiveInput).Contains("Input 8"));
            var copy = TabForge.Views.InputsShownDialog.Copy(plugins.HiddenInputs);
            Check("the hidden inputs are kept per device and copy without sharing lists",
                copy.Count == 1 && copy["Fake 8"].Recording.SequenceEqual(new[] { "Input 3", "Input 4" }) && copy["Fake 8"].LiveInput.SequenceEqual(new[] { "Input 8" }) && !ReferenceEquals(copy["Fake 8"].Recording, plugins.HiddenInputs["Fake 8"].Recording));
            var reloaded = System.Text.Json.JsonSerializer.Deserialize<TabForge.Services.PluginSettings>(System.Text.Json.JsonSerializer.Serialize(plugins))!;
            Check("the hidden inputs survive the settings file", reloaded.HiddenInputs["Fake 8"].Recording.Count == 2 && reloaded.HiddenInputs["Fake 8"].LiveInput.Single() == "Input 8");

            // A picker rebuild (device report, shown inputs changed) selects the same id and writes nothing.
            var written = 0; var trackInput = "Input 4";
            var picker = TabForge.Views.AudioInputPicker.Bind(new System.Windows.Controls.ComboBox(), () => trackInput);
            picker.SelectionChanged += (_, _) => { if (picker.SelectedValue is string chosen && chosen != trackInput) { trackInput = chosen; written++; } };
            AudioInputs.SetDevice("Fake 2", new[] { "", "" });
            TabForge.Views.AudioInputPicker.Refresh(picker, trackInput);
            Check("a picker rebuilt for a smaller device keeps the track's input and writes nothing",
                written == 0 && trackInput == "Input 4" && picker.SelectedValue as string == "Input 4" && (picker.SelectedItem as AudioInputChoice)?.Label == "Input 4" + AudioInputs.NotAvailable);
            Check("another device starts with every input shown", AudioInputs.For(AudioInputUse.Recording).Count == 3 && AudioInputs.For(AudioInputUse.LiveInput).Count == 3);
            TabForge.Views.InputsShownDialog.Apply(plugins, "Fake 8", Array.Empty<string>(), Array.Empty<string>());
            Check("showing every input again removes the device's entry", plugins.HiddenInputs.Count == 0);
            var changes = 0; Action counted = () => changes++;
            AudioInputs.Changed += counted;
            try { AudioInputs.SetDevice("Fake 2", new[] { "", "" }); AudioInputs.SetHidden(plugins.HiddenInputs); AudioInputs.SetHidden(plugins.HiddenInputs); }
            finally { AudioInputs.Changed -= counted; }
            Check("a device report and new choices each tell the open pickers once", changes == 2, $"{changes}");

            // Settings and songs keep a well-formed input whatever the device offers.
            var live = new TabForge.LiveInput.LiveInputSettings { Input = "Input 5" };
            live.Normalize();
            var odd = new TabForge.LiveInput.LiveInputSettings { Input = "Line 9" };
            odd.Normalize();
            Check("live input settings keep Input 5 while the device list lacks it; a malformed id falls back", live.Input == "Input 5" && odd.Input == AudioInputs.Input1);
            var song = new SongProject();
            song.Tracks.Add(new TrackModel { Kind = TrackKind.Audio, AudioInput = AudioInputs.Stereo });
            var plain = song.FormatVersion;
            song.Tracks[0].AudioInput = "Input 5";
            Check("a song that uses an input beyond 1 and 2 is written as schema 4, others as before", plain == 3 && song.FormatVersion == 4, $"{plain} {song.FormatVersion}");
            var kept = AudioInputs.Choices("Input 5");
            Check("an input the device lacks stays selected and is marked", kept.Any(c => c.Id == "Input 5" && c.Label == "Input 5" + AudioInputs.NotAvailable) && kept.Last().Id == AudioInputs.Midi && !AudioInputs.IsAvailable("Input 5"));
        }
        finally { AudioInputs.SetHidden(null); AudioInputs.SetDevice(device, names); }

        // Device report: the input list is complete also when the device cannot be asked; the one-channel ASIO set-up opens one input.
        var wasapi = new EngineConfig("WASAPI", "out", 48000, 256, false, "in");
        Check("a failing channel probe still reports two inputs; a working one reports the device's",
            TabForge.AudioEngine.Output.AudioOutputFactory.RecordableInputs(wasapi, _ => throw new InvalidCastException("probe")).Length == 2
            && TabForge.AudioEngine.Output.AudioOutputFactory.RecordableInputs(wasapi, _ => 8).Length == 8 && TabForge.AudioEngine.Output.AudioOutputFactory.RecordableInputs(wasapi, _ => 0).Length == 2);
        Check("ASIO: first = last opens that one channel (doubled to inputs 1 and 2); otherwise every channel from the first",
            TabForge.AudioEngine.Output.AudioOutputFactory.AsioInputCount(wasapi with { AsioInput = 1, AsioInputLast = 1 }, 8) == 1
            && TabForge.AudioEngine.Output.AudioOutputFactory.AsioInputCount(wasapi with { AsioInput = 0, AsioInputLast = 1 }, 8) == 8
            && TabForge.AudioEngine.Output.AudioOutputFactory.AsioInputCount(wasapi with { AsioInput = 2, AsioInputLast = 63 }, 8) == 6);

        // Monitor mix: channel k of the block carries the constant k.
        using (var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}"))
        {
            var block = new EA.InputBlock(64, 8) { Frames = 64, Channels = 8 };
            for (var c = 0; c < 8; c++) Array.Fill(block.Ch[c], c + 1);
            (float L, float R) Mix(int slot, int mode)
            {
                var chain = new EMix.TrackChain(slot, -1, null, Array.Empty<EMix.TrackChain.Effect>(), 64) { ArmMode = mode, Monitor = true };
                var l = new float[64]; var r = new float[64];
                chain.Render(l, r, 0, 64, new EP.TransportInfo { Tempo = 120 }, shared, block);
                return (l[32], r[32]);
            }
            var unity = Mix(0, 0).L;   // input 1 carries 1: the chain's own gain
            var five = Mix(1, AudioInputs.WireMode("Input 5"));
            var pair = Mix(2, AudioInputs.WireMode("Inputs 3+4 (stereo)"));
            var beyond = Mix(3, AudioInputs.WireMode("Input 9"));
            var straddle = Mix(4, AudioInputAddress.Encode(7, true));
            Check("a track armed on input 5 hears channel 5 on both sides", unity > 0 && Near(five.L, 5 * unity) && Near(five.R, 5 * unity), $"{five} at unity {unity}");
            Check("a track armed on the pair 3+4 hears 3 left and 4 right", Near(pair.L, 3 * unity) && Near(pair.R, 4 * unity), $"{pair}");
            Check("an input beyond the device is silence", beyond == (0f, 0f) && straddle == (0f, 0f), $"{beyond} {straddle}");
            Check("the original modes still read inputs 1 and 2", Near(Mix(5, 1).L, 2 * unity) && Near(Mix(6, 2).R, 2 * unity) && Near(Mix(6, 2).L, unity));
        }

        // Capture: an 8-channel feed keeps its channels through the ring and in the recorder's block.
        var capture = new EA.InputCapture("fake 8", 48000, 8, 5);
        var fed = new float[2048 * 8];
        for (var i = 0; i < fed.Length; i++) fed[i] = i % 8 + 1;
        var folder = Path.Combine(Path.GetTempPath(), $"tf-inputs-{Guid.NewGuid():N}");
        try
        {
            var recorder = new EA.Recorder(folder, new[] { (0, "Five", AudioInputs.WireMode("Input 5")), (1, "Pair", AudioInputs.WireMode("Inputs 7+8 (stereo)")), (2, "Two", 1), (3, "Gone", AudioInputs.WireMode("Input 12")) },
                0, 48000, startWriter: false, inputChannels: capture.Channels);
            capture.BlockCaptured = recorder.Enqueue;
            capture.Feed(fed);
            var read = new EA.InputBlock(256, EA.InputCapture.MaxChannels);
            capture.Read(read, 256);
            Check("the capture delivers all 8 channels to the audio thread", capture.Channels == 8 && read.Channels == 8 && Enumerable.Range(0, 8).All(c => read.Ch[c][0] == c + 1 && read.Ch[c][255] == c + 1));
            var takes = recorder.Finish();
            float[] Samples(int slot, out int channels)
            {
                using var reader = new NAudio.Wave.AudioFileReader(takes.First(t => t.Take.Slot == slot).Take.Path);
                channels = reader.WaveFormat.Channels;
                var data = new float[4096 * channels];
                return data.AsSpan(0, reader.Read(data, 0, data.Length)).ToArray();
            }
            var mono5 = Samples(0, out var c5); var pair78 = Samples(1, out var c78); var two = Samples(2, out var c2); var gone = Samples(3, out _);
            Check("the take of input 5 is mono and holds channel 5", c5 == 1 && mono5.Length == 2048 && mono5.All(v => v == 5), $"{c5} ch, {mono5.Length}");
            Check("the take of the pair 7+8 is stereo: 7 left, 8 right", c78 == 2 && pair78.Length == 4096 && Enumerable.Range(0, 2048).All(i => pair78[i * 2] == 7 && pair78[i * 2 + 1] == 8), $"{c78} ch, {pair78.Length}");
            Check("the take of input 2 holds channel 2", c2 == 1 && two.Length == 2048 && two.All(v => v == 2));
            Check("the take of an input the device lacks is silence of the same length", gone.Length == 2048 && gone.All(v => v == 0));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }

        // A one-input device is doubled, as before; resampling keeps the channels apart.
        var mono = new EA.InputCapture("fake 1", 48000, 1, 5);
        mono.Feed(Enumerable.Repeat(0.5f, 1024).ToArray());
        var monoBlock = new EA.InputBlock(64, 8);
        mono.Read(monoBlock, 64);
        Check("a one-input device feeds inputs 1 and 2", monoBlock.Channels == 2 && monoBlock.L[0] == 0.5f && monoBlock.R[0] == 0.5f);
        var slow = new EA.InputCapture("fake 5 at 24 kHz", 48000, 5, 5, sourceRate: 24000);
        var last = new float[5]; long frames = 0;
        slow.BlockCaptured = (b, n) => { frames += n; for (var c = 0; c < 5; c++) last[c] = b[(n - 1) * 5 + c]; };
        var slowFed = new float[12000 * 5];
        for (var i = 0; i < slowFed.Length; i++) slowFed[i] = i % 5 + 1;
        slow.Feed(slowFed);
        Check("a resampled 5-channel input keeps each channel's level", frames > 23000 && Enumerable.Range(0, 5).All(c => Math.Abs(last[c] - (c + 1)) < 0.01f), $"{frames} frames, {string.Join(" ", last)}");

        static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f * Math.Max(1f, Math.Abs(b));
    }
}
