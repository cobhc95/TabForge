using TabForge.Platform.Contracts;
using System;
using global::Avalonia.Controls;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: choosing, once, the adapter of each operating-system port the Avalonia app uses (the Windows adapter on Windows, a stub elsewhere), and the small
//   ports that only the app needs (process integrity, native window handles).
// Does not own: the Windows calls (the adapters in Platform/Windows/).
// Tests: PortabilityGuardTests (no Windows-only call outside the adapter files).
internal static class PlatformAdapters
{
    public static IFileAssociations FileAssociations() => OperatingSystem.IsWindows() ? new WindowsFileAssociations() : new NullFileAssociations();

    /// <summary>The audio device lists Settings shows: the Windows endpoints, or miniaudio's devices off Windows (and on Windows when the miniaudio driver is chosen with the dev switch).</summary>
#if WINDOWS
    public static IAudioDevices AudioDevices() => OperatingSystem.IsWindows() ? new TabForge.Platform.Windows.WindowsAudioDevices() : new TabForge.Platform.Portable.PortableAudioDevices();
#else
    public static IAudioDevices AudioDevices() => new TabForge.Platform.Portable.PortableAudioDevices();
#endif

    public static IProcessIntegrity ProcessIntegrity { get; } = OperatingSystem.IsWindows() ? new WindowsProcessIntegrity() : new NullProcessIntegrity();

    public static INativeWindowHandles NativeWindows { get; } = OperatingSystem.IsWindows() ? new WindowsNativeWindowHandles() : new NullNativeWindowHandles();
}

/// <summary>The native window handle of a top-level window, and a wrapper for a child handle a native control host shows.</summary>
internal interface INativeWindowHandles
{
    /// <summary>The window's native handle (an HWND on Windows); zero without a native window.</summary>
    IntPtr Of(TopLevel? window);

    /// <summary>The handle of a native child window in the form Avalonia's native control host takes.</summary>
    global::Avalonia.Platform.IPlatformHandle Wrap(IntPtr handle);
}

/// <summary>The non-Windows stub: native plug-in windows are not hosted there yet, so there is no handle.</summary>
internal sealed class NullNativeWindowHandles : INativeWindowHandles
{
    public IntPtr Of(TopLevel? window) => IntPtr.Zero;
    public global::Avalonia.Platform.IPlatformHandle Wrap(IntPtr handle) => throw new PlatformNotSupportedException("Native child windows are not hosted on this platform yet.");
}
