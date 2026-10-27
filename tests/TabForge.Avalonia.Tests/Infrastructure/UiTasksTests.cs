using System;
using System.Threading.Tasks;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: UiTasks.Forget reports a faulted task and stays quiet for a finished one.</summary>
public sealed class UiTasksTests
{
    [Fact]
    public void FaultedTaskIsReported()
    {
        string? reported = null;
        UiTasks.Forget(Task.FromException(new InvalidOperationException("boom")), "Paste", text => reported = text);
        Assert.Equal("Paste failed: boom", reported);
    }

    [Fact]
    public void CompletedTaskReportsNothing()
    {
        string? reported = null;
        UiTasks.Forget(Task.CompletedTask, "Paste", text => reported = text);
        Assert.Null(reported);
    }
}
