using System.Runtime.InteropServices;

namespace TabForge.Avalonia.Platform;

// Owns: attaching a double-clicked (console-less) Windows process to its parent's console so a start-up failure can be printed.
internal static class WindowsConsole
{
    private const int AttachParentProcess = -1;

    internal static void AttachParent() => AttachConsole(AttachParentProcess);

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern bool AttachConsole(int dwProcessId);
}
