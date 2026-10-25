using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the transport bar fit rules: a wrapped height fits one row when no taller than the tallest group, and shrinking stops at the minimum button size.
/// Does not own: the layout of the groups (TransportBarView) or the button drawing.
/// </summary>
public class TransportWrapRulesTests
{
    [Fact]
    public void OneRowFitsAndTwoRowsDoNot()
    {
        Assert.True(TransportWrapRules.FitsOneRow(38, 38));
        Assert.False(TransportWrapRules.FitsOneRow(2 * 36, 36));
    }

    [Fact]
    public void ShrinkingStopsAtTheMinimumSize()
    {
        Assert.Equal(33, TransportWrapRules.NextSmallerSize(34, 26, 1));
        Assert.Equal(26, TransportWrapRules.NextSmallerSize(26, 26, 1));
        Assert.Equal(26, TransportWrapRules.NextSmallerSize(27, 26, 4));
    }
}
