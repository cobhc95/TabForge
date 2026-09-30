using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Documents;

/// <summary>
/// One immutable undo state of a song (Audit 3 M-06). The song header and each track header are compact JSON; every bar is a
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

/// <summary>A track of a <see cref="ProjectState"/>: its header chunk, one chunk per bar, and the bar numbers (kept outside the
/// chunks so renumbering bars after an insert or move does not re-store them).</summary>
internal sealed class TrackState
{
    public TrackState(StateChunk header, StateChunk[] bars, int[] numbers)
    {
        Header = header;
        Bars = bars;
        Numbers = numbers;
    }

    public StateChunk Header { get; }
    public StateChunk[] Bars { get; }
    public int[] Numbers { get; }

    public bool ContentEquals(TrackState other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (!Header.SameContent(other.Header) || Bars.Length != other.Bars.Length ||
            !Numbers.AsSpan().SequenceEqual(other.Numbers)) return false;
        for (var bar = 0; bar < Bars.Length; bar++)
            if (!Bars[bar].SameContent(other.Bars[bar])) return false;
        return true;
    }
}

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

/// <summary>
/// Builds <see cref="ProjectState"/>s for one document and restores them. UI thread only: it remembers the last state (and the live
/// bars it was taken from) so the next capture compares bars against it and reuses every unchanged chunk.
/// </summary>
internal sealed class ProjectStateEncoder
{
    private readonly BarWriter _writer = new();
    private ProjectState? _last;
    // The bars of the song _last was taken from (or restored into), per chunk slot; weak on the song so a replaced song is not kept alive.
    private readonly ConditionalWeakTable<SongProject, MeasureModel?[][]> _lastBars = new();
    private Dictionary<ulong, StateChunk>? _index;
    // Only guards the caches above if a caller ever runs off the UI thread; reading a song while it is edited is still the caller's problem.
    private readonly object _gate = new();

    public ProjectState Encode(SongProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        lock (_gate) return EncodeLocked(project);
    }

