using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Follow playback switch and the hand-scroll pause: the pause ends after the Resume seconds on a fake clock, the switch off stops all
/// following, the hotkey flips and saves the one setting, and the score and the timeline both read it; the two Preferences rows are in the catalogue.
/// Tests: FollowHold, ScorePreviewView, ArrangementView, MainWindowViewToggles, SettingsCatalog.
/// </summary>
public class FollowPlaybackTests
{
    [Fact]
    public void AHandScrollPausesTheFollowAndItResumesAfterTheResumeSeconds()
    {
        long now = 0;
        var hold = new FollowHold { Now = () => now };
        var follow = new FollowSettings { ResumeAfterSeconds = 3 };
        Assert.True(hold.Allows(follow));
        hold.Pause();
        now = 2999;
        Assert.False(hold.Allows(follow));
        hold.Pause();   // a second scroll restarts the wait
        now = 5000;
        Assert.False(hold.Allows(follow));
        now = 5999;
        Assert.True(hold.Allows(follow));
    }

    [Fact]
    public void FollowPlaybackOffNeverAllowsAFollow()
    {
        Assert.False(new FollowHold().Allows(new FollowSettings { FollowPlayback = false }));
        Assert.True(new FollowSettings().FollowPlayback);
        Assert.Equal(3, new FollowSettings().ResumeAfterSeconds);
    }

    [Fact]
    public void TheResumeDelayIsKeptInsideItsRangeAndTheFrameGateSurvivesAZeroCap()
    {
        Assert.Equal(10, SettingsValidator.Normalize(new AppSettings { Follow = new FollowSettings { ResumeAfterSeconds = 500 } }).Follow.ResumeAfterSeconds);
        Assert.Equal(1, SettingsValidator.Normalize(new AppSettings { Follow = new FollowSettings { ResumeAfterSeconds = -4 } }).Follow.ResumeAfterSeconds);
        Assert.True(new FollowFrameGate().Allow(0));
    }

    [AvaloniaFact]
    public void TheScoreScrollsWhileFollowingPausesOnAWheelAndResumesOnTheClock()
    {
        var rig = new ScoreRig();
        rig.PlayBars(0, 14);
        Assert.True(rig.Viewer.Offset.Y > 0, "the score followed the playhead");

        rig.Wheel();
        var held = rig.Viewer.Offset.Y;
        rig.PlayBars(15, 26);
        Assert.Equal(held, rig.Viewer.Offset.Y, 1);

        rig.Now += rig.Settings.Follow.ResumeAfterSeconds * 1000L;
        rig.PlayBars(27, 39);
        Assert.NotEqual(held, rig.Viewer.Offset.Y);
        rig.Close();
    }

    [AvaloniaFact]
    public void TheScoreDoesNotScrollWhenFollowPlaybackIsOff()
    {
        var rig = new ScoreRig();
        rig.Settings.Follow.FollowPlayback = false;
        var before = rig.Viewer.Offset;
        rig.PlayBars(0, 39);
        Assert.Equal(before, rig.Viewer.Offset);
        rig.Close();
    }

    [AvaloniaFact]
    public void TheTimelineAndTheScoreReadTheSameSetting()
    {
        var settings = new AppSettings();
        var document = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var pane = new ArrangementView(document.Project, document);
        new TimelineSettingsBinding(settings).Attach(pane);
        var score = new ScorePreviewView();
        new ScoreSettingsApplier(score).Apply(settings);

        Assert.Same(settings.Follow, pane.Follow);
        Assert.Same(settings.Follow, score.Follow);
        settings.Follow.FollowPlayback = false;
        Assert.False(pane.FollowHold.Allows(pane.Follow));
        Assert.False(score.FollowHold.Allows(score.Follow));
    }

