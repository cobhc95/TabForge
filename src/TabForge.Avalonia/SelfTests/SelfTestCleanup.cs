using System;
using System.IO;

namespace TabForge;

// Owns: the one best-effort delete the self-tests use to clean up their temporary files and folders.

public static partial class SelfTest
{
    /// <summary>Deletes a temporary file or folder (recursively); a locked or already-gone path is left behind, since this is only test cleanup.</summary>
    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }
        catch (IOException) { }   // swallow: test cleanup of a temporary file or folder.
        catch (UnauthorizedAccessException) { }   // swallow: test cleanup of a temporary file or folder.
    }
}
