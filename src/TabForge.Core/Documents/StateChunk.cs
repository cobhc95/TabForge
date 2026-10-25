using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace TabForge.Documents;

// Owns: the immutable encoded bytes that undo states share, with their SHA-256 and the 64-bit key of the reuse index.
// Does not own: the choice of which chunk to reuse (ProjectStateEncoder) and the state fingerprint (ProjectState).
// Tests: TestUndoDeltaStates.
/// <summary>Immutable bytes shared between undo states.</summary>
internal sealed class StateChunk
{
    private byte[]? _sha;
    private ulong? _key;

    public StateChunk(byte[] bytes) => Bytes = bytes;

    public byte[] Bytes { get; }
    public byte[] Sha => _sha ??= SHA256.HashData(Bytes);
    public ulong Key => _key ??= KeyOf(Bytes);

    public bool SameContent(StateChunk other) =>
        ReferenceEquals(this, other) || (Bytes.Length == other.Bytes.Length && Bytes.AsSpan().SequenceEqual(other.Bytes));

    /// <summary>Fast non-cryptographic 64-bit key for the reuse index (always confirmed by comparing the bytes).</summary>
    public static ulong KeyOf(ReadOnlySpan<byte> data)
    {
        var hash = 0x9E3779B97F4A7C15UL ^ (ulong)data.Length;
        var words = MemoryMarshal.Cast<byte, ulong>(data);
        foreach (var word in words) hash = BitOperations.RotateLeft((hash ^ word) * 0xFF51AFD7ED558CCDUL, 31);
        for (var index = words.Length * 8; index < data.Length; index++)
            hash = BitOperations.RotateLeft((hash ^ data[index]) * 0xC4CEB9FE1A85EC53UL, 17);
        hash ^= hash >> 33;
        hash *= 0xFF51AFD7ED558CCDUL;
        return hash ^ (hash >> 33);
    }
}
