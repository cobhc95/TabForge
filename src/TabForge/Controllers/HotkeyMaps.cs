using TabForge.Services;

namespace TabForge.Controllers;

// Owns: a window's three gesture-to-command maps (everywhere, a selected clip, the focused track list), rebuilt from the bindings.
// Does not own: running the commands (the window's RunHotkey, ClipEditController, TrackClipboardFlow), key routing (WindowKeyRouter).
// Tests: TestMenuGestureTextFollowsBindings, TestKeyRoutingOrder.
/// <summary>The gesture maps a window routes its keys through.</summary>
internal sealed class HotkeyMaps
{
    /// <summary>Gesture to command, everywhere in the window.</summary>
    public Dictionary<string, string> Global { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gesture to command while a clip is selected on the timeline.</summary>
    public Dictionary<string, string> Clip { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gesture to command while the track list has the focus.</summary>
    public Dictionary<string, string> TrackRow { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gesture to command while bars are selected and the timeline has the focus.</summary>
    public Dictionary<string, string> Range { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Rebuild(HotkeySettings settings)
    {
        Range = HotkeyCatalog.BuildMap(settings, rangeContext: true);
        Global = HotkeyCatalog.BuildMap(settings);
        Clip = HotkeyCatalog.BuildMap(settings, clipContext: true);
        TrackRow = HotkeyCatalog.BuildMap(settings, trackRowContext: true);
    }
}
