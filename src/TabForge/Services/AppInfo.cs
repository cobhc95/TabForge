using System.Reflection;

namespace TabForge.Services;

// Owns: the application's version and pre-release facts for display.
// Does not own: update checking.
// Tests: TestUpdateCheck.
/// <summary>Product identity shown in About, Settings and the window; the version comes from the csproj.</summary>
public static class AppInfo
{
    /// <summary>e.g. "0.1.0-beta.2" (the csproj Version, without the build hash).</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    /// <summary>Set by the PreRelease build property (Directory.Build.props): a plain version such as 0.5.0 can still be a pre-release.</summary>
    private static bool PreReleaseFlag { get; } =
        typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => a.Key == "PreRelease" && string.Equals(a.Value, "true", StringComparison.OrdinalIgnoreCase));

    public static bool IsPreRelease => Version.Contains('-') || PreReleaseFlag;

    /// <summary>"0.5" for a final release with patch 0 (0.5.0), "0.5.1" for other final releases,
    /// "0.6.0 beta.1 (pre-release)" for a suffixed version, "0.5.0 (pre-release)" for a plain version marked pre-release.</summary>
    public static string DisplayVersion => FormatDisplay(Version, PreReleaseFlag);
    /// <summary>The version line of the About box and the Settings bottom bar.</summary>
    public static string VersionLine => $"TabForge {DisplayVersion}";

    /// <summary>The display rule on its own, so it can be tested with any version.</summary>
    public static string FormatDisplay(string version, bool preReleaseFlag)
    {
        var dash = version.IndexOf('-');
        if (dash >= 0) return $"{version[..dash]} {version[(dash + 1)..]} (pre-release)";
        if (preReleaseFlag) return $"{version} (pre-release)";
        var parts = version.Split('.');
        return parts.Length == 3 && parts[2] == "0" ? $"{parts[0]}.{parts[1]}" : version;
    }
}
