using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the launch-trust checks of the helper processes: the pipe token (a peer without it is refused), the shared block's identity check,
//   the engine and import worker refusing a start without the token, the release-page address check, the licences launch (no shell for Notepad)
//   and IsInside on unnormalised paths.
// Does not own: the audio protocol itself (golden tests) or the import itself (ImportWorker tests).
public sealed class IpcLaunchTrustTests
{
    private static (NamedPipeServerStream Server, NamedPipeClientStream Client) Connected()
    {
        var name = "TabForge.Test." + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);   // explicit buffers: a zero-sized pipe blocks a synchronous Write until the peer reads
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        client.Connect(5000);
        server.WaitForConnectionAsync(new System.Threading.CancellationTokenSource(5000).Token).GetAwaiter().GetResult();   // bounded: a missing peer fails the test instead of hanging the run
        return (server, client);
    }

    [Fact]
    public void PipePeerWithTheLaunchTokenIsAccepted()
    {
        var token = PipeAuth.NewToken();
        Assert.True(PipeAuth.IsWellFormed(token));
        var (server, client) = Connected();
        using (server) using (client)
        {
            PipeAuth.WriteHello(client, token);
            Assert.True(PipeAuth.ReadAndVerify(server, token, 3000));
        }
    }

    [Fact]
    public void PipePeerWithoutTheTokenIsRefused()
    {
        var (server, client) = Connected();
        using (server) using (client)
        {
            client.Write(new byte[] { 1, 0, 0, 0, 0 });   // a frame straight away, no hello
            Assert.False(PipeAuth.ReadAndVerify(server, PipeAuth.NewToken(), 1000));
        }
    }

    [Fact]
    public void PipePeerWithAWrongTokenOrSilentIsRefused()
    {
        var (server, client) = Connected();
        using (server) using (client)
        {
            PipeAuth.WriteHello(client, PipeAuth.NewToken());
            Assert.False(PipeAuth.ReadAndVerify(server, PipeAuth.NewToken(), 1000));
        }
        var (server2, client2) = Connected();
        using (server2) using (client2) Assert.False(PipeAuth.ReadAndVerify(server2, PipeAuth.NewToken(), 300));
    }

    [Fact]
    public void EngineStartCarriesTheTokenAsItsFifthArgument()
    {
        var start = EngineStartInfo.For("TabForge.exe", "0123456789abcdef", 4242, Path.GetTempPath(), "tok");
        Assert.Equal("tok", start.ArgumentList.Last());
        Assert.Equal(5, start.ArgumentList.Count);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void EngineAndImportWorkerRefuseAStartWithoutTheToken()
    {
        Assert.Equal(2, TabForge.AudioEngine.EngineHost.Run(new[] { "--audio-engine", "0123456789abcdef", "1" }));
        Assert.Equal(2, ImportWorker.Run(new[] { "--import-worker", "TabForge.ImportWorker.0123456789abcdef0123456789abcdef", "1" }));
        Assert.Equal(2, ImportWorker.Run(new[] { "--import-worker", "TabForge.ImportWorker.0123456789abcdef0123456789abcdef", "1", "not-a-token" }));
    }

    [Fact]
    public void SharedBlockOpenRefusesAMappingThatIsNotOurs()
    {
        var name = "TabForge.Test." + Guid.NewGuid().ToString("N");
        using var foreign = MemoryMappedFile.CreateNew(name, SharedBlock.Size + 100_000);
        Assert.Throws<InvalidDataException>(() => SharedBlock.Open(name));
        var ours = "TabForge.Test." + Guid.NewGuid().ToString("N");
        using var created = SharedBlock.Create(ours);
        using var opened = SharedBlock.Open(ours);
    }

    [Theory]
    [InlineData("https://github.com/cobhc95/TabForge/releases/tag/v1.2.3", true)]
    [InlineData("https://github.com/cobhc95/TabForge/releases", true)]
    [InlineData("http://github.com/cobhc95/TabForge/releases", false)]
    [InlineData("https://evil.example/cobhc95/TabForge/releases", false)]
    [InlineData("https://github.com.evil.example/cobhc95/TabForge/releases", false)]
    [InlineData("https://github.com/other/TabForge/releases", false)]
    [InlineData("https://user@github.com/cobhc95/TabForge/releases", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    public void OnlyTheRepositoryReleasePagesMayBeOpened(string address, bool allowed) =>
        Assert.Equal(allowed, UpdateService.IsReleasePage(new Uri(address)));

    [Fact]
    public void LicencesOpenInNotepadWithoutTheShellAndNothingOutsideTheAppFolderOpens()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "tf-app");
        var md = Path.Combine(baseDir, "THIRD_PARTY.md");
        var start = MainWindowHelpMenu.LicencesStartInfo(md, baseDir)!;
        Assert.False(start.UseShellExecute);
        Assert.Equal(md, Assert.Single(start.ArgumentList));
        Assert.Null(MainWindowHelpMenu.LicencesStartInfo(Path.Combine(baseDir, "..", "x.md"), baseDir));
        Assert.Null(MainWindowHelpMenu.LicencesStartInfo(@"C:\Windows\System32\calc.exe", baseDir));
        Assert.NotNull(MainWindowHelpMenu.LicencesStartInfo(MainWindowHelpMenu.LicencesUrl, baseDir));
    }

    [Fact]
    public void IsInsideResolvesDotDotAndSlashesBeforeComparing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-proj");
        Assert.True(MediaPathPolicy.IsInside(Path.Combine(folder, "a", "x.wav"), folder));
        Assert.False(MediaPathPolicy.IsInside(folder + @"\..\secret\x.wav", folder));
        Assert.True(MediaPathPolicy.IsInside((folder + @"\sub\x.wav").Replace('\\', '/'), folder));
    }

    [Fact]
    public async Task ASilentPeerIsDroppedAndTheRealPeerIsStillAccepted()
    {
        var name = "TabForge.Test." + Guid.NewGuid().ToString("N");
        var token = PipeAuth.NewToken();
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
        var accept = PipeAuth.AcceptAsync(server, token, 8000, 300);
        using var squatter = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        squatter.Connect(5000);   // connects first and says nothing
        await Task.Delay(700);    // longer than the per-peer window: dropped, the server listens again
        using var real = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        real.Connect(5000);
        PipeAuth.WriteHello(real, token);
        Assert.True(await accept);
    }

    [Fact]
    public void ATakenPipeNameFailsTheSecondServer()
    {
        var name = "TabForge.Test." + Guid.NewGuid().ToString("N");
        const PipeOptions options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance;
        using var first = new NamedPipeServerStream(name, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, options);
        Assert.True(Record.Exception(() => new NamedPipeServerStream(name, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, options)) is IOException or UnauthorizedAccessException);   // Windows reports a taken first-instance name as access denied
    }

    [Fact]
    public void IsInsideSeesThroughShortNamesAndJunctions()
    {
        var root = Path.Combine(Path.GetTempPath(), "tf-in-" + Guid.NewGuid().ToString("N"));
        var inside = Path.Combine(root, "project");
        var outside = Path.Combine(root, "secret");
        Directory.CreateDirectory(inside); Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "x.wav"), "x");
            var link = Path.Combine(inside, "link");
            var made = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false })!;
            made.WaitForExit();
            if (made.ExitCode == 0) Assert.False(MediaPathPolicy.IsInside(Path.Combine(link, "x.wav"), inside));   // a junction out of the folder
            Assert.True(MediaPathPolicy.IsInside(Path.Combine(inside, "new", "y.wav"), inside));                      // a file not yet there
        }
        finally { try { Directory.Delete(root, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort cleanup: a junction can refuse deletion */ } }
    }
}
