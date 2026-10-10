using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TabForge.Playback;

namespace TabForge.Audio;

/// <summary>
/// How long the Windows MIDI synth (Microsoft GS Wavetable) takes from <c>midiOutShortMsg</c> to sound at the Windows mixer. TabForge holds the
/// faster of "engine plug-ins" and "Windows MIDI" back by the difference so mixed songs stay in time; a guessed constant (60 ms) left plug-in
/// drums early on some machines, so this measures it. The probe plays one very quiet percussion hit (velocity 6 on the drum channel, about 60 dB
/// below a normal hit and further reduced by the Windows path) and reads it back with WASAPI loopback of the default output in this process,
/// the same idea as <see cref="WindowsPathOffset"/>. Run on demand only (Settings, the Measure button, or --probe-midi-latency); nothing is measured while other audio is playing, and a reading is used only when at least 3 of 5 trials are clean and the median is 20-600 ms.
/// </summary>
internal static class WindowsMidiLatency
{
    public const double MinPlausibleMs = 20, MaxPlausibleMs = 600;

    /// <summary>Median of the clean trials in ms, or null when it could not be measured cleanly (something else playing, no loopback data).</summary>
    public static (double? Ms, string Detail) Measure(IMidiOutput output, int deviceId = -1, int trials = 5)
    {
        try
        {
            using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            using var capture = new WasapiLoopbackCapture(device);
            var fmt = capture.WaveFormat;
            if (fmt.Encoding is not (WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible) || fmt.BitsPerSample != 32) return (null, $"loopback format {fmt}");
            var channels = Math.Max(1, fmt.Channels);
            var rate = fmt.SampleRate;
            var gate = new object();
            var packets = new List<(long Arrived, int Frames, int FirstAudible)>();
            capture.DataAvailable += (_, e) =>
            {
                var arrived = Stopwatch.GetTimestamp();
                var frames = e.BytesRecorded / (4 * channels);
                var first = -1;
                for (var f = 0; f < frames && first < 0; f++)
                    for (var c = 0; c < channels; c++)
                        if (Math.Abs(BitConverter.ToSingle(e.Buffer, (f * channels + c) * 4)) > 1e-9f) { first = f; break; }
                lock (gate) packets.Add((arrived, frames, first));
            };
            capture.StartRecording();
            Thread.Sleep(250);
            var results = new List<double>();
            var busy = 0;
            for (var trial = 0; trial < trials; trial++)
            {
                bool Quiet() { lock (gate) return packets.All(p => p.FirstAudible < 0); }
                lock (gate) packets.Clear();
                Thread.Sleep(200);
                if (!Quiet()) { busy++; continue; }   // other audio is playing: this trial would measure it
                lock (gate) packets.Clear();
                var sent = Stopwatch.GetTimestamp();
                output.Send(deviceId, 0x99, 38, 6);
                Thread.Sleep(450);
                output.Send(deviceId, 0x89, 38, 0);
                (long Arrived, int Frames, int FirstAudible)? hit;
                lock (gate) hit = packets.Where(p => p.FirstAudible >= 0).Select(p => ((long, int, int)?)p).FirstOrDefault();
                if (hit is not { } h) continue;
                var onset = h.Item1 - (long)((h.Item2 - h.Item3) * (double)Stopwatch.Frequency / rate);   // packets arrive when they end
                var ms = (onset - sent) * 1000.0 / Stopwatch.Frequency;
                if (ms is > 0 and < MaxPlausibleMs) results.Add(ms);
                Thread.Sleep(200);
            }
            capture.StopRecording();
            if (results.Count == 0) return (null, busy > 0 ? "other audio was playing" : "no sound seen on loopback");
            if (results.Count < 3) return (null, $"only {results.Count} clean trial(s) of {trials}: {(busy > 0 ? "other audio was playing" : "the hit was not heard reliably")}");
            results.Sort();
            var median = results[results.Count / 2];
            if (median is < MinPlausibleMs or > MaxPlausibleMs) return (null, $"implausible reading {median:0.0} ms (expected {MinPlausibleMs}-{MaxPlausibleMs} ms); not stored");
            return (median, $"{results.Count} trial(s): {string.Join(", ", results.Select(r => r.ToString("0.0")))} ms; median {median:0.0} ms");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or NotSupportedException) // Not logged: probe: the message is returned to the caller
        {
            return (null, ex.Message);
        }
    }
}
