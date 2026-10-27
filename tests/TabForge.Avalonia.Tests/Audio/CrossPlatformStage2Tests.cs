using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using TabForge.Audio.Contracts;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: cross-platform Stage 2: the app folder layout of each OS (checked on any machine), the path comparer, the shell commands, and a headless round trip
//   of the portable IPC (file-backed shared memory + short-named pipe with the token hello), run on Windows too although Windows keeps its native IPC.
// Does not own: the Windows IPC (IpcLaunchTrustTests, SharedMemoryTrustTests).
// Tests: itself.
public sealed class CrossPlatformStage2Tests
{
    private static string? NoEnv(string _) => null;

    [Fact]
    public void WindowsLayoutKeepsTheShippedPaths()
    {
        var f = new AppFolderLayout(HostOs.Windows, @"C:\Users\u", @"C:\Users\u\AppData\Roaming", @"C:\Users\u\AppData\Local", @"C:\Temp", NoEnv, @"C:\Users\u\Music", @"C:\Users\u\Videos");
        Assert.Equal(Path.Combine(@"C:\Users\u\AppData\Roaming", "TabForge"), f.Settings);
        Assert.Equal(Path.Combine(@"C:\Users\u\AppData\Local", "TabForge"), f.Data);
        Assert.Equal(Path.Combine(f.Data, "Diagnostics"), f.Logs);
        Assert.Equal(Path.Combine(f.Data, "SoundFonts"), f.SoundFonts);
        Assert.Equal(f.Settings, f.PluginCache);
        Assert.Equal(@"C:\Users\u\Music", f.Music);
        Assert.Equal(Path.Combine(@"C:\Users\u\AppData\Roaming", "REAPER"), f.ReaperResources);
    }

    [Fact]
    public void MacLayoutUsesLibrary()
    {
        var f = new AppFolderLayout(HostOs.MacOS, "/Users/u", "", "", "/tmp", NoEnv);
        Assert.Equal(Path.Combine("/Users/u", "Library", "Application Support", "TabForge"), f.Settings);
        Assert.Equal(f.Settings, f.Data);
        Assert.Equal(Path.Combine("/Users/u", "Library", "Caches", "TabForge"), f.Cache);
        Assert.Equal(Path.Combine("/Users/u", "Movies"), f.Videos);
    }

    [Fact]
    public void LinuxLayoutFollowsXdgWithFallbacks()
    {
        var fallback = new AppFolderLayout(HostOs.Linux, "/home/u", "", "", "/tmp", NoEnv);
        Assert.Equal(Path.Combine("/home/u", ".config", "TabForge"), fallback.Settings);
        Assert.Equal(Path.Combine("/home/u", ".local", "share", "TabForge"), fallback.Data);
        Assert.Equal(Path.Combine("/home/u", ".cache", "TabForge"), fallback.Cache);
        Assert.Equal("/tmp", fallback.Runtime);

        var set = new AppFolderLayout(HostOs.Linux, "/home/u", "", "", "/tmp", name => name switch
        {
            "XDG_CONFIG_HOME" => "/cfg", "XDG_DATA_HOME" => "/data", "XDG_CACHE_HOME" => "relative/ignored", "XDG_RUNTIME_DIR" => "/run/user/1000", _ => null,
        });
        Assert.Equal(Path.Combine("/cfg", "TabForge"), set.Settings);
        Assert.Equal(Path.Combine("/data", "TabForge"), set.Data);
        Assert.Equal(Path.Combine("/home/u", ".cache", "TabForge"), set.Cache);   // a relative XDG value is ignored
        Assert.Equal("/run/user/1000", set.Runtime);
    }

    [Fact]
    public void PathComparerIgnoresCaseExceptOnLinux()
    {
        Assert.True(PathComparer.For(HostOs.Windows).Equals(@"C:\A", @"c:\a"));
        Assert.True(PathComparer.For(HostOs.MacOS).Equals("/A", "/a"));
        Assert.False(PathComparer.For(HostOs.Linux).Equals("/A", "/a"));
        Assert.Equal(StringComparison.Ordinal, PathComparer.ComparisonFor(HostOs.Linux));
        if (OperatingSystem.IsWindows()) Assert.Same(StringComparer.OrdinalIgnoreCase, PathComparer.Default);
    }

