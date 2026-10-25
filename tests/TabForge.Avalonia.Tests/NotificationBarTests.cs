using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Features.Recording.Rules;
using TabForge.Platform;
using TabForge.ScoreEditing;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the unified top bar (NotificationBar over the video-saved StatusNoticeBarView): transient Info 5 s, Info with action 8 s,
//   sticky Warn/Error until dismissed/acted/resolved, newest replaces, stickies restore after transients, same source updates in place,
//   and the kept modal (delete bar still confirms).
// Does not own: the callers (engine/autosave/approval/import/video/record) or the row itself (StatusNoticeBarView).
// Tests: this class.
// Threads: UI thread only (AvaloniaFact); timeouts use an injectable fake timer, never sleeps.
public sealed class NotificationBarTests
{
    private sealed class FakeUiThread : IUiThread
    {
        public FakeTimer Timer { get; } = new();
        public bool CheckAccess() => true;
        public bool IsShuttingDown => false;
        public void Post(Action work, UiPriority priority = UiPriority.Normal) => work();
        public Task InvokeAsync(Action work, UiPriority priority = UiPriority.Normal) { work(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> work, UiPriority priority = UiPriority.Normal) => Task.FromResult(work());
        public Task YieldAsync(UiPriority priority) => Task.CompletedTask;
        public void Invoke(Action work, UiPriority priority = UiPriority.Send) => work();
        public T Invoke<T>(Func<T> work, UiPriority priority = UiPriority.Send) => work();
        public IUiTimer CreateTimer(TimeSpan interval, UiPriority priority = UiPriority.Background)
        {
            Timer.Interval = interval;
            return Timer;
        }
    }

    private sealed class FakeTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event EventHandler? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void Fire()
        {
            if (IsEnabled) Tick?.Invoke(this, EventArgs.Empty);
        }
    }

    private static (NotificationBar Bar, StatusNoticeBarView View, FakeUiThread Ui, Window Window) OpenBar()
    {
        var view = new StatusNoticeBarView("Notice", "Fix", () => { });
        var ui = new FakeUiThread();
        var bar = new NotificationBar(view, ui);
        var window = new Window { Content = view };
        window.Show();
        return (bar, view, ui, window);
    }

