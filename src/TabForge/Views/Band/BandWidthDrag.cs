using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Band;

// Owns: a drag of the grip between instrument and lane. While it runs, the latest asked width goes to the instrument column of the
//   affected rows (all rows, or the dragged one in "this row only" mode) at most once per rendered frame, and the lanes keep their
//   engraving (clipped by the row). When it ends, the width is stored (settings or the song's Band layout) and the lanes fit again, once.
// Does not own: the rows, what a stored width means (BandViewController.ApplyLayout), saving the settings (the end callback).
// Tests: TestBandInstrumentRows.
internal sealed class BandWidthDrag
{
    private readonly Func<BandSettings> _band;
    private readonly BandLayoutState _state;
    private readonly Func<IReadOnlyList<BandRow>> _rows;
    private readonly Action _ended;
    private TrackModel? _track;
    private double _pending = double.NaN, _width;

    public BandWidthDrag(Func<BandSettings> band, BandLayoutState state, Func<IReadOnlyList<BandRow>> rows, Action ended)
    {
        _band = band; _state = state; _rows = rows; _ended = ended;
    }

    /// <summary>True from the first move of a drag until it ends: the stored width is not the shown one meanwhile.</summary>
    public bool Active => _track is not null;

    /// <summary>How many times a width was handed to the rows (a self-test counter: at most one per frame).</summary>
    public int Applies { get; private set; }

    /// <summary>A move of the grip on <paramref name="track"/>'s row: remembered, shown with the next frame.</summary>
    public void Request(TrackModel track, double width)
    {
        _pending = BandChoices.ClampWidth(width);
        if (ReferenceEquals(_track, track)) return;
        if (_track is null) CompositionTarget.Rendering += OnFrame;
        _track = track;
        var rows = _rows();
        for (var i = 0; i < rows.Count; i++) rows[i].Lane.HoldFit = true;
    }

    private void OnFrame(object? sender, EventArgs e) => Apply();

    private void Apply()
    {
        if (double.IsNaN(_pending)) return;
        _width = _pending;
        _pending = double.NaN;
        Applies++;
        var own = _band().WidthPerRow;
        var rows = _rows();
        for (var i = 0; i < rows.Count; i++)
            if (!own || ReferenceEquals(rows[i].Track, _track)) rows[i].DragInstrumentWidth(_width);
    }

    /// <summary>The drag ended: the last width is shown and stored, the lanes fit to their new width, then the end callback runs.</summary>
    public void Commit()
    {
        if (_track is not { } track) return;
        Apply();
        Stop();
        if (_band().WidthPerRow) _state.SetWidth(track, _width); else _band().InstrumentWidth = _width;
        _ended();
    }

    /// <summary>Ends a drag without storing anything (the view closes).</summary>
    public void Stop()
    {
        if (_track is null) return;
        CompositionTarget.Rendering -= OnFrame;
        _track = null;
        _pending = double.NaN;
        var rows = _rows();
        for (var i = 0; i < rows.Count; i++) rows[i].Lane.HoldFit = false;
    }
}
