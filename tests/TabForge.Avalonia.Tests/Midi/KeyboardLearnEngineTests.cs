using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Keyboard Learn engine checks: wait-gating pauses at the awaited chord and resumes on its press, the hands filter judges one hand,
//   the loop range bounds the judged run, and the pane and pop-out bars stay in sync through the shared engine.
// Does not own: the engine, the bars, the strip or playback (the session stub holds a fixed moment).
// Tests: this class.
public class KeyboardLearnEngineTests
{
    private static (AvaloniaPlaybackSessions Sessions, SongDocument Document, AvaloniaPlaybackSession Session) Held(int bar = 12)
    {
        var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(global::Avalonia.Threading.Dispatcher.UIThread), () => new PlayingStateStub(bar, 0.5));
        var song = FullDemoSongFactory.Create();
        KeyboardPlayAlongStub.WriteRiff(song.Tracks[0], 12);
        var document = new SongDocument(() => null) { Project = song };
        var session = sessions.For(document)!;
        session.PlayOrPause();
        session.PumpFrame();
        return (sessions, document, session);
    }

    private static KeyboardLearnEngine EngineWithSettings(SongDocument document, AvaloniaPlaybackSession session, bool wait = false)
    {
        var engine = new KeyboardLearnEngine();
        var settings = new AppSettings();
        if (wait) settings.Learn.WaitForNotes = true;
        engine.AttachSettings(settings, null);
        engine.BindSong(document, session);
        return engine;
    }

    [AvaloniaFact]
    public void WaitGating_PausesAtChordAndResumesOnItsPress()
    {
        var held = Held();
        using var sessions = held.Sessions;
        using var engine = EngineWithSettings(held.Document, held.Session, wait: true);
        var bar = new KeyboardModeBar(new WrapPanel());
        var strip = new KeyboardStripView();

        engine.Follow(strip, bar);
        Assert.True(engine.IsWaiting);
        Assert.Equal(1, engine.Pauses);
        Assert.True(held.Session.IsPaused);
        Assert.True(strip.Waiting);

        var awaited = new List<int>();
        for (var m = 0; m < 128; m++) if (engine.Awaited.Has(m)) awaited.Add(m);
        Assert.NotEmpty(awaited);
        Assert.NotNull(engine.Judge);
        var judge = engine.Judge!;
        foreach (var m in awaited)
            engine.PressKey(m, true, judge.Results.First(r => !r.IsResolved && r.Expected.Midi == m).Expected.OnsetSec);

        engine.Follow(strip, bar);
        Assert.False(engine.IsWaiting);
        Assert.Equal(1, engine.Resumes);
        Assert.False(held.Session.IsPaused);
        Assert.Same(judge, strip.Judge);
        Assert.True(strip.Score is { Hits: > 0 });
        Assert.False(strip.Waiting);
    }

    [AvaloniaFact]
    public void HandsFilter_LeftHandJudgesOnlyLeftHandNotes()
    {
        var held = Held();
        using var sessions = held.Sessions;
        using var engine = new KeyboardLearnEngine();
        var settings = new AppSettings();
        engine.AttachSettings(settings, null);
        engine.BindSong(held.Document, held.Session);
        settings.Learn.Hands = KeyboardHands.FilterNames[(int)KeyboardHandsFilter.Left];
        var bar = new KeyboardModeBar(new WrapPanel());
        var strip = new KeyboardStripView();

        engine.Follow(strip, bar);
        Assert.NotNull(engine.Judge);
        var judge = engine.Judge!;
        Assert.NotEmpty(judge.Results);
        var left = new HashSet<int>(strip.Source!.Notes.Where(n => n.LeftHand).Select(n => n.Fret));
        Assert.NotEmpty(left);
        Assert.True(strip.Source.Notes.Count > judge.Results.Count);
        Assert.All(judge.Results, r => Assert.Contains(r.Expected.Midi, left));
    }

    [AvaloniaFact]
    public void LoopRange_BoundsTheJudgedRun()
    {
        var held = Held();
        using var sessions = held.Sessions;
        using var engine = EngineWithSettings(held.Document, held.Session);
        held.Session.SetLoop(true);
        var moment = held.Session.Transport.PlayheadMs;
        engine.LoopSecondsProvider = () => (0, moment / 1000 + 0.5);
        var bar = new KeyboardModeBar(new WrapPanel());
        var strip = new KeyboardStripView();

        engine.Follow(strip, bar);
        Assert.NotNull(engine.Judge);
        var loopJudge = engine.Judge!;
        Assert.NotNull(engine.Loop);
        Assert.Equal(engine.Loop, strip.Loop);
        Assert.NotEmpty(loopJudge.Results);
        Assert.All(loopJudge.Results, r => Assert.InRange(r.Expected.OnsetSec, moment / 1000 - 0.06, moment / 1000 + 0.5));
        Assert.True(strip.Source!.Notes.Max(n => n.OnsetMs) > moment + 500);
    }

    [AvaloniaFact]
    public void PopoutSync_BothBarsFollowTheSharedEngine()
    {
        using var engine = new KeyboardLearnEngine();
        var settings = new AppSettings();
        engine.AttachSettings(settings, null);
        var paneBar = new KeyboardModeBar(new WrapPanel());
        var popBar = new KeyboardModeBar(new WrapPanel());
        var strip = new KeyboardStripView();

        paneBar.Hands = KeyboardHandsFilter.Right;
        engine.ApplyBar(paneBar);
        Assert.Equal(KeyboardHands.FilterNames[(int)KeyboardHandsFilter.Right], settings.Learn.Hands);

        engine.Follow(strip, popBar);
        Assert.Equal(KeyboardHandsFilter.Right, popBar.Hands);

        engine.ToggleWait();
        engine.Follow(strip, paneBar);
        engine.Follow(strip, popBar);
        Assert.True(paneBar.WaitOn);
        Assert.True(popBar.WaitOn);
        Assert.True(settings.Learn.WaitForNotes);

        Assert.True(engine.SkipWait() == false); // nothing awaited while stopped
    }
}
