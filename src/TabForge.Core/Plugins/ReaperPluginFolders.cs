using System.IO;
using TabForge.Services;

namespace TabForge.Plugins;

// Owns: the plug-in folders Reaper itself is set up with (reaper.ini, key vstpath64, else vstpath), as extra folders to look in when a chain from Reaper names a plug-in.
// Does not own: scanning them (VstScannerService) or the user's own folder list (PluginSettings.Folders).
// Tests: TabForge.Avalonia.Tests.RfxImportMatchTests.
public static class ReaperPluginFolders
{
    private const long MaxIniBytes = 4L * 1024 * 1024;

    /// <summary>The existing local folders of <c>vstpath64</c> (else <c>vstpath</c>) in <paramref name="iniPath"/> (default: %APPDATA%\REAPER\reaper.ini); none when Reaper is not set up here or the file is unreadable.</summary>
    public static List<string> Roots(string? iniPath = null)
    {
        iniPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "REAPER", "reaper.ini");
        try
        {
            if (!File.Exists(iniPath)) return new();
            var text = System.Text.Encoding.UTF8.GetString(InputLimits.ReadBoundedBytes(iniPath, MaxIniBytes, "reaper.ini"));
            var lines = text.Split('\n').Select(l => l.Trim()).ToList();
            // vstpath64 is the 64-bit list; older files only have vstpath.
            var line = lines.FirstOrDefault(l => l.StartsWith("vstpath64=", StringComparison.OrdinalIgnoreCase))
                ?? lines.FirstOrDefault(l => l.StartsWith("vstpath=", StringComparison.OrdinalIgnoreCase));
            if (line is null) return new();
            return line[(line.IndexOf('=') + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(f => Environment.ExpandEnvironmentVariables(f.Trim('"', ' ')))
                .Where(f => VstScannerService.IsLocalScanFolder(f) && Directory.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Trace.Error(Trace.Import, "reaper.ini: " + ex.Message);
            return new();
        }
    }
}
