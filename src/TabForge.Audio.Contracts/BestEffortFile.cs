namespace TabForge;

// Owns: the one best-effort delete of a file or folder (a staging file, a marker, a leftover): IO and access errors are swallowed and reported.
// Does not own: deletes that must succeed (those throw at the caller), path rules (SafeFileNames, ClipPathGuard) or the per-user folders (UserPaths).
// Tests: covered by the callers' tests (recording, reset, video export).
// Need to delete something that may already be gone or locked? Use BestEffortFile here instead of a local TryDelete with its own catch.

/// <summary>Deletes files and folders without throwing when they are locked, read-only or already gone.</summary>
public static class BestEffortFile
{
    /// <summary>Deletes the file; on an IO or access error calls <paramref name="onFailure"/> (when given) and returns false. A missing file counts as deleted.</summary>
    public static bool TryDelete(string path, Action<Exception>? onFailure = null)
    {
        try { File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { onFailure?.Invoke(ex); return false; } // swallow: best effort, reported through onFailure
    }

    /// <summary>Deletes the folder (with everything in it when <paramref name="recursive"/>); on an IO or access error calls <paramref name="onFailure"/> and returns false.</summary>
    public static bool TryDeleteFolder(string path, bool recursive = true, Action<Exception>? onFailure = null)
    {
        try { Directory.Delete(path, recursive); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { onFailure?.Invoke(ex); return false; } // swallow: best effort, reported through onFailure
    }
}
