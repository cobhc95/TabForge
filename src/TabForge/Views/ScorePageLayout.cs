namespace TabForge.Views;

/// <summary>Resolved horizontal geometry for one engraved measure.</summary>
internal readonly record struct ScoreMeasurePosition(
    int MeasureIndex, int SystemIndex, int ColumnIndex, double X, double Width, double NaturalWidth);

/// <summary>A system row whose measures were packed from intrinsic widths and conservatively justified.</summary>
internal sealed class ScoreSystemPosition
{
    public required int Index { get; init; }
    public required IReadOnlyList<ScoreMeasurePosition> Measures { get; init; }
    public required double X { get; init; }
    public required double Width { get; init; }
    public int FirstMeasure => Measures[0].MeasureIndex;
    public int LastMeasure => Measures[^1].MeasureIndex;
}

/// <summary>
/// Automatic score-system composer. Preferred readable widths are derived from intrinsic measure
/// widths; non-final systems are proportionally justified to the full page width (dense bars take the larger
/// share), while a short final system stays compact. Force/prevent breaks and an optional fixed count are explicit overrides.
/// </summary>
internal sealed class ScorePageLayout
{
    internal const double MinimumMeasureWidth = 70;
    /// <summary>Breathing room required when admitting a bar to a system (1.0 = minimum readable width).</summary>
    internal const double PreferredPackingFactor = 1.05;
    /// <summary>Non-final systems are justified to the full width unless that would stretch bars beyond this.</summary>
    internal const double MaximumExpansionJustified = 2.50;
    /// <summary>A final system this full (natural width / page width) is justified like any other system.</summary>
    internal const double FinalSystemJustifyThreshold = 0.60;
    internal const double MaximumExpansionSingleMeasure = 1.08;
    internal const double MaximumExpansionTwoMeasures = 1.16;
    internal const double MaximumExpansionThreeMeasures = 1.28;
    internal const double MaximumExpansionFullSystem = 1.65;
    internal const double MaximumExpansionFinalSingle = 1.04;
    internal const double MaximumExpansionFinalPair = 1.10;
    internal const double MaximumExpansionFinalPartial = 1.16;
    internal const double MaximumExpansionFinalFull = 1.20;

    private readonly ScoreMeasurePosition[] _measures;

    private ScorePageLayout(IReadOnlyList<ScoreSystemPosition> systems, ScoreMeasurePosition[] measures)
    {
        Systems = systems;
        _measures = measures;
    }

    public IReadOnlyList<ScoreSystemPosition> Systems { get; }
    public int SystemCount => Systems.Count;
    public ScoreMeasurePosition Measure(int measureIndex) => _measures[measureIndex];
    public int SystemForMeasure(int measureIndex) => _measures[measureIndex].SystemIndex;

