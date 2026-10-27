using System;
using System.IO;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: round-1 unified-crash narrow guards: placement Restore never throws and reports bool, first-chance
// writes the full exception, rotates past 512 KB and never recurses on its own failed write, and single-instance
// helpers bypass and same-exe names differ.
// Tests: MainWindowPlacement.Restore, Program.WriteFirstChanceEntry, AvaloniaSingleInstance.
public sealed class UnifiedCrashRoundOneTests
{
    [AvaloniaFact]
    public void RestoreNeverThrowsAndReportsBool()
    {
        var window = new global::Avalonia.Controls.Window();
        Assert.True(MainWindowPlacement.Restore(window, null));
        Assert.True(MainWindowPlacement.Restore(window, null));
    }

    [Fact]
    public void FirstChanceWritesFullToStringAndRotatesPastCap()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-firstchance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var log = Path.Combine(folder, "firstchance.log");
            File.WriteAllText(log, new string('x', (int)(Program.FirstChanceRotationBytes + 1024)));
            Exception failure;
            try { throw new InvalidOperationException("unified-boom"); }
            catch (Exception ex) { failure = ex; }
            Program.WriteFirstChanceEntry(log, failure);
            Assert.True(File.Exists(Path.Combine(folder, "firstchance.1.log")));
            var text = File.ReadAllText(log);
            Assert.Contains("System.InvalidOperationException", text);
            Assert.Contains("unified-boom", text);
            Assert.Contains("at ", text);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception) { }
        }
    }

    [Fact]
    public void FirstChanceWriteThatFailsDoesNotRecurse()
    {
        // A log path that cannot be opened (a folder) makes the write throw; a handler that logs that throw must not recurse.
        var folder = Path.Combine(Path.GetTempPath(), "tf-firstchance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var calls = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> handler = (_, e) =>
        {
            calls++;
            Program.WriteFirstChanceEntry(folder, e.Exception);
        };
        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            Program.WriteFirstChanceEntry(folder, new InvalidOperationException("unwritable"));
            Assert.InRange(calls, 1, 8);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception) { }
        }
    }

    [Fact]
    public void HelpersBypassAndLockNamesFollowExePath()
    {
        Assert.True(AvaloniaSingleInstance.IsHelperArgs(new[] { "--audio-engine" }));
        Assert.True(AvaloniaSingleInstance.IsHelperArgs(new[] { "--plugin-host" }));
        Assert.True(AvaloniaSingleInstance.IsHelperArgs(new[] { "--plugin-info" }));
        Assert.True(AvaloniaSingleInstance.IsHelperArgs(new[] { "--import-worker" }));
        Assert.False(AvaloniaSingleInstance.IsHelperArgs(Array.Empty<string>()));
        Assert.False(AvaloniaSingleInstance.IsHelperArgs(new[] { "song.tforge" }));
        var a = AvaloniaSingleInstance.LockNameFor(@"C:\App\TabForge.exe");
        var b = AvaloniaSingleInstance.LockNameFor(@"C:\App\TabForge.exe");
        var c = AvaloniaSingleInstance.LockNameFor(@"D:\Other\TabForge.exe");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
