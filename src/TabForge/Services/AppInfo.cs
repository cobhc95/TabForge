using System.Reflection;

namespace TabForge.Services;

/// <summary>Product identity shown in About, Settings and the window; the version comes from the csproj.</summary>
public static class AppInfo
{
    /// <summary>e.g. "0.1.0-beta.2" (the csproj Version, without the build hash).</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    public static bool IsPreRelease => Version.Contains('-');

    /// <summary>"0.1.0 beta.1 (pre-release)".</summary>
    public static string DisplayVersion => IsPreRelease
        ? $"{Version.Split('-')[0]} {Version[(Version.IndexOf('-') + 1)..]} (pre-release)"
        : Version;
}
