using System;

namespace TabForge.Avalonia.Platform;

// Owns: the static system-window maths of a Band card (which rows of the staff a window of systems takes and the unused room above it), split from BandTrackPanel.cs.
// Does not own: the card itself or where the window is applied (BandTrackPanel).
public sealed partial class BandTrackPanel
{
    /// <summary>
    /// The rows a window of <paramref name="count"/> systems takes in <paramref name="staff"/> (scaled by its zoom): the window starts at the system holding
    /// <paramref name="measure"/>, moved up so it never runs past the last system. For a view without its header block (a Band card), whose first
    /// system starts at the top. Null for an audio track or a song without bars.
    /// </summary>
    internal static (double Top, double Height)? SystemWindow(ScorePreviewView staff, int measure, int count) =>
        PagedLayout(staff) is { } layout ? SystemWindowFromFirst(staff, FirstSystem(layout, staff.Probe.Track!.Measures.Count, measure, count), count) : null;

    // The staff's page layout, or null for an audio track, a song without bars or a layout without systems.
    private static TabForge.Views.ScorePageLayout? PagedLayout(ScorePreviewView staff)
    {
        var probe = staff.Probe;
        if (probe.Track is not { IsAudio: false } track || track.Measures.Count == 0) return null;
        var layout = probe.Layout.GetLayout(track);
        return layout.SystemCount == 0 ? null : layout;
    }

    // The system a window of count systems starts at: the one holding the measure, moved up so the window never runs past the last system.
    private static int FirstSystem(TabForge.Views.ScorePageLayout layout, int measures, int measure, int count) =>
        Math.Clamp(layout.SystemForMeasure(Math.Clamp(measure, 0, measures - 1)), 0, Math.Max(0, layout.SystemCount - count));

    /// <summary>The unused room above the tab of the first system of a window of <paramref name="count"/> systems at <paramref name="measure"/>, before the zoom; 0 for an audio track.</summary>
    internal static double TopCrop(ScorePreviewView staff, int measure, int count) =>
        PagedLayout(staff) is { } layout ? TopCropFromFirst(staff, FirstSystem(layout, staff.Probe.Track!.Measures.Count, measure, count), count) : 0;

    /// <summary>The rows a window of <paramref name="count"/> systems takes when it starts at system <paramref name="first"/> (the page the card
    /// keeps while the playing system is inside it), clamped at the song's end; null for an audio track or a song without bars.</summary>
    internal static (double Top, double Height)? SystemWindowFromFirst(ScorePreviewView staff, int first, int count)
    {
        if (PagedLayout(staff) is not { } layout) return null;
        var probe = staff.Probe;
        var at = Math.Clamp(first, 0, Math.Max(0, layout.SystemCount - count));
        var shown = Math.Min(count, layout.SystemCount);
        var systemHeight = probe.StaffTop(1) - probe.StaffTop(0);
        var crop = probe.UnusedTopRoom(at);
        var top = probe.StaffTop(at) - probe.StaffTop(0) + crop;
        return (top * staff.Zoom, (shown * systemHeight - crop) * staff.Zoom);
    }

    /// <summary>The unused room above the tab of the window starting at system <paramref name="first"/>, before the zoom; 0 for an audio track.</summary>
    internal static double TopCropFromFirst(ScorePreviewView staff, int first, int count) =>
        PagedLayout(staff) is { } layout ? staff.Probe.UnusedTopRoom(Math.Clamp(first, 0, Math.Max(0, layout.SystemCount - count))) : 0;
}
