namespace TabForge.Documents;

// Owns: the track level of an undo state: the header chunk, one chunk per bar, and the bar numbers, which stay outside the chunks so
//   renumbering bars after an insert or move does not re-store them.
// Does not own: the bar bytes (BarCodec) and the reuse of chunks between states (ProjectStateEncoder).
// Tests: TestUndoDeltaStates.
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
