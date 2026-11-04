using System;
using System.Collections.Generic;
using TabForge.Avalonia.Transport;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: display-rate delivery, timer-to-window handoff and one request chain across playback restarts.
// Does not own: native vsync timing (Avalonia) or the position drawn on a frame (PlayheadClockTests).
public class AnimationFrameSourceTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(240)]
    [InlineData(360)]
    public void EveryDisplayFrameIsDeliveredWithoutACap(int refreshHz)
    {
        var pending = new Queue<Action<TimeSpan>>();
        var fallback = new ManualFrames();
        var source = new AnimationFrameSource(fallback, callback => { pending.Enqueue(callback); return true; });
        var delivered = 0;
        source.Frame += () => delivered++;
        source.Start();
        source.Start();
        for (var frame = 0; frame < refreshHz; frame++)
        {
            Assert.Single(pending);
            pending.Dequeue()(TimeSpan.FromSeconds(frame / (double)refreshHz));
        }
        Assert.Equal(refreshHz, delivered);
        Assert.False(fallback.IsRunning);
        source.Stop();
        pending.Dequeue()(TimeSpan.FromSeconds(1));
        Assert.Empty(pending);
        Assert.Equal(refreshHz, delivered);
    }

    [Fact]
    public void TimerHandsOffToTheWindowAndResumesIfTheWindowDisappears()
    {
        var pending = new Queue<Action<TimeSpan>>();
        var fallback = new ManualFrames();
        var hasWindow = false;
        var source = new AnimationFrameSource(fallback, callback =>
        {
            if (!hasWindow) return false;
            pending.Enqueue(callback);
            return true;
        });
        source.Start();
        Assert.True(fallback.IsRunning);
        hasWindow = true;
        fallback.Fire();
        Assert.False(fallback.IsRunning);
        Assert.Single(pending);
        hasWindow = false;
        pending.Dequeue()(TimeSpan.Zero);
        Assert.True(source.IsRunning);
        Assert.True(fallback.IsRunning);
        source.Stop();
        Assert.False(fallback.IsRunning);
    }

    [Fact]
    public void RestartInsideAFrameDoesNotCreateASecondRequestChain()
    {
        var pending = new Queue<Action<TimeSpan>>();
        var source = new AnimationFrameSource(new ManualFrames(), callback => { pending.Enqueue(callback); return true; });
        source.Frame += () => { source.Stop(); source.Start(); };
        source.Start();
        pending.Dequeue()(TimeSpan.Zero);
        Assert.Single(pending);
        source.Stop();
        pending.Dequeue()(TimeSpan.Zero);
        Assert.Empty(pending);
    }

    private sealed class ManualFrames : IFrameSource
    {
        public event Action? Frame;
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Fire() { if (IsRunning) Frame?.Invoke(); }
    }
}
