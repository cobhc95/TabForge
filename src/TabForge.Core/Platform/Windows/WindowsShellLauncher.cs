using System.Diagnostics;

namespace TabForge.Platform.Windows;

// Owns: the Windows shell calls behind IShellLauncher: the shell's own "open" for folders, files and web addresses, and Explorer's select-this-file.
/// <summary>The Windows adapter of <see cref="IShellLauncher"/>.</summary>
public sealed class WindowsShellLauncher : IShellLauncher
{
    public bool OpenFolder(string folder) => Directory.Exists(folder) && Shell(folder);
    public bool OpenFile(string file) => File.Exists(file) && Shell(file);
    public bool OpenUrl(string url) => ShellCommands.IsWebAddress(url) && Shell(url);

    public bool Reveal(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return false;
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add("/select," + Path.GetFullPath(path));
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return false; }   // Not logged: the caller reports the refusal
    }

    private static bool Shell(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return false; }   // Not logged: the caller reports the refusal
    }
}
