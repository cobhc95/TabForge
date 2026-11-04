namespace TabForge.Models;

/// <summary>
/// One partial capo on a track: the absolute fret it clamps and the strings it covers. Bit <c>i</c> of
/// <see cref="StringMask"/> is string index <c>i</c> (0 = the highest string, like <see cref="TrackModel.StringTunings"/>).
/// A partial at fret 0 (off) or with no bits covers nothing.
/// </summary>
public sealed class PartialCapo
{
    public int Fret { get; set; }

    /// <summary>Bit i set: the partial covers string index i.</summary>
    public int StringMask { get; set; }

    public PartialCapo Clone() => new() { Fret = Fret, StringMask = StringMask };

    /// <summary>True when this partial holds exactly the same fret and strings as <paramref name="other"/>.</summary>
    public bool SameAs(PartialCapo other) => Fret == other.Fret && StringMask == other.StringMask;
}
