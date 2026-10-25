// Owns: the middle-click rule of the title bar: a click is a press and a release within a few pixels, and one click fires once.
// Does not own: reading the pointer (the Avalonia handler and the Windows hook), the new-tab action.
// Tests: MiddleClickTrackerTests (TabForge.Avalonia.Tests.MiddleClickTrackerTests).
using TabForge.Platform;

namespace TabForge.Shell;

/// <summary>Tracks one middle press and decides on release whether it was a click; UI thread only, one instance per input path per window (native hook and Avalonia title row each own one).</summary>
public sealed class MiddleClickTracker
{
    /// <summary>The farthest the pointer may travel between press and release, in pixels, for the release to count as a click.</summary>
    public const double SlopDips = 8;

    private UiPoint? _pressedAt;

    public void Press(UiPoint at) => _pressedAt = at;

    public void Cancel() => _pressedAt = null;

    /// <summary>True when this release completes a press that stayed within <see cref="SlopDips"/>; the press is consumed either way.</summary>
    public bool Release(UiPoint at)
    {
        if (_pressedAt is not { } start) return false;
        _pressedAt = null;
        return Math.Abs(at.X - start.X) <= SlopDips && Math.Abs(at.Y - start.Y) <= SlopDips;
    }
}