    [AvaloniaFact]
    public void TheHotkeyFlipsFollowPlaybackSavesItAndTellsTheUser()
    {
        var path = Path.Combine(Path.GetTempPath(), "follow-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using (var settings = MainWindowSettings.Open(path))
            {
                var commands = new MainWindowCommands(new Window(), () => { });
                var notices = new List<string>();
                MainWindowViewToggles.Register(commands, settings, notices.Add);

                commands.Run("View.FollowPlayback");
                Assert.False(settings.Settings.Follow.FollowPlayback);
                commands.Run("View.FollowPlayback");
                Assert.True(settings.Settings.Follow.FollowPlayback);
                commands.Run("View.FollowPlayback");
                Assert.Equal(new[] { "Follow playback off", "Follow playback on", "Follow playback off" }, notices);
                SettingsFileService.SaveAtomic(path, settings.Settings);
            }
            Assert.False(SettingsFileService.Load(path).Follow.FollowPlayback);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheHotkeyIsInTheCatalogueOnAKeyNothingElseUses()
    {
        var action = HotkeyCatalog.All.Single(a => a.Id == "View.FollowPlayback");
        Assert.Equal("Ctrl+Shift+F", action.DefaultGesture);
        Assert.Single(HotkeyCatalog.All, a => a.DefaultGesture == "Ctrl+Shift+F" || a.DefaultGesture2 == "Ctrl+Shift+F");
    }

    [Fact]
    public void BothPreferencesRowsAreInTheCatalogueAndSearchable()
    {
        var rows = SettingsCatalog.Build(new AppSettings());
        var master = rows.Single(r => r.Key == "follow.playback");
        var resume = rows.Single(r => r.Key == "follow.resume");
        Assert.Equal("View.FollowPlayback", master.HotkeyAction);
        Assert.Equal((1d, 10d), (resume.Min, resume.Max));
        Assert.Contains("follow", master.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resume", resume.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scroll", resume.SearchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A score in a scroll viewer following a fake-engine session on a fake clock.</summary>
    private sealed class ScoreRig
    {
        private readonly AvaloniaPlaybackSessionTests.FakeEngine _engine = new();
        private readonly AvaloniaPlaybackSession _session;
        private readonly Window _window;
        private readonly ScorePreviewView _score = new();
        private readonly ScoreTimeline _timeline;

        public ScoreRig()
        {
            var project = FullDemoSongFactory.Create();
            _session = new AvaloniaPlaybackSession(_engine, new AvaloniaUiThread(Dispatcher.UIThread), () => project);
            _score.Show(new SongDocument(() => null) { Project = project });
            Settings = new AppSettings();
            new ScoreSettingsApplier(_score).Apply(Settings);
            _score.FollowHold.Now = () => Now;
            Viewer = new ScrollViewer { Content = _score, Width = 900, Height = 600 };
            _window = new Window { Content = Viewer, Width = 900, Height = 600 };
            _window.Show();
            _score.Refresh();
            Dispatcher.UIThread.RunJobs();
            _score.BindPlayback(_session);
            _session.PlayOrPause();
            _timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
            _engine.RaiseTimelineChanged(_timeline);
        }

        public AppSettings Settings { get; }
        public ScrollViewer Viewer { get; }
        public long Now { get; set; }

        public void Wheel() =>
            _score.RaiseEvent(new PointerWheelEventArgs(_score, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), _window,
                new Point(200, 150), 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, new Vector(0, -10))
            { RoutedEvent = InputElement.PointerWheelChangedEvent });

        /// <summary>Plays bars <paramref name="first"/> to <paramref name="last"/> one frame each, then lets a glide settle.</summary>
        public void PlayBars(int first, int last)
        {
            for (var bar = first; bar <= last; bar++)
            {
                var span = _timeline.Bars[bar];
                _engine.Report(new PlaybackPosition { Bar = bar, ElapsedMs = span.StartMs + (span.EndMs - span.StartMs) / 2 });
                Frame();
            }
            for (var settle = 0; settle < 60; settle++) Frame();
        }

        private void Frame()
        {
            _session.PumpFrame();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }

        public void Close()
        {
            _window.Close();
            _session.Dispose();
        }
    }
}
