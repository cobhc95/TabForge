using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Band;

// Owns: which tracks the Band view shows, in what order, how many rows fit the screen and each row's own height; the drop maths of a row drag.
// Also: keeps the layout in the song (SongProject.BandLayout) on every change and reads it back when a song comes on show; with the sync
//   setting on, the Band order follows the song's track order.
// Does not own: the rows themselves (BandView, BandViewController) or the song's track order (the host moves tracks).
// Tests: TestBandPillsAndRows, TestBandRowSizing, TestBandReorder, TestBandStoppedInstruments, TestBandLayoutSaved.
internal sealed class BandLayoutState
{
    public const int MinRowsPerScreen = 1;
    public const int MaxRowsPerScreen = 5;
    public const int DefaultRowsPerScreen = 3;
    /// <summary>How many tracks are shown when a song opens in the Band view.</summary>
    public const int DefaultShown = 3;
    public const double MinRowHeight = 80;
    private const double MaxSavedHeight = 4000;

    private readonly List<TrackModel> _order = new();
    private readonly HashSet<TrackModel> _shown = new();
    private readonly Dictionary<TrackModel, double> _heights = new();
    private readonly HashSet<TrackModel> _hiddenInstruments = new();
    private readonly Dictionary<TrackModel, double> _widths = new();
    private readonly Func<BandSettings> _settings;
    private SongProject? _project;
    private int _defaultRows;
    private bool _ownRows;

    /// <summary>Bumped whenever the shown set or the order changes, so callers can skip re-checking them.</summary>
    public int Version { get; private set; }

    public BandLayoutState(Func<BandSettings>? settings = null) => _settings = settings ?? (() => new BandSettings());

    public int RowsPerScreen { get; private set; } = DefaultRowsPerScreen;

    /// <summary>Every track in the Band view's own order (shown or not).</summary>
    public IReadOnlyList<TrackModel> Order => _order;

    public bool IsShown(TrackModel track) => _shown.Contains(track);

    /// <summary>The shown tracks in Band order.</summary>
    public List<TrackModel> ShownTracks() => _order.Where(_shown.Contains).ToList();

    /// <summary>Follows the song: a new song starts with its saved layout (else its first tracks shown); added tracks join hidden at the end, removed ones leave.</summary>
    public void Sync(SongProject project)
    {
        var settings = _settings();
        var sync = settings.KeepOrderInSync;
        var rowsDefault = Math.Clamp(settings.RowsPerScreen, MinRowsPerScreen, MaxRowsPerScreen);
        if (!ReferenceEquals(project, _project))
        {
            _project = project;
            _order.Clear(); _shown.Clear(); _heights.Clear(); _hiddenInstruments.Clear(); _widths.Clear();
            _defaultRows = rowsDefault;
            RowsPerScreen = rowsDefault;
            _ownRows = false;
            Version++;
            if (project.BandLayout is { } saved) Load(project, saved, sync);
            else
            {
                _order.AddRange(project.Tracks);
                foreach (var track in project.Tracks.Take(DefaultShown)) _shown.Add(track);
            }
            return;
        }
        // A song with no rows per screen of its own follows the setting when it changes.
        if (rowsDefault != _defaultRows)
        {
            _defaultRows = rowsDefault;
            if (!_ownRows) { RowsPerScreen = rowsDefault; _heights.Clear(); }
        }
        var same = _order.Count == project.Tracks.Count && _order.All(project.Tracks.Contains);
        var ordered = !sync || _order.SequenceEqual(project.Tracks);
        if (same && ordered) return;
        Version++;
        if (!same)
        {
            _order.RemoveAll(t => !project.Tracks.Contains(t));
            _shown.RemoveWhere(t => !project.Tracks.Contains(t));
            foreach (var track in _heights.Keys.Where(t => !project.Tracks.Contains(t)).ToList()) _heights.Remove(track);
            foreach (var track in _widths.Keys.Where(t => !project.Tracks.Contains(t)).ToList()) _widths.Remove(track);
            _hiddenInstruments.RemoveWhere(t => !project.Tracks.Contains(t));
            foreach (var track in project.Tracks) if (!_order.Contains(track)) _order.Add(track);
        }
        if (sync) { _order.Clear(); _order.AddRange(project.Tracks); }
    }

    /// <summary>Reads a saved layout: ids of tracks that are gone are ignored, tracks the layout does not know join hidden at the end.</summary>
    private void Load(SongProject project, BandLayoutData saved, bool sync)
    {
        var byId = new Dictionary<Guid, TrackModel>();
        foreach (var track in project.Tracks) byId[track.Id] = track;
        if (!sync)
            foreach (var id in saved.Order ?? new()) if (byId.TryGetValue(id, out var t) && !_order.Contains(t)) _order.Add(t);
        foreach (var track in project.Tracks) if (!_order.Contains(track)) _order.Add(track);
        foreach (var id in saved.Shown ?? new()) if (byId.TryGetValue(id, out var t)) _shown.Add(t);
        foreach (var (id, height) in saved.Heights ?? new())
            if (byId.TryGetValue(id, out var t) && double.IsFinite(height)) _heights[t] = Math.Clamp(height, MinRowHeight, MaxSavedHeight);
        foreach (var id in saved.HiddenInstruments ?? new()) if (byId.TryGetValue(id, out var t)) _hiddenInstruments.Add(t);
        foreach (var (id, width) in saved.InstrumentWidths ?? new())
            if (byId.TryGetValue(id, out var t) && double.IsFinite(width)) _widths[t] = BandChoices.ClampWidth(width);
        if (saved.RowsPerScreen <= 0) return;
        _ownRows = true;
        RowsPerScreen = Math.Clamp(saved.RowsPerScreen, MinRowsPerScreen, MaxRowsPerScreen);
    }

