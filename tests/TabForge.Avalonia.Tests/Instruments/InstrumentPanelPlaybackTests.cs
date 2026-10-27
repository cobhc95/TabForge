using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the instrument strip's playback-note refresh. A paused seek still fills the playing notes at the new position (the only event it gets is
/// PlayheadBarChanged, with no frame), while a bar-change frame does not fill twice.
/// Does not own: the note-list computation (InstrumentPlaybackLists) or the drawing (InstrumentPanelView).
/// Tests: this class.
/// </summary>
[Collection(TimingCollection.Name)]   // runs a wall-clock playback engine; keep it out of the parallel playhead tests
public class InstrumentPanelPlaybackTests
{
    [AvaloniaFact]
    public void SeekWhilePausedStillRefreshesThePlayingNotes()
    {
        var project = FullDemoSongFactory.Create();
        var document = new SongDocument(() => null) { Project = project };
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        using var engine = new PlaybackEngine(new RoutedMidiOutput(new NullSink(), client));
        using var session = new AvaloniaPlaybackSession(engine, AvaloniaUiThread.ForApplication(), () => project);
        var score = new ScorePreviewView();
        score.Show(document);
        var strip = new InstrumentPanelStrip(score, activePlayback: () => session);
        var window = new Window { Width = 1100, Height = 500, Content = strip };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        session.PlayOrPause();
        for (var frame = 0; frame < 20; frame++)
        {
            session.PumpFrame();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        for (var wait = 0; wait < 200 && session.Transport.Timeline is null; wait++)
        {
            session.PumpFrame();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        session.PlayOrPause();   // pause: IsPlaying stays true, the frames stop (the paused-seek case)
        Dispatcher.UIThread.RunJobs();
        Assert.True(session.IsPaused);
        var before = strip.Panel.LitMidis().ToArray();

        session.Seek(10, 0);     // only PlayheadBarChanged fires; without that subscription this leaves the old notes lit (RhyL starts at bar 9, so bar 10 differs from the bar-0 preview; bar 2 is still before its first notes)
        Dispatcher.UIThread.RunJobs();
        var after = strip.Panel.LitMidis().ToArray();

        Assert.NotEqual(before, after);
        Assert.NotEmpty(after);
        session.Stop();
        window.Close();
    }

    /// <summary>A MIDI sink that drops every message; the test checks the panel, not the sound.</summary>
    private sealed class NullSink : IMidiOutput
    {
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}