    /// <param name="validate">False: the autosave writer, which must rescue a model that fails validation as a raw copy.</param>
    public SongProject Restore(ProjectState state, SongProject? live, bool validate = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate) return RestoreLocked(state, live, validate);
    }

    private ProjectState EncodeLocked(SongProject project)
    {
        var previous = _last;
        long fresh = 0;
        var song = Reuse(ProjectService.UndoSongHeader(project), previous?.Song, ref fresh);
        var sourceTracks = project.Tracks ?? new List<TrackModel>();
        var tracks = new TrackState[sourceTracks.Count];
        var liveBars = new MeasureModel?[sourceTracks.Count][];
        for (var trackIndex = 0; trackIndex < sourceTracks.Count; trackIndex++)
        {
            var track = sourceTracks[trackIndex];
            var prior = previous is not null && trackIndex < previous.Tracks.Length ? previous.Tracks[trackIndex] : null;
            var header = Reuse(ProjectService.UndoTrackHeader(track), prior?.Header, ref fresh);
            var measures = track?.Measures;
            var count = measures?.Count ?? 0;
            var bars = new StateChunk[count];
            var numbers = new int[count];
            var live = new MeasureModel?[count];
            var same = prior is not null && ReferenceEquals(header, prior.Header) && prior.Bars.Length == count;
            for (var bar = 0; bar < count; bar++)
            {
                var measure = measures![bar];
                live[bar] = measure;
                numbers[bar] = measure?.Number ?? 0;
                _writer.Reset();
                BarCodec.Write(_writer, measure);
                var bytes = _writer.Written;
                var slot = prior is not null && bar < prior.Bars.Length ? prior.Bars[bar] : null;
                StateChunk chunk;
                if (slot is not null && slot.Bytes.AsSpan().SequenceEqual(bytes)) chunk = slot;
                else if (FindInPrevious(bytes) is { } moved) chunk = moved;
                else
                {
                    chunk = new StateChunk(bytes.ToArray());
                    fresh += chunk.Bytes.Length;
                }
                bars[bar] = chunk;
                if (same) same = ReferenceEquals(chunk, prior!.Bars[bar]) && numbers[bar] == prior.Numbers[bar];
            }
            tracks[trackIndex] = same ? prior! : new TrackState(header, bars, numbers);
            liveBars[trackIndex] = live;
        }

        // The first state of a document is its baseline (the song itself is already in memory): only later changes are charged.
        var state = new ProjectState(song, tracks, previous is null ? 0 : fresh);
        Remember(state, project, liveBars);
        return state;
    }

    /// <summary>
    /// Rebuilds a song from <paramref name="state"/>. Bars of <paramref name="live"/> (the song being replaced, last encoded by
    /// this encoder) whose content is unchanged are moved over after re-checking their bytes; only the other bars are decoded.
    /// </summary>
    private SongProject RestoreLocked(ProjectState state, SongProject? live, bool validate)
    {
        var reusable = live is not null && _lastBars.TryGetValue(live, out var lastBars) ? ReusableBars(lastBars) : null;
        var used = new HashSet<MeasureModel>(ReferenceEqualityComparer.Instance);
        var renumbered = new List<(MeasureModel Measure, int Number)>();
        var project = ProjectService.RestoreUndoSongHeader(state.Song.Bytes);
        var liveBars = new MeasureModel?[state.Tracks.Length][];
        try
        {
            foreach (var trackState in state.Tracks)
            {
                var track = ProjectService.RestoreUndoTrackHeader(trackState.Header.Bytes);
                var bars = new MeasureModel?[trackState.Bars.Length];
                for (var bar = 0; bar < bars.Length; bar++)
                {
                    var chunk = trackState.Bars[bar];
                    var measure = reusable is not null && reusable.TryGetValue(chunk, out var candidates)
                        ? TakeUnchanged(candidates, chunk, used, renumbered)
                        : null;
                    measure ??= BarCodec.Read(chunk.Bytes);
                    if (measure is not null) measure.Number = trackState.Numbers[bar];
                    bars[bar] = measure;
                }
                var measures = new List<MeasureModel>(bars.Length);
                foreach (var measure in bars) measures.Add(measure!);
                track.Measures = measures;
                project.Tracks.Add(track);
                liveBars[project.Tracks.Count - 1] = bars;
            }
            if (validate) ProjectValidator.Validate(project);
        }
        catch
        {
            // The song being replaced stays as it was: give its moved-over bars their own numbers back.
            foreach (var (measure, number) in renumbered) measure.Number = number;
            throw;
        }
        Remember(state, project, liveBars);
        return project;
    }

    /// <summary>A live bar recorded with <paramref name="chunk"/> that still encodes to exactly its bytes (each bar is moved once).</summary>
    private MeasureModel? TakeUnchanged(List<MeasureModel> candidates, StateChunk chunk, HashSet<MeasureModel> used,
        List<(MeasureModel Measure, int Number)> renumbered)
    {
        while (candidates.Count > 0)
        {
            var candidate = candidates[^1];
            candidates.RemoveAt(candidates.Count - 1);
            if (!used.Add(candidate)) continue;
            _writer.Reset();
            BarCodec.Write(_writer, candidate);
            if (!_writer.Written.SequenceEqual(chunk.Bytes)) continue;
            renumbered.Add((candidate, candidate.Number));
            return candidate;
        }
        return null;
    }

    private void Remember(ProjectState state, SongProject project, MeasureModel?[][] liveBars)
    {
        if (!ReferenceEquals(state, _last)) _index = null;
        _last = state;
        _lastBars.Clear();
        _lastBars.AddOrUpdate(project, liveBars);
    }

    private Dictionary<StateChunk, List<MeasureModel>>? ReusableBars(MeasureModel?[][] lastBars)
    {
        if (_last is null) return null;
        var map = new Dictionary<StateChunk, List<MeasureModel>>(ReferenceEqualityComparer.Instance);
        for (var trackIndex = 0; trackIndex < _last.Tracks.Length && trackIndex < lastBars.Length; trackIndex++)
        {
            var chunks = _last.Tracks[trackIndex].Bars;
            var bars = lastBars[trackIndex];
            for (var bar = 0; bar < chunks.Length && bar < bars.Length; bar++)
            {
                if (bars[bar] is not { } measure) continue;
                if (!map.TryGetValue(chunks[bar], out var list)) map[chunks[bar]] = list = new List<MeasureModel>(1);
                list.Add(measure);
            }
        }
        return map;
    }

    private static StateChunk Reuse(byte[] bytes, StateChunk? previous, ref long fresh)
    {
        if (previous is not null && previous.Bytes.AsSpan().SequenceEqual(bytes)) return previous;
        fresh += bytes.Length;
        return new StateChunk(bytes);
    }

    /// <summary>A bar that moved (inserted/deleted/reordered bars or tracks): find its chunk anywhere in the previous state.</summary>
    private StateChunk? FindInPrevious(ReadOnlySpan<byte> bytes)
    {
        if (_last is null) return null;
        if (_index is null)
        {
            _index = new Dictionary<ulong, StateChunk>();
            foreach (var track in _last.Tracks)
                foreach (var chunk in track.Bars)
                    _index.TryAdd(chunk.Key, chunk);
        }
        return _index.TryGetValue(StateChunk.KeyOf(bytes), out var found) && found.Bytes.AsSpan().SequenceEqual(bytes) ? found : null;
    }
}

