namespace TabForge.Audio.Contracts;

// Owns: the one marker of a VST2 parameter-list state (a plug-in with no chunk of its own) and the shape test, shared by the engine and the chain file reader/writer.
// Does not own: reading or applying the values (Vst2Plugin).
// Tests: no dedicated test class.
public static class ParameterListState
{
    /// <summary>The first eight bytes of a parameter-list state; one little-endian float (0..1) per parameter follows.</summary>
    public static ReadOnlySpan<byte> Mark => new byte[] { 0xEF, 0xBE, 0xAD, 0xDE, 0x0D, 0xF0, 0xAD, 0xDE };

    /// <summary>True when <paramref name="state"/> is a mark followed by at least one whole float.</summary>
    public static bool IsList(ReadOnlySpan<byte> state) => state.Length >= 12 && (state.Length - 8) % 4 == 0 && state[..8].SequenceEqual(Mark);
}
