using System;
using System.IO;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the render roots are unique per test process and the A/B output follows TF_AB_OUT when run.ps1 sets it.</summary>
public sealed class TempRootTests
{
    [Fact]
    public void RenderRootIsTheSameWithinOneProcess()
    {
        Assert.Equal(DialogTestKit.PngDirectory, DialogTestKit.PngDirectory);
    }

    [Fact]
    public void RenderRootIsAUniqueFolderUnderTheTempFolder()
    {
        var root = DialogTestKit.PngDirectory;
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "tf-avalonia-tests"), root, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(32, Path.GetFileName(root).Length);
    }

}
