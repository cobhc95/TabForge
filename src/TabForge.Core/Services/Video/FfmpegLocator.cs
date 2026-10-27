using System.IO;

namespace TabForge.Services.Video;

// Owns: finding an external ffmpeg executable (TABFORGE_FFMPEG, next to the app, then PATH). Nothing is bundled; a missing ffmpeg only switches the FFmpeg encoder off.
// Does not own: running it (FfmpegVideoEncoder) or the command lines (FfmpegArguments).
// Tests: VideoEncoderPortTests.
/// <summary>Where ffmpeg lives on this machine, or null when it is not installed.</summary>
public static class FfmpegLocator
{
    public const string EnvironmentVariable = "TABFORGE_FFMPEG";

    private static readonly Lazy<string?> Found = new(() => FindIn(Environment.GetEnvironmentVariable(EnvironmentVariable), AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"), OperatingSystem.IsWindows(), File.Exists));

    /// <summary>The ffmpeg executable of this machine (looked up once per process), or null.</summary>
    public static string? Find() => Found.Value;

    /// <summary>The lookup with its inputs given, so it can be tested: an explicit file, then the app folder (and its ffmpeg subfolder), then each PATH folder.</summary>
    internal static string? FindIn(string? explicitPath, string appFolder, string? pathVariable, bool windows, Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && exists(explicitPath.Trim())) return explicitPath.Trim();
        var name = windows ? "ffmpeg.exe" : "ffmpeg";
        foreach (var folder in new[] { appFolder, Path.Combine(appFolder, "ffmpeg") })
            if (!string.IsNullOrEmpty(folder) && Path.Combine(folder, name) is var local && exists(local)) return local;
        foreach (var folder in (pathVariable ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = folder.Trim().Trim('"');
            if (dir.Length > 0 && Path.Combine(dir, name) is var candidate && exists(candidate)) return candidate;
        }
        return null;
    }
}
