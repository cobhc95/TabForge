using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the live key highlight: the key-state model (note-on presses, note-off and velocity-0 note-on release, All notes off clears, a key held
//   twice needs two releases, the pitches sounding at a beat) and the piano roll lighting a key the MIDI keyboard holds, repainting once for many messages.
// Does not own: the MIDI input itself (MidiInputHub).
// Tests: PianoRollKeyState, PianoRollPlayheadLayer.
public sealed class PianoRollKeyStateTests
{
    [Fact]
    public void NoteOnPressesAndNoteOffOrVelocityZeroReleases()
    {
        var keys = new PianoRollKeyState();
        Assert.True(keys.Message(0x90, 60, 100));
        Assert.True(keys.IsDown(60));
        Assert.True(keys.Message(0x91, 64, 80));   // any channel
        Assert.True(keys.Message(0x90, 64, 0));    // velocity 0 is a release
        Assert.False(keys.IsDown(64));
        Assert.True(keys.Message(0x90, 60, 90));   // held from two devices
        Assert.True(keys.Message(0x80, 60, 0));
        Assert.True(keys.IsDown(60));
        Assert.True(keys.Message(0x80, 60, 0));
        Assert.False(keys.IsDown(60));
        Assert.False(keys.Message(0x80, 60, 0));   // nothing held: no change, no repaint
        Assert.False(keys.Message(0xE0, 0, 64));   // pitch bend: not a key
        keys.Message(0x90, 48, 100);
        keys.Message(0x90, 52, 100);
        Assert.True(keys.Message(0xB0, 123, 0));   // All notes off
        var down = new bool[128];
        keys.CopyTo(down);
        Assert.DoesNotContain(true, down);
    }

    [Fact]
    public void TheSoundingKeysAreTheUnmutedNotesUnderTheBeat()
    {
        var notes = new[] { new PianoRollNote(0, 1, 60, 90), new PianoRollNote(0.5, 1, 64, 90), new PianoRollNote(0.5, 1, 67, 90, Muted: true), new PianoRollNote(2, 1, 72, 90) };
        var sounding = new bool[128];
        PianoRollKeyState.Sounding(notes, 0.75, sounding);
        Assert.True(sounding[60]);
        Assert.True(sounding[64]);
        Assert.False(sounding[67]);
        Assert.False(sounding[72]);
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void StepInputAuditionsItsOwnNoteOnlyWhenMonitoringDoesNotSoundTheKey(bool monitorSounds, int auditions)
    {
        var document = PianoRollSongs.Midi();
        var view = new PianoRollView(new PianoRollHost { Document = () => document, MonitorSounds = _ => monitorSounds });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        var heard = 0;
        view.Surface.Audition = _ => heard++;
        view.Surface.StepInput = true;
        view.StepNote(60, 100);
        Assert.Equal(auditions, heard);
        window.Close();
    }

    [AvaloniaFact]
    public void AKeyHeldOnTheMidiKeyboardLightsOnThePianoRollAndManyMessagesRepaintOnce()
    {
        var document = PianoRollSongs.Midi();
        var view = new PianoRollView(new PianoRollHost { Document = () => document });
        var window = new Window { Width = 900, Height = 600, Content = view };
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        for (var i = 0; i < 50; i++) { view.MidiMessage(0x90, 60, 100); view.MidiMessage(0x80, 60, 0); }   // a burst: one posted repaint
        view.MidiMessage(0x90, 60, 100);
        Dispatcher.UIThread.RunJobs();
        using (window.CaptureRenderedFrame()) { }
        Assert.True(view.Surface.Keys.IsDown(60));
        Assert.Equal(1, view.Surface.Playhead.LitKeys);
        view.MidiMessage(0x80, 60, 0);
        Dispatcher.UIThread.RunJobs();
        using (window.CaptureRenderedFrame()) { }
        Assert.Equal(0, view.Surface.Playhead.LitKeys);
        window.Close();
    }
}