    private static string NoticeText(StatusNoticeBarView view) =>
        view.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Name == "NoticeText").Text ?? "";

    [AvaloniaFact]
    public void ExportMidiShowsInfoAndHidesAfterFiveSeconds()
    {
        var (bar, view, ui, window) = OpenBar();
        try
        {
            bar.Show("Exported X.mid", NotificationKind.Info);
            Assert.True(view.IsVisible);
            Assert.Equal("Exported X.mid", NoticeText(view));
            Assert.Equal(NotificationKind.Info, bar.CurrentKind);
            Assert.Equal(TimeSpan.FromSeconds(5), ui.Timer.Interval);
            ui.Timer.Fire();
            Assert.False(view.IsVisible);
        }
        finally { window.Close(); bar.Dispose(); }
    }

    [AvaloniaFact]
    public void SuccessWithActionHidesAfterEightSeconds()
    {
        var (bar, view, ui, window) = OpenBar();
        try
        {
            var opened = 0;
            bar.Show("Video saved: X.mp4", NotificationKind.Info, ("Open folder", () => opened++));
            Assert.True(view.IsVisible);
            Assert.Equal(TimeSpan.FromSeconds(8), ui.Timer.Interval);
            var button = view.GetLogicalDescendants().OfType<Button>().First(b => b.Name == "NoticeButton");
            Assert.Equal("Open folder", button.Content);
            DialogTestKit.Click(button);
            Assert.Equal(1, opened);
            Assert.False(view.IsVisible);
        }
        finally { window.Close(); bar.Dispose(); }
    }

    [AvaloniaFact]
    public void AutosaveWarnStaysWhileTransientShowsAndReturns()
    {
        var (bar, view, ui, window) = OpenBar();
        try
        {
            var retried = 0;
            bar.Show("Autosave failed: disk full", NotificationKind.Warn, ("Retry now", () => retried++), source: NotificationBar.AutosaveSource);
            Assert.True(view.IsVisible);
            Assert.Equal(NotificationKind.Warn, bar.CurrentKind);
            // Sticky Warn has no timer: firing does nothing.
            ui.Timer.Fire();
            Assert.True(view.IsVisible);
            Assert.Equal("Autosave failed: disk full", NoticeText(view));
            bar.Show("Exported X.mid", NotificationKind.Info);
            Assert.Equal("Exported X.mid", NoticeText(view));
            Assert.Equal(TimeSpan.FromSeconds(5), ui.Timer.Interval);
            ui.Timer.Fire();
            Assert.True(view.IsVisible);
            Assert.Equal("Autosave failed: disk full", NoticeText(view));
            var retry = view.GetLogicalDescendants().OfType<Button>().First(b => b.Name == "NoticeButton");
            Assert.Equal("Retry now", retry.Content);
            DialogTestKit.Click(retry);
            Assert.Equal(1, retried);
            // Retry is sticky: it stays until resolved.
            Assert.True(view.IsVisible);
            bar.HideSticky(NotificationBar.AutosaveSource);
            Assert.False(view.IsVisible);
        }
        finally { window.Close(); bar.Dispose(); }
    }

    [AvaloniaFact]
    public void EngineErrorUpdatesInPlaceWithoutDuplicates()
    {
        var (bar, view, _, window) = OpenBar();
        try
        {
            bar.Show("Crashy stopped working and was switched off; playback continues", NotificationKind.Error, source: NotificationBar.EngineSource);
            bar.Show("Crashy stopped working and was switched off; playback continues", NotificationKind.Error, source: NotificationBar.EngineSource);
            Assert.Equal(1, bar.StickyCount);
            Assert.True(view.IsVisible);
            bar.Show("Audio output: device lost", NotificationKind.Error, source: NotificationBar.EngineSource);
            Assert.Equal(1, bar.StickyCount);
            Assert.Equal("Audio output: device lost", NoticeText(view));
            Assert.True(view.IsVisible);
        }
        finally { window.Close(); bar.Dispose(); }
    }

    [AvaloniaFact]
    public void RecordWithNothingArmedShowsInfoWithoutModal()
    {
        var (bar, view, ui, window) = OpenBar();
        try
        {
            // Production has no modal; the text goes to the top bar only.
            bar.Show(TransportRecording.NothingArmedText, NotificationKind.Info);
            Assert.True(view.IsVisible);
            Assert.Equal(TransportRecording.NothingArmedText, NoticeText(view));
            Assert.Equal(NotificationKind.Info, bar.CurrentKind);
            ui.Timer.Fire();
            Assert.False(view.IsVisible);
        }
        finally { window.Close(); bar.Dispose(); }
    }

    [AvaloniaFact]
    public void VideoSaveFailureShowsErrorWithoutModal()
    {
        var (bar, view, ui, window) = OpenBar();
        try
        {
            bar.Show("The video could not be saved: disk full", NotificationKind.Error, source: NotificationBar.VideoSource);
            Assert.True(view.IsVisible);
            Assert.Equal(NotificationKind.Error, bar.CurrentKind);
            // Sticky Error: no auto-hide.
            ui.Timer.Fire();
            Assert.True(view.IsVisible);
            // The close X dismisses it.
            bar.DismissCurrent();
            Assert.False(view.IsVisible);
        }
        finally { window.Close(); bar.Dispose(); }
    }

    [AvaloniaFact]
    public void DeleteBarStillRequiresConfirmModal()
    {
        // Kept (N3): delete asks first; the top bar stays out of it.
        Assert.Equal("Delete bar 1 from every track?", ScoreBarCommands.ConfirmDeleteText(0));
        var view = new StatusNoticeBarView("Notice", "Fix", () => { });
        var bar = new NotificationBar(view, new FakeUiThread());
        try
        {
            Assert.False(view.IsVisible);
            Assert.Null(bar.CurrentText);
        }
        finally { bar.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void KindTintUsesThemeTokensInBothThemes(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var view = new StatusNoticeBarView("Notice", "Fix", () => { });
        var window = new Window { Content = view };
        window.Show();
        try
        {
            foreach (var kind in new[] { NotificationKind.Info, NotificationKind.Warn, NotificationKind.Error })
            {
                view.SetKind(kind);
                view.SetAction("Fix", () => { });
                view.Show(kind.ToString());
                Assert.True(view.IsVisible);
                view.Hide();
            }
        }
        finally { window.Close(); }
    }
}