/// <summary>Growable byte buffer reused for every bar encoded.</summary>
internal sealed class BarWriter
{
    private byte[] _buffer = new byte[4096];
    private int _length;

    public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _length);

    public void Reset() => _length = 0;

    private Span<byte> Take(int count)
    {
        if (_length + count > _buffer.Length) Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + count));
        var span = _buffer.AsSpan(_length, count);
        _length += count;
        return span;
    }

    public void VarU(ulong value)
    {
        if (_length + 10 > _buffer.Length) Array.Resize(ref _buffer, _buffer.Length * 2);
        var buffer = _buffer;
        var at = _length;
        while (value >= 0x80)
        {
            buffer[at++] = (byte)(value | 0x80);
            value >>= 7;
        }
        buffer[at++] = (byte)value;
        _length = at;
    }

    public void Int(int value) => VarU((uint)((value << 1) ^ (value >> 31)));

    public void NInt(int? value) => VarU(value is { } v ? (ulong)(uint)((v << 1) ^ (v >> 31)) + 1 : 0);

    public void NBool(bool? value) => VarU(value is null ? 0UL : value.Value ? 2UL : 1UL);

    public void Dbl(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        if (bits == 0) { VarU(0); return; }
        VarU(1);
        BinaryPrimitives.WriteInt64LittleEndian(Take(8), bits);
    }

    public void NDbl(double? value)
    {
        if (value is null) { VarU(2); return; }
        Dbl(value.Value);
    }

    public void Str(string? value)
    {
        if (value is null) { VarU(0); return; }
        VarU((ulong)value.Length + 1);
        if (value.Length > 0) MemoryMarshal.AsBytes(value.AsSpan()).CopyTo(Take(value.Length * 2));
    }

    public void Count(int? count) => VarU(count is { } c ? (ulong)c + 1 : 0);
}

internal ref struct BarReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public BarReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public bool AtEnd => _position == _data.Length;

    public ulong VarU()
    {
        ulong value = 0;
        var shift = 0;
        while (true)
        {
            var next = _data[_position++];
            value |= (ulong)(next & 0x7F) << shift;
            if (next < 0x80) return value;
            shift += 7;
        }
    }

    private static int Unzig(ulong raw) => (int)((uint)raw >> 1) ^ -(int)((uint)raw & 1);

    public int Int() => Unzig(VarU());

    public int? NInt()
    {
        var raw = VarU();
        return raw == 0 ? null : Unzig(raw - 1);
    }

    public bool? NBool() => VarU() switch { 0 => null, 1 => false, _ => true };

    private double DblTagged(ulong tag)
    {
        if (tag == 0) return 0d;
        var bits = BinaryPrimitives.ReadInt64LittleEndian(_data.Slice(_position, 8));
        _position += 8;
        return BitConverter.Int64BitsToDouble(bits);
    }

    public double Dbl() => DblTagged(VarU());

    public double? NDbl()
    {
        var tag = VarU();
        return tag == 2 ? null : DblTagged(tag);
    }

    public string? Str()
    {
        var raw = VarU();
        if (raw == 0) return null;
        var length = (int)(raw - 1);
        if (length == 0) return "";
        var text = new string(MemoryMarshal.Cast<byte, char>(_data.Slice(_position, length * 2)));
        _position += length * 2;
        return text;
    }

    /// <summary>List length, or -1 for a null list.</summary>
    public int Count() => (int)VarU() - 1;
}

