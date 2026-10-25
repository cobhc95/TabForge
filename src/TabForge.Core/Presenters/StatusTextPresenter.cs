// Owns: the status bar's texts from plain state: the position line (the playing position while the transport runs, else the editing cursor,
//       and the audio-track hint), the bar's written:expected duration with its marked state and tooltip, the time signature and key labels,
//       and the master readout's text and key label.
// Does not own: the text controls, the marked-bar brush, the master readout's change check, the playhead and selection (the caller reads them),
//       or the tempo map and bar analysis (MusicTime decides those).
// Tests: no dedicated test class.
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>The window state the status bar text is built from; the caller reads it on the UI thread. A struct, so the per-frame status update allocates no box.</summary>
/// <param name="Project">The song whose bars are shown.</param>
/// <param name="SelectedTrack">The track the editing cursor is on, or null when there is none.</param>
/// <param name="TransportRunning">True while the transport plays; the status then shows the playhead.</param>
/// <param name="PlayheadBar">The playhead's bar (zero based), or -1 when there is no playhead.</param>
/// <param name="PlayheadCell">The playhead's cell (zero based).</param>
/// <param name="SelectedBar">The editing cursor's bar (zero based).</param>
/// <param name="SelectedCell">The editing cursor's cell (zero based).</param>
/// <param name="DurationDenominator">The current note value's denominator (4 is a quarter).</param>
/// <param name="Dots">The current note's dots.</param>
/// <param name="Triplet">True when the current note is a triplet.</param>
public readonly record struct StatusTextInput(
    SongProject Project,
    TrackModel? SelectedTrack,
    bool TransportRunning,
    int PlayheadBar,
    int PlayheadCell,
    int SelectedBar,
    int SelectedCell,
    int DurationDenominator,
    int Dots,
    bool Triplet);

/// <summary>The texts the status bar shows for one <see cref="StatusTextInput"/>.</summary>
/// <param name="MasterKeyLabel">The key as the master readout words it ("C major", "2♯"); the master readout text is made by <see cref="StatusTextPresenter.MasterInfoText"/>.</param>
public readonly record struct StatusTextReadout(
    string PositionText,
    string BarStateText,
    bool BarStateMarked,
    string BarStateTooltip,
    string TimeSignatureText,
    string KeyText,
    int Tempo,
    string MasterKeyLabel,
    string DurationName,
    int Dots,
    bool Triplet);

/// <summary>
/// The status bar's texts. <see cref="Compose"/> decides what each text says; <see cref="MasterInfoText"/> formats the master readout line.
/// Thread: UI thread only (the caller passes plain values read there).
/// </summary>
public static class StatusTextPresenter
{
    private const string MarkedBarTooltip = "This bar is incomplete or too long for its time signature";
    private const string PlainBarTooltip = "Bar duration: written : expected";

    /// <summary>The status texts for the given window state. Playing shows the playhead; a stopped transport shows the editing cursor.</summary>
    public static StatusTextReadout Compose(StatusTextInput input)
    {
        var project = input.Project;
        var track = input.SelectedTrack;
        // While the transport runs the status shows the playing position (like the standard LCD), not the editing cursor.
        var playing = input.TransportRunning && input.PlayheadBar >= 0;
        var shownBar = playing ? Math.Clamp(input.PlayheadBar, 0, Math.Max(0, BarRangeEditor.MaxMeasures(project) - 1)) : input.SelectedBar;
        var shownCell = playing ? Math.Max(0, input.PlayheadCell) : input.SelectedCell;
        var positionText = track is null ? "No track"
            : track.IsAudio ? $"{track.Name}  ·  {EditorGuard.Message}"   // nothing to edit on an audio track: the hint replaces the cursor
            : $"Measure {shownBar + 1}  ·  {track.Name}  ·  cell {shownCell + 1}";

        // The bar's actual:theoretical duration; the time signature and key come from the bar, or the song where the bar sets none.
        var bar = MusicTime.BarOf(project, shownBar);
        var numerator = bar?.TimeSigNum ?? project.TimeSignatureNumerator;
        var denominator = bar?.TimeSigDenom ?? project.TimeSignatureDenominator;
        var state = MusicTime.AnalyzeBar(project, shownBar, track);
        var shownKey = bar?.KeySignature ?? project.KeySignature;
        var shownKeyMinor = bar?.KeySignatureMinor ?? project.KeySignatureMinor;

        return new StatusTextReadout(
            PositionText: positionText,
            BarStateText: $"{numerator}/{denominator}  {state.Used:0.##}:{state.Slots}",
            BarStateMarked: state.Marked,
            BarStateTooltip: state.Marked ? MarkedBarTooltip : PlainBarTooltip,
            TimeSignatureText: $"{numerator}/{denominator}",
            KeyText: KeyNames.Short(shownKey, shownKeyMinor),
            Tempo: MusicTime.TempoAt(project, shownBar),
            MasterKeyLabel: MasterKeyLabel(shownKey),
            DurationName: MusicTime.DurationName(input.DurationDenominator),
            Dots: input.Dots,
            Triplet: input.Triplet);
    }

    /// <summary>The master readout line: tempo, key, note value, then the dots and the triplet when the note has them.</summary>
    public static string MasterInfoText(int tempo, string key, string duration, int dots, bool triplet)
        => $"♩={tempo}  ·  {key}  ·  {duration}{(dots == 1 ? " dotted" : dots >= 2 ? " double-dotted" : "")}{(triplet ? " triplet" : "")}";

    private static string MasterKeyLabel(int signature) => signature == 0 ? "C major" : signature > 0 ? $"{signature}♯" : $"{-signature}♭";
}
