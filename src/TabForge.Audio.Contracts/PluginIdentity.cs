using System.Security.Cryptography;

namespace TabForge.Audio.Contracts;

// Owns: the content identity of the exact plug-in binary about to be loaded (SHA-256 hold, no replace-after-check).

/// <summary>
/// Content identity of the exact binary about to be loaded. The UI process records a SHA-256 when the user scans or approves a
/// plug-in (see PluginTrust) and sends it as <see cref="PluginSpec.ExpectedSha256"/>; the engine calls <see cref="Hold"/> right
/// before it loads the file. <see cref="Hold"/> hashes the file through a handle that denies other writers and keeps that handle
/// open until the load has finished, so the bytes that were hashed are the bytes that get mapped (no replace-after-check window
/// through ordinary file writes). Never call this from an audio callback: it reads the whole file.
/// </summary>
public static class PluginIdentity
{
    /// <summary>Expected hash meaning "the file did not exist when it was approved": any file found there now counts as changed.</summary>
    public const string ExpectMissing = "-";

    /// <summary>The file the host loads: the DLL itself, or a VST3 bundle's Contents\x86_64-win\&lt;bundle name&gt;; null when missing.</summary>
    public static string? BinaryOf(string path)
    {
        try
        {
            if (File.Exists(path)) return path;
            if (!Directory.Exists(path)) return null;
            return PluginModulePaths.BinaryOf(path, PluginModulePaths.Current, DiskModuleFileView.Instance);   // Windows: Contents/x86_64-win/<bundle name>; macOS / Linux layouts in PluginModulePaths
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>Hex SHA-256 of a whole stream (positioned at its start).</summary>
    public static string Sha256Hex(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));

    /// <summary>Hex SHA-256 of a byte block; the one hex-hash helper the app shares.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// Verifies <paramref name="spec"/>'s binary against <see cref="PluginSpec.ExpectedSha256"/> and returns the handle to keep
    /// open while loading (dispose it afterwards). Returns null for a location-trusted spec. Throws <see cref="PluginChangedException"/>
    /// when the file changed, cannot be read, or the spec has no approved hash; the message says what to do.
    /// </summary>
    public static IDisposable? Hold(PluginSpec spec) => Hold(spec.Path, spec.ExpectedSha256, spec.LocationTrusted);

    /// <summary>
    /// The one owner of the rule "no plug-in code runs before it is trusted", for every process that loads a plug-in (the engine,
    /// the isolated plug-in host, the identify probe). <paramref name="locationTrusted"/> skips the hash (the binary is in a folder only an
    /// administrator can write); otherwise an empty <paramref name="expectedSha256"/> fails closed: nothing is loaded without an approved SHA-256.
    /// </summary>
    public static IDisposable? Hold(string path, string expectedSha256, bool locationTrusted)
    {
        if (locationTrusted) return null;
        var expected = expectedSha256;
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (expected.Length == 0)
            throw new PluginChangedException($"'{name}' has no approved SHA-256 to check, so it was not loaded. Approve it first (Review plug-ins).");
        var bin = BinaryOf(path);
        if (expected == ExpectMissing)
        {
            if (bin is null) return null;   // still missing: the loader reports it
            throw new PluginChangedException($"'{name}' did not exist when you approved it and a file is there now. It was not loaded. Use Review plug-ins to approve it if you trust it.");
        }
        if (bin is null) return null;   // gone since: the loader reports "not found"
        FileStream stream;
        try { stream = new FileStream(bin, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PluginChangedException($"'{name}' could not be opened to check that it is unchanged since you approved it ({ex.Message}). It was not loaded. Close whatever is using the file (an installer or updater), or check the drive or network location, then try again.");
        }
        try
        {
            var actual = Sha256Hex(stream);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) { stream.Position = 0; return stream; }
            throw new PluginChangedException($"'{name}' changed since you approved it (approved SHA-256 {Short(expected)}, now {Short(actual)}). It was not loaded. Use Review plug-ins to approve the new file if you trust it.");
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static string Short(string hash) => hash.Length > 12 ? hash[..12] + "…" : hash;
}

/// <summary>The binary about to be loaded is not the one that was approved (or could not be read to check it).</summary>
public sealed class PluginChangedException : Exception
{
    public PluginChangedException(string message) : base(message) { }
}
