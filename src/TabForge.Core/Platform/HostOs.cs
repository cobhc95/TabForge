namespace TabForge.Platform;

// Owns: the operating-system family the portable rules switch on (folders, shell commands, path comparison), and the one place this process asks which it is.
// Does not own: what each rule does per family (AppFolderLayout, ShellCommands, PathComparer).
// Tests: AppFoldersTests, ShellCommandsTests, PathComparerTests.
/// <summary>The operating-system families TabForge distinguishes.</summary>
public enum HostOs
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>The family of the system this process runs on.</summary>
public static class HostSystem
{
    public static HostOs Current { get; } = OperatingSystem.IsWindows() ? HostOs.Windows : OperatingSystem.IsMacOS() ? HostOs.MacOS : HostOs.Linux;
}
