using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: the key and time signature positions and widths per bar, and the tempo text shown at the song start and at each tempo change.
// Does not own: the page layout (ScoreLayoutEngine.cs, ScoreLayoutEngine.Incremental.cs) or the drawing of the signatures (ScoreRenderer.Bars.cs).
// Tests: TestKeySignaturesCarryForward.

internal sealed partial class ScoreLayoutEngine
{
    internal bool KeySignatureChanges(TrackModel track, int measureIndex)
    {
        if (_host.Project is null) return false;
        if (measureIndex <= 0) return true;
        var previous = track.Measures[measureIndex - 1];
        var current = track.Measures[measureIndex];
        return (current.KeySignature ?? _host.Project.KeySignature) != (previous.KeySignature ?? _host.Project.KeySignature) ||
               (current.KeySignatureMinor ?? _host.Project.KeySignatureMinor) != (previous.KeySignatureMinor ?? _host.Project.KeySignatureMinor);
    }

    internal int PreviousKeySignature(TrackModel track, int measureIndex)
        => measureIndex <= 0 || _host.Project is null ? 0
            : track.Measures[measureIndex - 1].KeySignature ?? _host.Project.KeySignature;

    internal double KeySignatureWidth(TrackModel track, int measureIndex)
    {
        var current = track.Measures[measureIndex].KeySignature ?? _host.Project?.KeySignature ?? 0;
        var (naturals, accidentals) = ScoreClefKey.KeySignatureGlyphs(PreviousKeySignature(track, measureIndex), current);
        var count = naturals + accidentals;
        return count == 0 ? 0 : count * 10.5 + (naturals > 0 && accidentals > 0 ? 3 : 0) + 6;
    }

    internal bool TimeSignatureShown(TrackModel track, int measureIndex)
    {
        if (_host.Project is null) return false;
        if (measureIndex <= 0) return true;
        var current = track.Measures[measureIndex];
        var previous = track.Measures[measureIndex - 1];
        return (current.TimeSigNum ?? _host.Project.TimeSignatureNumerator) != (previous.TimeSigNum ?? _host.Project.TimeSignatureNumerator) ||
               (current.TimeSigDenom ?? _host.Project.TimeSignatureDenominator) != (previous.TimeSigDenom ?? _host.Project.TimeSignatureDenominator);
    }

    internal (string Num, string Den) TimeSignatureParts(MeasureModel measure)
        => ((measure.TimeSigNum ?? _host.Project?.TimeSignatureNumerator ?? 4).ToString(),
            (measure.TimeSigDenom ?? _host.Project?.TimeSignatureDenominator ?? 4).ToString());

    internal double TimeSignatureWidth(MeasureModel measure)
    {
        var (num, den) = TimeSignatureParts(measure);
        return Math.Max(ScoreText.MakeTextIn(ScoreTextArea.BarInfo, num, 22, ScoreText.Brush(Colors.White), FontWeights.Bold).Width,
                        ScoreText.MakeTextIn(ScoreTextArea.BarInfo, den, 22, ScoreText.Brush(Colors.White), FontWeights.Bold).Width);
    }

    /// <summary>Tempo mark at the start of the song and at every tempo change (the reference style "♩ = 120").</summary>
    internal string? TempoText(MeasureModel measure, int measureIndex)
    {
        if (measure.TempoChange.HasValue) return $"♩ = {measure.TempoChange}";
        return measureIndex == 0 && _host.Project is not null && _host.Project.Tempo > 0 ? $"♩ = {_host.Project.Tempo}" : null;
    }
}

