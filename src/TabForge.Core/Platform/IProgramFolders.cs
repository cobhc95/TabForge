namespace TabForge.Platform;

// Owns: the machine-wide install folders that plug-in scanning and plug-in trust ask about (Program Files on Windows).
// Does not own: the per-user folders (UserPaths) or the plug-in scan policy (VstScannerService, PluginTrust).
// Tests: PortabilityGuardTests (no Windows folder lookup outside the platform files).
/// <summary>The install folders of the machine: where programs (and so VST plug-ins) are installed by an administrator.</summary>
public interface IProgramFolders
{
    /// <summary>The 64-bit program folder ("" when the system has no such concept).</summary>
    string ProgramFiles { get; }

    /// <summary>Folders only an administrator can write (Program Files, its 32-bit twin, the common folders); plug-ins inside them are trusted by location.</summary>
    IReadOnlyList<string> ProtectedInstallFolders { get; }
}

/// <summary>The non-Windows stub: no Program Files, nothing trusted by location (a plug-in there needs approval, the safe answer).</summary>
public sealed class NullProgramFolders : IProgramFolders
{
    public string ProgramFiles => "";
    public IReadOnlyList<string> ProtectedInstallFolders => Array.Empty<string>();
}

/// <summary>The program folders of this system, chosen once at start-up.</summary>
public static class ProgramFolders
{
    public static IProgramFolders Current { get; } =
#if WINDOWS
        new Windows.WindowsProgramFolders();
#else
        new NullProgramFolders();
#endif
}