/// <summary>
/// Exact binary form of one bar (everything the JSON form holds except <see cref="MeasureModel.Number"/>, which the track state
/// keeps separately). <see cref="Handled"/> lists the covered JSON properties; a self-test fails when the model gains one it misses.
/// </summary>
internal static class BarCodec
{
    public static readonly IReadOnlyDictionary<Type, string[]> Handled = new Dictionary<Type, string[]>
    {
        [typeof(MeasureModel)] = new[]
        {
            nameof(MeasureModel.Number), nameof(MeasureModel.Cells), nameof(MeasureModel.TimeSigNum), nameof(MeasureModel.TimeSigDenom),
            nameof(MeasureModel.KeySignature), nameof(MeasureModel.KeySignatureMinor), nameof(MeasureModel.Clef), nameof(MeasureModel.RepeatStart),
            nameof(MeasureModel.RepeatEnd), nameof(MeasureModel.RepeatCount), nameof(MeasureModel.AlternateEnding), nameof(MeasureModel.AlternateEndingMask),
            nameof(MeasureModel.IsDoubleBar), nameof(MeasureModel.SimileOneBar), nameof(MeasureModel.SimileTwoBar), nameof(MeasureModel.SectionName),
            nameof(MeasureModel.TempoChange), nameof(MeasureModel.MidBarTempos), nameof(MeasureModel.TripletFeel), nameof(MeasureModel.TripletFeelKind),
            nameof(MeasureModel.FreeTime), nameof(MeasureModel.ForceLineBreak), nameof(MeasureModel.PreventLineBreak), nameof(MeasureModel.Anacrusis),
            nameof(MeasureModel.Directions), nameof(MeasureModel.Voice2Cells)
        },
        [typeof(TabCell)] = new[]
        {
            nameof(TabCell.Notes), nameof(TabCell.RhythmicPosition), nameof(TabCell.DurationDenominator), nameof(TabCell.Dots), nameof(TabCell.IsTriplet),
            nameof(TabCell.TupletNumerator), nameof(TabCell.TupletDenominator), nameof(TabCell.Tuplet), nameof(TabCell.IsRest), nameof(TabCell.IsTied),
            nameof(TabCell.SoundDurationPercent), nameof(TabCell.OctaveShiftSemitones), nameof(TabCell.BeamMode), nameof(TabCell.BreakSecondaryBeamBefore),
            nameof(TabCell.StemDirection), nameof(TabCell.IsGrace), nameof(TabCell.GraceBeforeBeat), nameof(TabCell.Fermata), nameof(TabCell.Accent),
            nameof(TabCell.Staccato), nameof(TabCell.Tenuto), nameof(TabCell.WhammyPoints), nameof(TabCell.TremoloPickDenominator), nameof(TabCell.BrushStepSlots), nameof(TabCell.ChordName),
            nameof(TabCell.Text), nameof(TabCell.Lyrics), nameof(TabCell.Mix)
        },
        [typeof(TabNote)] = new[]
        {
            nameof(TabNote.StringIndex), nameof(TabNote.Fret), nameof(TabNote.MidiValue), nameof(TabNote.Velocity), nameof(TabNote.Ghost), nameof(TabNote.Dead),
            nameof(TabNote.IsGraceNote), nameof(TabNote.GraceBeforeBeat), nameof(TabNote.GraceOnsetOffsetSlots), nameof(TabNote.GraceDurationSlots),
            nameof(TabNote.Tied), nameof(TabNote.BendPoints), nameof(TabNote.BendTypeName), nameof(TabNote.BendStyleName), nameof(TabNote.SlideTargetMidi),
            nameof(TabNote.TrillTargetMidi), nameof(TabNote.TrillDurationDenominator), nameof(TabNote.HarmonicFret), nameof(TabNote.LeftHandFinger),
            nameof(TabNote.RightHandFinger), nameof(TabNote.Techniques)
        },
        [typeof(BendPointModel)] = new[] { nameof(BendPointModel.Offset), nameof(BendPointModel.Value) },
        [typeof(MixChange)] = new[]
        {
            nameof(MixChange.Program), nameof(MixChange.Volume), nameof(MixChange.Pan), nameof(MixChange.Chorus), nameof(MixChange.Reverb),
            nameof(MixChange.Phaser), nameof(MixChange.Tremolo), nameof(MixChange.Tempo), nameof(MixChange.TransitionBeats), nameof(MixChange.AllTracks)
        },
        [typeof(TempoPoint)] = new[] { nameof(TempoPoint.Slot), nameof(TempoPoint.Tempo), nameof(TempoPoint.RampSlots) },
    };

