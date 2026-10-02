using TabForge.Audio.Contracts;
using System.IO;
using System.Text;
using NAudio.Midi;
using NAudio.Wave;
using TabForge.AudioEngine.Synth;

namespace TabForge.Diagnostics;

/// <summary>
/// `TabForge.exe --level-match &lt;report&gt; [engine-only]`: plays short test notes through the Microsoft GS Wavetable Synth
/// (winmm) while capturing the default render device with WASAPI loopback, renders the same notes offline through the
/// engine's GM synth, and reports the level difference (engine minus GS, dB) per program, per drum note, per velocity
/// and for the CC7 / CC11 curves. With "engine-only" the GS side is skipped (no speakers).
/// </summary>
internal static class LevelMatch
{
    private sealed record Test(string Group, string Name, int Channel, int Program, int Note, int Velocity, int Cc7 = 100, int Cc11 = 127);

    public static readonly int[] Velocities = { 40, 64, 90, 110, 127 };

    /// <summary>Attack (first 100 ms), tail (400..1200 ms) RMS in dBFS and pan (right minus left RMS, dB) from onset.</summary>
    private static (double Attack, double Tail, double Pan) Shape(float[] stereo)
    {
        // onset relative to the peak (-30 dB): an absolute threshold found soft attacks later after the capture path's attenuation
        var top = 0f; foreach (var x in stereo) top = Math.Max(top, Math.Abs(x));
        var onset = top <= 0 ? -1 : Array.FindIndex(stereo, x => Math.Abs(x) > top * 0.0316f);
        if (onset < 0) return (-120, -120, 0);
        onset &= ~1;
        double Rms(int fromMs, int toMs, int channel)
        {
            double sum = 0; var n = 0;
            for (var i = onset + fromMs * 96 + channel; i < Math.Min(stereo.Length, onset + toMs * 96); i += channel < 0 ? 1 : 2) { sum += stereo[Math.Max(0, i)] * (double)stereo[Math.Max(0, i)]; n++; }
            return 10 * Math.Log10(Math.Max(sum / Math.Max(1, n), 1e-12));
        }
        return (Rms(0, 100, -1), Rms(400, 1200, -1), Rms(0, 400, 1) - Rms(0, 400, 0));
    }

    private static readonly Dictionary<Test, (double Attack, double Tail, double Pan)> GsShape = new();

    private static List<Test> Tests()
    {
        var list = new List<Test>();
        foreach (var p in new[] { 0, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 48, 65 })
            foreach (var v in Velocities)
                list.Add(new Test("program", $"prog {p}", 0, p, p is >= 32 and <= 39 ? 40 : p is >= 24 and <= 31 ? 52 : 60, v));
        for (var n = 35; n <= 59; n++)
            foreach (var v in Velocities)
                list.Add(new Test("drum", $"drum {n}", 9, 0, n, v));
        foreach (var cc in new[] { 127, 120, 110, 100, 64, 32 }) list.Add(new Test("cc7", $"CC7 {cc}", 0, 0, 60, 100, Cc7: cc));
        foreach (var cc in new[] { 100, 64, 32 }) list.Add(new Test("cc11", $"CC11 {cc}", 0, 0, 60, 100, Cc7: 127, Cc11: cc));
        return list;
    }

    private static IEnumerable<(double Ms, int Status, int D1, int D2)> Events(Test t)
    {
        yield return (0, 0xB0 | t.Channel, 121, 0);
        if (t.Channel != 9) yield return (0, 0xC0 | t.Channel, t.Program, 0);
        yield return (0, 0xB0 | t.Channel, 7, t.Cc7);
        yield return (0, 0xB0 | t.Channel, 11, t.Cc11);
        yield return (0, 0xB0 | t.Channel, 10, 64);
        yield return (50, 0x90 | t.Channel, t.Note, t.Velocity);
        yield return (650, 0x80 | t.Channel, t.Note, 0);
    }

