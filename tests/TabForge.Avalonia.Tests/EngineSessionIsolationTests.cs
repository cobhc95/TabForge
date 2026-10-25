using System;
using System.IO;
using System.Linq;
using TabForge.AudioEngine;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: two engine sessions in one process share no log writer, main-thread queue or thread flags; the queue is unbounded and warns once per crossing of the high-water mark.
/// Does not own: the log file format or rotation (EngineLogRunFileTests, full suite).
/// Tests: this class.
/// </summary>
public sealed class EngineSessionIsolationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-engine-session-" + Guid.NewGuid().ToString("N"));

    public EngineSessionIsolationTests() => Directory.CreateDirectory(_folder);

    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } }

    private string[] Lines(EngineLogWriter log, string name)
    {
        log.Flush();
        var path = Path.Combine(_folder, name);
        return File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
    }

    [Fact]
    public void TwoSessionsShareNeitherLogNorQueueNorThreadFlags()
    {
        var logA = new EngineLogWriter(); var logB = new EngineLogWriter();
        logA.UseFileForTests(Path.Combine(_folder, "a.log")); logB.UseFileForTests(Path.Combine(_folder, "b.log"));
        var a = new EngineSession(logA); var b = new EngineSession(logB);

        Assert.NotSame(a.Log, b.Log);
        Assert.NotSame(a.Threads, b.Threads);
        Assert.NotSame(a.Retired, b.Retired);
        Assert.NotSame(a.Editors, b.Editors);

        a.Log.Write("only-in-a");
        b.Log.Write("only-in-b");
        Assert.Contains(Lines(logA, "a.log"), l => l.EndsWith("only-in-a")); Assert.DoesNotContain(Lines(logA, "a.log"), l => l.Contains("only-in-b"));
        Assert.Contains(Lines(logB, "b.log"), l => l.EndsWith("only-in-b")); Assert.DoesNotContain(Lines(logB, "b.log"), l => l.Contains("only-in-a"));

        var ran = 0;
        a.Threads.Post(() => ran++);
        b.Threads.RunPending();
        Assert.Equal(0, ran);
        Assert.Equal(1, a.Threads.Queued); Assert.Equal(0, b.Threads.Queued);
        a.Threads.RunPending();
        Assert.Equal(1, ran);

        a.Threads.MarkMainThread();
        Assert.True(a.Threads.IsMainThread); Assert.False(b.Threads.IsMainThread);
        var flags = new[] { false, false };
        var thread = new System.Threading.Thread(() => { a.Threads.MarkAudioThread(); flags[0] = a.Threads.IsAudioThread; flags[1] = b.Threads.IsAudioThread; });
        thread.Start(); thread.Join();
        Assert.True(flags[0]); Assert.False(flags[1]);
    }

    [Fact]
    public void QueueIsUnboundedAndWarnsOncePerCrossingOfTheHighWaterMark()
    {
        var log = new EngineLogWriter();
        log.UseFileForTests(Path.Combine(_folder, "q.log"));
        var threads = new EngineThreads(log);
        var ran = 0;
        var total = EngineThreads.HighWaterMark + 500;

        for (var i = 0; i < total; i++) threads.Post(() => ran++);
        Assert.Equal(total, threads.Queued);   // nothing dropped
        Assert.Single(Lines(log, "q.log"), l => l.Contains("main-thread queue over"));

        threads.RunPending(int.MaxValue);
        Assert.Equal(total, ran);
        Assert.Equal(0, threads.Queued);

        for (var i = 0; i <= EngineThreads.HighWaterMark; i++) threads.Post(() => ran++);   // crosses again
        Assert.Equal(2, Lines(log, "q.log").Count(l => l.Contains("main-thread queue over")));
    }
}
