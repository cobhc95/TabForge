using System;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Platform;
using TabForge.Avalonia.Shell.Win32;

namespace TabForge.Avalonia.Platform;

// Owns: reading a window's HWND, wrapping a child HWND for a native control host, and the native repaint of a window, behind INativeWindowHandles.
/// <summary>The Windows adapter of <see cref="INativeWindowHandles"/>.</summary>
internal sealed class WindowsNativeWindowHandles : INativeWindowHandles
{
    public IntPtr Of(TopLevel? window) => window?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    public global::Avalonia.Platform.IPlatformHandle Wrap(IntPtr handle) => new global::Avalonia.Platform.PlatformHandle(handle, "HWND");

    public void Redraw(IntPtr handle) => PluginEditorWin32.Redraw(handle);
}
