using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the hashing, size-bounded write-only stream used when saving.
// Does not own: the rest of the class (see the main file).
// Tests: no dedicated test class.
/// <summary>Write-only pass-through that SHA-256 hashes and bounds what is written to the inner stream.</summary>
internal sealed class HashingLimitStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private readonly IncrementalHash? _hash;
    private long _written;

    private readonly string? _what;

    public HashingLimitStream(Stream inner, long limit, bool hash = true, string? what = null)
    {
        _inner = inner;
        _limit = limit;
        _what = what;
        _hash = hash ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _written += buffer.Length;
        if (_written > _limit)
            throw new InvalidDataException(_what is null ? ProjectService.SizeLimitMessage : $"The {_what} would be larger than the {_limit / (1024 * 1024)} MiB limit for its expanded project data (the limit does not apply to the size of the file).");
        _hash?.AppendData(buffer);
        _inner.Write(buffer);
    }

    public byte[] Finish() => _hash?.GetHashAndReset() ?? Array.Empty<byte>();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _written;
    public override long Position { get => _written; set => throw new NotSupportedException(); }
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) _hash?.Dispose(); base.Dispose(disposing); }
}
