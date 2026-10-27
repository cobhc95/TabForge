using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TabForge.Services;
using Trace = TabForge.Services.Trace;

namespace TabForge.Avalonia.Platform;

// Owns: opening a folder in the system file manager from the Avalonia windows: Windows through the window's own launcher, other systems through IShellLauncher (open / xdg-open).
// Does not own: choosing or creating the folder (the live-video and render windows), tracing (TabForge.Services.Trace).
// Tests: TabForge.Avalonia.Tests.FolderLauncherTests.
public static class FolderLauncher
{
    /// <summary>Opens <paramref name="folder"/> in the file manager; a missing folder or a launch failure is logged, never shown as a crash.</summary>
    public static void Open(Visual owner, string folder)
    {
        if (!Directory.Exists(folder)) return;
        if (!OperatingSystem.IsWindows()) { if (!TabForge.Platform.ShellLauncher.Current.OpenFolder(folder)) Trace.Error(Trace.Ui, "open folder: no handler"); return; }   // macOS open, Linux xdg-open
        if (TopLevel.GetTopLevel(owner) is { } top) _ = LaunchAsync(top.Launcher, folder);
    }

    private static async Task LaunchAsync(ILauncher launcher, string folder)
    {
        try
        {
            if (!await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder))) Trace.Error(Trace.Ui, "open folder: no handler");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            Trace.Error(Trace.Ui, "open folder: " + ex.Message);
        }
    }
}
