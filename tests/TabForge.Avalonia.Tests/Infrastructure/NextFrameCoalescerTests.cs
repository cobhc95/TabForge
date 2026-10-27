using System;
using System.Threading.Tasks;
using TabForge.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the guards of the one frame coalescer: a burst of requests is one run, the run reads the latest state, and a refused post does not swallow later requests.
/// Tests: TabForge.Platform.NextFrameCoalescer.
/// </summary>
public class NextFrameCoalescerTests
{
    [Fact]
    public void ManyRequestsRunOncePerPass()
    {
        var ui = new QueueUi();
        var runs = 0;
        var coalescer = new NextFrameCoalescer(ui, () => runs++);
        for (var i = 0; i < 50; i++) coalescer.Request();
        Assert.Equal(0, runs);
        ui.Run();
        Assert.Equal(1, runs);
        coalescer.Request();
        ui.Run();
        Assert.Equal(2, runs);
    }

    [Fact]
    public void CancelDropsTheQueuedRunAndFreesTheNextRequest()
    {
        var ui = new QueueUi();
        var runs = 0;
        var coalescer = new NextFrameCoalescer(ui, () => runs++);
        coalescer.Request();
        coalescer.Cancel();
        coalescer.Request();
        ui.RunAll();
        Assert.Equal(1, runs);   // the cancelled run did nothing; the request after it ran once
    }

    [Fact]
    public void ARefusedPostLeavesTheNextRequestFree()
    {
        var ui = new QueueUi { Refuse = true };
        var runs = 0;
        var coalescer = new NextFrameCoalescer(ui, () => runs++);
        Assert.Throws<InvalidOperationException>(coalescer.Request);
        ui.Refuse = false;
        coalescer.Request();
        ui.Run();
        Assert.Equal(1, runs);
    }

    private sealed class QueueUi : IUiThread
    {
        private Action? _work;
        public bool Refuse;
        public void Run() { var work = _work; _work = null; work?.Invoke(); }
        public void RunAll() => Run();
        public void Post(Action work, UiPriority priority = UiPriority.Normal)
        {
            if (Refuse) throw new InvalidOperationException("refused");
            _work += work;
        }
        public bool CheckAccess() => true;
        public bool IsShuttingDown => false;
        public Task InvokeAsync(Action work, UiPriority priority = UiPriority.Normal) { work(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> work, UiPriority priority = UiPriority.Normal) => Task.FromResult(work());
        public Task YieldAsync(UiPriority priority) => Task.CompletedTask;
        public void Invoke(Action work, UiPriority priority = UiPriority.Send) => work();
        public T Invoke<T>(Func<T> work, UiPriority priority = UiPriority.Send) => work();
        public IUiTimer CreateTimer(TimeSpan interval, UiPriority priority = UiPriority.Background) => throw new NotSupportedException("The coalescer test uses no timer.");
    }
}
