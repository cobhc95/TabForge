// Owns: the snap / grid settings dialog's logic without UI types: its texts and tips, the switches (label, tip, indent, read and write
//       of the setting) and committing a typed snap distance (whole pixels, clamped to 1-40; anything else restores the value).
// Does not own: the window (Avalonia SnapSettingsPopup), the hotkey text of the first switch (the caller
//       passes the label) or saving the settings (the caller's change callback).
// Tests: TabForge.Avalonia.Tests.ClipSnapPluginDialogTests.
using TabForge.Services;

namespace TabForge.Presenters;

public static class SnapSettingsPresenter
{
    public const string Title = "Snap / grid settings";
    public const string EnableLabel = "Enable snapping";
    public const string EnableTip = "Clips snap while you move or trim them. Hold Alt while dragging to bypass.";
    public const string GridSizeLabel = "Grid size";
    public const string GridSizeTip = "Grid line spacing: a whole bar, or a note value (1/4 is a beat in 4/4)";
    public const string SnapToHeading = "Snap to";
    public const string DistanceLabel = "Snap distance";
    public const string DistanceUnit = "pixels";
    public const string DistanceTip = "How near (in pixels) an edge must be to snap to a clip or the playhead";
    public const string CloseButton = "Close";

    /// <summary>One switch: its label, tip, left indent and the setting it reads and writes.</summary>
    public sealed record Switch(string Label, string Tip, double Indent, Func<SnapSettings, bool> Get, Action<SnapSettings, bool> Set);

    public static readonly Switch Enable = new(EnableLabel, EnableTip, 0, s => s.Enabled, (s, v) => s.Enabled = v);

    /// <summary>The switches under "Snap to", in order.</summary>
    public static readonly IReadOnlyList<Switch> SnapTo =
    [
        new("Grid", "The nearest grid line of the song's bars", 12, s => s.ToGrid, (s, v) => s.ToGrid = v),
        new("Grid at any distance", "Snap to the grid however far away it is (off: only within the snap distance)", 30, s => s.GridAtAnyDistance, (s, v) => s.GridAtAnyDistance = v),
        new("Media items (the edges of other clips)", "Clip start and end edges, on every track", 12, s => s.ToItems, (s, v) => s.ToItems = v),
        new("Playhead", "The playback cursor", 12, s => s.ToPlayhead, (s, v) => s.ToPlayhead = v),
    ];

    /// <summary>Commits a typed distance: true (and the clamped value set) for a whole number, false for anything else.</summary>
    public static bool CommitDistance(SnapSettings snap, string text)
    {
        if (!int.TryParse(text.Trim(), out var px)) return false;
        snap.DistancePx = Math.Clamp(px, 1, 40);
        return true;
    }
}
