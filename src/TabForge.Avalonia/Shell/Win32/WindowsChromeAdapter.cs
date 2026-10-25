using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using static TabForge.Shell.NativeHitCodes;

namespace TabForge.Avalonia.Shell.Win32;

// Owns: the Windows move and resize drags, which start a native drag by handing Windows a non-client press (the state and caption
//       actions are the base class's).
// Does not own: hit testing (NcHitTestHook, Core/Shell/ChromeHitRules) or the title bar.
// Tests: TabForge.Avalonia.Tests.NcHitTestHookTests, TabForge.Avalonia.Tests.ChromeModeTests.
/// <summary>Window-chrome actions for an Avalonia window on Windows. UI thread only.</summary>
public sealed class WindowsChromeAdapter : AvaloniaWindowChrome
{
    public WindowsChromeAdapter(Window window) : base(window) { }

    public override void BeginMove() => SendNonClientPress(HtCaption);

    public override void BeginResize(ResizeEdge edge) => SendNonClientPress(NcHitTestHook.CodeOf(edge));

    private void SendNonClientPress(int hitCode)
    {
        if (!OperatingSystem.IsWindows() || HostWindow.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle) return;
        ReleaseCapture(); // the pointer press that asked for the drag holds capture; Windows' drag loop needs it free
        SendMessage(handle.Handle, WmNcLButtonDown, new IntPtr(hitCode), IntPtr.Zero);
    }

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReleaseCapture();
}
