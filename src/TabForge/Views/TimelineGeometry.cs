using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Canonical immutable horizontal geometry for an arrangement. Bar starts, widths, and right
/// boundaries are built together so hit testing, section drawing, and overlays share one axis.
/// </summary>
internal sealed class TimelineGeometry
{
    private readonly SongProject _project;
    private readonly double _unitWidth;
    private readonly double[] _barX;
    private readonly double[] _barWidth;
    private readonly double[] _boundaries;
    private readonly int[] _barSlots;

    public int BarCount => _barWidth.Length;
    public double TotalWidth => _boundaries[^1];

    public TimelineGeometry(SongProject project, double unitWidth)
    {
        _project = project;
        _unitWidth = unitWidth;
        var barCount = CountBars(project);
        _barX = new double[barCount];
        _barWidth = new double[barCount];
        _boundaries = new double[barCount + 1];
        _barSlots = new int[barCount];

        var x = 0.0;
        for (var bar = 0; bar < barCount; bar++)
        {
            var slots = MusicTime.BarSlots(project, bar);
            var width = unitWidth * Math.Clamp(slots / 16.0, 0.25, 4.0);
            _barX[bar] = x;
            _barWidth[bar] = width;
            _barSlots[bar] = slots;
            _boundaries[bar] = x;
            x += width;
        }
        _boundaries[barCount] = x;
    }

    public double XOfBar(int bar)
    {
        var index = Math.Clamp(bar, 0, BarCount);
        return index == BarCount ? _boundaries[BarCount] : _barX[index];
    }

    public double WidthOfBar(int bar)
    {
        if (bar >= 0 && bar < BarCount) return _barWidth[bar];
        return _unitWidth * Math.Clamp(MusicTime.BarSlots(_project, bar) / 16.0, 0.25, 4.0);
    }

    public double RightOfBar(int bar) => _boundaries[Math.Clamp(bar + 1, 0, BarCount)];

    public int BarAt(double x)
    {
        if (BarCount == 0) return 0;
        if (double.IsNaN(x) || x >= TotalWidth) return BarCount - 1;
        if (x <= 0) return 0;

        var low = 0;
        var high = _boundaries.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_boundaries[middle] <= x) low = middle + 1;
            else high = middle;
        }
        return Math.Clamp(low - 1, 0, BarCount - 1);
    }

    /// <summary>Low-frequency validation used when a project is rebound, not on render/hit-test paths.</summary>
    public bool Matches(SongProject project, double unitWidth)
    {
        if (!ReferenceEquals(_project, project) || Math.Abs(_unitWidth - unitWidth) >= 0.001 ||
            CountBars(project) != BarCount)
            return false;
        for (var bar = 0; bar < BarCount; bar++)
            if (_barSlots[bar] != MusicTime.BarSlots(project, bar)) return false;
        return true;
    }

    private static int CountBars(SongProject project)
    {
        var count = 0;
        for (var track = 0; track < project.Tracks.Count; track++)
            count = Math.Max(count, project.Tracks[track].Measures.Count);
        return count;
    }
}
