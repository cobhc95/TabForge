namespace TabForge.Services;

// Owns: the extension list, the ProgID names and the descriptions of TabForge's file associations (the values the registry writes).
// Does not own: the registry writes (adapter: Platform/Windows/WindowsFileAssociations.cs, behind IFileAssociations) or the installer's associations.
// Tests: TestInstallerAssociationParity.
/// <summary>
/// Names of TabForge's Windows file associations for Guitar Pro and TabForge files. Only TabForge's own ProgIDs (TabForge.gp5 and so on) are named here.
/// </summary>
public static class FileAssociations
{
    public static readonly string[] Extensions = FileTypes.AllOpenable;

    private const string ProgIdPrefix = "TabForge";

    internal static string ProgIdFor(string extension) => $"{ProgIdPrefix}{extension}"; // e.g. TabForge.gp5

    // installer/TabForge.iss [Registry] repeats these keys; the self-test compares the two.
    internal static string Description(string extension) => extension == ".tforge"
        ? "TabForge project"
        : $"Score file ({extension})";
}