    private static int Bit(bool value, int shift) => value ? 1 << shift : 0;

    public static void Write(BarWriter w, MeasureModel? m)
    {
        if (m is null) { w.VarU(0); return; }
        w.VarU((ulong)(1 | Bit(m.RepeatStart, 1) | Bit(m.RepeatEnd, 2) | Bit(m.IsDoubleBar, 3) | Bit(m.SimileOneBar, 4) | Bit(m.SimileTwoBar, 5) |
                       Bit(m.TripletFeel, 6) | Bit(m.FreeTime, 7) | Bit(m.ForceLineBreak, 8) | Bit(m.PreventLineBreak, 9) | Bit(m.Anacrusis, 10)));
        w.NInt(m.TimeSigNum);
        w.NInt(m.TimeSigDenom);
        w.NInt(m.KeySignature);
        w.NBool(m.KeySignatureMinor);
        w.NInt(m.TempoChange);
        w.Int(m.RepeatCount);
        w.Int(m.AlternateEnding);
        w.Int(m.AlternateEndingMask);
        w.Str(m.Clef);
        w.Str(m.SectionName);
        w.Str(m.TripletFeelKind);
        w.Str(m.Directions);
        WriteCells(w, m.Cells);
        WriteCells(w, m.Voice2Cells);
        w.Count(m.MidBarTempos?.Count);
        if (m.MidBarTempos is { } tempos)
            foreach (var point in tempos)
            {
                if (point is null) { w.VarU(0); continue; }
                w.VarU(1);
                w.Dbl(point.Slot);
                w.Int(point.Tempo);
                w.Dbl(point.RampSlots);
            }
    }

