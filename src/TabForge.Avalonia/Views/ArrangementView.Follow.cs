using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The arrangement's playhead follow switch and hand-scroll pause: the Follow settings the owner keeps current, and the pause a sideways hand scroll starts.
/// Owns: the follow state of <see cref="ArrangementView"/>. Does not own: the scrolling itself (TimelineFollowScroll) or the settings (FollowSettings, FollowHold).
/// Tests: TabForge.Avalonia.Tests.FollowPlaybackTests.
/// </summary>
public sealed partial class ArrangementView
{
    /// <summary>The Follow settings; the owner (TimelineSettingsBinding) keeps them current.</summary>
    public FollowSettings Follow { get; set; } = new();

    /// <summary>The hand-scroll pause of the timeline's follow.</summary>
    internal FollowHold FollowHold { get; } = new();

    /// <summary>A sideways hand scroll (Shift + wheel, a touchpad's sideways swipe, the scroll bar): pauses the follow when the settings say so.</summary>
    private void NoteHandScroll()
    {
        if (Follow.StopOnManualScroll) FollowHold.Pause();
    }

    private void FollowPlayheadIfAllowed()
    {
        if (FollowHold.Allows(Follow)) TimelineFollowScroll.FollowPlayhead(_sideways, Timeline);
    }
}
