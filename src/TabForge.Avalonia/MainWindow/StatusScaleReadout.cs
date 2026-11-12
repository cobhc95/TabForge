using System;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.TransportPopups;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the status bar scale readout: showing the shared pick's text on the bar, the scale popup opened from the readout (above the bar's button or above the fretboard's), and the
//   status bar and fretboard hosts of the master panel.
// Does not own: the pick itself (ScalePick: detection, picks, the fretboard highlight, the deferred save), the button (StatusBarView), the panel rows (ScalePanel), the settings file (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.StatusBarScaleTests.
/// <summary>UI thread only.</summary>
internal sealed class StatusScaleReadout
{
    private const string TooltipSuffix = "Click to see the alternatives, choose your own, or change what it listens to.";

    private readonly StatusBarView _view;
    private ScalePanel? _popup;

    /// <param name="pick">The one shared scale pick this bar shows; its changes update the bar.</param>
    public StatusScaleReadout(StatusBarView view, ScalePick pick)
    {
        _view = view;
        Pick = pick;
        Pick.Refreshed += ShowOnBar;
        _view.ScaleRequested += Open;
    }

    /// <summary>A readout with a pick of its own over these settings, song and save (a status bar shown on its own).</summary>
    public StatusScaleReadout(StatusBarView view, Func<EditingSettings?> editing, Func<IEditableDocument> document, Func<SongProject> project, Action saveDeferred)
        : this(view, new ScalePick(editing, document, project, saveDeferred)) { }

    /// <summary>The shared pick (the piano roll and the fretboard read and write the same one).</summary>
    public ScalePick Pick { get; }

    // The bar shows what the pick last read; the pick tells when it changed.
    private void ShowOnBar()
    {
        if (Pick.ShownText is not { } text) return;
        _view.ShowScale(text, Pick.ShownContext is { } context ? context + ". " + TooltipSuffix : "Drum tracks have no scale.");
    }

    /// <summary>How many detections the engine ran (tests).</summary>
    internal int DetectionRuns => Pick.DetectionRuns;

    /// <summary>True once the readout was shown at least once (Current can be read).</summary>
    internal bool HasResult => Pick.HasResult;

    /// <summary>The readout the panels show now.</summary>
    public ScaleReadoutResult Current => Pick.Current;

    public ScaleReadoutSettings Settings => Pick.Settings;

    /// <summary>True when the next status bar pick is kept in the song for its section and track.</summary>
    public bool KeepChoice { get => Pick.KeepChoice; set => Pick.KeepChoice = value; }

    /// <summary>How the hosts reach each other's panels (the window sets it once).</summary>
    public ScaleHostLinks Links => Pick.Links;

    /// <summary>Reads the section at <paramref name="bar"/> for <paramref name="track"/> and shows it; the text changes only when the readout does.</summary>
    public void Show(SongProject song, int bar, TrackModel? track) => Pick.Show(song, bar, track);

    /// <summary>Opens the scale popup above the readout (never for a drum track: it has no scale).</summary>
    public void Open()
    {
        if (!Pick.HasResult) return;
        if (Pick.ReadsDrumTrack) return;
        _popup?.Close();
        _popup = new ScalePanel(BarHost());
        _popup.OpenAbove(_view.ScaleAnchor);
    }

    /// <summary>Opens the fretboard's scale popup above <paramref name="anchor"/>: the same body as the status bar's, with the fretboard look and the full scale list. Picks stick to the section and track.</summary>
    /// <returns>False when nothing opened (a drum track has no scale).</returns>
    public bool OpenForFretboard(global::Avalonia.Controls.Control anchor)
    {
        if (Pick.ReadsDrumTrack) return false;
        if (!Pick.HasResult) Pick.Recompute(Pick.Project, 0);
        if (!Pick.HasResult) return false;
        _popup?.Close();
        var wide = (global::Avalonia.VisualTree.VisualExtensions.GetVisualRoot(anchor) as global::Avalonia.Controls.TopLevel)?.ClientSize.Width >= ScalePopupLayout.WideThreshold;
        _popup = new ScalePanel(FretboardHost(wide));
        _popup.OpenAbove(anchor);
        return true;
    }

    /// <summary>The status bar host of the master panel (tests and the opener).</summary>
    internal IScalePanelHost BarHost() => new StatusBarScaleHost(Pick, Links);

    /// <summary>The fretboard host of the master panel.</summary>
    internal IScalePanelHost FretboardHost(bool wide = false) => new FretboardScaleHost(Pick, Links, wide);

    /// <summary>The open popup, for tests.</summary>
    internal ScalePanel? Popup => _popup;
}
