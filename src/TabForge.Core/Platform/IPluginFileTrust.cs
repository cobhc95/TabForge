namespace TabForge.Platform;

// Owns: the two operating-system questions plug-in trust asks about a binary (its final path, its embedded signature).
// Does not own: any trust decision (PluginTrust keeps all of it) or the Windows calls (adapter: Platform/Windows/WindowsPluginFileTrust.cs).
// Tests: no dedicated test class.
/// <summary>
/// File facts for <c>PluginTrust</c>. Callers treat a throw, a null final path or a false signature answer as "not trusted".
/// </summary>
public interface IPluginFileTrust
{
    /// <summary>The raw final path once every link and junction is followed (the <c>\\?\</c> form on Windows), or null when unknown.</summary>
    string? FinalPath(string fullPath);

    /// <summary>True only when the file's embedded signature verifies (no UI, no revocation lookup).</summary>
    bool HasValidEmbeddedSignature(string fullPath);
}