    [Fact]
    public void ShellCommandsPerSystem()
    {
        var mac = ShellCommands.For(HostOs.MacOS, ShellVerb.Reveal, "/a/b.gp")!;
        Assert.Equal("open", mac.Program);
        Assert.Equal(new[] { "-R", "/a/b.gp" }, mac.Arguments);
        Assert.Equal(new[] { "/a" }, ShellCommands.For(HostOs.MacOS, ShellVerb.Open, "/a")!.Arguments);
        var linux = ShellCommands.For(HostOs.Linux, ShellVerb.Open, "/a");
        Assert.Equal("xdg-open", linux!.Program);
        var reveal = ShellCommands.For(HostOs.Linux, ShellVerb.Reveal, Path.Combine(Path.GetTempPath(), "x", "b.gp"))!;
        Assert.Equal(Path.Combine(Path.GetTempPath(), "x"), Path.TrimEndingDirectorySeparator(reveal.Arguments[0]));
        Assert.Null(ShellCommands.For(HostOs.Windows, ShellVerb.Open, "x"));
        Assert.True(ShellCommands.IsWebAddress("https://github.com/x"));
        Assert.False(ShellCommands.IsWebAddress("file:///etc/passwd"));
        Assert.False(ShellCommands.IsWebAddress("calc.exe"));
    }

    [Fact]
    public void PortableSharedMemoryRoundTripsBetweenTwoMappings()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-portable-shm-" + Guid.NewGuid().ToString("N"));
        var name = EngineNames.SharedMemory(Guid.NewGuid().ToString("N"));
        try
        {
            using var engine = SharedBlock.CreatePortable(name, folder);
            using var app = SharedBlock.OpenPortable(name, folder);
            Assert.True(app.TryWrite(new TimedMidi { Timestamp = 42, Slot = 3, Status = 0x90, Data1 = 60, Data2 = 100 }));
            Assert.True(engine.TryRead(out var got));
            Assert.Equal((42L, 3, (byte)0x90, (byte)60), (got.Timestamp, got.Slot, got.Status, got.Data1));
            engine.SampleRate = 48000; engine.CpuLoad = 0.25;
            Assert.Equal(48000, app.SampleRate);
            Assert.Equal(0.25, app.CpuLoad);
            Assert.Throws<IOException>(() => SharedBlock.CreatePortable(name, folder));   // the name is taken
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void PortableSharedMemoryFileGoesWhenTheCreatorCloses()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-portable-shm-" + Guid.NewGuid().ToString("N"));
        try
        {
            var name = EngineNames.SharedMemory(Guid.NewGuid().ToString("N"));
            using (SharedBlock.CreatePortable(name, folder)) Assert.Single(Directory.GetFiles(folder));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task ShortPipeNameRoundTripsWithTheTokenHello()
    {
        var name = PipeNames.Short("TabForge.AudioEngine." + Guid.NewGuid().ToString("N"));
        Assert.True(name.Length <= 24);
        var token = PipeAuth.NewToken();
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
        var accept = PipeAuth.AcceptAsync(server, token, 5000);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        client.Connect(5000);
        PipeAuth.WriteHello(client, token);
        Assert.True(await accept);
        client.Write(new byte[] { 1, 2, 3 });
        client.Flush();
        var got = new byte[3];
        await server.ReadExactlyAsync(got);
        Assert.Equal(new byte[] { 1, 2, 3 }, got);
    }

    [Fact]
    public void PipeNamesKeepTheWindowsNameAndShortenElsewhere()
    {
        const string name = "TabForge.AudioEngine.0123456789abcdef0123456789abcdef";
        if (OperatingSystem.IsWindows()) Assert.Equal(name, PipeNames.ForPlatform(name));
        else Assert.True(PipeNames.ForPlatform(name).Length < 30);
        Assert.Equal(PipeNames.Short(name), PipeNames.Short(name));
    }
}
