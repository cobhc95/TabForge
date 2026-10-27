using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using TabForge.Avalonia.Transport;
using TabForge.Models;
using TabForge.Playback;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the proof, through a real PlaybackEngine behind an AvaloniaPlaybackSession, that the count-in clicks before the first note, that a loop
///     area wraps back to its first bar, and that the speed trainer raises the tempo at each wrap.
/// Does not own: the audible output (a capture sink takes the MIDI), the frame timer (the test pumps frames itself), the transport bar.
/// Tests: this class.
/// </summary>
[Collection(TimingCollection.Name)]   // the tests pump a wall-clock engine: run alone so scheduler contention cannot stretch a frame and make the playhead overstep a section start
public class TransportLoopCountInTests
{
    private const int FastTempo = 480;           // one 4/4 bar is half a second
    private const int Bars = 6;
    private const int SongNote = 64;
    private const double TimeoutMs = 6000;
    private const int FrameSleepMs = 5;

    [Fact]
    public void CountInPlaysOneBarOfClicksBeforeTheFirstNote()
    {
        var project = Song();
        var sink = new CaptureSink();
        using var engine = Engine(sink);
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.SetCountIn(true);
        session.PlayOrPause();
        Assert.True(WaitFor(session, () => sink.FirstSongNoteMs is not null), "the song never played a note");
        var clicksBefore = sink.ClicksBefore(sink.FirstSongNoteMs!.Value);
        Assert.Equal(4, clicksBefore);
    }

