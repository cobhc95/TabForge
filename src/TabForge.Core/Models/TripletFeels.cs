namespace TabForge.Models;

// Owns: the triplet-feel values (stored as text in .tforge files) and the rule that maps them.
// Does not own: MeasureModel, which holds the value (SongProject.cs).
// Tests: TestGpRoundTripCounts.

/// <summary>
/// Values of <see cref="MeasureModel.TripletFeelKind"/> (stored as text in .tforge files) and the one rule
/// that resolves a bar's swing: an explicit kind wins, otherwise the legacy <see cref="MeasureModel.TripletFeel"/>
/// flag means 8th swing.
/// </summary>
public static class TripletFeels
{
    public const string None = "None";
    public const string Eighth = "Triplet8th";
    public const string Sixteenth = "Triplet16th";

    public static string Effective(MeasureModel measure) =>
        measure.TripletFeelKind is Eighth or Sixteenth ? measure.TripletFeelKind
        : measure.TripletFeel ? Eighth : None;
}
