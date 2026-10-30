using System;
using System.IO;
using System.Linq;
using TabForge.Rendering;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the render job's private temp locations carrying full-entropy (whole-GUID) ids, never truncated ones.
// Does not own: the render run itself (RenderParityTests) or the staging/publish step (RenderStaging).
public sealed class RenderJobTempTests
{
    private static bool IsHex(string value) => value.Length > 0 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    [Fact]
    public void TempAndEventLeavesCarryAWholeGuid()
    {
        var (temp, eventFile, jobId) = RenderJob.TempPaths(mp3: true);
        Assert.Equal(32, jobId.Length);
        Assert.True(IsHex(jobId), "the job id is a whole GUID, not a truncated one");
        Assert.Contains(jobId, Path.GetFileName(temp));
        Assert.Contains(jobId, Path.GetFileName(eventFile));
        Assert.StartsWith(Path.GetTempPath(), temp, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetTempPath(), eventFile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryCallMintsAFreshId()
    {
        var first = RenderJob.TempPaths(mp3: true);
        var second = RenderJob.TempPaths(mp3: true);
        Assert.NotEqual(first.JobId, second.JobId);
        Assert.NotEqual(first.EventFile, second.EventFile);
        Assert.NotEqual(first.Temp, second.Temp);
    }

    [Fact]
    public void WithoutMp3ThereIsNoScratchFolder()
    {
        var (temp, eventFile, jobId) = RenderJob.TempPaths(mp3: false);
        Assert.Equal("", temp);
        Assert.Equal(32, jobId.Length);
        Assert.Contains(jobId, Path.GetFileName(eventFile));
    }
}