    private static void WriteCells(BarWriter w, List<TabCell>? cells)
    {
        w.Count(cells?.Count);
        if (cells is null) return;
        foreach (var c in cells)
        {
            if (c is null) { w.VarU(0); continue; }
            w.VarU((ulong)(1 | Bit(c.IsTriplet, 1) | Bit(c.IsRest, 2) | Bit(c.IsTied, 3) | Bit(c.BreakSecondaryBeamBefore, 4) | Bit(c.IsGrace, 5) |
                           Bit(c.GraceBeforeBeat, 6) | Bit(c.Fermata, 7) | Bit(c.Staccato, 8) | Bit(c.Tenuto, 9)));
            w.Int(c.DurationDenominator);
            w.Int(c.Dots);
            w.Int(c.TupletNumerator);
            w.Int(c.TupletDenominator);
            w.Int(c.SoundDurationPercent);
            w.Int(c.OctaveShiftSemitones);
            w.Int(c.Accent);
            w.Int(c.TremoloPickDenominator);
            w.Dbl(c.BrushStepSlots);
            w.Int((int)c.BeamMode);
            w.Int((int)c.StemDirection);
            w.NDbl(c.RhythmicPosition);
            w.Str(c.ChordName);
            w.Str(c.Text);
            w.Str(c.Lyrics);
            if (c.Mix is not { } mix) w.VarU(0);
            else
            {
                w.VarU((ulong)(1 | Bit(mix.AllTracks, 1)));
                w.NInt(mix.Program);
                w.NInt(mix.Volume);
                w.NInt(mix.Pan);
                w.NInt(mix.Chorus);
                w.NInt(mix.Reverb);
                w.NInt(mix.Phaser);
                w.NInt(mix.Tremolo);
                w.NInt(mix.Tempo);
                w.Int(mix.TransitionBeats);
            }
            WritePoints(w, c.WhammyPoints);
            w.Count(c.Notes?.Count);
            if (c.Notes is null) continue;
            foreach (var n in c.Notes)
            {
                if (n is null) { w.VarU(0); continue; }
                w.VarU((ulong)(1 | Bit(n.Ghost, 1) | Bit(n.Dead, 2) | Bit(n.IsGraceNote, 3) | Bit(n.GraceBeforeBeat, 4) | Bit(n.Tied, 5) | Bit(n.HarmonicFret.HasValue, 6)));
                w.Int(n.StringIndex);
                w.Int(n.Fret);
                w.Int(n.MidiValue);
                w.Int(n.Velocity);
                w.Int(n.SlideTargetMidi);
                w.Int(n.TrillTargetMidi);
                w.Int(n.TrillDurationDenominator);
                w.NInt(n.LeftHandFinger);
                w.NInt(n.RightHandFinger);
                w.Dbl(n.GraceOnsetOffsetSlots);
                w.Dbl(n.GraceDurationSlots);
                if (n.HarmonicFret is { } harmonicFret) w.Dbl(harmonicFret);
                w.Str(n.BendTypeName);
                w.Str(n.BendStyleName);
                WritePoints(w, n.BendPoints);
                w.Count(n.Techniques?.Count);
                if (n.Techniques is { } techniques)
                    foreach (var technique in techniques) w.Str(technique);
            }
        }
    }

    private static void WritePoints(BarWriter w, List<BendPointModel>? points)
    {
        w.Count(points?.Count);
        if (points is null) return;
        foreach (var point in points)
        {
            if (point is null) { w.VarU(0); continue; }
            w.VarU(1);
            w.Dbl(point.Offset);
            w.Dbl(point.Value);
        }
    }

    public static MeasureModel? Read(ReadOnlySpan<byte> bytes)
    {
        var r = new BarReader(bytes);
        var flags = r.VarU();
        if (flags == 0) return null;
        bool F(int shift) => (flags & (1UL << shift)) != 0;
        var m = new MeasureModel
        {
            RepeatStart = F(1), RepeatEnd = F(2), IsDoubleBar = F(3), SimileOneBar = F(4), SimileTwoBar = F(5),
            TripletFeel = F(6), FreeTime = F(7), ForceLineBreak = F(8), PreventLineBreak = F(9), Anacrusis = F(10),
            TimeSigNum = r.NInt(), TimeSigDenom = r.NInt(), KeySignature = r.NInt(), KeySignatureMinor = r.NBool(), TempoChange = r.NInt(),
            RepeatCount = r.Int(), AlternateEnding = r.Int(), AlternateEndingMask = r.Int(),
            Clef = r.Str()!, SectionName = r.Str()!, TripletFeelKind = r.Str()!, Directions = r.Str()!,
        };
        m.Cells = ReadCells(ref r)!;
        m.Voice2Cells = ReadCells(ref r)!;
        var tempoCount = r.Count();
        if (tempoCount < 0) m.MidBarTempos = null;
        else
        {
            var tempos = new List<TempoPoint>(tempoCount);
            for (var index = 0; index < tempoCount; index++)
            {
                if (r.VarU() == 0) { tempos.Add(null!); continue; }
                var slot = r.Dbl();
                var tempo = r.Int();
                tempos.Add(new TempoPoint(slot, tempo, r.Dbl()));
            }
            m.MidBarTempos = tempos;
        }
        if (!r.AtEnd) throw new InvalidDataException("The undo state is damaged.");
        return m;
    }

