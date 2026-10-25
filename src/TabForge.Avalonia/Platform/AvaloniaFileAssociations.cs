using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the Avalonia app's Windows adapter for the file-association port: the per-user (HKCU\Software\Classes) reads and writes of TabForge's
//   ProgIDs and "Open with" entries, and the Explorer refresh (SHChangeNotify).
// Does not own: the extensions, ProgID names and descriptions (Services/FileAssociations.cs), when to register (Shell/FileAssociationSync.cs).
// Tests: TabForge.Avalonia.Tests.FileAssociationSyncTests (the sync through a fake port; the registry itself is the owner's machine and is not written by tests).
// Thread: any; each call reads or writes the registry again, so the adapter holds no state.
/// <summary>
/// <see cref="IFileAssociations"/> over the per-user registry, so no administrator rights are needed. Only TabForge's own ProgIDs are written or removed;
/// other programs' registrations are never touched, and TabForge is added to each extension's "Open with" list rather than taking the default when another program has one.
/// </summary>
public sealed class AvaloniaFileAssociations : IFileAssociations
{
    private const string ClassesRoot = @"Software\Classes";

    // SHCNE_ASSOCCHANGED: Explorer refreshes icons and "Open with" without a sign-out.
    private const int ShellAssociationChanged = 0x08000000;

    public bool IsRegistered(string exePath)
    {
        foreach (var extension in FileAssociations.Extensions)
        {
            using var command = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{FileAssociations.ProgIdFor(extension)}\shell\open\command");
            if (command?.GetValue(null) is not string value || !value.Contains(exePath, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public void Register(string exePath)
    {
        foreach (var extension in FileAssociations.Extensions)
        {
            var progId = FileAssociations.ProgIdFor(extension);
            using (var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{progId}"))
            {
                key.SetValue(null, FileAssociations.Description(extension));
                using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exePath}\",0");
                using var command = key.CreateSubKey(@"shell\open\command");
                command.SetValue(null, $"\"{exePath}\" \"%1\"");
            }
            using (var ext = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{extension}\OpenWithProgids"))
                ext.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
            // Claim the default only when no other program has (never override the user's choice).
            using var extensionKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{extension}");
            if (extensionKey.GetValue(null) is not string current || string.IsNullOrWhiteSpace(current))
                extensionKey.SetValue(null, progId);
        }
        NotifyShell();
    }

    public void Unregister()
    {
        foreach (var extension in FileAssociations.Extensions)
        {
            var progId = FileAssociations.ProgIdFor(extension);
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{progId}", throwOnMissingSubKey: false);
            using (var openWith = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{extension}\OpenWithProgids", writable: true))
                openWith?.DeleteValue(progId, throwOnMissingValue: false);
            using var extensionKey = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{extension}", writable: true);
            if (extensionKey?.GetValue(null) is string current && current == progId) extensionKey.DeleteValue("", throwOnMissingValue: false);
        }
        NotifyShell();
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    private static void NotifyShell() => SHChangeNotify(ShellAssociationChanged, 0, IntPtr.Zero, IntPtr.Zero);
}
