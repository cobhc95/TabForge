using System.Buffers.Binary;
using System.Security.Cryptography;

namespace TabForge.Documents;

// Owns: one immutable undo state of a song: the header chunk and one chunk per bar, shared with the previous state when unchanged, and
//   the content fingerprint and equality. The chunk encoder is ProjectStateEncoder; the bar bytes are BarCodec.
// Does not own: the undo stack that keeps the states, and the song model (Models).
// Tests: TestUndoDeltaStates (its CheckUndoBarCodecRoundTrip and CheckUndoFingerprints checks).
/// <summary>
/// One immutable undo state of a song. The song header and each track header are compact JSON; every bar is a
/// separate binary chunk. Chunks whose content did not change are shared with the previous state, so an edit stores (and later
/// restores) only the bars it touched instead of the whole song. Equal content always gives equal <see cref="ContentEquals"/> and
/// <see cref="Fingerprint"/>, exactly like the full JSON snapshots this replaces.
/// </summary>
public sealed class ProjectState
{
    private string? _fingerprint;

    internal ProjectState(StateChunk song, TrackState[] tracks, long ownBytes)
    {
        Song = song;
        Tracks = tracks;
        OwnBytes = ownBytes;
    }

    internal StateChunk Song { get; }
    internal TrackState[] Tracks { get; }

    /// <summary>Bytes first stored by this state (chunks shared with the state before it are not counted again).</summary>
    public long OwnBytes { get; }

    /// <summary>SHA-256 of the content (hex), computed on first use from the per-chunk hashes.</summary>
    public string Fingerprint => _fingerprint ??= ComputeFingerprint();

    public bool ContentEquals(ProjectState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (!Song.SameContent(other.Song) || Tracks.Length != other.Tracks.Length) return false;
        for (var index = 0; index < Tracks.Length; index++)
            if (!Tracks[index].ContentEquals(other.Tracks[index])) return false;
        return true;
    }

    private string ComputeFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> number = stackalloc byte[4];
        hash.AppendData(Song.Sha);
        BinaryPrimitives.WriteInt32LittleEndian(number, Tracks.Length);
        hash.AppendData(number);
        foreach (var track in Tracks)
        {
            hash.AppendData(track.Header.Sha);
            BinaryPrimitives.WriteInt32LittleEndian(number, track.Bars.Length);
            hash.AppendData(number);
            for (var bar = 0; bar < track.Bars.Length; bar++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(number, track.Numbers[bar]);
                hash.AppendData(number);
                hash.AppendData(track.Bars[bar].Sha);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
