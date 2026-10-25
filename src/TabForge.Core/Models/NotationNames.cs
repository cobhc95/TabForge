namespace TabForge.Models;

// Owns: the person-facing name of each NotationMode and the one way a stored or typed notation text becomes a NotationMode, so the View menu,
//   the hotkey list, Preferences and new songs cannot drift apart.
// Does not own: the enum itself (NotationEnums) or where the choice is stored (AppSettings.Notation).
// Tests: MainWindowNewTabTests.
public static class NotationNames
{
    /// <summary>The names in menu order: both, tablature only, standard only.</summary>
    public static IReadOnlyList<string> All { get; } = Enum.GetValues<NotationMode>().Select(Display).ToArray();

    /// <summary>The name shown for <paramref name="mode"/>.</summary>
    public static string Display(NotationMode mode) => mode switch
    {
        NotationMode.TabOnly => "Tablature only",
        NotationMode.StaffOnly => "Standard notation only",
        _ => "Tablature + standard",
    };

    /// <summary>The mode for a stored name ("TabOnly") or a shown name, ignoring case; tablature + standard when the text names none.</summary>
    public static NotationMode Normalize(string? text)
    {
        if (Enum.TryParse<NotationMode>(text, true, out var mode) && Enum.IsDefined(mode)) return mode;
        foreach (var candidate in Enum.GetValues<NotationMode>())
            if (string.Equals(Display(candidate), text, StringComparison.OrdinalIgnoreCase)) return candidate;
        return NotationMode.TabAndStaff;
    }
}
