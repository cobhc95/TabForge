using System;
using System.Linq;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the status bar scale readout: reading the section at the playhead or cursor through ScaleReadoutEngine, showing its text on the bar, the scale popup
//   (opened from the readout, editing the saved scale settings and the picks), a pick kept in the song as one undo step, and driving the fretboard scale highlight
//   when the readout scale changes (a deferred settings save, never a broadcast per frame).
// Does not own: the detection and picks (ScaleReadoutEngine), the button (StatusBarView), the panel rows (ScalePanel), the settings file (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.StatusBarScaleTests.
/// <summary>UI thread only.</summary>
internal sealed class StatusScaleReadout
{
    private const string TooltipSuffix = "Click to see the alternatives, choose your own, or change what it listens to.";

    private readonly StatusBarView _view;
    private readonly Func<EditingSettings?> _editing;
    private readonly Func<IEditableDocument> _document;
    private readonly Func<SongProject> _project;
    private readonly Action _saveDeferred;
    private readonly ScaleReadoutEngine _engine = new();
    private readonly ScaleReadoutSettings _fallback = new();
    private ScaleReadoutResult? _result;
    private int _bar;
    private Guid? _trackId;   // the track read, by id: an undo may replace the song objects
    private string? _shownKey, _lastApplied;
    private ScalePanel? _popup;

    /// <param name="editing">The editing settings (null without a settings file: the readout then uses its defaults and drives no fretboard).</param>
    /// <param name="document">The shown song, whose kept picks are edited as one undo step.</param>
    /// <param name="saveDeferred">Saves the settings after the frame (a changed setting, a new fretboard highlight).</param>
    public StatusScaleReadout(StatusBarView view, Func<EditingSettings?> editing, Func<IEditableDocument> document, Func<SongProject> project, Action saveDeferred)
    {
        _view = view;
        _editing = editing;
        _document = document;
        _project = project;
        _saveDeferred = saveDeferred;
        _view.ScaleRequested += Open;
    }

    /// <summary>How many detections the engine ran (tests).</summary>
    internal int DetectionRuns => _engine.DetectionRuns;

    /// <summary>True once the readout was shown at least once (Current can be read).</summary>
    internal bool HasResult => _result is not null;

    /// <summary>The readout the panels show now.</summary>
    public ScaleReadoutResult Current => _result ?? throw new InvalidOperationException("The scale readout has not been shown yet.");

    public ScaleReadoutSettings Settings => _editing()?.ScaleReadout ?? _fallback;

    /// <summary>True when the next status bar pick is kept in the song for its section and track.</summary>
    public bool KeepChoice { get; set; }

    /// <summary>How the hosts reach each other's panels (the window sets it once).</summary>
    public ScaleHostLinks Links { get; } = new();

    public bool Flats => (MusicTime.BarOf(_project(), _bar)?.KeySignature ?? _project().KeySignature) < 0;

    public event Action? Refreshed;

    public bool HasSelection => _document().Selection.HasRange;

    public (int First, int Last)? SelectionRange => _document().Selection.HasRange ? (_document().Selection.StartBar, _document().Selection.EndBar) : null;

    /// <summary>Reads the section at <paramref name="bar"/> for <paramref name="track"/> and shows it; the text changes only when the readout does.</summary>
    public void Show(SongProject song, int bar, TrackModel? track)
    {
        _bar = bar;
        _trackId = track?.Id;
        // Drum tracks have no scale: the readout shows a dash and no detection runs for them.
        if (track is not null && (track.IsDrumTrack || track.MidiChannel == 9))
        {
            _result = new ScaleReadoutResult("—", ScaleReadoutEngine.ContextOf(song, bar, track, Settings, SelectionRange),
                ScaleReadoutEngine.KeyScale(song, bar), Array.Empty<ScaleMatch>(), false, null, false);
            const string drumKey = "—";
            if (drumKey != _shownKey)
            {
                _shownKey = drumKey;
                _view.ShowScale("—", "Drum tracks have no scale.");
                Refreshed?.Invoke();
            }
            return;
        }
        var result = _engine.Compute(song, bar, track, Settings, SelectionRange);
        _result = result;
        var key = result.Text + "|" + result.Context.Text;
        if (key != _shownKey)
        {
            _shownKey = key;
            _view.ShowScale(result.Text, result.Context.Text + ". " + TooltipSuffix);
            Refreshed?.Invoke();
        }
        ApplyToFretboard(result);
    }

