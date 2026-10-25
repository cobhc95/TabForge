using System;
using System.Diagnostics;
using System.IO;
using TabForge.Platform.Windows;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Windows path adapter's answer for a folder junction (WindowsPathResolver): the file behind the junction is reported, and a
/// folder itself is refused the way File.OpenHandle refuses it.
/// Does not own: the media judgement of the answer (MediaPathPolicy) or the full-suite adapter checks (TestPathResolverAdapter).
/// Tests: this class.
/// </summary>
public sealed class WindowsPathResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tf-pathres-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // Removes the junction only (its target folder stays), then the rest.
        var junction = Path.Combine(_root, "junction");
        if (Directory.Exists(junction)) Directory.Delete(junction, recursive: false);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FileBehindAFolderJunctionIsReportedAsTheFileItReallyIs()
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        var targetFile = Path.Combine(target, "song.wav");
        File.WriteAllText(targetFile, "x");
        var junction = Path.Combine(_root, "junction");
        var made = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{junction}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
        made?.WaitForExit();
        Assert.True(made is { ExitCode: 0 }, "mklink /J is needed for this test");

        var final = new WindowsPathResolver().FinalPath(Path.Combine(junction, "song.wav"));

        Assert.NotNull(final);
        Assert.EndsWith(@"target\song.wav", final, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FolderItselfIsRefusedLikeFileOpenRefusesIt()
    {
        Directory.CreateDirectory(_root);

        Assert.Throws<UnauthorizedAccessException>(() => new WindowsPathResolver().FinalPath(_root));
    }
}
