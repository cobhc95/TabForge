using System.Diagnostics;
using System.Runtime.InteropServices;
using TabForge.Playback;

namespace TabForge;

/// <summary>The held-back Windows MIDI line delivers a dense stream on time without keeping a core busy (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);
    [DllImport("kernel32.dll")] private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

    /// <summary>Runs on the delay thread: stamps each delivery against its due time and reads that thread's own CPU counters.</summary>
    private sealed class DelayCpuSink(long[] due) : IMidiOutput
    {
        public readonly long[] Error = new long[due.Length];
        public int Count;
        public ulong FirstCycles, LastCycles;
        public long FirstCpu100Ns, LastCpu100Ns, FirstTicks, LastTicks;
        public IReadOnlyList<MidiOutputDeviceInfo> Devices => Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2)
        {
            var now = Stopwatch.GetTimestamp();
            var i = Count;
            if (i >= due.Length) return;
            Error[i] = now - Volatile.Read(ref due[i]);
            QueryThreadCycleTime(GetCurrentThread(), out LastCycles);
            GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user);
            LastCpu100Ns = kernel + user;
            LastTicks = now;
            if (i == 0) { FirstCycles = LastCycles; FirstCpu100Ns = LastCpu100Ns; FirstTicks = now; }
            Volatile.Write(ref Count, i + 1);
        }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private static void TestDelayLineDenseStreamIsCheapAndOnTime()
    {
        const int messages = 300, perSecond = 150, delayMs = 20;
        var freq = Stopwatch.Frequency;

        // Cycles per second of this machine, from a short busy stretch on this thread.
        QueryThreadCycleTime(GetCurrentThread(), out var c0);
        var t0 = Stopwatch.GetTimestamp();
        while (Stopwatch.GetTimestamp() - t0 < freq / 20) { }
        QueryThreadCycleTime(GetCurrentThread(), out var c1);
        var cyclesPerSecond = (c1 - c0) * (double)freq / (Stopwatch.GetTimestamp() - t0);

        var due = new long[messages];
        var sink = new DelayCpuSink(due);
        using (var line = new TabForge.Audio.RoutedMidiOutput.DelayLine(sink))
        {
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < messages; i++)
            {
                var at = start + i * freq / perSecond;
                while (Stopwatch.GetTimestamp() < at) Thread.Sleep(1);
                var d = Stopwatch.GetTimestamp() + freq * delayMs / 1000;
                Volatile.Write(ref due[i], d);
                line.Push(d, 0, 0x90, 60, 100);
            }
            Poll.Until(() => Volatile.Read(ref sink.Count) == messages, 500, 2);
        }

        var count = Volatile.Read(ref sink.Count);
        var seconds = Math.Max(0.001, (sink.LastTicks - sink.FirstTicks) / (double)freq);
        var cpuCycles = (sink.LastCycles - sink.FirstCycles) / cyclesPerSecond / seconds * 100;
        var cpuTimes = (sink.LastCpu100Ns - sink.FirstCpu100Ns) / 1e7 / seconds * 100;
        var errors = sink.Error.Take(count).Select(e => e * 1000.0 / freq).OrderBy(e => e).ToArray();
        var mean = errors.Length > 0 ? errors.Average() : 0;
        var p99 = errors.Length > 0 ? errors[(int)(errors.Length * 0.99)] : 0;
        var detail = FormattableString.Invariant(
            $"sent={count}/{messages}, cpu={cpuCycles:0.0}% of a core (cycles), {cpuTimes:0.0}% (thread times), error mean={mean:0.000} ms, p99={p99:0.000} ms, min={errors.FirstOrDefault():0.000} ms, max={errors.LastOrDefault():0.000} ms");
        Log.Add("  INFO  held-back MIDI line: " + detail);
        Check("held-back MIDI line: a dense stream is all delivered, never early", count == messages && errors.FirstOrDefault() >= 0, detail);
        Check("held-back MIDI line: a dense stream costs under 10% of a core", cpuCycles < 10 && cpuTimes < 10, detail);
        Check("held-back MIDI line: a dense stream is delivered within 2 ms (p99)", p99 < 2, detail);
    }
}
