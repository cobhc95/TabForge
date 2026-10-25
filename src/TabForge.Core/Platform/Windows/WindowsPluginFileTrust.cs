using System.Runtime.InteropServices;

namespace TabForge.Platform.Windows;

// Owns: the wintrust.dll WinVerifyTrust call for plug-in binaries; the final path comes from WindowsPathResolver.
// Does not own: any trust decision (PluginTrust) or what an unknown answer means (the caller denies).
// Tests: no dedicated test class.
/// <summary><see cref="IPluginFileTrust"/> over Win32. Stateless; each call asks Windows again.</summary>
public sealed class WindowsPluginFileTrust : IPluginFileTrust
{
    private readonly IPathResolver finalPaths = new WindowsPathResolver();

    public string? FinalPath(string fullPath) => finalPaths.FinalPath(fullPath);

    public bool HasValidEmbeddedSignature(string fullPath)
    {
        var pathPtr = Marshal.StringToHGlobalUni(fullPath);
        var filePtr = IntPtr.Zero;
        try
        {
            var file = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = pathPtr };
            filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(), dwUIChoice = 2 /* WTD_UI_NONE */, fdwRevocationChecks = 0 /* WTD_REVOKE_NONE */,
                dwUnionChoice = 1 /* WTD_CHOICE_FILE */, pFile = filePtr, dwStateAction = 0 /* WTD_STATEACTION_IGNORE */,
                dwProvFlags = 0x10 | 0x1000 /* WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL */,
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0;
        }
        catch (Exception) { return false; } // Not logged: any failure (WinVerifyTrust missing, bad path, marshalling) reads as not signed: fail closed
        finally
        {
            if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);
}
