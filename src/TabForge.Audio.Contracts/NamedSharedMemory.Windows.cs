using System.ComponentModel;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace TabForge.Audio.Contracts;

/// <summary>
/// Named shared memory of the Windows build: the page-file mapping that TabForge and the audio engine open by the same name.
/// Owns: creating (current-user access, SYSTEM/Administrators read, set at creation) and opening the named mapping. Does not own: the layout inside it (<see cref="SharedBlock"/>).
/// Tests: the engine tests that create or open a SharedBlock (net8.0-windows build);
/// SharedMemoryTrustTests (the mapping's DACL).
/// Compiled into net8.0-windows only; the net8.0 build uses NamedSharedMemory.NonWindows.cs.
/// </summary>
internal static class NamedSharedMemory
{
    private const int PageReadWrite = 0x04, ErrorAlreadyExists = 183;

    /// <summary>
    /// Creates a new named mapping of <paramref name="capacity"/> bytes; fails if the name already exists. The access list is set by the
    /// creating call itself (no window with the default list): full access for the current user, read for SYSTEM and Administrators
    /// (so dumps and debuggers still work); nothing is inherited.
    /// </summary>
    public static MemoryMappedFile Create(string name, long capacity)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("The current user is unknown.");
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW($"D:P(A;;GA;;;{sid})(A;;GR;;;SY)(A;;GR;;;BA)", 1, out var descriptor, out _)) throw new Win32Exception();
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor, InheritHandle = 0 };
            using var created = CreateFileMappingW(new IntPtr(-1), ref attributes, PageReadWrite, (uint)(capacity >> 32), (uint)(capacity & 0xFFFFFFFF), name);
            var error = Marshal.GetLastWin32Error();
            if (created.IsInvalid) throw new Win32Exception(error);
            if (error == ErrorAlreadyExists) throw new IOException("The shared memory name is already taken.");
            // Our handle closes at the end of this scope; the mapping lives on through the one OpenExisting returns.
            return MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);
        }
        finally { LocalFree(descriptor); }
    }

    /// <summary>Opens the existing named mapping created by the other process.</summary>
    public static MemoryMappedFile Open(string name) =>
        MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int InheritHandle; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileMappingW(IntPtr file, ref SecurityAttributes attributes, int protect, uint sizeHigh, uint sizeLow, string name);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, int revision, out IntPtr descriptor, out int size);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
