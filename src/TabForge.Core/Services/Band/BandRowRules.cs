using TabForge.Models;

namespace TabForge.Services;

// Owns: the rules of how many rows (systems) a track's Band pane shows: the limits, the snap of a dragged height to whole rows, and the
//   per-song store of the chosen counts (SongProject.BandPaneRows, by track id).
// Does not own: the pane, the drag or the menu (BandTrackPanel, BandView), the default count a pane has without a choice (BandCardFit).
// Tests: TabForge.Avalonia.Tests.BandRowsTests.
public static class BandRowRules
{
    /// <summary>The fewest rows a pane shows, and the most the user can choose.</summary>
    public const int MinRows = 1, MaxRows = 6;

    /// <summary>The row count kept inside the limits.</summary>
    public static int Clamp(int rows) => Math.Clamp(rows, MinRows, MaxRows);

    /// <summary>The most whole rows that fit <paramref name="height"/> (never fewer than <see cref="MinRows"/>, never more than <paramref name="available"/> or <see cref="MaxRows"/>), so a row is never cut.</summary>
    /// <param name="heightOfRows">The pane height with the given number of rows.</param>
    public static int Snap(double height, Func<int, double> heightOfRows, int available)
    {
        var rows = MinRows;
        for (var n = MinRows + 1; n <= Math.Min(MaxRows, available); n++)
            if (heightOfRows(n) <= height) rows = n;
        return rows;
    }

    /// <summary>The rows chosen for the track in this song, or null when it has none (the default applies).</summary>
    public static int? RowsOf(SongProject project, Guid trackId) =>
        project.BandPaneRows is { } stored && stored.TryGetValue(trackId, out var rows) ? Clamp(rows) : null;

    /// <summary>Stores the track's row count; false when it already holds that count.</summary>
    public static bool Set(SongProject project, Guid trackId, int rows)
    {
        rows = Clamp(rows);
        var stored = project.BandPaneRows ??= new();
        if (stored.TryGetValue(trackId, out var old) && old == rows) return false;
        stored[trackId] = rows;
        return true;
    }

    /// <summary>Forgets every chosen count; false when there was none.</summary>
    public static bool Clear(SongProject project)
    {
        if (project.BandPaneRows is not { Count: > 0 }) return false;
        project.BandPaneRows = null;
        return true;
    }

    /// <summary>Repairs what a file holds: counts inside the limits, none for a track the song does not have.</summary>
    public static void Sanitize(SongProject project)
    {
        if (project.BandPaneRows is not { } stored) return;
        var ids = project.Tracks?.Select(track => track.Id).ToHashSet() ?? new HashSet<Guid>();
        foreach (var (id, rows) in stored.ToList())
        {
            if (!ids.Contains(id)) stored.Remove(id);
            else if (rows != Clamp(rows)) stored[id] = Clamp(rows);
        }
        if (stored.Count == 0) project.BandPaneRows = null;
    }
}
