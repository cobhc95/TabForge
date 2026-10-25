using TabForge.Avalonia.Views;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Timeline.ZoomIn and Timeline.ZoomOut commands (the transport bar's zoom buttons, the Ctrl+Alt+Plus and Minus hotkeys) and saves the factor a wheel zoom settled at (the wheel over
//   the timeline previews in ArrangementView): each button moves the arrangement timeline one zoom step and saves the factor; the saved factor is applied to each new arrangement.
// Does not own: the steps (TimelineZoomRules in Core), the bar layout and scroll position (TimelineView, ArrangementView.SetTimelineZoom) or the score zoom (MainWindowScoreRows).
// Tests: TabForge.Avalonia.Tests.TimelineZoomTests.
public static class MainWindowTimelineZoom
{
    public const string ZoomInId = "Timeline.ZoomIn";
    public const string ZoomOutId = "Timeline.ZoomOut";

    /// <summary>The Timeline.Snap command id this class also handles.</summary>
    public const string SnapId = "Timeline.Snap";

    /// <param name="settings">Holds the saved factor; null (no settings file) zooms from the default and saves nothing.</param>
    public static void Register(MainWindowCommands commands, MainWindowDocumentPanes panes, MainWindowSettings? settings)
    {
        var fallback = new TimelineSettings();
        TimelineSettings Current() => settings?.Settings.Timeline ?? fallback;
        // The buttons and hotkeys keep the discrete steps and the middle of the view; the wheel is continuous (ArrangementView.ZoomByWheel).
        void Zoom(int direction)
        {
            Current().ZoomFactor = TimelineZoomRules.Step(Current().ZoomFactor, direction);
            settings?.SaveTimelineZoom(Current().ZoomFactor);   // not the SettingsChanged broadcast: re-applying all settings on the UI thread stalls the playhead; the other windows' panes follow through TimelineZoomSaved
            panes.Arrangement?.SetTimelineZoom(Current().ZoomFactor);
        }
        commands.Add(ZoomInId, () => Zoom(1));
        commands.Add(ZoomOutId, () => Zoom(-1));
        commands.Add(SnapId, () => panes.Arrangement?.Transport.ToggleSnap());
        void Attach(ArrangementView arrangement)
        {
            arrangement.SetTimelineZoom(Current().ZoomFactor);
            // A wheel or pinch zoom previews in the arrangement and reports once when it settles: the factor is saved then, not per tick.
            arrangement.ZoomSettled += factor => { Current().ZoomFactor = factor; settings?.SaveTimelineZoom(factor); };
            arrangement.Input.SectionAnimator.Enabled = Current().SectionDragAnimation && settings?.Settings.Appearance.ReduceAnimations != true;
            arrangement.Timeline.SectionGlowIntensity = settings?.Settings.Follow.SectionGlowIntensity ?? arrangement.Timeline.SectionGlowIntensity;
        }
        panes.ArrangementCreated += Attach;
        // The dock may have made the first arrangement before this registration ran; it gets the same hooks.
        if (panes.Arrangement is { } existing) Attach(existing);
    }
}
