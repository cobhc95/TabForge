namespace TabForge.Presenters;

/// <summary>A row of the FX chain list as a drag sees it: where it is, how deep it sits, whether it is a container that may take the dragged rows, whether it is dragged.</summary>
public readonly record struct DropRow(double Top, double Height, int Depth, bool Container, bool Moving);

/// <summary>Where a drop goes: in front of row <see cref="Insert"/> (the row count: the end), into the container row <see cref="Into"/> (-1: none),
/// with the insertion line in front of row <see cref="Insert"/> at the indent of <see cref="LineDepth"/> (-1: no line).</summary>
public readonly record struct DropTarget(int Insert, int Into, int LineDepth);

/// <summary>
/// Where a drag in the FX chain list would drop for a pointer position: the zones of a container row, and the end of an open container
/// chosen by the pointer's horizontal position; plus the order of rows after a move.
/// Owns: the drop rules and the indent step. Does not own: the drag gesture and the marks (Avalonia FxChainList), what a drop does to the chain (ChainTree).
/// Tests: TabForge.Avalonia.Tests.FxChainListTests.
/// </summary>
public static class FxChainDropMath
{
    /// <summary>The indent of one container level, in DIPs.</summary>
    public const double Indent = 16;
    /// <summary>The part of a container row, from its top and from its bottom, that drops in front of or behind it instead of into it.</summary>
    public const double EdgeShare = 0.2;
    /// <summary>The pointer's distance right of a container's plug-ins indent from which a drop at the end of that container goes inside.</summary>
    public const double InsideSlack = 8;

    /// <summary>The drop for a pointer at (<paramref name="x"/>, <paramref name="y"/>), in the coordinates of the rows' tops.</summary>
    public static DropTarget At(IReadOnlyList<DropRow> rows, double x, double y)
    {
        var (previous, insert) = (-1, rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Moving) continue;
            if (r.Container && y > r.Top + r.Height * EdgeShare && y < r.Top + r.Height * (1 - EdgeShare)) return new DropTarget(i, i, -1);
            if (y < r.Top + r.Height / 2) { insert = i; break; }
            previous = i;
        }
        var depth = insert < rows.Count ? rows[insert].Depth : 0;
        if (previous >= 0 && rows[previous].Depth > depth && x >= rows[previous].Depth * Indent + InsideSlack)
        {
            var inner = rows[previous].Depth;
            var parent = previous;
            while (parent >= 0 && rows[parent].Depth >= inner) parent--;
            if (parent >= 0 && rows[parent].Container) return new DropTarget(insert, parent, inner);
        }
        return new DropTarget(insert, -1, depth);
    }

    /// <summary>The rows in their order after the <paramref name="moving"/> rows (ascending) went together in front of row <paramref name="insert"/>.</summary>
    public static List<T> Reorder<T>(IReadOnlyList<T> rows, IReadOnlyList<int> moving, int insert)
    {
        var result = new List<T>(rows.Count);
        for (var i = 0; i <= rows.Count; i++)
        {
            if (i == Math.Min(insert, rows.Count)) result.AddRange(moving.Select(m => rows[m]));
            if (i < rows.Count && !moving.Contains(i)) result.Add(rows[i]);
        }
        return result;
    }
}
