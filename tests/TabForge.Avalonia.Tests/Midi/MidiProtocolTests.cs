using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the values of the named MIDI protocol numbers (status kinds, controllers) and the one per-channel reset order.
// Does not own: what the senders do with them (CompiledMidiGoldenTests pins the compiler's bytes).
// Tests: MidiStatus, MidiCc, MidiPanic.
public sealed class MidiProtocolTests
{
    [Fact]
    public void StatusKindsAreTheMidiOneValues()
    {
        Assert.Equal(new[] { 0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0, 0xE0 },
            new[] { MidiStatus.NoteOff, MidiStatus.NoteOn, MidiStatus.PolyPressure, MidiStatus.ControlChange, MidiStatus.ProgramChange, MidiStatus.ChannelPressure, MidiStatus.PitchBend });
        Assert.Equal(0xB0, MidiStatus.KindOf(0xB5));
        Assert.Equal(5, MidiStatus.ChannelOf(0xB5));
    }

    [Fact]
    public void ControllersAreTheMidiOneNumbers()
    {
        Assert.Equal(new[] { 0, 1, 6, 7, 10, 11, 32, 38, 64, 91, 93, 100, 101, 120, 121, 123 },
            new[] { MidiCc.BankSelectMsb, MidiCc.Modulation, MidiCc.DataEntryMsb, MidiCc.Volume, MidiCc.Pan, MidiCc.Expression, MidiCc.BankSelectLsb, MidiCc.DataEntryLsb,
                MidiCc.Sustain, MidiCc.Reverb, MidiCc.Chorus, MidiCc.RpnLsb, MidiCc.RpnMsb, MidiCc.AllSoundOff, MidiCc.ResetAllControllers, MidiCc.AllNotesOff });
    }

    [Fact]
    public void AChannelResetSendsAllNotesOffThenAllSoundOff() =>
        Assert.Equal(new[] { 123, 120 }, MidiPanic.ResetControllers.ToArray());
}
