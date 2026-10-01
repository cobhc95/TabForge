namespace TabForge.Plugins;

/// <summary>
/// The list of plug-ins switched off after they crashed (<see cref="TabForge.Services.PluginSettings.Quarantined"/>).
/// Paths are added by the engine client on a crash; this is the way back: Allow again takes a path off the list.
/// It never touches trust or approval records, so an untrusted file stays blocked by <see cref="PluginTrust"/>.
/// </summary>
internal static class PluginQuarantine
{
    public static bool Contains(ICollection<string>? list, string? path) =>
        list is { Count: > 0 } && !string.IsNullOrEmpty(path) && list.Contains(path, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The quarantine list is engine-owned state: a settings snapshot from Preferences must never replace it (a plug-in that
    /// crashed while Preferences was open would be silently allowed again). Call before the snapshot becomes the live settings.
    /// </summary>
    public static void KeepLive(TabForge.Services.PluginSettings incoming, TabForge.Services.PluginSettings live) =>
        incoming.Quarantined = live.Quarantined;

    /// <summary>Removes every entry equal to <paramref name="path"/> (any case). Returns how many were removed.</summary>
    public static int AllowAgain(List<string>? list, string? path)
    {
        if (list is null || string.IsNullOrEmpty(path)) return 0;
        return list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
    }
}
