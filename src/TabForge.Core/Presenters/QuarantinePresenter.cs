// Owns: the "Plug-ins switched off after a crash" window's rules without UI types: its title and texts, the rows for the quarantined
//       plug-in paths (the file name shown and the full path as the tool tip), and the empty-list text.
// Does not own: the window and its buttons (Views/QuarantineWindow.cs, TabForge.Avalonia/Views/QuarantineWindow.cs), the quarantine list
//       and the allow-again rule (PluginQuarantine, with the trust code), and the settings save and engine re-sync that follow an Allow again.
// Tests: QuarantineWindowTests.
namespace TabForge.Presenters;

/// <summary>One quarantined plug-in: its stored path and the file name the window shows for it.</summary>
public sealed record QuarantineEntry(string Path, string FileName);

public sealed class QuarantinePresenter
{
    public const string Title = "Plug-ins switched off after a crash";
    public const string Caption = "These plug-ins crashed and are not loaded. Allow again loads one on the next playback; trust and approval stay as they are.";
    public const string EmptyText = "No plug-ins are switched off.";
    public const string AllowAgainText = "Allow again";
    public const string AllowAgainToolTip = "Load this plug-in again on the next playback";
    public const string CloseText = "Close";

    public QuarantinePresenter(IReadOnlyList<string> quarantinedPaths)
    {
        Entries = quarantinedPaths.Select(path => new QuarantineEntry(path, FileNameOf(path))).ToList();
    }

    /// <summary>The quarantined plug-ins, in the order the settings hold them.</summary>
    public IReadOnlyList<QuarantineEntry> Entries { get; }

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>The name shown for a plug-in path: its file name without trailing separators, or the whole path when it has none.</summary>
    public static string FileNameOf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var separator = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        var name = separator < 0 ? trimmed : trimmed[(separator + 1)..];
        return name.Length > 0 ? name : path;
    }
}
