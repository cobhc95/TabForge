using TabForge.Models;
using TabForge.Services;

namespace TabForge.Presenters;

// Owns: what every host of the master scale panel shows (the shared section ids, in order) and the piano roll host's readout: the detected scales of the shown
//   notes under the shared scale settings, the pick (null follows the setting, "Auto", or a "Root Scale" highlight) and the scale the roll follows.
// Does not own: the drawing (the app's ScalePanel), the detection maths (SectionScaleDetector), the piano roll pick storage (EditingSettings).
// Tests: TabForge.Avalonia.Tests.ScalePanelHostTests.
public static class ScalePanelModel
{
    public const string HeaderId = "Header", DetectedId = "Detected", PickAnotherId = "PickAnother", ReadFromId = "ReadFrom", ListenToId = "ListenTo", FamiliesId = "Families";

    /// <summary>The sections every host shows, top to bottom.</summary>
    public static IReadOnlyList<string> SharedSections { get; } = new[] { HeaderId, DetectedId, PickAnotherId, ReadFromId, ListenToId, FamiliesId };

    /// <summary>The piano roll's readout for its shown <paramref name="notes"/>.</summary>
    /// <param name="pick">The stored pick: null follows <paramref name="settingsHighlight"/>, "Auto" detects, else a "Root Scale" highlight.</param>
    public static ScaleReadoutResult PianoRollReadout(IEnumerable<PianoRollNote> notes, string? pick, string? settingsHighlight, ScaleReadoutSettings settings, string trackLabel)
    {
        var histogram = PianoRollScale.HistogramOf(notes);
        IReadOnlyList<ScaleMatch> top = histogram.NoteCount == 0 ? Array.Empty<ScaleMatch>() : SectionScaleDetector.Rank(histogram, settings.ConsideredScales(), settings.PreferCommon).Take(ScaleReadoutEngine.TopCount).ToList();
        var normal = PianoRollScale.NormalizePick(pick);
        var auto = PianoRollScale.IsAuto(normal);
        var manual = normal is not null && !auto ? ScaleReadoutEngine.Parse(normal) : null;
        var followed = ScaleReadoutEngine.Parse(PianoRollScale.EffectiveHighlight(settingsHighlight, normal, top));
        var effective = manual ?? followed ?? (top.Count > 0 ? top[0] : new ScaleMatch(0, "Major", 0, 0));
        var context = new ScaleContext("Piano roll", 0, 0, null, "*", trackLabel, Array.Empty<TrackModel>());
        return new ScaleReadoutResult(effective.Display(), context, effective, top, auto, manual?.Highlight, false);
    }
}
