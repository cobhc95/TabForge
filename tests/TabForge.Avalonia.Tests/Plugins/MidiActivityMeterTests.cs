using System;
using System.Diagnostics;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.LiveInput;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the MIDI activity on the FX window's track meters: the blink is full at a note-on and fades out over the decay, nothing shows before a
///   note-on or after the fade, and a track with no audio input shows it in the Input bar (the tooltip says MIDI in) while an armed audio input
///   keeps its audio level.
/// Does not own: the audio levels (TrackFxStripTests, LiveInputStripTests) or the engine's note stamps (AudioEngineClient.Write).
/// Tests: LiveStripRules.MidiActivity, TrackFxStrip.Tick, LiveStripMeters.
/// </summary>
public class MidiActivityMeterTests
{
    private const long Frequency = 1_000_000;

    [Fact]
    public void TheBlinkIsFullAtTheNoteOnAndFadesOutOverTheDecay()
    {
        var noteOn = 5_000_000L;
        Assert.Equal(1, LiveStripRules.MidiActivity(noteOn, noteOn, Frequency));
        Assert.Equal(0.5, LiveStripRules.MidiActivity(noteOn, noteOn + (long)(LiveStripRules.MidiDecayMs / 2 * 1000), Frequency), 6);
        Assert.Equal(0, LiveStripRules.MidiActivity(noteOn, noteOn + (long)(LiveStripRules.MidiDecayMs * 1000), Frequency));
        Assert.Equal(0, LiveStripRules.MidiActivity(noteOn, noteOn - 1, Frequency));   // a note still to come
        Assert.Equal(0, LiveStripRules.MidiActivity(0, noteOn, Frequency));            // none yet
    }

    [AvaloniaFact]
    public void ATrackWithoutAudioInputShowsTheMidiBlinkInItsInputBar()
    {
        var track = FxTestKit.Track(FxChainWindow.SampleChain());
        var engine = new FxTestKit.FakeEngine();
        var window = FxTestKit.Window(track, FxTestKit.Host(track, engine));
        window.Show();
        var strip = window.Strip!;
        strip.Tick(0, DateTime.UtcNow);
        Assert.Equal(0, strip.Meters.MidiLevel);   // no MIDI yet: the meter is as before

        engine.NoteOn = Stopwatch.GetTimestamp();
        strip.Tick(0, DateTime.UtcNow);
        Assert.True(strip.Meters.MidiLevel > 0.5, $"level {strip.Meters.MidiLevel}");
        Assert.StartsWith("MIDI in", strip.Meters.Tip());

        engine.NoteOn = Stopwatch.GetTimestamp() - Stopwatch.Frequency;   // a second ago: faded out
        strip.Tick(0, DateTime.UtcNow);
        Assert.Equal(0, strip.Meters.MidiLevel);
        Assert.StartsWith("Input", strip.Meters.Tip());
        window.Close();
    }
}