    /// <summary>RMS (over 400 ms from onset) and peak, in dBFS, of interleaved stereo.</summary>
    private static (double Rms, double Peak) Measure(float[] stereo)
    {
        // onset relative to the peak (-30 dB): an absolute threshold found soft attacks later after the capture path's attenuation
        var top = 0f; foreach (var x in stereo) top = Math.Max(top, Math.Abs(x));
        var onset = top <= 0 ? -1 : Array.FindIndex(stereo, x => Math.Abs(x) > top * 0.0316f);
        if (onset < 0) return (-120, -120);
        onset &= ~1;
        var n = Math.Min(stereo.Length - onset, 48000 * 2 * 400 / 1000);
        double sum = 0, peak = 0;
        for (var i = onset; i < onset + n; i++) { sum += stereo[i] * (double)stereo[i]; peak = Math.Max(peak, Math.Abs(stereo[i])); }
        return (10 * Math.Log10(Math.Max(sum / Math.Max(1, n), 1e-12)), Gain.ToDb(Math.Max(peak, 1e-6)));
    }

    private static (double Rms, double Peak) Engine(Test t) => Measure(EngineRender(t));

    private static float[] EngineRender(Test t)
    {
        var (l, r) = GmSynthProbe.Render(Events(t).Select(e => new GmSynthProbe.Event(e.Ms, e.Status, e.D1, e.D2)), 1400);
        var s = new float[l.Length * 2];
        for (var i = 0; i < l.Length; i++) { s[2 * i] = l[i]; s[2 * i + 1] = r[i]; }
        return s;
    }

