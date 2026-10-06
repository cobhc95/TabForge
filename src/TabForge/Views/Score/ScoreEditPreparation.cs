using TabForge.Services;

namespace TabForge.Views.Score;

// Owns: edit guard, optional rest-fill wrapping, and failed-mutation range cleanup.
// Does not own: edit transactions, dirty state, undo capture, or cursor invalidation.
// Tests: TestScoreLayoutPartial; existing editor guard and bar-fill command coverage.
internal static class ScoreEditPreparation
{
    internal static bool TryPrepare(IScoreEditContext context, Func<bool> mutate, out Func<bool> prepared)
    {
        prepared = mutate;
        if (context.Project is not { } project || EditorGuard.Blocks(project, context.SelectedTrackIndex)) return false;
        if (!context.FillBarsWithRests) return true;
        var range = context.HasSelection
            ? context.SelectionRange()
            : (context.SelectedMeasure, 0, context.SelectedMeasure, 0);
        prepared = BarFill.Wrap(project, context.SelectedTrackIndex, range, mutate);
        return true;
    }

    internal static bool RunMutation(Func<bool> mutate, ScoreLayoutEngine layout)
    {
        try { return mutate(); }
        catch
        {
            layout.InvalidatePendingMeasures(changed: false);
            throw;
        }
    }
}
