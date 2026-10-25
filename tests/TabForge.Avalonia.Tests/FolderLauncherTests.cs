using System;
using System.IO;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the start info the folder launcher builds for File Explorer (the live-video "Open folder" and the render window's "Open folder").
// Does not own: the launch itself or the windows that call it.
// Tests: this class.
public sealed class FolderLauncherTests
{
    [Fact]
    public void StartInfoNamesTheWindowsFolderExplorerAndPassesTheFolderAsOneArgument()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-open-folder");

        var start = FolderLauncher.StartInfo(folder);

        Assert.True(Path.IsPathFullyQualified(start.FileName));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.Equal(new[] { folder }, start.ArgumentList);
    }

    /// <summary>The pinned Explorer path must exist on the machine the tests run on, or Open folder cannot launch it.</summary>
    [Fact]
    public void StartInfoNamesAnExplorerThatExists()
    {
        Assert.True(File.Exists(FolderLauncher.StartInfo(Path.GetTempPath()).FileName));
    }
}
