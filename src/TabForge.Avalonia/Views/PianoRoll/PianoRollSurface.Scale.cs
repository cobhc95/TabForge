using System.Collections.Generic;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll surface's scale lock display: while the lock is on, the rows outside the locked scale dim so the in-key rows stand out.
// Does not own: the rows (PianoRollSurface), the scale table (PianoRollScale) or the snap (PianoRollScaleLock).
// Tests: TabForge.Avalonia.Tests.PianoRollScaleLockTests (the snap), PianoRollToolbarLayoutTests (the alignment).
public sealed partial class PianoRollSurface
{
    /// <summary>How far an off-scale row fades towards the grid line while the scale lock is on.</summary>
    private const double OffScaleDim = 0.35;

    /// <summary>The locked scale's pitch classes (the scale highlight while the Lock toggle is on); null paints every row as usual.</summary>
    internal IReadOnlySet<int>? LockScale { get; set; }

    /// <summary>The scale step input filters by (the scale the roll follows, whether or not Lock is on) and the filter: Off, Snap or Only scale notes.</summary>
    internal IReadOnlySet<int>? StepScale { get; set; }

    internal string StepFilter { get; set; } = PianoRollScaleLock.StepSnap;

    /// <summary>True when <paramref name="pitch"/> paints dimmed: the lock is on and its class is outside the scale.</summary>
    internal bool DimsRow(int pitch) => LockScale is { } scale && !scale.Contains(((pitch % 12) + 12) % 12);

    /// <summary>A row's fill, dimmed towards the grid line when the lock leaves it outside the scale (theme tokens only).</summary>
    internal UiColor ScaleRowFill(UiColor row, UiColor line, int pitch) =>
        DimsRow(pitch) ? UiTokens.Blend(row, line, OffScaleDim) : row;
}
