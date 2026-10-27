using System;
using System.IO;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Avalonia crash log (AvaloniaCrashLog): each entry carries the local time, the app version and the full exception, and the
/// file stays under its ceiling while keeping the newest entry.
/// Tests: this class.
/// </summary>
public sealed class AvaloniaCrashLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-crash-log-" + Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_folder, AvaloniaCrashLog.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void EntryHoldsTheTimeVersionAndFullException()
    {
        var log = new AvaloniaCrashLog(LogPath, "9.9.9-test", () => new DateTime(2026, 10, 17, 16, 3, 29));

        log.Record(new ArgumentOutOfRangeException("index", "Index was out of range."));

        var text = File.ReadAllText(LogPath);
        Assert.Contains("[2026-10-17 16:03:29] TabForge 9.9.9-test", text);
        Assert.Contains("System.ArgumentOutOfRangeException", text);
        Assert.Contains("Index was out of range.", text);
    }

    [Fact]
    public void MarkAppendsOneLineWithTheTimeAndVersion()
    {
        var log = new AvaloniaCrashLog(LogPath, "9.9.9-test", () => new DateTime(2026, 10, 17, 16, 3, 29));

        log.Mark("start: process 1");
        log.Mark("exit: the process ended in order");

        var lines = File.ReadAllLines(LogPath);
        Assert.Equal("[2026-10-17 16:03:29] TabForge 9.9.9-test start: process 1", lines[0]);
        Assert.EndsWith("exit: the process ended in order", lines[1]);
    }

    [Fact]
    public void ProfileFolderIsWrittenAsTheUserProfileVariable()
    {
        var text = AvaloniaCrashLog.RedactProfile(@"at Load(C:\Users\someone\AppData\song.tforge)", @"C:\Users\someone");

        Assert.Equal(@"at Load(%USERPROFILE%\AppData\song.tforge)", text);
    }

    [Fact]
    public void FileStaysUnderItsCeilingAndKeepsTheNewestEntry()
    {
        var log = new AvaloniaCrashLog(LogPath, "1.0", () => DateTime.Now);
        var large = new InvalidOperationException(new string('x', 4000));

        for (var i = 0; i < 200; i++) log.Record(large);   // about 800 KB written in all
        log.Record(new InvalidOperationException("newest failure"));

        var text = File.ReadAllText(LogPath);
        Assert.True(text.Length <= AvaloniaCrashLog.MaxCharacters);
        Assert.Contains("newest failure", text);
    }
}
