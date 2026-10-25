namespace TabForge.AudioEngine.Mixing;

// Owns: the last program / controller / pitch-bend value each slot's MIDI set on each channel, replayed into a chain that newly appears in the slot.
// Does not own: when a chain is new (MixEngine) or the synth's own copy of the state (GmSynth).
// Threads: the audio thread writes and replays; the main thread only marks a slot to forget (a volatile flag).

/// <summary>
/// Last program / controller / pitch-bend value each slot's MIDI set on each channel (255 = never set). Kept even while the slot has
/// no chain yet, and replayed into any chain that newly appears in a slot, so a synth created after the song's setup was sent (cold
/// start, tab switch, chain rebuild, device restart) never falls back to piano. Owned by the engine session so it survives a new mix.
/// </summary>
public sealed class ChannelStateMemory
{
    private readonly byte[] _cc = CreateUnset(MixEngine.MaxSlots * 16 * 128);
    private readonly byte[] _program = CreateUnset(MixEngine.MaxSlots * 16);
    private readonly short[] _bend = new short[MixEngine.MaxSlots * 16];
    // Slots whose track was unloaded (main thread sets, audio thread clears): the remembered state belongs to that track, so it is
    // dropped before the slot's next chain is primed. Otherwise the next song's track in the slot starts with the previous
    // track's volume, expression and program on every channel its own setup does not send.
    private readonly bool[] _forget = new bool[MixEngine.MaxSlots];

    private static byte[] CreateUnset(int length) { var a = new byte[length]; Array.Fill(a, (byte)255); return a; }

    /// <summary>Audio thread: notes a program, controller or pitch-bend message of the slot.</summary>
    public void Remember(int slot, byte status, byte d1, byte d2)
    {
        var ch = status & 0x0F;
        switch (status & 0xF0)
        {
            case 0xB0 when d1 < 120: _cc[(slot * 16 + ch) * 128 + d1] = d2; break;
            case 0xC0: _program[slot * 16 + ch] = d1; break;
            case 0xE0: _bend[slot * 16 + ch] = (short)(1 + (d1 | (d2 << 7))); break;
        }
    }

    /// <summary>Main thread: the slot's track was unloaded; its state is dropped at the next <see cref="Replay"/>.</summary>
    public void Forget(int slot) => Volatile.Write(ref _forget[slot], true);

    /// <summary>Audio thread: puts the slot's remembered state into the chain at frame 0: bank select, then the program, the other controllers, the bend.</summary>
    public void Replay(int slot, TrackChain chain)
    {
        for (var ch = 0; ch < 16; ch++)
        {
            var baseIndex = (slot * 16 + ch) * 128;
            if (_cc[baseIndex] != 255) chain.AddEvent(0, (byte)(0xB0 | ch), 0, _cc[baseIndex]);
            if (_cc[baseIndex + 32] != 255) chain.AddEvent(0, (byte)(0xB0 | ch), 32, _cc[baseIndex + 32]);
            if (_program[slot * 16 + ch] != 255) chain.AddEvent(0, (byte)(0xC0 | ch), _program[slot * 16 + ch], 0);
            ReplayRpn(chain, ch, baseIndex);
            for (var cc = 1; cc < 120; cc++)
                if (cc != 32 && !IsRpnController(cc) && _cc[baseIndex + cc] != 255) chain.AddEvent(0, (byte)(0xB0 | ch), (byte)cc, _cc[baseIndex + cc]);
            var bend = _bend[slot * 16 + ch] - 1;
            if (bend >= 0) chain.AddEvent(0, (byte)(0xE0 | ch), (byte)(bend & 0x7F), (byte)(bend >> 7));
        }
    }

    private static bool IsRpnController(int cc) => cc is 6 or 38 or 100 or 101;

    /// <summary>The registered parameter as a unit: its number (CC101, CC100) must be selected before the data entry (CC6, CC38) that applies to it.</summary>
    private void ReplayRpn(TrackChain chain, int ch, int baseIndex)
    {
        ReplayController(chain, ch, baseIndex, 101);
        ReplayController(chain, ch, baseIndex, 100);
        ReplayController(chain, ch, baseIndex, 6);
        ReplayController(chain, ch, baseIndex, 38);
    }

    private void ReplayController(TrackChain chain, int ch, int baseIndex, int cc)
    {
        if (_cc[baseIndex + cc] != 255) chain.AddEvent(0, (byte)(0xB0 | ch), (byte)cc, _cc[baseIndex + cc]);
    }

    /// <summary>Audio thread: applies a pending <see cref="Forget"/> of the slot.</summary>
    public void ApplyForget(int slot)
    {
        if (!Volatile.Read(ref _forget[slot])) return;
        Volatile.Write(ref _forget[slot], false);
        Array.Fill(_cc, (byte)255, slot * 16 * 128, 16 * 128);
        Array.Fill(_program, (byte)255, slot * 16, 16);
        Array.Clear(_bend, slot * 16, 16);
    }
}
