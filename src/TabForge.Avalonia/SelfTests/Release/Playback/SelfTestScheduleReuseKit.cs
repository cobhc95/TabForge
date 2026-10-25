using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TabForge.Playback;

namespace TabForge;

// Owns: MIDI schedule-recording output helper for playback checks.

/// <summary>A MIDI output for the playback checks that records what the schedule sends and waits for schedule conditions (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private sealed class ScheduleReuseOutput : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly List<(int Status, int Note, int Velocity)> _messages = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public (int Status, int Note, int Velocity)[] Messages { get { lock (_gate) return _messages.ToArray(); } }
        public void Send(int deviceId, int status, int data1, int data2) { lock (_gate) _messages.Add((status, data1, data2)); }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private static void WaitForScheduleSeek(PlaybackEngine engine, int bar)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 1200 && engine.Playhead().Bar != bar) Thread.Sleep(2);
    }

    private static bool WaitForScheduleCondition(Func<bool> condition, int timeoutMs)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(2);
        }
        return condition();
    }
}