    /// <summary>Writes the layout into the song so a save keeps it.</summary>
    private void Save()
    {
        if (_project is null) return;
        var data = _project.BandLayout ??= new BandLayoutData();
        data.Order = _order.Select(t => t.Id).ToList();
        data.Shown = _order.Where(_shown.Contains).Select(t => t.Id).ToList();
        data.Heights = _heights.ToDictionary(h => h.Key.Id, h => h.Value);
        data.HiddenInstruments = _order.Where(_hiddenInstruments.Contains).Select(t => t.Id).ToList();
        data.InstrumentWidths = _widths.ToDictionary(w => w.Key.Id, w => w.Value);
        data.RowsPerScreen = _ownRows ? RowsPerScreen : 0;
    }

    /// <summary>Shows or hides a track's row; there is no limit on how many are shown.</summary>
    public void Toggle(TrackModel track)
    {
        if (!_order.Contains(track)) return;
        if (!_shown.Remove(track)) _shown.Add(track);
        Version++;
        Save();
    }

    public void ChangeRowsPerScreen(int delta)
    {
        RowsPerScreen = Math.Clamp(RowsPerScreen + delta, MinRowsPerScreen, MaxRowsPerScreen);
        _ownRows = true;
        _heights.Clear();   // custom heights would hide the new rows: every row refits to the viewport share
        Save();
    }

    public double? HeightOf(TrackModel track) => _heights.TryGetValue(track, out var h) ? h : null;

    /// <summary>A row's own height: never below the minimum and never taller than the panel's viewport (at least the minimum).</summary>
    public static double ClampHeight(double height, double viewport) => Math.Clamp(height, MinRowHeight, Math.Max(MinRowHeight, viewport));

    public double SetHeight(TrackModel track, double height, double viewport)
    {
        var clamped = _heights[track] = ClampHeight(height, viewport);
        Save();
        return clamped;
    }

    /// <summary>Back to the view's defaults: row heights, instrument widths, hidden instruments and rows per screen. Which tracks show, and their order, stay.</summary>
    public void ResetView()
    {
        _heights.Clear(); _widths.Clear(); _hiddenInstruments.Clear();
        _ownRows = false;
        RowsPerScreen = Math.Clamp(_defaultRows, MinRowsPerScreen, MaxRowsPerScreen);
        Version++;
        Save();
    }

    public void ResetHeights()
    {
        if (_heights.Count == 0) return;
        _heights.Clear();
        Save();
    }

    public void ResetHeight(TrackModel track)
    {
        if (_heights.Remove(track)) Save();
    }

    public bool IsInstrumentHidden(TrackModel track) => _hiddenInstruments.Contains(track);

    /// <summary>Hides or shows one row's instrument.</summary>
    public void ToggleInstrument(TrackModel track)
    {
        if (!_order.Contains(track)) return;
        if (!_hiddenInstruments.Remove(track)) _hiddenInstruments.Add(track);
        Version++;
        Save();
    }

    public double? WidthOf(TrackModel track) => _widths.TryGetValue(track, out var w) ? w : null;

    public void SetWidth(TrackModel track, double width)
    {
        _widths[track] = BandChoices.ClampWidth(width);
        Version++;
        Save();
    }

    public void ResetWidth(TrackModel track)
    {
        if (!_widths.Remove(track)) return;
        Version++;
        Save();
    }

    public void ResetWidths()
    {
        if (_widths.Count == 0) return;
        _widths.Clear();
        Version++;
        Save();
    }

    /// <summary>Moves a shown track so it is the <paramref name="shownIndex"/>th shown row; the song's track order is not touched.</summary>
    public void MoveShown(TrackModel track, int shownIndex)
    {
        if (!_shown.Contains(track)) return;
        _order.Remove(track);
        var others = ShownTracks();
        shownIndex = Math.Clamp(shownIndex, 0, others.Count);
        Version++;
        _order.Insert(shownIndex < others.Count ? _order.IndexOf(others[shownIndex]) : _order.Count, track);
        Save();
    }

    /// <summary>
    /// Where a dragged row would land: its index among the rows once it is dropped. A row moving down passes a neighbour when its bottom edge
    /// crosses the neighbour's middle, moving up when its top edge does.
    /// </summary>
    public static int DropIndex(IReadOnlyList<double> pitches, int from, double draggedTop)
    {
        var bottom = draggedTop + pitches[from];
        var target = from;
        var y = 0.0;
        for (var i = 0; i < pitches.Count; i++)
        {
            var mid = y + pitches[i] / 2;
            y += pitches[i];
            if (i > from && bottom > mid) target = i;
            if (i < from && draggedTop < mid) { target = i; break; }
        }
        return target;
    }

    /// <summary>The top of slot <paramref name="index"/> once row <paramref name="from"/> is moved there.</summary>
    public static double SlotTop(IReadOnlyList<double> pitches, int from, int index)
    {
        var y = 0.0;
        var count = 0;
        for (var i = 0; i < pitches.Count && count < index; i++)
        {
            if (i == from) continue;
            y += pitches[i];
            count++;
        }
        return y;
    }
}
