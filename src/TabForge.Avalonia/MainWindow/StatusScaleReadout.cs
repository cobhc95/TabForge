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
// Does not own: the detection and picks (ScaleReadoutEngine), the button (StatusBarView), the popup rows (ScaleReadoutPopup), the settings file (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.StatusBarScaleTests.
/// <summary>UI thread only.</summary>
internal sealed class StatusScaleReadout : IScalePopupHost
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
    private ScaleReadoutPopup? _popup;

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

    /// <inheritdoc />
    public ScaleReadoutResult Current => _result ?? throw new InvalidOperationException("The scale readout has not been shown yet.");

    /// <inheritdoc />
    public ScaleReadoutSettings Settings => _editing()?.ScaleReadout ?? _fallback;

    /// <inheritdoc />
    public bool KeepChoice { get; set; }

    /// <inheritdoc />
    public bool Flats => (MusicTime.BarOf(_project(), _bar)?.KeySignature ?? _project().KeySignature) < 0;

    /// <inheritdoc />
    public event Action? Refreshed;

    /// <summary>Reads the section at <paramref name="bar"/> for <paramref name="track"/> and shows it; the text changes only when the readout does.</summary>
    public void Show(SongProject song, int bar, TrackModel? track)
    {
        _bar = bar;
        _trackId = track?.Id;
        var result = _engine.Compute(song, bar, track, Settings);
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

    /// <inheritdoc />
    public void Pick(ScaleMatch scale)
    {
        if (_result is null) return;
        var context = _result.Context;
        if (KeepChoice) DocumentEdits.Run(_document(), project => _engine.KeepPick(project, context, scale.Highlight), label: "Keep scale");
        else _engine.PickForSession(context, scale.Highlight);
        Recompute();
    }

    /// <inheritdoc />
    public void UseAuto()
    {
        if (_result is null) return;
        var context = _result.Context;
        _engine.ForgetSessionPick(context);
        if (_engine.PickOf(_project(), context, out _) is not null) DocumentEdits.Run(_document(), project => _engine.ReturnToAuto(project, context), label: "Scale back to auto");
        Recompute();
    }

    /// <inheritdoc />
    public void SettingChanged()
    {
        _saveDeferred();
        Recompute();
    }

    /// <summary>Opens the scale popup above the readout.</summary>
    public void Open()
    {
        if (_result is null) return;
        _popup?.Close();
        _popup = new ScaleReadoutPopup(this);
        _popup.OpenAbove(_view.ScaleAnchor);
    }

    /// <summary>The open popup, for tests.</summary>
    internal ScaleReadoutPopup? Popup => _popup;

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
