using Microsoft.Win32;

namespace TabForge.Services;

// Owns: the per-user Windows file association registration for Guitar Pro and TabForge files.
// Does not own: the installer's associations and any other program's keys.
// Tests: TestInstallerAssociationParity.
/// <summary>
/// Windows integration: "Open with TabForge" for Guitar Pro and TabForge files, registered per user
/// (HKCU\Software\Classes, no administrator rights) so it can be switched on and off from Settings.
/// Only TabForge's own ProgIDs are written or removed; other programs' registrations are never touched,
/// and TabForge is added to each extension's "Open with" list rather than forcibly taking the default.
/// </summary>
public static class FileAssociations
{
    public static readonly string[] Extensions = FileTypes.AllOpenable;

    private const string ClassesRoot = @"Software\Classes";
    private const string ProgIdPrefix = "TabForge";

    internal static string ProgIdFor(string extension) => $"{ProgIdPrefix}{extension}"; // e.g. TabForge.gp5

    // installer/TabForge.iss [Registry] repeats these keys; the self-test compares the two (B-05).
    internal static string Description(string extension) => extension == ".tforge"
        ? "TabForge project"
        : $"Score file ({extension})";

    /// <summary>True when every extension lists TabForge (by ProgID) with a command pointing at <paramref name="exePath"/>.</summary>
    public static bool IsRegistered(string exePath)
    {
        foreach (var extension in Extensions)
        {
            using var command = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ProgIdFor(extension)}\shell\open\command");
            if (command?.GetValue(null) is not string value || !value.Contains(exePath, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static void Register(string exePath)
    {
        foreach (var extension in Extensions)
        {
            var progId = ProgIdFor(extension);
            using (var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{progId}"))
            {
                key.SetValue(null, Description(extension));
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

    public static void Unregister()
    {
        foreach (var extension in Extensions)
        {
            var progId = ProgIdFor(extension);
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{progId}", throwOnMissingSubKey: false);
            using (var openWith = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{extension}\OpenWithProgids", writable: true))
                openWith?.DeleteValue(progId, throwOnMissingValue: false);
            using var extensionKey = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{extension}", writable: true);
            if (extensionKey?.GetValue(null) is string current && current == progId) extensionKey.DeleteValue("", throwOnMissingValue: false);
        }
        NotifyShell();
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    // SHCNE_ASSOCCHANGED: Explorer refreshes icons and "Open with" without a sign-out.
    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
}