    private static List<TabCell>? ReadCells(ref BarReader r)
    {
        var count = r.Count();
        if (count < 0) return null;
        var cells = new List<TabCell>(count);
        for (var index = 0; index < count; index++)
        {
            var flags = r.VarU();
            if (flags == 0) { cells.Add(null!); continue; }
            bool F(int shift) => (flags & (1UL << shift)) != 0;
            var c = new TabCell
            {
                IsTriplet = F(1), IsRest = F(2), IsTied = F(3), BreakSecondaryBeamBefore = F(4), IsGrace = F(5),
                GraceBeforeBeat = F(6), Fermata = F(7), Staccato = F(8), Tenuto = F(9),
                DurationDenominator = r.Int(), Dots = r.Int(), TupletNumerator = r.Int(), TupletDenominator = r.Int(),
                SoundDurationPercent = r.Int(), OctaveShiftSemitones = r.Int(), Accent = r.Int(), TremoloPickDenominator = r.Int(), BrushStepSlots = r.Dbl(),
                BeamMode = (BeamMode)r.Int(), StemDirection = (StemDirection)r.Int(),
                RhythmicPosition = r.NDbl(), ChordName = r.Str(), Text = r.Str(), Lyrics = r.Str()!,
            };
            var mixFlags = r.VarU();
            if (mixFlags != 0)
                c.Mix = new MixChange
                {
                    AllTracks = (mixFlags & 2) != 0,
                    Program = r.NInt(), Volume = r.NInt(), Pan = r.NInt(), Chorus = r.NInt(), Reverb = r.NInt(),
                    Phaser = r.NInt(), Tremolo = r.NInt(), Tempo = r.NInt(), TransitionBeats = r.Int(),
                };
            c.WhammyPoints = ReadPoints(ref r)!;
            var noteCount = r.Count();
            if (noteCount < 0) c.Notes = null!;
            else
            {
                var notes = new List<TabNote>(noteCount);
                for (var noteIndex = 0; noteIndex < noteCount; noteIndex++)
                {
                    var noteFlags = r.VarU();
                    if (noteFlags == 0) { notes.Add(null!); continue; }
                    bool N(int shift) => (noteFlags & (1UL << shift)) != 0;
                    var n = new TabNote
                    {
                        Ghost = N(1), Dead = N(2), IsGraceNote = N(3), GraceBeforeBeat = N(4), Tied = N(5),
                        StringIndex = r.Int(), Fret = r.Int(), MidiValue = r.Int(), Velocity = r.Int(),
                        SlideTargetMidi = r.Int(), TrillTargetMidi = r.Int(), TrillDurationDenominator = r.Int(),
                        LeftHandFinger = r.NInt(), RightHandFinger = r.NInt(),
                        GraceOnsetOffsetSlots = r.Dbl(), GraceDurationSlots = r.Dbl(),
                    };
                    if (N(6)) n.HarmonicFret = r.Dbl();
                    n.BendTypeName = r.Str()!; n.BendStyleName = r.Str()!;
                    n.BendPoints = ReadPoints(ref r)!;
                    var techniqueCount = r.Count();
                    if (techniqueCount < 0) n.Techniques = null!;
                    else
                    {
                        // The model's case-insensitive set; a set that held two spellings of one name keeps both (ordinal).
                        var techniques = n.Techniques;
                        for (var t = 0; t < techniqueCount; t++)
                        {
                            var name = r.Str()!;
                            if (!techniques.Add(name))
                            {
                                techniques = new HashSet<string>(techniques, StringComparer.Ordinal);
                                techniques.Add(name);
                            }
                        }
                        n.Techniques = techniques;
                    }
                    notes.Add(n);
                }
                c.Notes = notes;
            }
            cells.Add(c);
        }
        return cells;
    }

    private static List<BendPointModel>? ReadPoints(ref BarReader r)
    {
        var count = r.Count();
        if (count < 0) return null;
        var points = new List<BendPointModel>(count);
        for (var index = 0; index < count; index++)
        {
            if (r.VarU() == 0) { points.Add(null!); continue; }
            var offset = r.Dbl();
            points.Add(new BendPointModel { Offset = offset, Value = r.Dbl() });
        }
        return points;
    }
}
