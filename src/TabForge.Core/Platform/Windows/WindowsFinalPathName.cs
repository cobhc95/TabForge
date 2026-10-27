using System.IO;

namespace TabForge.Platform.Windows;

// Owns: the Win32 calls that give the real (link-followed) name of an existing file or folder for MediaPathPolicy (CreateFileW + GetFinalPathNameByHandleW).
// Does not own: what the policy does with the answer.
internal static class WindowsFinalPathName
{
    /// <summary>The final path of an existing file or folder with the extended prefix removed; null when unknown or resolved onto the network.</summary>
    public static string? Of(string existing)
    {
        // Backup semantics so a folder opens; no access rights are needed to ask for the name. Links on the way are followed, so the name is the real place.
        using var handle = CreateFileW(existing, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var buffer = new char[32_768];
        var n = GetFinalPathNameByHandleW(handle.DangerousGetHandle(), buffer, (uint)buffer.Length, 0);
        if (n == 0 || n >= buffer.Length) return null;
        var text = new string(buffer, 0, (int)n);
        if (text.StartsWith(@"\?\UNC\", StringComparison.OrdinalIgnoreCase)) return null;   // resolved onto the network: not judged here
        return text.StartsWith(@"\?\", StringComparison.Ordinal) ? text[4..] : text;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(IntPtr file, char[] buffer, uint length, uint flags);
}
