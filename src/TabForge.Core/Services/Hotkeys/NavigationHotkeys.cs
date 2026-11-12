namespace TabForge.Services;

// Owns: which commands count as navigation and transport, the ones that act on the song while another TabForge window has the focus.
// Does not own: the bindings (HotkeySettings), the catalogue (HotkeyCatalog), the two routes (ToolWindowKeyForwarding, PluginEditorHotkeys).
// Tests: TabForge.Avalonia.Tests.NavigationHotkeyForwardingTests.
/// <summary>The command ids forwarded to the main window from tool windows and plug-in editors. Fixed, read-only, any thread.</summary>
public static class NavigationHotkeys
{
    private static readonly HashSet<string> Ids = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transport.PlayPause", "Transport.PlayFromStart", "Transport.Stop", "Transport.Record", "Transport.Loop",
        "Transport.Metronome", "Transport.CountIn", "Transport.Panic", "Transport.FocusTrack",
        "Playback.SpeedUp", "Playback.SpeedDown", "Playback.SpeedReset",
        "Bar.First", "Bar.Last", "Section.Previous", "Section.Next", "Track.Previous", "Track.Next",
    };

    /// <summary>Every forwarded command id.</summary>
    public static IReadOnlyCollection<string> All => Ids;

    public static bool Contains(string id) => Ids.Contains(id);
}
