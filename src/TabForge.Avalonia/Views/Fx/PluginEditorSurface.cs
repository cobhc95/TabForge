using System;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Platform;
using TabForge.Avalonia.Shell.Win32;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the native child window inside the FX chain window that the audio engine (another process) parents a plug-in editor to, so the plug-in
//   controls appear docked. TabForge never loads plug-in code: it only lends this window.
// Does not own: opening the editor (FxEditorController), its placement (PluginEditorWin32), its size fit (FxEditorArea).
// Tests: none dedicated(the handle is zero in a headless window, which has no native parent).
public sealed class PluginEditorSurface : NativeControlHost
{
    /// <summary>The native window the editor is parented to; zero until the control is attached to a window with a native handle.</summary>
    public IntPtr Area { get; private set; }

    /// <summary>The window handle of the area was created or destroyed.</summary>
    public event Action? AreaChanged;

    /// <summary>Test seam: stands in for the native window a headless window cannot create.</summary>
    internal void SetAreaForTest(IntPtr area) { Area = area; AreaChanged?.Invoke(); }

    /// <summary>Test seam: the attach fails the way a refused native window does (NativeControlHost throws from its attach).</summary>
    internal bool RefuseNativeWindowForTest { get; set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (RefuseNativeWindowForTest) throw new InvalidOperationException("Unable to create child window for native control host.");
        base.OnAttachedToVisualTree(e);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        Area = PluginEditorWin32.CreateArea(parent.Handle);
        AreaChanged?.Invoke();
        return Area == IntPtr.Zero ? base.CreateNativeControlCore(parent) : new PlatformHandle(Area, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (Area == IntPtr.Zero) { base.DestroyNativeControlCore(control); return; }
        PluginEditorWin32.DestroyArea(Area);
        Area = IntPtr.Zero;
        AreaChanged?.Invoke();
    }
}
