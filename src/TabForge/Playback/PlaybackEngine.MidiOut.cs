using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TabForge.Models;
using TabForge.Services;
using TempoMath = TabForge.Audio.Contracts.TempoMath;

namespace TabForge.Playback;

/// <summary>Everything the engine sends to the MIDI output: serialised output operations, channel setup, panic, live and preview notes, volume scaling.</summary>
public sealed partial class PlaybackEngine : IDisposable
{
    public IReadOnlyList<MidiOutputDeviceInfo> GetDevices() => _output.Devices;

    /// <summary>Sends every channel's program, volume and pan again at the next tick (a synth that was just created has none).</summary>
    public void RearmChannelSetup()
    {
        _rearm = true;
        // Not playing: nothing will consume the flag, so send the setup to the (new) synths now.
        if (_running) return;
        if (_project is { } project) SendSetupFromProject(project);
        else if (_timeline is { } timeline) lock (_outputGate) SendChannelSetupCore(timeline);
    }

    /// <summary>Stopped: compiles the song's channel setup from the live project and sends it.</summary>
    private void SendSetupFromProject(SongProject project)
    {
        _project = project;
        try
        {
            var opts = (_options ?? new PlaybackOptions()).Clone();
            opts.Metronome = false;
            opts.CountIn = false;
            opts.Loop = false;
            var timeline = MidiTimelineBuilder.Build(project, opts);
            lock (_outputGate) SendChannelSetupCore(timeline);
        }
        catch (Exception ex) { Debug.WriteLine($"Channel setup refresh failed: {ex}"); }
    }

    /// <summary>All notes off, on a background thread so a slow driver cannot freeze the UI.</summary>
    public void PanicAsync()
    {
        var timeline = _timeline;
        EnqueueOutputOperation(() =>
        {
            _output.ResetAll();
            SendNeutralExpressionState(timeline);
        });
    }

    private void SendNeutralExpressionState(ScoreTimeline? timeline)
    {
        if (timeline is null) return;
        foreach (var e in timeline.ChannelSetup)
        {
            var kind = e.Status & 0xF0;
            if ((kind == 0xE0 && e.Data1 == 0 && e.Data2 == 64) ||
                (kind == 0xB0 && e.Data1 == 1 && e.Data2 == 0))
                _output.Send(e.DeviceId, e.Status, e.Data1, e.Data2);
        }
    }

    /// <summary>
    /// Reset/setup operations are serialized in call order. Resume therefore cannot send setup
    /// before a queued pause reset and then have that reset erase the freshly armed channel state.
    /// </summary>
    private Task EnqueueOutputOperation(Action operation)
    {
        lock (_outputOperationGate)
        {
            var previous = _outputOperationTail;
            _outputOperationTail = Task.Run(() =>
            {
                try { previous.GetAwaiter().GetResult(); }
                catch (Exception ex) { Debug.WriteLine($"Previous MIDI cleanup operation failed: {ex}"); }
                lock (_outputGate)
                {
                    try { operation(); }
                    catch (Exception ex) { Debug.WriteLine($"MIDI output cleanup operation failed: {ex}"); }
                }
            });
            return _outputOperationTail;
        }
    }

    private void ResetAndSetup(ScoreTimeline timeline, int generation)
    {
        _activeMetronomeNotes.Clear();
        EnqueueOutputOperation(() =>
        {
            _output.ResetAll();
            if (_running && generation == Volatile.Read(ref _generation) && !_paused)
                SendChannelSetupCore(timeline);
        }).GetAwaiter().GetResult();
    }

    private void RearmChannelSetup(ScoreTimeline timeline, int generation)
    {
        EnqueueOutputOperation(() =>
        {
            if (_running && generation == Volatile.Read(ref _generation) && !_paused)
                SendChannelSetupCore(timeline);
        }).GetAwaiter().GetResult();
    }

    /// <summary>The master knob scales everything: channel volume (CC7) of every track, the metronome and note preview.</summary>
    private int MasterScaled(int status, int data1, int data2)
    {
        if ((status & 0xF0) != 0xB0 || data1 != 7) return data2;
        var scaled = Math.Clamp((int)Math.Round(data2 * Volatile.Read(ref _masterVolume) / 100.0), 0, 127);
        Volatile.Write(ref _channelVolume[status & 0x0F], scaled);
        return scaled;
    }

    /// <summary>
    /// Metronome velocity. The click shares the drum channel, so it is lifted by that channel's current
    /// CC7 (the drum track's fader must not quieten the click), then scaled by master like everything else.
    /// </summary>
    private int MetronomeVelocity(int velocity, int channel)
    {
        var master = Volatile.Read(ref _masterVolume) / 100.0;
        var channelLevel = Math.Max(8, Volatile.Read(ref _channelVolume[channel & 0x0F])) / 127.0;
        return Math.Clamp((int)Math.Round(velocity * master / channelLevel), 1, 127);
    }

    private void SendChannelSetupCore(ScoreTimeline timeline)
    {
        foreach (var e in timeline.ChannelSetup)
            _output.Send(e.DeviceId, e.Status, e.Data1, MasterScaled(e.Status, e.Data1, e.Data2));
    }

    // Preview notes: one token per (device, channel, pitch). A new preview of the same key retriggers (off, then on) and
    // makes the earlier note's pending off a no-op, so an old timer can never cut the newer note.
    private readonly Dictionary<(int Device, int Channel, int Note), long> _previewTokens = new();
    private long _previewSerial;

    public async Task PreviewNoteAsync(int deviceId, int channel = 0, int program = 24, int note = 64, int ms = 350)
    {
        var ch = channel & 0x0F;
        var pitch = Math.Clamp(note, 0, 127);
        var key = (deviceId, ch, pitch);
        long token;
        lock (_outputGate)
        {
            token = ++_previewSerial;
            var retrigger = _previewTokens.ContainsKey(key);
            _previewTokens[key] = token;
            if (retrigger) _output.Send(deviceId, 0x80 | ch, pitch, 0);
            _output.Send(deviceId, 0xC0 | ch, Math.Clamp(program, 0, 127), 0);
            var velocity = Math.Clamp((int)Math.Round(100 * Volatile.Read(ref _masterVolume) / 100.0), 1, 127);
            _output.Send(deviceId, 0x90 | ch, pitch, velocity);
        }
        try { await Task.Delay(Math.Clamp(ms, 60, 8000)).ConfigureAwait(false); }
        finally
        {
            lock (_outputGate)
                if (_previewTokens.TryGetValue(key, out var current) && current == token)
                {
                    _previewTokens.Remove(key);
                    _output.Send(deviceId, 0x80 | ch, pitch, 0);
                }
        }
    }

    /// <summary>MIDI input monitoring: one message straight out (see <see cref="IMidiOutput.SendLive"/>).</summary>
    public void SendLive(int deviceId, int status, int data1, int data2)
    {
        lock (_outputGate) _output.SendLive(deviceId, status, data1, data2);
    }

    public void PreviewNote(int deviceId, int channel, int program, int note, int ms = 300) =>
        _ = PreviewNoteAsync(deviceId, channel, program, note, ms);
}
