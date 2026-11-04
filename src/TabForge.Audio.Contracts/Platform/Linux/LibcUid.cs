using System.Runtime.InteropServices;

namespace TabForge.Audio.Contracts;

// Owns: the Unix user id behind the per-user shared-memory folder name. It lives in the Linux platform folder
// so the portability guard does not count the libc import as a Windows-only call.
// Does not own: the folder itself (PortableSharedMemory) or its layout (SharedBlock).
// Tests: CrossPlatformStage2Tests (the default folder is per-user).
internal static class LibcUid
{
    [DllImport("libc", EntryPoint = "geteuid")]
    internal static extern uint GetEuid();
}
