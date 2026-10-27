namespace TabForge.Platform;

// Owns: how this system compares file paths: ignoring case on Windows and macOS (case-insensitive volumes by default), exactly on Linux.
// Does not own: normalising a path (Path.GetFullPath, MediaPathPolicy) or comparing text that is not a path.
// Tests: PathComparerTests.
/// <summary>Path equality for dictionaries, sets and checks. Windows behaviour is OrdinalIgnoreCase, exactly as before.</summary>
public static class PathComparer
{
    /// <summary>The comparer of the running system.</summary>
    public static StringComparer Default { get; } = For(HostSystem.Current);

    /// <summary>The comparison of the running system, for the string methods (StartsWith, Equals).</summary>
    public static StringComparison Comparison { get; } = ComparisonFor(HostSystem.Current);

    public static StringComparer For(HostOs os) => os == HostOs.Linux ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static StringComparison ComparisonFor(HostOs os) => os == HostOs.Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
