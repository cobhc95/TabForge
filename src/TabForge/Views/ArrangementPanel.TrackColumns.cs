using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// ArrangementPanel: the track-column layout and tuning button (forwarded to their controllers) and the controls in the columns.
public sealed partial class ArrangementPanel : ITrackColumnHost, ITuningButtonHost, ITrackRowWidgetHost
{
    void ITrackRowWidgetHost.RaiseMixEditStarting() => MixEditStarting?.Invoke(this, EventArgs.Empty);
    void ITrackRowWidgetHost.RaiseMixEditEnded() => MixEditEnded?.Invoke(this, EventArgs.Empty);
    void ITrackRowWidgetHost.RaisePanStyleChanged(bool knobs) => PanStyleChanged?.Invoke(this, knobs);

    public static readonly string[] DefaultColumnOrder = TrackColumnLayout.DefaultOrder;
    /// <summary>Width (DIPs) a column's header label needs at the header font, so no header ever trims.</summary>
    public static double HeaderNeed(string label) => TrackColumnLayout.HeaderNeed(label);
    /// <summary>Each column's minimum width and the width its header needs (self-test: minimum >= need).</summary>
    public static IEnumerable<(string id, double min, double need)> ColumnMinimumsForTest() => TrackColumnLayout.MinimumsForTest();
    /// <summary>Raised after the user finishes resizing or reordering track columns.</summary>
    public event EventHandler? ColumnLayoutChanged;

    /// <summary>Column order, widths and the whole controls-area width, for saving in settings.</summary>
    public (List<string> order, Dictionary<string, double> widths, double area) ColumnState
    {
        get => _columns.State;
        set => _columns.State = value;
    }

    public void ResetColumnLayout() => _columns.Reset(ControlsWidth);

    /// <summary>Track columns the user hid (Mixer: "Show volume / pan in the track list"). They keep their place.</summary>
    public IReadOnlyCollection<string> HiddenColumns
    {
        get => _columns.HiddenColumns;
        set => _columns.HiddenColumns = value;
    }

    double ITrackColumnHost.ControlsAreaWidth
    {
        get => ColumnDefinitions[0].Width.Value;
        set => ColumnDefinitions[0].Width = new GridLength(value);
    }
    void ITrackColumnHost.RebuildControls() => RebuildControls();
    ContextMenu ITrackColumnHost.BuildColumnHeaderMenu() => BuildEmptyAreaMenu(new Control[] { ResetColumnsItem() });
    void ITrackColumnHost.RaiseColumnLayoutChanged() => ColumnLayoutChanged?.Invoke(this, EventArgs.Empty);
    void ITuningButtonHost.RaiseTuningIconClicked() => TuningIconClicked?.Invoke(this, EventArgs.Empty);
    void ITuningButtonHost.RaiseTuningNumberClicked() => TuningNumberClicked?.Invoke(this, EventArgs.Empty);
    void ITuningButtonHost.RaiseTuningMenuRequested() => TuningMenuRequested?.Invoke(this, EventArgs.Empty);
    void ITuningButtonHost.RaiseTuningShiftEdited(int semitones) => TuningShiftEdited?.Invoke(this, semitones);

    /// <summary>Master (all tracks) volume knob in the arrangement header, 0–100 %.</summary>
    public KnobControl MasterVolumeKnob { get; private set; } = null!;
    /// <summary>Global tuning button in the arrangement header; the window builds its menu.</summary>
    public Button TuningButton { get; private set; } = null!;

    public void SetTuningLabel(int semitones) =>
        _tuning.SetLabel(semitones == 0 ? "0" : semitones > 0 ? $"+{semitones}" : $"{semitones}");
    public void SetTuningLabel(string text) => _tuning.SetLabel(text);

    /// <summary>Left click on the tuning-fork icon (opens the global tuning window when enabled).</summary>
    public event EventHandler? TuningIconClicked;
    /// <summary>Left click on the shift number (type-in dialog).</summary>
    public event EventHandler? TuningNumberClicked;
    /// <summary>Right click anywhere on the tuning button (quick menu).</summary>
    public event EventHandler? TuningMenuRequested;
    /// <summary>Inline edit committed: the new absolute semitone shift.</summary>
    public event EventHandler<int>? TuningShiftEdited;
    /// <summary>Show pan as rotary knobs (default) or as the older horizontal sliders.</summary>
    public bool PanKnobs { get; set; } = true;
    /// <summary>The user switched the pan control style from a pan control's context menu.</summary>
    public event EventHandler<bool>? PanStyleChanged;

    /// <summary>Reads the "auto-resize track list to fit" setting (owned by the main window's settings).</summary>
    public Func<bool>? AutoFitState { get; set; }
    public event Action? AutoFitToggleRequested;

    /// <summary>Show track volume as rotary knobs instead of the standard slider (default slider).</summary>
    public bool VolumeKnobs { get; set; }
}
