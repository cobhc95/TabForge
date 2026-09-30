using System.IO;

namespace TabForge.Services;

/// <summary>
/// The one place that resolves every per-user folder (settings, plug-in approvals and scan cache, chain states, presets, drum maps,
/// templates, recent files (inside settings), recovery, autosave, crash logs and diagnostics). By default these are
/// %APPDATA%\TabForge and %LOCALAPPDATA%\TabForge; with <c>--profile &lt;folder&gt;</c> everything lives under that folder instead and the
/// real user folders are never read or written (for test runs that must leave the user's own data untouched).
/// </summary>
public static class UserPaths
{
    public const string ProfileSwitch = "--profile";

    /// <summary>The folder given with <c>--profile</c>, or null for the normal per-user locations.</summary>
    public static string? ProfileRoot { get; private set; }

    public static bool IsProfile => ProfileRoot is not null;

    /// <summary>Roaming data: settings.json, plug-in library, chain states, presets, drum maps, templates.</summary>
    public static string Roaming => ProfileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabForge");

    /// <summary>Local data: Recovery (autosave), Diagnostics (crash logs, self-test logs).</summary>
    public static string Local => ProfileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabForge");

    public static string SettingsFile => Path.Combine(Roaming, "settings.json");
    public static string Templates => Path.Combine(Roaming, "Templates");
    public static string Recovery => Path.Combine(Local, "Recovery");
    public static string Diagnostics => Path.Combine(Local, "Diagnostics");

    /// <summary>Test seam: sets or clears the profile folder.</summary>
    internal static void SetProfile(string? folder) =>
        ProfileRoot = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder);

    /// <summary>
    /// Finds <c>--profile &lt;folder&gt;</c> (also <c>--profile=&lt;folder&gt;</c>) in <paramref name="args"/>, activates it and returns the
    /// arguments without it. Safe to call more than once on the same arguments.
    /// </summary>
    public static string[] ApplyProfileArgument(string[] args)
    {
        var rest = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == ProfileSwitch && i + 1 < args.Length) { SetProfile(args[++i]); continue; }
            if (args[i].StartsWith(ProfileSwitch + "=", StringComparison.Ordinal)) { SetProfile(args[i][(ProfileSwitch.Length + 1)..]); continue; }
            rest.Add(args[i]);
        }
        return rest.ToArray();
    }
}