    public void PickFor(ScaleMatch scale, bool keep, bool fromFretboard)
    {
        if (_result is null) return;
        var context = _result.Context;
        if (fromFretboard && Settings is { ShowOnFretboard: false } link) { link.ShowOnFretboard = true; _saveDeferred(); }   // a pick made at the fretboard lights it at once
        if (keep) DocumentEdits.Run(_document(), project => _engine.KeepPick(project, context, scale.Highlight), label: "Keep scale");
        else _engine.PickForSession(context, scale.Highlight);
        Recompute();
    }

    public void UseAuto()
    {
        if (_result is null) return;
        var context = _result.Context;
        _engine.ForgetSessionPick(context);
        if (_engine.PickOf(_project(), context, out _) is not null) DocumentEdits.Run(_document(), project => _engine.ReturnToAuto(project, context), label: "Scale back to auto");
        Recompute();
    }

    /// <summary>Tells the open panels and the piano roll to read again (a piano roll option changed).</summary>
    internal void NotifyRefreshed() => Refreshed?.Invoke();

    public void SettingChanged()
    {
        _saveDeferred();
        Recompute();
    }

    /// <summary>Opens the scale popup above the readout (never for a drum track: it has no scale).</summary>
    public void Open()
    {
        if (_result is null) return;
        if (_project().Tracks.FirstOrDefault(t => t.Id == _trackId) is { } track && (track.IsDrumTrack || track.MidiChannel == 9)) return;
        _popup?.Close();
        _popup = new ScalePanel(new StatusBarScaleHost(this, Links));
        _popup.OpenAbove(_view.ScaleAnchor);
    }

    /// <summary>Opens the fretboard's scale popup above <paramref name="anchor"/>: the same body as the status bar's, with the fretboard look and the full scale list. Picks stick to the section and track.</summary>
    /// <returns>False when nothing opened (a drum track has no scale).</returns>
    public bool OpenForFretboard(global::Avalonia.Controls.Control anchor)
    {
        if (_project().Tracks.FirstOrDefault(t => t.Id == _trackId) is { } track && (track.IsDrumTrack || track.MidiChannel == 9)) return false;
        if (_result is null) Recompute(_project(), 0);
        if (_result is null) return false;
        _popup?.Close();
        var wide = (global::Avalonia.VisualTree.VisualExtensions.GetVisualRoot(anchor) as global::Avalonia.Controls.TopLevel)?.ClientSize.Width >= ScalePopupLayout.WideThreshold;
        _popup = new ScalePanel(FretboardHost(wide));
        _popup.OpenAbove(anchor);
        return true;
    }

    public EditingSettings? Editing => _editing();

    public PitchHistogram? Histogram => _result is null ? null : SectionScaleDetector.Collect(_project(), _result.Context.Tracks, _result.Context.FirstBar, _result.Context.LastBar);

    public void LookChanged() => _saveDeferred();

    public void ClearHighlight()
    {
        if (_editing() is { } editing) editing.ScaleHighlight = null;
        Settings.ShowOnFretboard = false;
        _lastApplied = null;
        _saveDeferred();
        Recompute();
    }

    private void Recompute(SongProject song, int bar) => Show(song, bar, song.Tracks.FirstOrDefault(t => t.Id == _trackId) ?? song.FirstNotationTrack);

    /// <summary>The status bar host of the master panel (tests and the opener).</summary>
    internal IScalePanelHost BarHost() => new StatusBarScaleHost(this, Links);

    /// <summary>The fretboard host of the master panel.</summary>
    internal IScalePanelHost FretboardHost(bool wide = false) => new FretboardScaleHost(this, Links, wide);

    /// <summary>The open popup, for tests.</summary>
    internal ScalePanel? Popup => _popup;

    private void Recompute()
    {
        if (_result is null) return;
        _shownKey = null;   // a pick changes the picked card even when the text stays
        var song = _project();
        Show(song, _bar, song.Tracks.FirstOrDefault(t => t.Id == _trackId));
    }

    // The readout scale becomes the fretboard scale highlight when it changes (and the link is on); a manual highlight stays until the readout scale changes.
    private void ApplyToFretboard(ScaleReadoutResult result)
    {
        if (_editing() is not { } editing || !editing.ScaleReadout.ShowOnFretboard) { _lastApplied = null; return; }
        var highlight = result.Effective.Highlight;
        if (highlight == _lastApplied) return;
        _lastApplied = highlight;
        if (editing.ScaleHighlight == highlight) return;
        editing.ScaleHighlight = highlight;
        _saveDeferred();
    }
}
