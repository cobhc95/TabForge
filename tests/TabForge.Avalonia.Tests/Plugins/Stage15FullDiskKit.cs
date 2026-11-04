using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Stage 15 full-disk guard and filler used by the fault tests. The owner's disk is never a target: the
// attribute skips unless the caller points at a small CI volume (temporary tmpfs on Linux, small VHD on Windows).
internal static class Stage15FullDiskKit
{
    public const string Variable = "TABFORGE_TEST_FULL_DISK";
    private const long MaxVolumeBytes = 64L * 1024 * 1024;

    public static string? Folder
    {
        get
        {
            var folder = Environment.GetEnvironmentVariable(Variable);
            return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) && IsSmallVolume(folder) ? folder : null;
        }
    }

    public static bool IsSmallVolume(string folder)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var root = Path.GetPathRoot(Path.GetFullPath(folder));
                if (string.IsNullOrEmpty(root)) return false;
                var drive = new DriveInfo(root);
                return drive.IsReady && drive.TotalSize <= MaxVolumeBytes;
            }
            var start = new ProcessStartInfo("/bin/df")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in new[] { "-B1", "--output=size", folder }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null || !process.WaitForExit(5000)) return false;
            var text = process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return process.ExitCode == 0 && text.Length >= 2 &&
                   long.TryParse(text[^1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) &&
                   size > 0 && size <= MaxVolumeBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Makes the shared-memory folder private on Unix (PortableMemory refuses a group/world-accessible folder).</summary>
    public static void MakeOwnerOnly(string folder)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// Fills the small volume with one file until the filesystem refuses more. Returns the filler path; the caller deletes
    /// it in a finally block so the CI runner's temporary volume is empty again. Never call this on a normal volume.
    /// </summary>
    public static string Fill(string folder)
    {
        if (!IsSmallVolume(folder)) throw new InvalidOperationException($"Refusing to fill {folder}: not a small test volume.");
        var filler = Path.Combine(folder, "stage15-filler-" + Guid.NewGuid().ToString("N") + ".bin");
        var chunk = new byte[64 * 1024];
        try
        {
            // Unbuffered writes of falling sizes: a failed 64 KiB write can leave a file-system reserve, and the block
            // file is only ~12-37 KiB, so a coarse fill would still let the block be created. 512-byte steps leave less
            // than half a KiB, which no block size can use.
            using var stream = new FileStream(filler, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1);
            var size = chunk.Length;
            while (size >= 512)
            {
                try { stream.Write(chunk, 0, size); }
                catch (IOException) { size /= 2; }
            }
        }
        catch (IOException) { /* the volume is full: this is the injected condition */ }
        return filler;
    }

    public static void Delete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void DeleteFolder(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>A test that only runs when <see cref="Stage15FullDiskKit.Variable"/> points at a small, disposable volume.</summary>
internal sealed class Stage15FullDiskFactAttribute : FactAttribute
{
    public Stage15FullDiskFactAttribute()
    {
        if (Stage15FullDiskKit.Folder is null)
            Skip = $"{Stage15FullDiskKit.Variable} must name a writable volume of at most 64 MiB (never the owner's disk)";
    }
}
