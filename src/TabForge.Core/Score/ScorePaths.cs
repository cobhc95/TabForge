using TabForge.Platform;

namespace TabForge.Views;

// Owns: the note flag path.
// Does not own: the geometry (StaffNotationGeometry, the renderers).
// Tests: TestDrawContextPort.
internal static class ScorePaths
{
    internal static IUiPath FlagGeometry(IPathFactory paths, double x, double y, bool up)
    {
        var direction = up ? 1.0 : -1.0;
        return paths.BuildPath(context =>
        {
            // A full flag: it leaves the stem tip with some body (2.6 px thick, under the 3.4 px gap between stacked flags), sweeps right
            // and curls down to a point 15 px below, like the reference's eighth, sixteenth and thirty-second flags.
            context.BeginFigure(new UiPoint(x, y), true, true);
            context.BezierTo(new UiPoint(x + 2, y + 3.8 * direction), new UiPoint(x + 6, y + 6 * direction),
                new UiPoint(x + 3.2, y + 13 * direction));
            context.BezierTo(new UiPoint(x + 3.5, y + 8.5 * direction), new UiPoint(x + 2.2, y + 6.5 * direction),
                new UiPoint(x, y + 2.8 * direction));
        });
    }
}
