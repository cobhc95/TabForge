using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TabForge.Avalonia.Shell.Win32;

// Owns: turning a new empty file into a reparse point (a link as the file system sees it) without privilege: a symbolic link needs
//   Developer Mode or elevation, a reparse point with a private tag does not. Used to make a file link for tests.
// Does not own: the portable choice (Platform/FolderLinks).
// Tests: SelfTestPairRecovery (the file-link case).
internal static class WindowsFileReparse
{
    /// <summary>Creates <paramref name="link"/> as an empty file carrying a private reparse tag; false when the system refused.</summary>
    internal static bool TryCreate(string link)
    {
        try
        {
            File.WriteAllBytes(link, Array.Empty<byte>());
            using var handle = CreateFileW(link, GenericWrite, 0, IntPtr.Zero, OpenExisting, FileFlagOpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid) { File.Delete(link); return false; }
            // REPARSE_GUID_DATA_BUFFER: tag, data length, reserved, GUID, data (none). Tag bit 31 clear: not a Microsoft tag.
            var buffer = new byte[24];
            BitConverter.GetBytes(PrivateTag).CopyTo(buffer, 0);
            Guid.NewGuid().ToByteArray().CopyTo(buffer, 8);
            var ok = DeviceIoControl(handle, FsctlSetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero);
            if (!ok) { handle.Dispose(); File.Delete(link); }
            return ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; } // swallow: no right to make it here; the caller reports it
    }

    private const uint PrivateTag = 0x0000_0C0D;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FsctlSetReparsePoint = 0x000900A4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
}
