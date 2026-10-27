using System.Diagnostics;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Output;

namespace TabForge.AudioEngine;

// Owns: the engine session's audio input, arms and recording, split from EngineSession.cs.
// Does not own: the other parts of the engine session (EngineSession).
internal sealed partial class EngineSession
{
    // ---- Input and recording ----

    /// <summary>Opens the audio input while any track is armed (and closes it when none is).</summary>
    private void ApplyArms()
    {
        AssertMain();
        foreach (var (slot, chain) in Loaded) { chain.ArmMode = Arms.TryGetValue(slot, out var mode) ? mode : -1; chain.Monitor = !Unmonitored.Contains(slot); }
        if (Arms.Count > 0 && Input is null && Mix is not null)
        {
            try
            {
                // ASIO: the driver delivers the input in its own callback (no second, competing device).
                Input = PlayerIsAsio && _asioBufferFrames > 0
                    // The driver-reported input latency (ASIOGetLatencies, it includes the buffer) when there is one.
                    ? new Audio.InputCapture(AsioInputLabel(), SampleRate, Math.Max(1, AudioOutputFactory.AsioInputChannelsUsed),
                        Math.Max(1, (int)Math.Round((AudioOutputFactory.AsioInputLatencyFrames > 0 ? AudioOutputFactory.AsioInputLatencyFrames : _asioBufferFrames) * 1000.0 / SampleRate)))
                    : AudioBackends.For(Config).OpenInput(Config, SampleRate, Log);
                Mix.Input = Input;
                Log.Write($"input opened: {Input.DeviceName}, {Input.Channels} channel(s), {Input.SourceRate} Hz" + (Input.SourceRate != SampleRate ? $" (resampled to {SampleRate} Hz)" : "") +
                                $", input latency {Input.LatencyMs} ms ({(AudioOutputFactory.AsioInputLatencyFrames > 0 && PlayerIsAsio ? "ASIO driver" : Input.LatencySource)})");
            }
            catch (Exception ex)
            {
                Log.Write($"input failed: {ex}");
                Send(EngineEvent.InputError, w => w.WriteString(ex.GetBaseException().Message));
            }
        }
        else if (Arms.Count == 0 && Input is not null)
        {
            if (Recorder is not null) StopRecording();
            if (Mix is not null) Mix.Input = null;
            Input.Dispose();
            Input = null;
        }
    }

    private void StartRecording(string folder, Dictionary<int, string> names, double offsetMs, int owner, double? songSecAtRead = null)
    {
        AssertMain();
        if (Recorder is not null || Input is null || Mix is null) return;
        var armed = Arms.Where(a => names.ContainsKey(a.Key)).Select(a => (a.Key, names[a.Key], a.Value)).ToList();
        if (armed.Count == 0) return;
        Log.Write($"recording aligned: input latency {Input.LatencyMs} ms, user offset {offsetMs:0.#} ms");
        try
        {
            // The start is read after the recorder is built (its queue allocation takes longer the more inputs are recorded) and right before the capture is attached.
            var recorder = Recorder = new Audio.Recorder(folder, armed, 0, SampleRate, Log, inputChannels: Input.Channels);
            recorder.StartSec = Math.Max(0, Audio.TakeAlignment.StartSec(songSecAtRead ?? Mix.SongSecFor(owner), Mix.DelayTicks / (double)Stopwatch.Frequency, Input.LatencyMs, offsetMs));
            recorder.Error += msg => Send(EngineEvent.InputError, w => w.WriteString($"Recording problem: {msg}"));
            Input.BlockCaptured = recorder.Enqueue;
            Log.Write($"recording {armed.Count} track(s) into {folder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Recorder = null;
            Send(EngineEvent.InputError, w => w.WriteString($"Recording could not start: {ex.Message}"));
        }
    }

    /// <summary>Takes still being written after Stop (main thread only); Shutdown finishes them so no file is left without its header.</summary>
    private readonly List<Audio.Recorder> _draining = new();

    /// <param name="wait">True (shutdown): close the files here. False: the disk thread finishes the take and sends the events, so a
    /// slow disk with a long queue never holds the main thread past TabForge's 5 s liveness limit (which would end the engine and lose the take).</param>
    private void StopRecording(bool wait = false)
    {
        AssertMain();
        if (Recorder is null) return;
        if (Input is not null) Input.BlockCaptured = null;
        var recorder = Recorder;
        Recorder = null;
        if (wait) { ReportTakes(recorder, recorder.Finish()); return; }
        _draining.Add(recorder);
        recorder.FinishInBackground(takes =>
        {
            ReportTakes(recorder, takes);
            Threads.Post(() => _draining.Remove(recorder));   // let the queue memory go
        });
    }

    /// <summary>Any thread: the Recorded events, then one loss summary if input was lost.</summary>
    private void ReportTakes(Audio.Recorder recorder, List<(Audio.Recorder.Take Take, double LengthSec)> takes)
    {
        var summary = recorder.LossSummary;
        if (summary is not null) Log.Write($"recording finished: {summary}");
        foreach (var (take, length) in takes)
            Send(EngineEvent.Recorded, w => { w.Write(take.Slot); w.WriteString(take.Path); w.Write(take.StartSec); w.Write(length); });
        if (summary is not null) Send(EngineEvent.RecordingLoss, w => w.WriteString(summary));
    }
}
