using System;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: reading a window's HWND and wrapping a child HWND for a native control host, behind INativeWindowHandles.
/// <summary>The Windows adapter of <see cref="INativeWindowHandles"/>.</summary>
internal sealed class WindowsNativeWindowHandles : INativeWindowHandles
{
    public IntPtr Of(TopLevel? window) => window?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    public global::Avalonia.Platform.IPlatformHandle Wrap(IntPtr handle) => new global::Avalonia.Platform.PlatformHandle(handle, "HWND");
}
