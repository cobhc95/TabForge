// Owns: the render dialog's custom bar and section ranges: the bar-range check and labels, and the bars a section pair spans.
// Does not own: the dialog (Views/RenderWindow) or the render plan (RenderSpecBuilder).
// Tests: no dedicated test class.
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Rendering;

/// <summary>The "Custom bars" / "Custom sections" choices of the render dialog (pure, so they are testable without a window).</summary>
public static class RenderBarRange
{
    /// <summary>Why a 1-based bar range is not renderable, or null when it is.</summary>
    public static string? Validate(string fromText, string toText, int barCount)
    {
        if (barCount <= 0) return "The song has no bars.";
        if (!int.TryParse(fromText.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var from) ||
            !int.TryParse(toText.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var to))
            return "Enter whole bar numbers.";
        if (from < 1 || to < 1 || from > barCount || to > barCount) return $"Bars run from 1 to {barCount}.";
        if (to < from) return "The last bar must not come before the first.";
        return null;
    }

    /// <summary>"Verse 1 (bar 9)" for each section in song order.</summary>
    public static string Label(MarkerModel marker) => $"{marker.Title} (bar {marker.MeasureIndex + 1})";

    /// <summary>
    /// First and last source bar (0-based, inclusive) from the start of section <paramref name="from"/> to the end of section
    /// <paramref name="to"/> (indices into <see cref="SectionLayout.Sorted"/>): its end is where the pane draws it, i.e. the bar before the next
    /// section starts (or the song end; a section resized shorter ends earlier). Null when the sections are not renderable.
    /// </summary>
    public static (int FirstBar, int LastBar)? SectionBars(SongProject project, int from, int to)
    {
        var sorted = SectionLayout.Sorted(project);
        var barCount = BarRangeEditor.MaxMeasures(project);
        if (barCount <= 0 || from < 0 || to < from || to >= sorted.Count) return null;
        var first = Math.Clamp(sorted[from].MeasureIndex, 0, barCount - 1);
        var end = SectionLayout.End(sorted, to, barCount);   // exclusive
        return end > first ? (first, end - 1) : null;
    }
}
