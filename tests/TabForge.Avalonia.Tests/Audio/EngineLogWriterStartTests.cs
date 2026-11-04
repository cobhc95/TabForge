using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TabForge.AudioEngine;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the promise that <see cref="EngineLogWriter.Write"/> never blocks, also on a writer's very first line.
/// Why: the first line started the low-priority writer thread, and Thread.Start waits for the new thread to run. On a busy machine that held the caller for seconds, and the
/// callers include RemotePlugin's crash report (a crash was noticed in 3 ms and reported 2-8 s later).
/// Tests: this class.
/// </summary>
[Collection("Vst32Native")]
public class EngineLogWriterStartTests
{
    [Fact]
    public void TheFirstLineDoesNotWaitForTheWriterThreadOnABusyMachine()
    {
        using var stop = new ManualResetEventSlim();
        var burners = Enumerable.Range(0, Environment.ProcessorCount * 2).Select(_ =>
        {
            var thread = new Thread(() => { while (!stop.IsSet) Thread.SpinWait(1000); }) { IsBackground = true };
            thread.Start();
            return thread;
        }).ToList();
        try
        {
            double worst = 0;
            for (var i = 0; i < 12; i++)
            {
                var writer = new EngineLogWriter();
                var at = Stopwatch.GetTimestamp();
                writer.Write("first line");
                worst = Math.Max(worst, Stopwatch.GetElapsedTime(at).TotalMilliseconds);
            }
            Assert.InRange(worst, 0, 400);
        }
        finally
        {
            stop.Set();
            burners.ForEach(t => t.Join(1000));
        }
    }
}
