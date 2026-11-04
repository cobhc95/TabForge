using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Win32.Input;

namespace TabForge.Avalonia.Shell.Win32;

// Owns: navigation and transport keys while a plug-in editor is the foreground window. Those windows belong to the engine or a bridge process (tfvst32, tfvst64), so their keys
//   never reach this process: the bound gestures are registered as Windows hotkeys for exactly as long as an editor is in front and is not expecting typing. Nothing is polled:
//   Windows reports foreground changes, and while an editor is in front, focus, caret and menu changes in its process.
// Does not own: which commands are forwarded (NavigationHotkeys), running them (MainWindowKeyboard), the editor windows (EditorWindows, native editor_window.cpp).
// Tests: TabForge.Avalonia.Tests.NavigationHotkeyForwardingTests (the typing rule); the Windows calls need a real plug-in window.
// Threads: UI thread only; the static state is shared by every main window of this process, because a hotkey can be registered only once. Windows only.
/// <summary>One per main window, from its opening to its Closed.</summary>
public sealed class PluginEditorHotkeys : IDisposable
{
    private const uint EventForeground = 0x3, EventMenuStart = 0x4, EventMenuPopupEnd = 0x7, EventObjectFocus = 0x8005, GaRootOwner = 3;
    private const int ObjIdCaret = -8, MenuModeFlags = 0x4 | 0x8 | 0x10;   // GUI_INMENUMODE, GUI_SYSTEMMENUMODE, GUI_POPUPMENUMODE
    private const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModWin = 8, ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x312, ClassNameChars = 64;
    private const string EditorClass = PluginEditorWin32.EditorPopupClass;

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int CaretLeft, CaretTop, CaretRight, CaretBottom;
    }

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);

    private static readonly List<PluginEditorHotkeys> Attached = new();   // the open main windows; ceiling: one per window
    private static readonly WinEventProc Proc = OnWinEvent;               // held here: Windows calls it for as long as a hook lives
    private static IntPtr _foregroundHook, _processHook;
    private static uint _processHookId;
    private static PluginEditorHotkeys? _holder;                          // the main window the hotkeys are registered on now

    private readonly Window _window;
    private readonly MainWindowKeyboard _keyboard;
    private readonly Win32Properties.CustomWndProcHookCallback _callback;
    private readonly List<(Key Key, KeyModifiers Modifiers)> _registered = new();   // index + 1 is the hotkey id; ceiling: the forwarded commands' gestures
    private IntPtr _hwnd;

    public PluginEditorHotkeys(Window window, MainWindowKeyboard keyboard)
    {
        _window = window;
        _keyboard = keyboard;
        _callback = WndProc;
        if (OperatingSystem.IsWindows()) window.Opened += OnOpened;
    }

    /// <summary>True for a window class that is a text field: the system edit controls and the common names built on them. Other fields show up through their caret.</summary>
    public static bool IsTextClass(string name) =>
        name.Equals("Edit", StringComparison.OrdinalIgnoreCase) || name.Equals("TEdit", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) || name.Contains(".EDIT.", StringComparison.OrdinalIgnoreCase);

    private void OnOpened(object? sender, EventArgs e)
    {
        _window.Opened -= OnOpened;
        if (_window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } handle) return;   // headless: no native window
        _hwnd = handle.Handle;
        Win32Properties.AddWndProcHookCallback(_window, _callback);
        Attached.Add(this);
        if (_foregroundHook == IntPtr.Zero) _foregroundHook = SetWinEventHook(EventForeground, EventForeground, IntPtr.Zero, Proc, 0, 0, 0);
        Update();
    }

    private static void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        var matters = evt is EventForeground or EventObjectFocus or (>= EventMenuStart and <= EventMenuPopupEnd) || idObject == ObjIdCaret;
        if (matters) Update();
    }

    private static void Update()
    {
        var foreground = GetForegroundWindow();
        var target = OwnerOfEditor(foreground);
        WatchProcess(target is null ? 0 : ProcessOf(foreground));
        if (target is not null && IsWindowEnabled(target._hwnd) && !ExpectsTyping()) target.Take();
        else _holder?.Release();
    }

    // The main window of a foreground plug-in editor of this app, or null: a plug-in's own dialog or a window of another program has a different class or owner.
    private static PluginEditorHotkeys? OwnerOfEditor(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero || Attached.Count == 0 || ClassOf(foreground) != EditorClass) return null;
        var root = GetAncestor(foreground, GaRootOwner);
        if (ProcessOf(root) != (uint)Environment.ProcessId) return null;
        return Attached.FirstOrDefault(a => a._hwnd == root) ?? Attached[0];
    }

    private static uint ProcessOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var process);
        return process;
    }

    private static string ClassOf(IntPtr hwnd)
    {
        var name = new StringBuilder(ClassNameChars);
        GetClassName(hwnd, name, ClassNameChars);
        return name.ToString();
    }

    // The foreground thread is in a menu, has a caret or has a text field focused.
    private static bool ExpectsTyping()
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info)) return false;
        return (info.Flags & MenuModeFlags) != 0 || info.Caret != IntPtr.Zero || (info.Focus != IntPtr.Zero && IsTextClass(ClassOf(info.Focus)));
    }

    // Focus, caret and menu events of the foreground editor's process only, and only while it is in front.
    private static void WatchProcess(uint process)
    {
        if (process == _processHookId) return;
        if (_processHook != IntPtr.Zero) UnhookWinEvent(_processHook);
        _processHook = process == 0 ? IntPtr.Zero : SetWinEventHook(EventMenuStart, EventObjectFocus, IntPtr.Zero, Proc, process, 0, 0);
        _processHookId = process;
    }

    // Reads the gestures now, so a changed binding counts from the next time an editor comes to the front. A gesture another program holds stays with the plug-in.
    private void Take()
    {
        if (ReferenceEquals(_holder, this)) return;
        _holder?.Release();
        _holder = this;
        foreach (var gesture in _keyboard.NavigationGestures())
        {
            if (!MainWindowKeyboard.TryParseGesture(gesture, out var key, out var modifiers)) continue;
            _registered.Add((key, modifiers));
            RegisterHotKey(_hwnd, _registered.Count, NativeModifiers(modifiers), (uint)KeyInterop.VirtualKeyFromKey(key));
        }
    }

    private void Release()
    {
        for (var i = 1; i <= _registered.Count; i++) UnregisterHotKey(_hwnd, i);
        _registered.Clear();
        if (ReferenceEquals(_holder, this)) _holder = null;
    }

    private static uint NativeModifiers(KeyModifiers modifiers)
    {
        var native = ModNoRepeat;
        if (modifiers.HasFlag(KeyModifiers.Control)) native |= ModControl;
        if (modifiers.HasFlag(KeyModifiers.Alt)) native |= ModAlt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) native |= ModShift;
        if (modifiers.HasFlag(KeyModifiers.Meta)) native |= ModWin;
        return native;
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey || (int)wParam < 1 || (int)wParam > _registered.Count) return IntPtr.Zero;
        handled = true;
        var (key, modifiers) = _registered[(int)wParam - 1];
        Dispatcher.UIThread.Post(() => _keyboard.RouteNavigation(key, modifiers, null), DispatcherPriority.Input);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _window.Opened -= OnOpened;
        if (_hwnd == IntPtr.Zero) return;
        Release();
        Win32Properties.RemoveWndProcHookCallback(_window, _callback);
        Attached.Remove(this);
        _hwnd = IntPtr.Zero;
        if (Attached.Count > 0) return;
        WatchProcess(0);
        UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
    }
}
