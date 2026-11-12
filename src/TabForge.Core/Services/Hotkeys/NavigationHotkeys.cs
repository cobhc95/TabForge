using TabForge.Commands;
namespace TabForge.Services;

// Owns: which commands count as navigation and transport, the ones that act on the song while another TabForge window has the focus.
// Does not own: the bindings (HotkeySettings), the catalogue (HotkeyCatalog), the two routes (ToolWindowKeyForwarding, PluginEditorHotkeys).
// Tests: TabForge.Avalonia.Tests.NavigationHotkeyForwardingTests.
/// <summary>The command ids forwarded to the main window from tool windows and plug-in editors. Fixed, read-only, any thread.</summary>
public static class NavigationHotkeys
{
    private static readonly HashSet<string> Ids = new(StringComparer.OrdinalIgnoreCase)
    {
        CommandIds.Transport.PlayPause, "Transport.PlayFromStart", CommandIds.Transport.Stop, CommandIds.Transport.Record, CommandIds.Transport.Loop,
        "Transport.Metronome", "Transport.CountIn", "Transport.Panic", "Transport.FocusTrack",
        "Playback.SpeedUp", "Playback.SpeedDown", "Playback.SpeedReset",
        CommandIds.Bar.First, CommandIds.Bar.Last, CommandIds.Section.Previous, CommandIds.Section.Next, "Track.Previous", "Track.Next",
    };

    /// <summary>Every forwarded command id.</summary>
    public static IReadOnlyCollection<string> All => Ids;

    public static bool Contains(string id) => Ids.Contains(id);
}
