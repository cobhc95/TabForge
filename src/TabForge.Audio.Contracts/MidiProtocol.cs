using System;

namespace TabForge;

// Owns: the names of the MIDI 1.0 protocol numbers the app sends and reads: the channel-voice status kinds (high nibble), the controller numbers it
//   uses, and the one order of the per-channel reset (all notes off, then all sound off) that the panics and stops send.
// Does not own: the data-byte maximum (MidiLimits), General MIDI program or drum numbers, or what each sender does with them.
// Tests: CompiledMidiGoldenTests (the compiler's bytes), MidiProtocolTests.

/// <summary>MIDI channel-voice status kinds: the high nibble of a status byte (OR the channel 0-15 into the low nibble).</summary>
public static class MidiStatus
{
    public const int NoteOff = 0x80;
    public const int NoteOn = 0x90;
    public const int PolyPressure = 0xA0;
    public const int ControlChange = 0xB0;
    public const int ProgramChange = 0xC0;
    public const int ChannelPressure = 0xD0;
    public const int PitchBend = 0xE0;

    /// <summary>The kind of <paramref name="status"/> (its high nibble).</summary>
    public static int KindOf(int status) => status & 0xF0;

    /// <summary>The channel (0-15) of <paramref name="status"/> (its low nibble).</summary>
    public static int ChannelOf(int status) => status & 0x0F;
}

/// <summary>MIDI controller (CC) numbers the app sends or reads.</summary>
public static class MidiCc
{
    public const int BankSelectMsb = 0;
    public const int Modulation = 1;
    public const int DataEntryMsb = 6;
    public const int Volume = 7;
    public const int Pan = 10;
    public const int Expression = 11;
    public const int BankSelectLsb = 32;
    public const int DataEntryLsb = 38;
    public const int Sustain = 64;
    public const int Reverb = 91;
    public const int Chorus = 93;
    public const int RpnLsb = 100;
    public const int RpnMsb = 101;
    /// <summary>Channel mode: silence every sounding voice at once (tails cut).</summary>
    public const int AllSoundOff = 120;
    /// <summary>Channel mode: controllers back to their defaults.</summary>
    public const int ResetAllControllers = 121;
    /// <summary>Channel mode: release every held note (a sustained note keeps sounding under CC 64).</summary>
    public const int AllNotesOff = 123;
    /// <summary>The first channel-mode controller (120-127); controllers below it are ordinary ones.</summary>
    public const int FirstChannelMode = 120;
}

/// <summary>The per-channel reset the panics and stops send: one shared order, so a hanging-note fix is made in one place.</summary>
public static class MidiPanic
{
    /// <summary>The controllers of one channel's reset, in the order they are sent (each with value 0): all notes off, then all sound off.</summary>
    /// <remarks>A span over constant data: no allocation, safe on the audio thread.</remarks>
    public static ReadOnlySpan<int> ResetControllers => new[] { MidiCc.AllNotesOff, MidiCc.AllSoundOff };
}
