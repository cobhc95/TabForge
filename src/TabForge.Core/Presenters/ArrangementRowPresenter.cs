// Owns: the arrangement track list's row geometry without UI or model types: the runs of tracks in one group, a row's top, the row
//       or group header found at a y position, and the total rows height, over a row source the view adapts from the project.
// Does not own: the row heights (the source says them), the group names, collapsing, drawing or the column layout.
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>The track list's rows as the geometry rules see them. Implemented by a struct so the generic callers do not box.</summary>
public interface IArrangementRowSource
{
    int Count { get; }
    /// <summary>Space above the first row (the master lane).</summary>
    double TopOffset { get; }
    /// <summary>Height of a group header strip.</summary>
    double HeaderHeight { get; }
    /// <summary>Height of a track's row; 0 for a collapsed track.</summary>
    double HeightOf(int index);
    /// <summary>Does a group header sit above this track?</summary>
    bool StartsGroup(int index);
}

/// <summary>Pure row geometry. Static and allocation-free apart from <see cref="GroupRuns"/>. Thread: UI thread.</summary>
public static class ArrangementRowPresenter
{
    /// <summary>The runs of consecutive tracks in one group: (group, first track, track count).</summary>
    public static List<(string Group, int Start, int Count)> GroupRuns(IReadOnlyList<string> groupOfTrack)
    {
        var runs = new List<(string, int, int)>();
        for (var i = 0; i < groupOfTrack.Count; i++)
        {
            if (runs.Count > 0 && runs[^1].Item1 == groupOfTrack[i]) runs[^1] = (runs[^1].Item1, runs[^1].Item2, runs[^1].Item3 + 1);
            else runs.Add((groupOfTrack[i], i, 1));
        }
        return runs;
    }

    /// <summary>Top of a track's row from the first row, group headers and collapsed rows included.</summary>
    public static double RowTop<T>(T rows, int index) where T : IArrangementRowSource
    {
        var y = rows.TopOffset;
        for (var i = 0; i <= index && i < rows.Count; i++)
        {
            if (rows.StartsGroup(i)) y += rows.HeaderHeight;
            if (i < index) y += rows.HeightOf(i);
        }
        return y;
    }

    /// <summary>The track whose row contains <paramref name="y"/> (a group header counts as its first track), or -1.</summary>
    public static int RowIndexAt<T>(T rows, double y) where T : IArrangementRowSource
    {
        if (rows.Count == 0 || y < rows.TopOffset) return -1;
        var top = rows.TopOffset;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows.StartsGroup(i))
            {
                top += rows.HeaderHeight;
                if (y < top) return i;
            }
            var height = rows.HeightOf(i);
            top += height;
            if (y < top && height > 0) return i;
        }
        return -1;
    }

    /// <summary>The group header at <paramref name="y"/>: the first track of its run, or -1.</summary>
    public static int GroupHeaderAt<T>(T rows, double y) where T : IArrangementRowSource
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows.StartsGroup(i)) continue;
            var top = RowTop(rows, i) - rows.HeaderHeight;
            if (y >= top && y < top + rows.HeaderHeight) return i;
        }
        return -1;
    }

    /// <summary>Bottom of the last row; <paramref name="emptyHeight"/> with no tracks.</summary>
    public static double RowsHeight<T>(T rows, double emptyHeight) where T : IArrangementRowSource
        => rows.Count == 0 ? emptyHeight : RowTop(rows, rows.Count - 1) + rows.HeightOf(rows.Count - 1);
}