    [Fact]
    public void PlayWhileAStartCompilesDoesNotBeginAgain()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var transport = new PlaybackTransport(engine);
        transport.Play(new SongProject());
        transport.Play(new SongProject());   // the first start still compiles: no second begin, no restart
        Assert.Equal(1, engine.Starts);
    }

    [Fact]
    public void VolumeChangesWhilePlayingDoNotRestartTheSchedule()
    {
        var project = Song();
        var sink = new CaptureSink();
        using var engine = Engine(sink);
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.ApplyClickOptions(new TabForge.Services.AudioSettings { MetronomeBoost = false });
        session.PlayOrPause();
        Assert.True(WaitFor(session, () => session.Transport.Timeline is not null), "the song never started");
        var restarts = 0;
        engine.TimelineChanged += _ => restarts++;
        // A volume drag applies the live click voice, not a recompile: no new timeline, no hiccup, no cut count-in.
        session.ApplyClickOptions(new TabForge.Services.AudioSettings { MetronomeBoost = false, MetronomeVolume = 50 });
        Assert.Equal(0, restarts);
        Assert.True(session.IsPlaying, "the song stopped");
        Assert.True(WaitFor(session, () => sink.SongNotes.Count >= 1), "no notes after the volume change " + sink.Dump());
    }

    /// <summary>A record count-in asked for while paused is held, not dropped: resuming plays the bar before the song goes on.</summary>
    [Fact]
    public void ARecordCountInRequestedWhilePausedPlaysOnResume()
    {
        var project = Song();
        var sink = new CaptureSink();
        using var engine = Engine(sink);
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.ApplyClickOptions(new TabForge.Services.AudioSettings { MetronomeBoost = false });
        session.PlayOrPause();
        Assert.True(WaitFor(session, () => sink.SongNotes.Count >= 1), "no notes");
        session.PlayOrPause();   // pause: the scheduler holds
        engine.RequestRecordCountIn();
        session.PlayOrPause();   // resume: the held bar plays first
        Assert.True(WaitFor(session, () => sink.ClicksBefore(double.MaxValue) >= 4), "the paused record count-in was dropped " + sink.Dump());
    }

    /// <summary>A record count-in asked for while stopped never leaks into the next start: stopping clears the request,
    /// so the run begins with no stray bar of clicks. (No scheduler runs between the request and the stop, so the handover is exact.)</summary>
    [Fact]
    public void ARecordCountInClearedByStopNeverPlaysInTheNextStart()
    {
        var project = Song();
        var sink = new CaptureSink();
        using var engine = Engine(sink);
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.ApplyClickOptions(new TabForge.Services.AudioSettings { MetronomeBoost = false });
        engine.RequestRecordCountIn();
        session.Stop();   // clears the untaken request
        session.PlayOrPause();
        Assert.True(WaitFor(session, () => sink.SongNotes.Count >= 1), "no notes after the start " + sink.Dump());
        Assert.Equal(0, sink.ClicksBefore(sink.SongNotes[0]));
    }

    [Fact]
    public void CountInHonoursTheLengthSetting()
    {
        var project = Song();
        var sink = new CaptureSink();
        using var engine = Engine(sink);
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.ApplyClickOptions(new TabForge.Services.AudioSettings { CountInBars = 2, MetronomeBoost = false });
        session.SetCountIn(true);
        session.PlayOrPause();
        Assert.True(WaitFor(session, () => sink.FirstSongNoteMs is not null), "the song never played a note");
        Assert.Equal(8, sink.ClicksBefore(sink.FirstSongNoteMs!.Value));
    }

    [Fact]
    public void LoopOverBarsThreeAndFourWrapsBackToBarThree()
    {
        var project = Song();
        var sink = new CaptureSink();
        using var engine = Engine(sink);
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.SetLoopRange(2, 3);
        session.SetLoop(true);
        session.Seek(2, 0);
        session.PlayOrPause();
        var bars = new List<int>();
        Assert.True(WaitFor(session, () =>
        {
            var bar = session.Transport.PlayheadBar;
            if (bar >= 0 && (bars.Count == 0 || bars[^1] != bar)) bars.Add(bar);
            return bars.Count >= 3;
        }), "bars seen: " + string.Join(",", bars));
        Assert.True(WaitFor(session, () => sink.HeardBars.Count >= 3), "heard: " + string.Join(",", sink.HeardBars));
        Assert.Equal(new[] { 2, 3, 2 }, sink.HeardBars.Take(3));   // the sound wraps
        Assert.Equal(new[] { 2, 3, 2 }, bars.Take(3));             // and so does the drawn playhead
    }

    [Fact]
    public void LoopWithCountInEachLoopClicksBeforeEveryRepeat()
    {
        var project = Song();
        var sink = new CaptureSink();
        using var engine = Engine(sink);
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.ApplyClickOptions(new TabForge.Services.AudioSettings { MetronomeBoost = false, LoopCountInEachLoop = true });
        session.SetLoopRange(2, 3);
        session.SetLoop(true);
        session.Seek(2, 0);
        session.PlayOrPause();
        // Two song notes in the loop (bars 3 and 4), then clicks, then bar 3's note again.
        Assert.True(WaitFor(session, () => sink.SongNotes.Count >= 3), "too few notes");
        var notes = sink.SongNotes.ToArray();
        Assert.True(sink.ClicksBetween(notes[1], notes[2]) >= 4, "no count-in before the repeat");
    }

    [Fact]
    public void SpeedTrainerRaisesTheTempoEachLoop()
    {
        var project = Song();
        var sink = new CaptureSink();
        using var engine = Engine(sink);
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.ApplyClickOptions(new TabForge.Services.AudioSettings { MetronomeBoost = false, LoopSpeedTrainer = true, LoopTrainerFrom = 50, LoopTrainerTo = 100, LoopTrainerStep = 50 });
        session.SetLoopRange(2, 2);
        session.SetLoop(true);
        session.Seek(2, 0);
        session.PlayOrPause();
        // A one-bar loop: the gap between repeats is the bar length at the loop's rate (50 % then 100 %).
        Assert.True(WaitFor(session, () => sink.SongNotes.Count >= 3), "too few notes");
        var notes = sink.SongNotes.ToArray();
        var first = notes[1] - notes[0];
        var second = notes[2] - notes[1];
        Assert.True(second < first * 0.75, $"the second loop ({second:0} ms) is not faster than the first ({first:0} ms)");
    }

    /// <summary>In the real window, the transport bar's count-in and loop buttons reach the engine options of the next Play.</summary>
    [global::Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransportButtonsInTheWindowTurnOnCountInAndLoopForThePlay(bool withSettingsFile)
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-transport-loop-" + Guid.NewGuid().ToString("N"));
        using var settings = withSettingsFile ? MainWindowSettings.Open(System.IO.Path.Combine(folder, "settings.json")) : null;
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var sessions = new AvaloniaPlaybackSessions(new TabForge.Avalonia.Platform.AvaloniaUiThread(global::Avalonia.Threading.Dispatcher.UIThread), () => engine);
        var document = new TabForge.Documents.SongDocument(() => null) { Project = Song() };
        var window = new MainWindow(settings, first: document, playback: sessions) { Width = 1600, Height = 1000 };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var buttons = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<TabForge.Avalonia.Platform.TransportBarView.TransportButton>().ToList();
        TabForge.Avalonia.Platform.TransportBarView.TransportButton Button(string name) => buttons.Single(b => b.Item.Name == name);
        Assert.True(Button("Click and beat count-in").IsEnabled, "count-in disabled");
        Assert.True(Button("Loop enable").IsEnabled, "loop disabled");
        Button("Click and beat count-in").Pressed!();
        Button("Loop enable").Pressed!();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Button("Play or pause").Pressed!();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(engine.LastOptions!.CountIn, "count-in not in the start options");
        Assert.True(engine.LastOptions!.Loop, "loop not in the start options");
        window.Close();
    }

    /// <summary>The real window over a real engine: count-in clicks come before the first note, and the loop wraps from the last bar to the first.</summary>
    [global::Avalonia.Headless.XUnit.AvaloniaFact]
    public void RealEngineInTheWindowCountsInAndLoops()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-transport-loop-" + Guid.NewGuid().ToString("N"));
        using var settings = MainWindowSettings.Open(System.IO.Path.Combine(folder, "settings.json"));
        settings.Settings.Audio.MetronomeBoost = false;
        var sink = new CaptureSink();
        using var sessions = new AvaloniaPlaybackSessions(new TabForge.Avalonia.Platform.AvaloniaUiThread(global::Avalonia.Threading.Dispatcher.UIThread), () => Engine(sink));
        var document = new TabForge.Documents.SongDocument(() => null) { Project = Song() };
        var window = new MainWindow(settings, first: document, playback: sessions) { Width = 1600, Height = 1000 };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var buttons = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<TabForge.Avalonia.Platform.TransportBarView.TransportButton>().ToList();
        TabForge.Avalonia.Platform.TransportBarView.TransportButton Button(string name) => buttons.Single(b => b.Item.Name == name);
        Button("Click and beat count-in").Pressed!();
        Button("Loop enable").Pressed!();
        Button("Play or pause").Pressed!();
        var session = sessions.For(document)!;
        var bars = new List<int>();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalMilliseconds < TimeoutMs && !(bars.Count > 1 && bars[^1] < bars[^2]))
        {
            session.PumpFrame();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var bar = session.Transport.PlayheadBar;
            if (bar >= 0 && (bars.Count == 0 || bars[^1] != bar)) bars.Add(bar);
            Thread.Sleep(FrameSleepMs);
        }
        Assert.True(sink.FirstSongNoteMs is not null, "no note played");
        Assert.Equal(4, sink.ClicksBefore(sink.FirstSongNoteMs!.Value));
        Assert.True(bars.Count > 1 && bars[^1] < bars[^2], "no wrap; bars seen: " + string.Join(",", bars));
        window.Close();
    }

    [global::Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoopTurnedOnWhilePlayingWrapsInTheWindow(bool area)
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-transport-loop-" + Guid.NewGuid().ToString("N"));
        using var settings = MainWindowSettings.Open(System.IO.Path.Combine(folder, "settings.json"));
        var sink = new CaptureSink();
        using var sessions = new AvaloniaPlaybackSessions(new TabForge.Avalonia.Platform.AvaloniaUiThread(global::Avalonia.Threading.Dispatcher.UIThread), () => Engine(sink));
        var document = new TabForge.Documents.SongDocument(() => null) { Project = Song() };
        var window = new MainWindow(settings, first: document, playback: sessions) { Width = 1600, Height = 1000 };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var arrangement = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<TabForge.Avalonia.Views.ArrangementView>().Single();
        if (area) arrangement.Input.Selection.SetRange(0, 1, 2, SelectionOrigin.Timeline);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var buttons = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<TabForge.Avalonia.Platform.TransportBarView.TransportButton>().ToList();
        buttons.Single(b => b.Item.Name == "Play or pause").Pressed!();
        var session = sessions.For(document)!;
        var bars = new List<int>();
        var clock = Stopwatch.StartNew();
        var pressed = false;
        while (clock.Elapsed.TotalMilliseconds < TimeoutMs && !(bars.Count > 1 && bars[^1] < bars[^2]))
        {
            session.PumpFrame();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var bar = session.Transport.PlayheadBar;
            if (bar >= 0 && (bars.Count == 0 || bars[^1] != bar)) bars.Add(bar);
            if (!pressed && bar >= 1) { pressed = true; buttons.Single(b => b.Item.Name == "Loop enable").Pressed!(); }
            Thread.Sleep(FrameSleepMs);
        }
        Assert.True(bars.Count > 1 && bars[^1] < bars[^2], "no wrap; bars seen: " + string.Join(",", bars) + " playing " + session.IsPlaying);
        window.Close();
    }

    /// <summary>A start inside the song or inside a bar still clicks a full bar first.</summary>
    [Theory]
    [InlineData(0, 4)]
    [InlineData(2, 0)]
    [InlineData(2, 4)]
    public void CountInClicksABarBeforeAStartInsideTheSong(int bar, int cell)
    {
        var project = Song();
        var sink = new CaptureSink();
        using var engine = Engine(sink);
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.ApplyClickOptions(new TabForge.Services.AudioSettings { MetronomeBoost = false });
        session.SetCountIn(true);
        session.Seek(bar, cell);
        session.PlayOrPause();
        Assert.True(WaitFor(session, () => sink.SongNotes.Count >= 1), "no note " + sink.Dump());
        Assert.True(sink.ClicksBefore(sink.SongNotes[0]) == 4, sink.Dump());
    }

    /// <summary>A count-in before a section while playing: the drawn playhead waits at the section start during the clicks. The only step back it may
    /// take is the engine's own count-in jump to that start (PlayheadClock's explicit jump); once the playhead has passed the start by a frame's worth
    /// before the count-in engages, that jump back is how it reaches the hold. Any other step back fails.</summary>
    [Fact]
    public void SectionCountInHoldsTheDrawnPlayheadAtTheSectionStart()
    {
        const int sectionBar = 2;
        var project = Song();
        project.Markers.Add(new MarkerModel { MeasureIndex = sectionBar, Title = "B" });
        var sink = new CaptureSink();
        using var engine = Engine(sink);
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.ApplyClickOptions(new TabForge.Services.AudioSettings { MetronomeBoost = false, CountInEachSection = true, CountInOnlyAtSongStart = false });
        session.SetCountIn(true);
        session.Seek(1, 0);
        session.PlayOrPause();
        var previous = double.NegativeInfinity;
        var backSteps = new List<(double From, double To, int Bar, int Cell)>();
        Assert.True(WaitFor(session, () =>
        {
            var drawn = session.Playhead;
            if (session.Transport.Timeline is not null && session.Transport.PlayheadBar >= 0 && !session.Transport.StartPending)
            {
                if (drawn.ElapsedMs < previous - 1e-6) backSteps.Add((previous, drawn.ElapsedMs, drawn.Bar, drawn.Cell));
                previous = drawn.ElapsedMs;
            }
            return drawn.Bar > sectionBar;
        }), "the section never played " + sink.Dump());
        Assert.True(sink.SongNotes.Count >= 2 && sink.ClicksBetween(sink.SongNotes[0], sink.SongNotes[1]) >= 4, "no count-in before the section " + sink.Dump());
        var unexpected = backSteps.Where(step => step.Bar != sectionBar || step.Cell != 0)
            .Select(step => $"{step.From:0.0} -> {step.To:0.0} (bar {step.Bar + 1}, cell {step.Cell})").ToList();
        Assert.True(backSteps.Count <= 1 && unexpected.Count == 0,
            "the drawn playhead stepped back outside the count-in's jump to the section start: " + string.Join("; ", unexpected));
    }

    // An engine whose clicks are single hits (no boost layers), so each click is one recorded note-on.
    private static PlaybackEngine Engine(IMidiOutput sink) => new(sink, new PlaybackPreferences { MetronomeBoost = false });

    private static bool WaitFor(AvaloniaPlaybackSession session, Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalMilliseconds < TimeoutMs)
        {
            session.PumpFrame();
            if (done()) return true;
            Thread.Sleep(FrameSleepMs);
        }
        return false;
    }

    // One guitar track of quarter-note bars, each bar a single note on its first beat (bar n plays SongNote + n), at a fast tempo.
    private static SongProject Song()
    {
        var project = new SongProject { Tempo = FastTempo };
        var track = new TrackModel { StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 }, MidiChannel = 0 };
        for (var bar = 0; bar < Bars; bar++)
        {
            var measure = new MeasureModel { Number = bar + 1 };
            measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = bar, MidiValue = SongNote + bar });
            measure.Cells[0].DurationDenominator = 4;
            track.Measures.Add(measure);
        }
        project.Tracks.Add(track);
        return project;
    }

    /// <summary>Records the wall time of each song note-on and each click (SendMetronome note-on).</summary>
    private sealed class CaptureSink : IMidiOutput
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _gate = new();
        private readonly List<double> _clicks = new();
        private readonly List<double> _notes = new();
        private readonly List<int> _bars = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();

        public void Send(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) != 0x90 || data2 == 0 || data1 < SongNote || data1 >= SongNote + Bars) return;
            lock (_gate) { _notes.Add(_clock.Elapsed.TotalMilliseconds); _bars.Add(data1 - SongNote); }
        }

        public void SendMetronome(int deviceId, int status, int data1, int data2)
        {
            if ((status & 0xF0) != 0x90 || data2 == 0) return;
            lock (_gate) _clicks.Add(_clock.Elapsed.TotalMilliseconds);
        }

        public double? FirstSongNoteMs { get { lock (_gate) return _notes.Count > 0 ? _notes[0] : null; } }
        /// <summary>The bar of each song note heard, in order.</summary>
        public IReadOnlyList<int> HeardBars { get { lock (_gate) return _bars.ToArray(); } }
        public IReadOnlyList<double> SongNotes { get { lock (_gate) return _notes.ToArray(); } }
        public int ClicksBefore(double ms) { lock (_gate) return _clicks.Count(at => at < ms); }
        public int ClicksBetween(double from, double to) { lock (_gate) return _clicks.Count(at => at > from && at < to); }
        /// <summary>The recorded times, for a failure message.</summary>
        public string Dump() { lock (_gate) return "clicks " + string.Join(",", _clicks.Select(c => c.ToString("0"))) + " notes " + string.Join(",", _notes.Take(12).Select(c => c.ToString("0"))); }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}
