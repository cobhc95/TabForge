namespace TabForge.Platform;

// Owns: reading this system's special folders once and choosing the folder layout (IAppFolders) from them; the one place that asks the OS for them.
// Does not own: the layout rules (AppFolderLayout) or the --profile redirect (UserPaths).
// Tests: AppFoldersTests (the layout of every family); PortabilityGuardTests (no other file asks for these folders).
/// <summary>The app folders of this system, chosen once at start-up.</summary>
public static class AppFolders
{
    public static IAppFolders Current { get; } = new AppFolderLayout(HostSystem.Current,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Path.GetTempPath(), Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
}