    private static Dictionary<Test, (double, double)> Gs(List<Test> tests, StringBuilder log)
    {
        var result = new Dictionary<Test, (double, double)>();
        var device = Enumerable.Range(0, MidiOut.NumberOfDevices).FirstOrDefault(i => MidiOut.DeviceInfo(i).ProductName.Contains("GS Wavetable", StringComparison.OrdinalIgnoreCase));
        using var midi = new MidiOut(device);
        using var capture = new WasapiLoopbackCapture();
        var fmt = capture.WaveFormat;
        log.AppendLine($"INFO  GS device {device} '{MidiOut.DeviceInfo(device).ProductName}', loopback {fmt.SampleRate} Hz {fmt.Channels} ch {fmt.Encoding} {fmt.BitsPerSample} bit");
        var buffer = new List<float>();
        var gate = new object();
        capture.DataAvailable += (_, e) =>
        {
            if (fmt.Encoding != WaveFormatEncoding.IeeeFloat && fmt.Encoding != WaveFormatEncoding.Extensible) return;
            lock (gate)
                for (var i = 0; i + 4 * fmt.Channels <= e.BytesRecorded; i += 4 * fmt.Channels)
                { buffer.Add(BitConverter.ToSingle(e.Buffer, i)); buffer.Add(fmt.Channels > 1 ? BitConverter.ToSingle(e.Buffer, i + 4) : BitConverter.ToSingle(e.Buffer, i)); }
        };
        capture.StartRecording();
        void Send(int s, int d1, int d2) => midi.Send(s | (d1 << 8) | (d2 << 16));
        // wake the synth up (its first note can be late / clipped)
        Send(0x90, 60, 1); Thread.Sleep(300); Send(0x80, 60, 0); Thread.Sleep(500);
        foreach (var t in tests)
        {
            for (var c = 0; c < 16; c++) Send(0xB0 | c, 120, 0);
            Thread.Sleep(t.Channel == 9 ? 450 : 150);   // cymbal tails (GS may ignore CC120) must not reach the next test
            lock (gate) buffer.Clear();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var e in Events(t))
            {
                var wait = (int)(e.Ms - clock.Elapsed.TotalMilliseconds);
                if (wait > 0) Thread.Sleep(wait);
                Send(e.Status, e.D1, e.D2);
            }
            Thread.Sleep(700);
            float[] got; lock (gate) got = buffer.ToArray();
            if (fmt.SampleRate != 48000) log.AppendLine("WARN  loopback rate is not 48 kHz: RMS window length differs slightly");
            result[t] = Measure(got);
            GsShape[t] = Shape(got);
        }
        // Path reference: the capture can include the device / session volume, so play engine renders through the same
        // default device (WASAPI shared) and measure them the same way. offset = loopback - offline; GS values are
        // reported on the offline (engine output) scale.
        var offsets = new List<double>();
        foreach (var t in tests.Where(t => t.Velocity == 90 && (t.Name is "prog 0" or "prog 25" or "drum 38")))
        {
            var (l, r) = GmSynthProbe.Render(Events(t).Select(e => new GmSynthProbe.Event(e.Ms, e.Status, e.D1, e.D2)), 1100);
            var stereo = new float[l.Length * 2];
            for (var i = 0; i < l.Length; i++) { stereo[2 * i] = l[i]; stereo[2 * i + 1] = r[i]; }
            var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { BufferLength = stereo.Length * 4 + 4096, ReadFully = false };
            var bytes = new byte[stereo.Length * 4]; Buffer.BlockCopy(stereo, 0, bytes, 0, bytes.Length);
            provider.AddSamples(bytes, 0, bytes.Length);
            Thread.Sleep(150);
            lock (gate) buffer.Clear();
            using (var output = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 50))
            {
                output.Init(provider); output.Play();
                Thread.Sleep(1500);
                output.Stop();
            }
            float[] got; lock (gate) got = buffer.ToArray();
            var offset = Measure(got).Rms - Measure(stereo).Rms;
            offsets.Add(offset);
            log.AppendLine($"INFO  path reference {t.Name}: loopback minus offline {offset:+0.00;-0.00} dB");
        }
        capture.StopRecording();
        // The per-sound reference above depends on each sound's onset; a steady tone gives the exact path attenuation.
        var tone = TabForge.Audio.WindowsPathOffset.Measure();
        log.AppendLine($"INFO  tone path measurement: {tone.Detail}");
        var pathOffset = TabForge.Audio.WindowsPathOffset.LastTotalDb ?? offsets.Average();
        log.AppendLine($"INFO  capture path offset {pathOffset:+0.00;-0.00} dB (removed from the GS figures below)");
        foreach (var t in tests)
        {
            result[t] = (result[t].Item1 - pathOffset, result[t].Item2 - pathOffset);
            if (GsShape.TryGetValue(t, out var sh)) GsShape[t] = (sh.Attack - pathOffset, sh.Tail - pathOffset, sh.Pan);
        }
        return result;
    }

    /// <summary>Starts capturing <paramref name="capture"/> into a stereo float buffer.</summary>
    private static (List<float> Buffer, object Gate) Record(NAudio.CoreAudioApi.WasapiCapture capture)
    {
        var fmt = capture.WaveFormat;
        var buffer = new List<float>();
        var gate = new object();
        capture.DataAvailable += (_, e) =>
        {
            if (fmt.Encoding != WaveFormatEncoding.IeeeFloat && fmt.Encoding != WaveFormatEncoding.Extensible) return;
            lock (gate)
                for (var i = 0; i + 4 * fmt.Channels <= e.BytesRecorded; i += 4 * fmt.Channels)
                { buffer.Add(BitConverter.ToSingle(e.Buffer, i)); buffer.Add(fmt.Channels > 1 ? BitConverter.ToSingle(e.Buffer, i + 4) : BitConverter.ToSingle(e.Buffer, i)); }
        };
        capture.StartRecording();
        return (buffer, gate);
    }

    /// <summary>
    /// `--level-match &lt;report&gt; volume`: how much Windows really attenuates what it plays (the Windows MIDI synth's
    /// path), measured with a 1 kHz tone through WASAPI shared + loopback capture, against the endpoint's reported
    /// MasterVolumeLevel (dB) and scalar, at a few master volumes (restored afterwards) and for this app's session volume.
    /// ASIO bypasses all of this, so the engine must apply the same attenuation itself (EngineHost.FollowWindowsVolume).
    /// </summary>
    private static int RunVolumeLaw(string path)
    {
        var log = new StringBuilder();
        var render = new NAudio.CoreAudioApi.MMDeviceEnumerator().GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
        var ev = render.AudioEndpointVolume;
        log.AppendLine($"INFO  endpoint {render.FriendlyName}: range {ev.VolumeRange.MinDecibels}..{ev.VolumeRange.MaxDecibels} dB step {ev.VolumeRange.IncrementDecibels}, hardware volume 0x{ev.HardwareSupport:X}");
        using var capture = new WasapiLoopbackCapture();
        var (buffer, gate) = Record(capture);
        var tone = new float[48000 * 2];
        for (var i = 0; i < tone.Length / 2; i++) tone[2 * i] = tone[2 * i + 1] = 0.1f * (float)Math.Sin(2 * Math.PI * 1000 * i / 48000.0);
        var reference = Measure(tone).Rms;
        double Play(float session)
        {
            var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) { BufferLength = tone.Length * 4 + 4096, ReadFully = false };
            var bytes = new byte[tone.Length * 4]; Buffer.BlockCopy(tone, 0, bytes, 0, bytes.Length);
            provider.AddSamples(bytes, 0, bytes.Length);
            Thread.Sleep(200);
            lock (gate) buffer.Clear();
            using (var output = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 50))
            {
                output.Init(provider);
                var manager = render.AudioSessionManager; manager.RefreshSessions();
                for (var i = 0; i < manager.Sessions.Count; i++)
                    if ((int)manager.Sessions[i].GetProcessID == Environment.ProcessId) manager.Sessions[i].SimpleAudioVolume.Volume = session;
                output.Play(); Thread.Sleep(1300); output.Stop();
                for (var i = 0; i < manager.Sessions.Count; i++)
                    if ((int)manager.Sessions[i].GetProcessID == Environment.ProcessId) manager.Sessions[i].SimpleAudioVolume.Volume = 1f;
            }
            float[] got; lock (gate) got = buffer.ToArray();
            return Measure(got).Rms - reference;
        }
        var saved = ev.MasterVolumeLevelScalar; var savedMute = ev.Mute;
        log.AppendLine("master scalar | reported dB | scalar² dB | measured dB | measured - reported");
        try
        {
            ev.Mute = false;
            foreach (var s in new[] { saved, 1f, 0.8f, 0.6f, 0.4f, 0.2f, 0.1f })
            {
                ev.MasterVolumeLevelScalar = s; Thread.Sleep(150);
                var reported = ev.MasterVolumeLevel; var measured = Play(1f);
                log.AppendLine($"{ev.MasterVolumeLevelScalar:0.000} | {reported:0.00} | {40 * Math.Log10(Math.Max(s, 1e-6)):0.00} | {measured:0.00} | {measured - reported:+0.00;-0.00}");
            }
            ev.MasterVolumeLevelScalar = 1f; Thread.Sleep(150);
            var full = Play(1f);
            foreach (var v in new[] { 0.5f, 0.25f })
            {
                var d = Play(v) - full;
                log.AppendLine($"session volume {v:0.00}: {d:0.00} dB (linear amplitude {Gain.ToDb(v):0.00}, squared {40 * Math.Log10(v):0.00})");
            }
        }
        finally { ev.MasterVolumeLevelScalar = saved; ev.Mute = savedMute; capture.StopRecording(); }
        Thread.Sleep(300);
        log.AppendLine($"INFO  TabForge path measurement (-50 dBFS tone, used under ASIO): {TabForge.Audio.WindowsPathOffset.Measure().Detail}");
        File.WriteAllText(path, log.ToString());
        Console.Out.WriteLine(log.ToString());
        return 0;
    }

    public static int Run(string[] args)
    {
        var path = args.Length > 1 ? args[1] : "level-match.txt";
        if (args.Any(a => a.Equals("volume", StringComparison.OrdinalIgnoreCase))) return RunVolumeLaw(path);
        var engineOnly = args.Any(a => a.Equals("engine-only", StringComparison.OrdinalIgnoreCase));
        var log = new StringBuilder();
        var tests = Tests();
        var gs = engineOnly ? null : Gs(tests, log);
        {
            var saved = GmSynthTuning.Calibrate; GmSynthTuning.Calibrate = false;
            var bank = DlsToSoundFont.LoadWindowsBank();
            GmSynthTuning.Calibrate = saved;
            foreach (var (b, prog, key) in new[] { (128, 0, 35), (128, 0, 36), (128, 0, 38), (128, 0, 42), (128, 0, 49), (0, 33, 40), (0, 0, 60), (0, 29, 52) })
            {
                var preset = bank.Presets.FirstOrDefault(x => x.BankNumber == b && x.PatchNumber == prog);
                if (preset == null) continue;
                foreach (var ir in preset.Regions.SelectMany(pr => pr.Instrument.Regions).Where(ir => ir.KeyRangeStart <= key && key <= ir.KeyRangeEnd))
                    log.AppendLine($"INFO  bank {b} prog {prog} key {key}: vel {ir.VelocityRangeStart}-{ir.VelocityRangeEnd} initialAttenuation {ir.InitialAttenuation:0.0} filterFc {ir.InitialFilterCutoffFrequency:0} Q {ir.InitialFilterQ:0.0} sample '{ir.Sample.Name}' rate {ir.Sample.SampleRate}");
            }
        }
        var calibrated = GmSynthTuning.Calibrate;
        GmSynthTuning.Calibrate = false;
        var raw = tests.ToDictionary(t => t, Engine);
        GmSynthTuning.Calibrate = true;
        var cal = tests.ToDictionary(t => t, Engine);
        var calShape = tests.ToDictionary(t => t, t => Shape(EngineRender(t)));
        GmSynthTuning.Calibrate = calibrated;
        log.AppendLine("test | vel | GS rms | GS peak | engine raw rms (diff) | engine calibrated rms (diff) | cal peak diff | cal attack diff | cal tail diff | pan GS / engine (R-L dB)");
        foreach (var t in tests)
        {
            var (gr, gp) = gs?[t] ?? (double.NaN, double.NaN);
            log.AppendLine($"{t.Name} | {t.Velocity} | {gr:0.0} | {gp:0.0} | {raw[t].Rms:0.0} ({raw[t].Rms - gr:+0.0;-0.0}) | {cal[t].Rms:0.0} ({cal[t].Rms - gr:+0.0;-0.0}) | {cal[t].Peak - gp:+0.0;-0.0} | {calShape[t].Attack - GsShape.GetValueOrDefault(t).Attack:+0.0;-0.0} | {calShape[t].Tail - GsShape.GetValueOrDefault(t).Tail:+0.0;-0.0} | {GsShape.GetValueOrDefault(t).Pan:+0.0;-0.0} / {calShape[t].Pan:+0.0;-0.0}");
        }
        if (gs != null)
        {
            foreach (var name in tests.Select(t => t.Name).Distinct())
            {
                var set = tests.Where(t => t.Name == name && gs[t].Item1 > -90).ToList();
                if (set.Count == 0) continue;
                log.AppendLine($"SUMMARY {name}: raw {set.Average(t => raw[t].Rms - gs[t].Item1):+0.0;-0.0} dB, calibrated {set.Average(t => cal[t].Rms - gs[t].Item1):+0.0;-0.0} dB");
            }
            foreach (var v in Velocities)
            {
                var set = tests.Where(t => t.Group is "program" or "drum" && t.Velocity == v && gs[t].Item1 > -90).ToList();
                log.AppendLine($"VELOCITY {v}: raw {set.Average(t => raw[t].Rms - gs[t].Item1):+0.0;-0.0} dB, calibrated {set.Average(t => cal[t].Rms - gs[t].Item1):+0.0;-0.0} dB");
            }
            var all = tests.Where(t => t.Group is "program" or "drum" && gs[t].Item1 > -90).ToList();
            log.AppendLine($"OVERALL raw {all.Average(t => raw[t].Rms - gs[t].Item1):+0.00;-0.00} dB, calibrated {all.Average(t => cal[t].Rms - gs[t].Item1):+0.00;-0.00} dB; calibrated max |diff| {all.Max(t => Math.Abs(cal[t].Rms - gs[t].Item1)):0.0} dB");
        }
        File.WriteAllText(path, log.ToString());
        Console.Out.WriteLine(log.ToString());
        return 0;
    }
}
