using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the toolbar's SCALE group (the scale button that names the scale and opens the master scale panel, and Lock to scale) and STEP INPUT group (the toggle and, only while it
//       is on, its own 'Scale filter' dropdown: Off, Snap, Only scale notes), the master panel's piano roll host wiring and the tips that name the scale.
//       The row fold, the scale highlight and the Move by choice are not bar controls: the panel edits Fold and Highlight, the Move notes popup edits Move by.
// Does not own: the panel (ScalePanel), the host's sections (PianoRollScaleHost), the rows and the lock (PianoRollView.Song), the step maths (PianoRollSurface.StepAdd).
// Tests: TabForge.Avalonia.Tests.PianoRollToolbarGroupTests, ScalePanelHostTests, PianoRollScaleLockTests.
public sealed partial class PianoRollView
{
    private const string ScaleArrow = " ▾";

    private void BuildScaleControls(EditingSettings editing)
    {
        PianoRollToolbarLayout.StyleButton(_scaleButton).MinWidth = PianoRollToolbarLayout.ComboMinWidth;
        _scaleButton.Focusable = false;
        AutomationProperties.SetName(_scaleButton, "Scale");
        _scaleButton.Click += (_, _) => OpenScalePopup();
        PianoRollToolbarLayout.StyleButton(_lockScale).Focusable = false;
        _lockScale.IsChecked = editing.PianoRollScaleLock;
        AutomationProperties.SetName(_lockScale, "Lock to scale");
        _lockScale.Click += (_, _) => { _host.Editing().PianoRollScaleLock = _lockScale.IsChecked == true; _host.SaveSettings(); RefreshDerived(); };
        PianoRollToolbarLayout.StyleButton(_step).Content = Icon(IconGeometry.StepInput);
        _step.Focusable = false;
        ToolTip.SetTip(_step, "Step input: each piano key (or MIDI key) adds a note of the snap length at the step cursor and moves it on; Right / Left move it, Backspace steps back");
        AutomationProperties.SetName(_step, "Step input");
        _step.Click += (_, _) =>
        {
            Surface.StepInput = _step.IsChecked == true;
            if (Surface.StepInput) Surface.StepBeat = PlayheadBeat() ?? 0;
            _stepOn = Surface.StepInput;
            Surface.InvalidateVisual();
            Surface.Focus();
        };
        _step.IsCheckedChanged += (_, _) => _stepFilter.IsVisible = _step.IsChecked == true;
        PianoRollToolbarLayout.StyleCombo(_stepFilter).MinWidth = DialogMetrics.PianoRollWideComboMinWidth;
        _stepFilter.Focusable = false;
        _stepFilter.SelectedItem = PianoRollScaleLock.StepFilters.Contains(editing.PianoRollStepScaleFilter) ? editing.PianoRollStepScaleFilter : PianoRollScaleLock.StepSnap;
        PrefixSelection(_stepFilter, "Scale filter: ");
        AutomationProperties.SetName(_stepFilter, "Step input scale filter");
        ToolTip.SetTip(_stepFilter, StepFilterTip);
        _stepFilter.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _stepFilter.SelectedItem is not string filter) return;
            _host.Editing().PianoRollStepScaleFilter = filter;
            Surface.StepFilter = filter;
            _host.SaveSettings();
        };
    }

    private const string StepFilterTip = "Scale filter for step input: Off writes exactly the key you press, Snap moves a key outside the scale to the nearest scale note, Only scale notes ignores keys outside the scale. Independent of Lock to scale";

    /// <summary>What the scale button says: the scale the roll follows ('D minor', 'D minor (auto)') or 'No scale', with a drop arrow.</summary>
    internal string ScaleButtonText => PianoRollScale.DisplayLabel(EffectiveScaleHighlight(), SharedAuto()) + ScaleArrow;

    // The scale button, Lock and the step filter follow the settings and the track (a drum track has no scale).
    private void RefreshScaleControls()
    {
        var editing = _host.Editing();
        _lockScale.IsChecked = editing.PianoRollScaleLock;
        _stepFilter.SelectedItem = PianoRollScaleLock.StepFilters.Contains(editing.PianoRollStepScaleFilter) ? editing.PianoRollStepScaleFilter : PianoRollScaleLock.StepSnap;
        _scaleButton.Content = ScaleButtonText;
        UpdateScaleTips(EffectiveScaleHighlight(), SharedAuto(), Presenter.Scale is null);
        // Drum tracks have no scale: the scale button, lock and step filter stay disabled while one shows.
        var drums = Presenter.Track?.IsDrumTrack == true || Presenter.Track?.MidiChannel == 9;
        _scaleButton.IsEnabled = _lockScale.IsEnabled = _stepFilter.IsEnabled = !drums;
        if (!drums) return;
        const string drumTip = "Drum tracks have no scale";
        ToolTip.SetTip(_scaleButton, drumTip);
        ToolTip.SetTip(_lockScale, drumTip);
        ToolTip.SetTip(_stepFilter, drumTip);
    }

    /// <summary>The scale button and Lock tips naming the scale the roll follows now ("C major (auto)", or "No scale" when none is picked).</summary>
    private void UpdateScaleTips(string? highlight, bool auto, bool noScale)
    {
        var label = PianoRollScale.DisplayLabel(highlight, auto);
        _scaleButton.Content = label + ScaleArrow;
        ToolTip.SetTip(_scaleButton, noScale
            ? "Scale: no scale is picked (click to pick one or let the roll detect it); the rows show every pitch"
            : $"Scale: {label}. Click for the scale panel: Auto-detect or pick a scale, highlight it on the grid, show rows and Lock to scale");
        ToolTip.SetTip(_lockScale, noScale
            ? "Lock to scale: no scale is picked, so notes stay where they are put (pick a scale to snap them into key)"
            : $"Lock to scale: drawn, moved and transposed notes snap to the nearest pitch of {label} (off-scale rows dim). Step input has its own scale filter");
        if (Presenter.Track?.IsDrumTrack != true && Presenter.Track?.MidiChannel != 9) ToolTip.SetTip(_stepFilter, StepFilterTip);
    }

    /// <summary>The master scale panel above the scale button, with the piano roll host's sections (Show rows, Highlight the scale on the grid, Lock to scale).</summary>
    internal TransportPopupFrame? OpenScalePopup()
    {
        if (Presenter.Track?.IsDrumTrack == true || Presenter.Track?.MidiChannel == 9) return null;
        if (SharedReadout_() is null) return null;
        var panel = new ScalePanel(ScaleHost);
        panel.OpenAbove(_scaleButton);
        return panel.Frame;
    }

    /// <summary>How this roll's panel reaches the fretboard panel (the window sets it).</summary>
    internal ScaleHostLinks Links { get; } = new();

    /// <summary>True when the roll shows and the scale button can open the panel.</summary>
    internal bool CanOpenScalePanel => IsEffectivelyVisible && _scaleButton.IsEffectivelyEnabled;

    internal PianoRollScaleHost ScaleHost => _scaleHost ??= new PianoRollScaleHost(Shared(), Links, _host.Editing, () => _document, SyncScaleControls);

    /// <summary>The window's status bar scale readout (the one shared pick); the window sets it. Without it the roll keeps a readout of its own.</summary>
    internal Func<StatusScaleReadout?> SharedReadout { get; set; } = () => null;

    private StatusScaleReadout? _own;
    private StatusScaleReadout? _hooked;
    private bool _sharedBusy;

    // The readout every scale feature reads: the window's, else this roll's own (so a roll shown alone behaves the same).
    private StatusScaleReadout Shared()
    {
        var readout = SharedReadout() ?? (_own ??= new StatusScaleReadout(new StatusBarView(), () => _host.Editing(), () => _document!, () => _document!.Project, _host.SaveSettings));
        if (!ReferenceEquals(readout, _hooked))
        {
            if (_hooked is not null) _hooked.Refreshed -= OnSharedScaleChanged;
            _hooked = readout;
            readout.Refreshed += OnSharedScaleChanged;
        }
        return readout;
    }

    // A pick made in the status bar, the fretboard or this roll's panel: rows, lock set, toolbar label and tips follow at once.
    private void OnSharedScaleChanged()
    {
        if (_sharedBusy || _document is null) return;
        _sharedBusy = true;
        try { RefreshDerived(); RefreshTools(); Surface.InvalidateVisual(); Surface.Playhead.InvalidateVisual(); }
        finally { _sharedBusy = false; }
    }

    // The readout with the track shown read (null without a song, a track or on a drum track). The window's readout follows the status bar; a roll's own one reads bar 0.
    private StatusScaleReadout? SharedReadout_()
    {
        if (_document is null || Presenter.Track is not { } track || track.IsDrumTrack || track.MidiChannel == 9) return null;
        var readout = Shared();
        if (ReferenceEquals(readout, _own) || !readout.HasResult)
        {
            var was = _sharedBusy;
            _sharedBusy = true;
            try { readout.Show(_document.Project, 0, track); }
            finally { _sharedBusy = was; }
        }
        MigrateOldPick(readout);
        return readout;
    }

    private bool SharedAuto() => SharedReadout_() is { } readout && readout.Current.IsAuto;

    // An old saved piano roll manual pick is applied once to the shared pick, only when this section and track has no pick yet.
    private void MigrateOldPick(StatusScaleReadout readout)
    {
        var editing = _host.Editing();
        if (editing.PianoRollScalePickMigrated) return;
        editing.PianoRollScalePickMigrated = true;
        var old = PianoRollScale.NormalizePick(editing.PianoRollScalePick);
        if (old is null || PianoRollScale.IsAuto(old) || readout.Current.Pick is not null || ScaleReadoutEngine.Parse(old) is not { } match) return;
        readout.PickFor(match, keep: false, fromFretboard: false);
        _host.SaveSettings();
    }

    // The panel changed the highlight, the fold or the lock: the same settings, so the roll and the toolbar are relit here.
    private void SyncScaleControls()
    {
        _host.SaveSettings();
        RefreshDerived();
        RefreshTools();
        Surface.InvalidateVisual();
        Surface.Playhead.InvalidateVisual();
    }
}
