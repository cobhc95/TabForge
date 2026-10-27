namespace TabForge.Platform.Windows;

// Owns: the Windows Program Files lookups behind IProgramFolders.
/// <summary>The Windows adapter of <see cref="IProgramFolders"/>.</summary>
public sealed class WindowsProgramFolders : IProgramFolders
{
    public string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public IReadOnlyList<string> ProtectedInstallFolders { get; } = new[]
        {
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
        }
        .Select(Environment.GetFolderPath).ToArray();
}
