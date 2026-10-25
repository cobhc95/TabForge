using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: path-geometry recording key encoding (PathShapeKey equality + PathRecorder op encoding).
// Does not own: the geometry (AvaloniaPathsAndText) or its cache.

/// <summary>
/// The content of one built path: its fill rule and every figure's points and flags as 64-bit patterns, so two keys are equal only when the
/// geometry built from them is identical. Owns: the key's equality. Does not own: the geometry (<see cref="AvaloniaPathsAndText"/>) or its cache.
/// </summary>
internal readonly struct PathShapeKey(long[] words) : IEquatable<PathShapeKey>
{
    /// <summary>Word 0 is the fill rule (0 even-odd, 1 non-zero); the rest are the recorded operations, see <see cref="PathRecorder"/>.</summary>
    public long[] Words { get; } = words;

    public bool Equals(PathShapeKey other) => Words.AsSpan().SequenceEqual(other.Words);
    public override bool Equals(object? obj) => obj is PathShapeKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(MemoryMarshal.AsBytes(Words.AsSpan()));
        return hash.ToHashCode();
    }
}

/// <summary>
/// Records the figures a path builder gives it as a <see cref="PathShapeKey"/>, and replays a key's words into any <see cref="IPathSink"/>.
/// Each operation is an opcode word followed by its numbers as bit patterns and its flags as bits. Owns: that encoding. Does not own: the cache.
/// Thread: one instance per owner, on the owner's thread; <see cref="Record"/> is not re-entrant (a figure that builds a path throws).
/// Tests: AvaloniaPathCacheTests (TabForge.Avalonia.Tests).
/// </summary>
internal sealed class PathRecorder : IPathSink
{
    private const long OpBegin = 1, OpLine = 2, OpBezier = 3, OpQuadratic = 4, OpArc = 5;
    private readonly List<long> _words = new();
    private bool _recording;

    /// <summary>Runs <paramref name="figures"/> and returns the content it gave, as a key.</summary>
    public PathShapeKey Record(Action<IPathSink> figures, UiFillRule fillRule)
    {
        if (_recording) throw new InvalidOperationException("A path figure builder built another path; build the inner path first.");
        _recording = true;
        try
        {
            _words.Clear();
            _words.Add(fillRule == UiFillRule.EvenOdd ? 0 : 1);
            figures(this);
            return new PathShapeKey(_words.ToArray());
        }
        finally
        {
            _recording = false;
        }
    }

    private void Put(double value) => _words.Add(BitConverter.DoubleToInt64Bits(value));
    private void Put(UiPoint point) { Put(point.X); Put(point.Y); }
    private static long Flag(bool on) => on ? 1 : 0;

    public void BeginFigure(UiPoint start, bool filled, bool closed)
    {
        _words.Add(OpBegin);
        Put(start);
        _words.Add(Flag(filled) | Flag(closed) << 1);
    }

    public void LineTo(UiPoint point, bool smoothJoin = false)
    {
        _words.Add(OpLine);
        Put(point);
        _words.Add(Flag(smoothJoin));
    }

    public void BezierTo(UiPoint control1, UiPoint control2, UiPoint end, bool smoothJoin = false)
    {
        _words.Add(OpBezier);
        Put(control1);
        Put(control2);
        Put(end);
        _words.Add(Flag(smoothJoin));
    }

    public void QuadraticTo(UiPoint control, UiPoint end)
    {
        _words.Add(OpQuadratic);
        Put(control);
        Put(end);
    }

    public void ArcTo(UiPoint end, double radiusX, double radiusY, double rotation, bool largeArc, bool clockwise)
    {
        _words.Add(OpArc);
        Put(end);
        Put(radiusX);
        Put(radiusY);
        Put(rotation);
        _words.Add(Flag(largeArc) | Flag(clockwise) << 1);
    }

    /// <summary>Sends the figures a key recorded to <paramref name="sink"/>, in order; the caller ends the open figure afterwards.</summary>
    public static void Replay(long[] words, IPathSink sink)
    {
        var at = 1;
        while (at < words.Length)
        {
            switch (words[at++])
            {
                case OpBegin:
                    sink.BeginFigure(Point(words, at), (words[at + 2] & 1) != 0, (words[at + 2] & 2) != 0);
                    at += 3;
                    break;
                case OpLine:
                    sink.LineTo(Point(words, at), words[at + 2] != 0);
                    at += 3;
                    break;
                case OpBezier:
                    sink.BezierTo(Point(words, at), Point(words, at + 2), Point(words, at + 4), words[at + 6] != 0);
                    at += 7;
                    break;
                case OpQuadratic:
                    sink.QuadraticTo(Point(words, at), Point(words, at + 2));
                    at += 4;
                    break;
                case OpArc:
                    sink.ArcTo(Point(words, at), Number(words, at + 2), Number(words, at + 3), Number(words, at + 4),
                        (words[at + 5] & 1) != 0, (words[at + 5] & 2) != 0);
                    at += 6;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown path operation at word {at - 1}.");
            }
        }
    }

    private static double Number(long[] words, int index) => BitConverter.Int64BitsToDouble(words[index]);
    private static UiPoint Point(long[] words, int index) => new(Number(words, index), Number(words, index + 1));
}
