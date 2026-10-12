using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Band;

// Owns: which tracks the Band view shows, in what order, how many rows fit the screen and each row's own height; the drop maths of a row drag.
// Also: the layout is the program's, not the song's: instruments, row heights and widths start from the Band settings and what the user
//   changes goes back there as a rule for every tab. The song keeps only which of its tracks show and their order
//   (SongProject.BandLayout.Shown / Order); with the sync setting on, the Band order follows the song's track order.
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
    private int _systems, _main, _resets;

    /// <summary>The systems the main (first) row's lane holds: 0 = automatic (one more than the others when that fits).</summary>
    public int MainSystems => _main;

    /// <summary>The systems each row's lane holds by default: 0 = as many as fit (at least one), a number = that many (cut to what fits),
    /// -1 = the rows share the viewport by "rows per screen" (the song or the user chose that).</summary>
    public int AutoSystems => _ownRows || _systems < 0 ? -1 : _systems;

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
            _systems = settings.SystemsPerRow; _main = settings.MainSystemsPerRow; _resets = settings.Resets;
            Version++;
            if (project.BandLayout is { } saved) Load(project, saved, sync);
            else
            {
                _order.AddRange(project.Tracks);
                foreach (var track in project.Tracks.Take(DefaultShown)) _shown.Add(track);
            }
            ApplyFretboardRule(settings.Fretboards);
            return;
        }
        // Reset view in any tab or window puts every Band view back to the defaults.
        if (settings.Resets != _resets) ApplyDefaults(settings);
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

    /// <summary>Reads which tracks the song shows and in what order (all it keeps of the Band view): ids of tracks that are gone are ignored,
    /// tracks it does not know join hidden at the end. Heights, widths and instruments an older file stored are not read.</summary>
    private void Load(SongProject project, BandLayoutData saved, bool sync)
    {
        var byId = new Dictionary<Guid, TrackModel>();
        foreach (var track in project.Tracks) byId[track.Id] = track;
        if (!sync)
            foreach (var id in saved.Order ?? new()) if (byId.TryGetValue(id, out var t) && !_order.Contains(t)) _order.Add(t);
        foreach (var track in project.Tracks) if (!_order.Contains(track)) _order.Add(track);
        foreach (var id in saved.Shown ?? new()) if (byId.TryGetValue(id, out var t)) _shown.Add(t);
    }

    /// <summary>Writes which tracks show and their order into the song; whatever else an older file stored there is left as it is.</summary>
    private void Save()
    {
        if (_project is null) return;
        var data = _project.BandLayout ??= new BandLayoutData();
        data.Order = _order.Select(t => t.Id).ToList();
        data.Shown = _order.Where(_shown.Contains).Select(t => t.Id).ToList();
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
        var settings = _settings();
        settings.SystemsPerRow = -1; settings.RowsPerScreen = _defaultRows = RowsPerScreen;   // songs with no layout of their own start this way too
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

    /// <summary>Back to the view's defaults: row heights (whole systems that fit), instrument widths, the first row's instrument only and rows per screen. Which tracks show, and their order, stay.</summary>
    public void ResetView()
    {
        var settings = _settings();
        settings.Fretboards = BandChoices.FretboardsFirst; settings.SystemsPerRow = settings.MainSystemsPerRow = 0; settings.RowsPerScreen = DefaultRowsPerScreen;
        settings.Resets++;
        ApplyDefaults(settings);
    }

    /// <summary>Takes the row heights, widths, instruments and rows per screen from the settings again, dropping what this view changed.</summary>
    private void ApplyDefaults(BandSettings settings)
    {
        _resets = settings.Resets;
        _heights.Clear(); _widths.Clear();
        _systems = settings.SystemsPerRow; _main = settings.MainSystemsPerRow;
        _defaultRows = RowsPerScreen = Math.Clamp(settings.RowsPerScreen, MinRowsPerScreen, MaxRowsPerScreen);
        _ownRows = false;
        ApplyFretboardRule(settings.Fretboards);
        Version++;
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
        RememberFretboardRule();
        Version++;
        Save();
    }

    /// <summary>A song comes on show with the instruments the rule names: the first shown row's only, all, or none.</summary>
    private void ApplyFretboardRule(string? rule)
    {
        _hiddenInstruments.Clear();
        if (rule == BandChoices.FretboardsAll) return;
        var first = rule == BandChoices.FretboardsNone ? null : ShownTracks().FirstOrDefault();
        foreach (var track in _order) if (!ReferenceEquals(track, first)) _hiddenInstruments.Add(track);
    }

    /// <summary>What the user last made of the shown rows becomes the rule for the next tabs: every instrument on, every one off,
    /// or the first row's only; any other mix stays in this view only and keeps the rule.</summary>
    private void RememberFretboardRule()
    {
        var shown = ShownTracks();
        if (shown.Count == 0) return;
        var hidden = shown.Count(_hiddenInstruments.Contains);
        var settings = _settings();
        if (hidden == shown.Count) settings.Fretboards = BandChoices.FretboardsNone;
        else if (shown.Count == 1) { if (settings.Fretboards == BandChoices.FretboardsNone) settings.Fretboards = BandChoices.FretboardsFirst; }
        else if (hidden == 0) settings.Fretboards = BandChoices.FretboardsAll;
        else if (hidden == shown.Count - 1 && !_hiddenInstruments.Contains(shown[0])) settings.Fretboards = BandChoices.FretboardsFirst;
    }

    /// <summary>A row was dragged to hold <paramref name="systems"/> systems: the next tabs start with that many in the main row, or in every other row.</summary>
    public void RememberSystems(bool main, int systems)
    {
        if (main) _settings().MainSystemsPerRow = Math.Max(1, systems); else _settings().SystemsPerRow = Math.Max(1, systems);
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
