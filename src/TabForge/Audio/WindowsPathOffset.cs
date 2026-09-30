using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace TabForge.Audio;

/// <summary>
/// How much quieter the Windows audio path is than its reported volumes. ASIO bypasses Windows, so to sound exactly like the
/// Windows MIDI synth the engine applies the endpoint volume (dB), TabForge's Volume Mixer slider and this measured rest.
/// Measured on an Audient iD4: a constant -18.3 dB at every master volume, which made the engine on ASIO ~16 dB louder.
/// The measurement plays a -50 dBFS 1 kHz tone for 0.4 s through WASAPI shared in this process (TabForge's own session, where
/// the Windows synth plays) and reads it back with loopback capture: about -80 dBFS at the output, inaudible.
/// </summary>
internal static class WindowsPathOffset
{
    private const float ToneDb = -50f;

    /// <summary>Total attenuation of the last clean measurement (dB), for diagnostics.</summary>
    public static double? LastTotalDb { get; private set; }

    /// <summary>Offset in dB (total measured attenuation minus endpoint dB minus session), or null when it could not be measured cleanly.</summary>
    public static (float? OffsetDb, string Detail) Measure()
    {
        try
        {
            using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using var capture = new WasapiLoopbackCapture(device);
            var fmt = capture.WaveFormat;
            if (fmt.Encoding is not (WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible) || fmt.BitsPerSample != 32) return (null, $"loopback format {fmt}");
            var rate = fmt.SampleRate;
            var samples = new List<float>();
            var gate = new object();
            capture.DataAvailable += (_, e) =>
            {
                lock (gate)
                    for (var i = 0; i + 4 * fmt.Channels <= e.BytesRecorded; i += 4 * fmt.Channels)
                        samples.Add(BitConverter.ToSingle(e.Buffer, i));
            };
            var amplitude = MathF.Pow(10, ToneDb / 20);
            var tone = new float[rate * 2 * 4 / 10];
            for (var i = 0; i < tone.Length / 2; i++) tone[2 * i] = tone[2 * i + 1] = amplitude * MathF.Sin(2 * MathF.PI * 1000 * i / rate);
            var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)) { BufferLength = tone.Length * 4 + 4096, ReadFully = false };
            var bytes = new byte[tone.Length * 4];
            Buffer.BlockCopy(tone, 0, bytes, 0, bytes.Length);
            provider.AddSamples(bytes, 0, bytes.Length);
            capture.StartRecording();
            Thread.Sleep(150);
            float session = 1f;
            using (var output = new WasapiOut(device, AudioClientShareMode.Shared, false, 30))
            {
                output.Init(provider);
                output.Play();
                Thread.Sleep(100);
                session = SessionVolume(device);
                Thread.Sleep(550);
                output.Stop();
            }
            capture.StopRecording();
            float[] got; lock (gate) got = samples.ToArray();
            // 1 kHz amplitude per 50 ms block (a whole number of periods): the loudest block is the tone, the quietest the floor.
            var block = rate / 20;
            double best = 0, floor = double.MaxValue;
            for (var start = 0; start + block <= got.Length; start += block / 2)
            {
                double re = 0, im = 0;
                for (var i = 0; i < block; i++) { var ph = 2 * Math.PI * 1000 * i / rate; re += got[start + i] * Math.Cos(ph); im += got[start + i] * Math.Sin(ph); }
                var a = 2 * Math.Sqrt(re * re + im * im) / block;
                best = Math.Max(best, a); floor = Math.Min(floor, a);
            }
            var endpointDb = device.AudioEndpointVolume.Mute ? -200f : device.AudioEndpointVolume.MasterVolumeLevel;
            var sessionDb = 40 * Math.Log10(Math.Max(session, 1e-5));
            if (best <= 1e-9 || (floor > 0 && 20 * Math.Log10(best / floor) < 20))
                return (null, $"no clean reading (tone {20 * Math.Log10(Math.Max(best, 1e-12)):0.0} dBFS, floor {20 * Math.Log10(Math.Max(floor, 1e-12)):0.0} dBFS)");
            var total = 20 * Math.Log10(best / amplitude);
            var offset = (float)(total - endpointDb - sessionDb);
            LastTotalDb = total;
            return (offset, $"total {total:0.00} dB = endpoint {endpointDb:0.00} dB + session {sessionDb:0.00} dB + offset {offset:0.00} dB");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return (null, ex.Message);
        }
    }

    private static float SessionVolume(MMDevice device)
    {
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        var volume = 1f;
        for (var i = 0; i < sessions.Count; i++)
            if ((int)sessions[i].GetProcessID == Environment.ProcessId) volume = Math.Min(volume, sessions[i].SimpleAudioVolume.Mute ? 0f : sessions[i].SimpleAudioVolume.Volume);
        return volume;
    }
}
