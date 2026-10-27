using System;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Low-integrity start-up check: which integrity levels count as restricted, and that this (normal) test process reads as not restricted.
/// Does not own: the message box that shows the notice (AvaloniaMessageBox).
/// Tests: TabForge.Avalonia.Tests.ProcessIntegrityTests.
/// </summary>
public class ProcessIntegrityTests
{
    [Theory]
    [InlineData(0x0000, true)]    // Untrusted
    [InlineData(0x1000, true)]    // Low: an agent sandbox or a Low-labelled exe
    [InlineData(0x2000, false)]   // Medium: a normal start
    [InlineData(0x2100, false)]   // Medium plus
    [InlineData(0x3000, false)]   // High (elevated)
    [InlineData(0x4000, false)]   // System
    public void OnlyLevelsBelowMediumAreRestricted(int rid, bool restricted) =>
        Assert.Equal(restricted, ProcessIntegrity.IsBelowMedium(rid));

    [Fact]
    public void TheTestProcessReadsItsOwnLevel()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Null(ProcessIntegrity.CurrentRid()); return; }
        var rid = ProcessIntegrity.CurrentRid();
        Assert.NotNull(rid);
        Assert.False(ProcessIntegrity.IsBelowMedium(rid.Value));
    }

    [Fact]
    public void TheNoticeNamesTheSymptomAndTheFix()
    {
        Assert.Contains("Access is denied", ProcessIntegrity.Notice);
        Assert.Contains("/setintegritylevel (OI)(CI)M", ProcessIntegrity.Notice);
    }
}
