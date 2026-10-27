using System.Linq;
using TabForge.Playback;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: which positions the playback movement lines join: next notes pair with the sounding note on the same string, and a repeated or same-chord note draws no line.
/// Does not own: drawing the lines (FretboardPlaybackMarkers).
/// Tests: FretboardMovementRules.
/// </summary>
public class FretboardMovementRulesTests
{
    private static NoteEvent N(int cell, int str, int fret) => new() { Cell = cell, StringIndex = str, Fret = fret };
    private static readonly NoteEvent[] None = System.Array.Empty<NoteEvent>();

    [Fact]
    public void ARepeatedChordDrawsNoLine()
    {
        Assert.Empty(FretboardMovementRules.Between(new[] { N(0, 5, 5), N(0, 4, 7) }, None, new[] { N(1, 5, 5), N(1, 4, 7) }));
        Assert.True(FretboardMovementRules.Repeats(new[] { N(0, 5, 5), N(0, 4, 7) }, N(1, 4, 7)));
    }

    [Fact]
    public void TheRestOfTheSameStrummedChordDrawsNoLine()
    {
        Assert.Empty(FretboardMovementRules.Between(new[] { N(0, 5, 5) }, None, new[] { N(0, 4, 7) }));
    }

    [Fact]
    public void AChordShapeShiftKeepsOneLinePerString()
    {
        var moves = FretboardMovementRules.Between(new[] { N(0, 5, 5), N(0, 4, 7) }, None, new[] { N(1, 5, 7), N(1, 4, 9) });
        Assert.Equal(new[] { ((5, 5), (5, 7)), ((4, 7), (4, 9)) }, moves.Select(m => (m.From, m.To)).ToArray());
    }

    [Fact]
    public void ANoteOnAnotherStringDrawsNoLine()
    {
        Assert.Empty(FretboardMovementRules.Between(new[] { N(0, 3, 5) }, None, new[] { N(1, 2, 5) }));
    }

    [Fact]
    public void AChordToOneOfItsStringsAtAnotherFretDrawsOneLine()
    {
        var moves = FretboardMovementRules.Between(new[] { N(0, 5, 5), N(0, 4, 7) }, None, new[] { N(1, 4, 9) });
        Assert.Equal(new[] { ((4, 7), (4, 9)) }, moves.Select(m => (m.From, m.To)).ToArray());
    }

    [Fact]
    public void AReleasedChordAtARestIsTheStartOfTheLine()
    {
        var moves = FretboardMovementRules.Between(None, new[] { N(0, 2, 3) }, new[] { N(1, 2, 5) });
        Assert.Single(moves);
    }
}
