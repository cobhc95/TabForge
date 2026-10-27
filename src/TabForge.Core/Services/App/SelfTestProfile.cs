using System.IO;

namespace TabForge.Services;

// Owns: the temp profile folder a self-test run created (if any) and its deletion.
// Does not own: choosing the profile (UserPaths).
// Tests: TabForge.Avalonia.Tests.SelfTestProfileOwnerIdTests.
/// <summary>The result of <see cref="UserPaths.ApplySelfTestProfile"/>: the remaining arguments, and the temp profile folder to delete on dispose (null for an explicit <c>--profile</c>, which is never deleted).</summary>
public sealed class SelfTestProfile : IDisposable
{
    internal const string FolderPrefix = "TabForge-selftest-";
    private const int MaxAgeDays = 1;

    private string? _autoFolder;
    private bool _cleanupLogged;

    internal SelfTestProfile(string[] args, string? autoFolder)
    {
        Args = args;
        _autoFolder = autoFolder;
    }

    /// <summary>The arguments without <c>--profile</c>.</summary>
    public string[] Args { get; }

    /// <summary>The temp folder created for this run; null when the run used an explicit <c>--profile</c>.</summary>
    public string? AutoFolder => _autoFolder;

    /// <summary>Deletes the temp folder, if any. Best-effort; logs the first failure only.</summary>
    public void Dispose()
    {
        var folder = _autoFolder;
        _autoFolder = null;
        if (folder is null) return;
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_cleanupLogged) return;
            _cleanupLogged = true;
            Trace.Error(Trace.Ui, "selftest: could not delete temp profile " + folder + ": " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Best-effort: deletes leftover <c>TabForge-selftest-*</c> temp folders not written to for over a day. Never throws.</summary>
    internal void SweepStale()
    {
        try
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(MaxAgeDays);
            foreach (var dir in Directory.EnumerateDirectories(Path.GetTempPath(), FolderPrefix + "*"))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(dir) < cutoff) Directory.Delete(dir, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // Not logged: a folder in use is simply kept
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // Not logged: the sweep is optional
    }
}
