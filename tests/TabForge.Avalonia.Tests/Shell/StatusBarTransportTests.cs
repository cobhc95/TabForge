using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the status bar transport state word (Ready, Playing, Paused, Stopped, Recording, Count-in, Rendering, Exporting video), the missing MIDI light and note-length items,
//   the position text following a moving playhead (bar.beat and performed time through a tempo change), the cursor text when stopped, the tempo and key following the playhead,
//   and the update throttling.
// Does not own: the wording rules (StatusTextPresenter) or the playhead clock (PlayheadClockTests).
// Tests: this class.
public sealed class StatusBarTransportTests
{
    /// <summary>24 bars of 4/4 at 150; 130 and key 2 sharps from bar 9 (index 8), 140 from bar 17 (index 16).</summary>
    private static SongProject Song()
    {
        var project = new SongProject { Tempo = 150 };
        var track = new TrackModel { Name = "Keys", AudioInput = AudioInputs.Midi, StringTunings = new List<int> { 64, 59, 55, 50, 45, 40 } };
        for (var bar = 0; bar < 24; bar++)
        {
            var measure = new MeasureModel { Number = bar + 1 };
            measure.Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5, MidiValue = 60 });
            track.Measures.Add(measure);
        }
        track.Measures[8].TempoChange = 130;
        track.Measures[8].KeySignature = 2;
        track.Measures[16].TempoChange = 140;
        project.Tracks.Add(track);
        return project;
    }

    internal sealed class Rig : IDisposable
    {
        public readonly SongDocument Document;
        public readonly AvaloniaPlaybackSessions Sessions;
        public readonly AvaloniaPlaybackSessionTests.FakeEngine Engine = new();
        public readonly AvaloniaPlaybackSessionTests.FakeUiThread Ui = new();
        public readonly AvaloniaPlaybackSession Session;
        public readonly ScoreTimeline Timeline;
        public readonly MainWindowTabs Tabs;
        public readonly Window Owner = new();
        public readonly MainWindowStatusBar Status;
        public readonly LivePlayhead Live;
        public double NowMs;
        public int LiveChanges;

        public Rig(double countInMs = 0, SongProject? song = null, MainWindowSettings? settings = null)
        {
            Document = new SongDocument(() => null) { Project = song ?? Song() };
            Timeline = MidiTimelineBuilder.Build(Document.Project, new PlaybackOptions { RepeatExpansion = true, RespectMuteSolo = false });
            Timeline.CountInMs = countInMs;
            Sessions = new AvaloniaPlaybackSessions(Ui, () => Engine);
            Session = Sessions.For(Document)!;
            Tabs = new MainWindowTabs(Document, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
            Status = new MainWindowStatusBar(Tabs, new ScorePreviewView(), settings, null, Owner);
            Live = new LivePlayhead(() => Document, Sessions, nowMs: () => NowMs);
            Live.Changed += () => LiveChanges++;
            Status.FollowPlayhead(Live);
        }

        public string Text(string name) => Status.View.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == name).Text ?? "";

        public void Play()
        {
            Session.PlayOrPause();
            Engine.RaiseTimelineChanged(Timeline);
        }

        /// <summary>The scheduler reports the timeline millisecond and the frame timer ticks once; the simulated wall clock first moves on by <paramref name="wallMs"/> (past the throttle).</summary>
        public void At(double ms, double wallMs = 500)
        {
            NowMs += wallMs;
            var bar = Timeline.BarAt(ms);
            var fraction = bar.SlotFraction(ms);
            Engine.Report(new PlaybackPosition { Bar = bar.Bar, Cell = (int)(fraction * bar.Slots), BarFraction = fraction, ElapsedMs = ms });
            Ui.FrameTimer.Fire();
        }

        public void Dispose()
        {
            Status.Dispose();
            Live.Dispose();
            Tabs.Dispose();
            Sessions.Dispose();
            Owner.Close();
        }
    }

    [Fact]
    public void StateWordNamesEveryPhaseAndActivity()
    {
        Assert.Equal("Ready", MainWindowStatusBar.StateText(TransportPhase.Ready, false, false, false));
        Assert.Equal("Playing", MainWindowStatusBar.StateText(TransportPhase.Playing, false, false, false));
        Assert.Equal("Paused", MainWindowStatusBar.StateText(TransportPhase.Paused, false, false, false));
        Assert.Equal("Stopped", MainWindowStatusBar.StateText(TransportPhase.Stopped, false, false, false));
        Assert.Equal("Recording", MainWindowStatusBar.StateText(TransportPhase.Playing, true, false, false));
        Assert.Equal("Count-in…", MainWindowStatusBar.StateText(TransportPhase.CountIn, true, false, false));
        Assert.Equal("Rendering…", MainWindowStatusBar.StateText(TransportPhase.Stopped, false, true, false));
        Assert.Equal("Exporting video…", MainWindowStatusBar.StateText(TransportPhase.Stopped, false, true, true));
    }

    [AvaloniaFact]
    public void TheStatusTextFollowsTheTransportFromReadyToStopped()
    {
        using var rig = new Rig();
        Assert.Equal("Ready", rig.Text("StatusText"));
        rig.Play();
        rig.At(100);
        Assert.Equal("Playing", rig.Text("StatusText"));
        rig.Session.PlayOrPause();
        Assert.Equal("Paused", rig.Text("StatusText"));
        rig.Session.PlayOrPause();
        Assert.Equal("Playing", rig.Text("StatusText"));
        rig.Session.Stop();
        Assert.Equal("Stopped", rig.Text("StatusText"));
    }

    [AvaloniaFact]
    public void ActivitiesTakeTheStateWord()
    {
        using var rig = new Rig();
        rig.Status.Activity.Rendering = true;
        Assert.Equal("Rendering…", rig.Text("StatusText"));
        rig.Status.Activity.ExportingVideo = true;
        Assert.Equal("Exporting video…", rig.Text("StatusText"));
        rig.Status.Activity.ExportingVideo = false;
        rig.Status.Activity.Rendering = false;
        Assert.Equal("Ready", rig.Text("StatusText"));
        var recording = false;
        rig.Status.Activity.TransportRecording = () => recording;
        recording = true;
        rig.Status.Activity.Notify();
        Assert.Equal("Recording", rig.Text("StatusText"));
        recording = false;
        rig.Status.Activity.Notify();
        Assert.Equal("Ready", rig.Text("StatusText"));
    }

    [AvaloniaFact]
    public void TheCountInShowsUntilTheMusicStarts()
    {
        using var rig = new Rig(countInMs: 2000);
        rig.Play();
        rig.At(500);
        Assert.Equal("Count-in…", rig.Text("StatusText"));
        rig.At(2500);
        Assert.Equal("Playing", rig.Text("StatusText"));
    }

    [AvaloniaFact]
    public void NoMidiLightAndNoNoteLengthItemAreShown()
    {
        using var rig = new Rig();
        Assert.DoesNotContain(rig.Status.View.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "MIDI");
        Assert.Empty(rig.Status.View.GetLogicalDescendants().OfType<Ellipse>());
        rig.Play();
        rig.At(100);
        Assert.DoesNotContain("quarter", rig.Text("StatusMaster"));
        Assert.StartsWith("♩=150", rig.Text("StatusMaster"));
    }

    [AvaloniaFact]
    public void PlayingShowsThePlayheadBarBeatAndPerformedTimeThroughATempoChange()
    {
        using var rig = new Rig();
        rig.Play();
        rig.At(250);   // bar 1 (1600 ms each at 150), beat 1
        Assert.Equal("Bar 1.1  ·  0:00.2  ·  Keys", rig.Text("StatusPosition"));
        rig.At(7250);   // bar 5 starts at 6400; 850 ms into it is beat 3
        Assert.Equal("Bar 5.3  ·  0:07.2  ·  Keys", rig.Text("StatusPosition"));
        // Bar 9 (index 8) starts at 12800 and lasts 1846 ms at 130: bar 10 starts at 14646; 15550 is 904 ms into it, beat 2.
        rig.At(15550);
        Assert.Equal("Bar 10.2  ·  0:15.5  ·  Keys", rig.Text("StatusPosition"));
        Assert.StartsWith("♩=130", rig.Text("StatusMaster"));
    }

    [AvaloniaFact]
    public void TheCursorTextShowsWhenStoppedAndPausedWithTheTimeAtTheCursor()
    {
        using var rig = new Rig();
        Assert.Equal("Bar 1  ·  0:00.0  ·  Keys  ·  cell 1", rig.Text("StatusPosition"));
        rig.Document.Highlight.Set(0, 9);   // bar 10 starts 14.6 s in (8 bars at 1600 ms, one at 1846 ms)
        Assert.Equal("Bar 10  ·  0:14.6  ·  Keys  ·  cell 1", rig.Text("StatusPosition"));
        rig.Play();
        rig.At(250);
        Assert.StartsWith("Bar 1.1  ·  0:00.2", rig.Text("StatusPosition"));
        rig.Session.PlayOrPause();   // paused: the cursor again
        Assert.Equal("Bar 10  ·  0:14.6  ·  Keys  ·  cell 1", rig.Text("StatusPosition"));
        rig.Session.Stop();
        Assert.Equal("Bar 10  ·  0:14.6  ·  Keys  ·  cell 1", rig.Text("StatusPosition"));
    }

    [AvaloniaFact]
    public void TheTempoAndKeyFollowThePlayheadAndFallBackToTheCursor()
    {
        using var rig = new Rig();
        rig.Status.Scale.Settings.ReadFrom = ScaleReadFrom.Key;   // the key signature, which changes at bar 9
        rig.Play();
        rig.At(1000);
        Assert.Equal(("♩=150  ·", "C major ▾"), (rig.Text("StatusMaster"), rig.Text("StatusScaleText")));
        rig.At(12800 + 500);   // bar 9: 130 and two sharps
        Assert.Equal(("♩=130  ·", "D major ▾"), (rig.Text("StatusMaster"), rig.Text("StatusScaleText")));
        rig.At(12800 + 1846 * 8 + 500);   // a later bar: 140 after the second change
        Assert.StartsWith("♩=140", rig.Text("StatusMaster"));
        rig.Session.Stop();
        Assert.Equal(("♩=150  ·", "C major ▾"), (rig.Text("StatusMaster"), rig.Text("StatusScaleText")));
    }

    [AvaloniaFact]
    public void TheTextChangesAtMostAboutEightTimesASecondWhileFramesRunAtSixtyHertz()
    {
        using var rig = new Rig();
        rig.Play();
        var last = rig.Text("StatusPosition");
        var changes = 0;
        for (var frame = 1; frame <= 180; frame++)   // three simulated seconds at 60 frames a second
        {
            rig.At(1000 + frame * 1000.0 / 60, 1000.0 / 60);
            var now = rig.Text("StatusPosition");
            if (now != last) changes++;
            last = now;
        }
        Assert.InRange(changes, 6, 3 * 10);
        Assert.InRange(rig.LiveChanges, 6, 3 * 10 + 4);
    }
}