    /// <param name="naturalWidths">Content-aware, readable lower bounds for each measure.</param>
    /// <param name="fixedMeasuresPerSystem">Optional fixed-count organization; null/zero uses automatic packing.</param>
    public static ScorePageLayout Create(double gridLeft, double availableWidth, IReadOnlyList<double> naturalWidths,
        IReadOnlyList<bool>? forceLineBreaks = null, IReadOnlyList<bool>? preventLineBreaks = null,
        int? fixedMeasuresPerSystem = null, bool centerRows = false)
    {
        ArgumentNullException.ThrowIfNull(naturalWidths);
        var usableWidth = double.IsFinite(availableWidth) ? Math.Max(1, availableWidth) : 1;
        var left = double.IsFinite(gridLeft) ? gridLeft : 0;
        var fixedCount = fixedMeasuresPerSystem.GetValueOrDefault() > 0
            ? fixedMeasuresPerSystem.GetValueOrDefault()
            : int.MaxValue;
        var minimums = naturalWidths.Select(width =>
            Math.Max(MinimumMeasureWidth, double.IsFinite(width) ? width : MinimumMeasureWidth)).ToArray();
        var preferredWidths = minimums.Select(width => width * PreferredPackingFactor).ToArray();

        // Greedy automatic composition: each next bar is admitted only when its preferred readable
        // spacing fits. A marked prevent-break may exceed the row width by explicit user choice;
        // force-break always starts a new row and therefore wins over prevent-break.
        var rows = new List<List<int>>();
        var row = new List<int>();
        var rowPreferredWidth = 0.0;
        var rowMinimumWidth = 0.0;
        for (var measure = 0; measure < minimums.Length; measure++)
        {
            var forceBreak = forceLineBreaks is not null && measure < forceLineBreaks.Count && forceLineBreaks[measure];
            // A prevent-break is honoured only while the row still fits the page at its tightest spacing: nothing may run past the page edge.
            var preventBreak = preventLineBreaks is not null && measure < preventLineBreaks.Count && preventLineBreaks[measure]
                && rowMinimumWidth + minimums[measure] <= usableWidth + 0.001;
            var wouldExceedWidth = rowPreferredWidth + preferredWidths[measure] > usableWidth + 0.001;
            var wouldExceedFixedCount = row.Count >= fixedCount;
            if (row.Count > 0 && (forceBreak || (!preventBreak && (wouldExceedWidth || wouldExceedFixedCount))))
            {
                rows.Add(row);
                row = new List<int>();
                rowPreferredWidth = 0;
                rowMinimumWidth = 0;
            }
            row.Add(measure);
            rowPreferredWidth += preferredWidths[measure];
            rowMinimumWidth += minimums[measure];
        }
        if (row.Count > 0) rows.Add(row);
        if (rows.Count == 0) rows.Add(new List<int>());

        var positions = new ScoreMeasurePosition[naturalWidths.Count];
        var systems = new List<ScoreSystemPosition>(rows.Count);
        for (var systemIndex = 0; systemIndex < rows.Count; systemIndex++)
        {
            var indexes = rows[systemIndex];
            if (indexes.Count == 0)
            {
                systems.Add(new ScoreSystemPosition
                {
                    Index = systemIndex,
                    Measures = Array.Empty<ScoreMeasurePosition>(),
                    X = left,
                    Width = usableWidth
                });
                continue;
            }

            var naturalTotal = indexes.Sum(index => minimums[index]);
            var finalSystem = systemIndex == rows.Count - 1;
            // Engraving convention: every system except a sparse final one spans the full page width,
            // so rows never leave half the page empty. Very sparse rows (e.g. one bar before a forced
            // break) and a short final row keep the conservative caps.
            // The reference justifies every system except a short final one, however few bars it holds; leaving
            // sparse rows at natural width produced half-empty lines in the middle of songs.
            var fillFullWidth = !finalSystem || naturalTotal >= usableWidth * FinalSystemJustifyThreshold;
            var expansionLimit = fillFullWidth ? double.PositiveInfinity : ExpansionLimit(indexes.Count, finalSystem);
            var justifiedTotal = Math.Min(usableWidth, naturalTotal * expansionLimit);
            var extra = Math.Max(0, justifiedTotal - naturalTotal);
            var rowLeft = left + (centerRows ? Math.Max(0, (usableWidth - justifiedTotal) / 2) : 0);
            var x = rowLeft;
            var rowMeasures = new List<ScoreMeasurePosition>(indexes.Count);
            for (var column = 0; column < indexes.Count; column++)
            {
                var measureIndex = indexes[column];
                var natural = minimums[measureIndex];
                // Natural width is also the content weight: dense/annotated bars receive a larger
                // share of the available expansion, while sparse bars remain compact.
                var width = natural + extra * (natural / naturalTotal);
                var position = new ScoreMeasurePosition(measureIndex, systemIndex, column, x, width, natural);
                positions[measureIndex] = position;
                rowMeasures.Add(position);
                x += width;
            }

            systems.Add(new ScoreSystemPosition
            {
                Index = systemIndex,
                Measures = rowMeasures,
                X = rowLeft,
                Width = justifiedTotal
            });
        }
        return new ScorePageLayout(systems, positions);
    }

    private static double ExpansionLimit(int measureCount, bool finalSystem)
    {
        if (finalSystem)
        {
            return measureCount switch
            {
                1 => MaximumExpansionFinalSingle,
                2 => MaximumExpansionFinalPair,
                3 => MaximumExpansionFinalPartial,
                _ => MaximumExpansionFinalFull
            };
        }

        return measureCount switch
        {
            1 => MaximumExpansionSingleMeasure,
            2 => MaximumExpansionTwoMeasures,
            3 => MaximumExpansionThreeMeasures,
            _ => MaximumExpansionFullSystem
        };
    }
}
