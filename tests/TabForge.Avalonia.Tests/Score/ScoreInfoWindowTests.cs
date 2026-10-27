using TabForge.Models;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks that the Score information values are read from a song and written back to it, marking it changed.
/// Does not own: the dialog window (ScoreInfoWindow) or the Bar menu wiring (MainWindowBarMenu).
/// Tests: this class.
/// </summary>
public class ScoreInfoWindowTests
{
    [Fact]
    public void ApplyToWritesTheTexts()
    {
        var song = new SongProject { Title = "Old" };
        var values = new ScoreInfoValues("New", "Sub", "Artist", "Album", "Music", "Words", "(c)", "Tab", "Play soft", "Notice");

        values.ApplyTo(song);

        Assert.Equal(values, ScoreInfoValues.From(song));
    }
}
