using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks that the built Avalonia exe embeds the per-monitor V2 DPI manifest and declares Windows 8 or later, which
/// Windows requires before it will create the layered native child window the docked plug-in editor sits in. The exe's engine
/// and plug-in host processes are the same exe, so the manifest is what keeps their plug-in editor windows from being bitmap-scaled.
/// Does not own: the other manifest settings, or the processes themselves (see AudioEngineEntryTests).
/// Tests: this class.
/// </summary>
public class AppManifestTests
{
    private const int RtManifest = 24;                 // RT_MANIFEST
    private const int ManifestResourceId = 1;          // CREATEPROCESS_MANIFEST_RESOURCE_ID
    private const uint LoadLibraryAsDataFile = 0x2;

    /// <summary>The supportedOS ids of Windows 8, 8.1 and 10/11; any one lifts the legacy compatibility level.</summary>
    private static readonly string[] Windows8OrLaterIds =
    {
        "{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}",
        "{1f676c76-80e1-4239-95bb-83d0f6d0da78}",
        "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}",
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);

    [Fact]
    public void BuiltExeEmbedsPerMonitorV2Manifest()
    {
        if (!OperatingSystem.IsWindows()) return;   // the manifest is a Windows resource; the other targets build no exe with it
        var exe = Path.Combine(AppContext.BaseDirectory, "TabForge.exe");
        Assert.True(File.Exists(exe), "The built Avalonia exe is not next to the test assembly.");

        Assert.Contains("PerMonitorV2", ReadEmbeddedManifest(exe));
    }

    [Fact]
    public void BuiltExeDeclaresWindows8OrLaterForLayeredChildWindows()
    {
        if (!OperatingSystem.IsWindows()) return;   // the manifest is a Windows resource; the other targets build no exe with it
        var exe = Path.Combine(AppContext.BaseDirectory, "TabForge.exe");
        Assert.True(File.Exists(exe), "The built Avalonia exe is not next to the test assembly.");

        var declared = XDocument.Parse(ReadEmbeddedManifest(exe)).Descendants()
            .Where(element => element.Name.LocalName == "supportedOS")
            .Select(element => (string?)element.Attribute("Id") ?? "");
        Assert.Contains(declared, id => Windows8OrLaterIds.Contains(id, StringComparer.OrdinalIgnoreCase));
    }

    private static string ReadEmbeddedManifest(string exe)
    {
        var module = LoadLibraryEx(exe, IntPtr.Zero, LoadLibraryAsDataFile);
        if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var resource = FindResource(module, (IntPtr)ManifestResourceId, (IntPtr)RtManifest);
            if (resource == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var size = (int)SizeofResource(module, resource);
            var bytes = new byte[size];
            Marshal.Copy(LockResource(LoadResource(module, resource)), bytes, 0, size);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            FreeLibrary(module);
        }
    }
}
