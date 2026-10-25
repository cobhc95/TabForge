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

    [Fact]
    public void AbOutputFollowsTheRunFolderWhenSet()
    {
        const string variable = "TF_AB_OUT";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            var runFolder = Path.Combine(Path.GetTempPath(), "tf-ab-test-run");
            Environment.SetEnvironmentVariable(variable, runFolder);
            Assert.Equal(Path.Combine(runFolder, "avalonia"), ParityShotsTests.OutputDirectory);
            Assert.Equal(Path.Combine(runFolder, "missing.txt"), ParityShotsTests.MissingListPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void AbOutputFallsBackToTheTestProcessFolderWhenUnset()
    {
        const string variable = "TF_AB_OUT";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            Assert.Equal(Path.Combine(DialogTestKit.PngDirectory, "tf-ab", "avalonia"), ParityShotsTests.OutputDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }
}
