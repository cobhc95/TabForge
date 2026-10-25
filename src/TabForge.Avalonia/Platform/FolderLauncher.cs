using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using TabForge.Services;
using Trace = TabForge.Services.Trace;

namespace TabForge.Avalonia.Platform;

// Owns: opening a folder in File Explorer from the Avalonia windows: the absolute Windows-folder Explorer path and the one argument both call sites share.
// Does not own: choosing or creating the folder (the live-video and render windows), tracing (TabForge.Services.Trace).
// Tests: TabForge.Avalonia.Tests.FolderLauncherTests.
public static class FolderLauncher
{
    /// <summary>File Explorer's file name in the Windows folder (C:\Windows\explorer.exe): the copy under System32 does not
    /// exist on every machine, and the name is never resolved through the shell's search order.</summary>
    private const string ExplorerExe = "explorer.exe";

    /// <summary>The start info that opens <paramref name="folder"/>: the absolute Windows-folder Explorer and the folder as one argument, not a command line.</summary>
    public static ProcessStartInfo StartInfo(string folder)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), ExplorerExe)) { UseShellExecute = false };
        start.ArgumentList.Add(folder);
        return start;
    }

    /// <summary>Opens <paramref name="folder"/> in File Explorer; a missing folder or a launch failure is logged, never shown as a crash.</summary>
    public static void Open(string folder)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(folder)) return;
        try { Process.Start(StartInfo(folder)); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Trace.Error(Trace.Ui, "open folder: " + ex.Message);
        }
    }
}
