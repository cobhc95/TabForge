using System.Runtime.InteropServices;
using System.Text;

namespace Gp5Driver;

// Owns: the Win32 calls the driver needs (desktops, processes, windows, messages, keyboard state, menus, capture).
// Does not own: what to send (Gp5Session.cs).
internal static class Native
{
    public const uint WM_NULL = 0, WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E, WM_CLOSE = 0x0010,
        WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105,
        WM_COMMAND = 0x0111, BM_CLICK = 0x00F5, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_ACTIVATE = 0x0006, WM_SETFOCUS = 0x0007;
    public const uint DESKTOP_ALL = 0x01FF | 0x000F0000;
    public const uint CREATE_NEW_CONSOLE = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public override string ToString() => $"{Left},{Top} {Right - Left}x{Bottom - Top}"; }
    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO { public int cbSize, flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret; }

    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateDesktop(string name, IntPtr dev, IntPtr mode, int flags, uint access, IntPtr sa);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr OpenDesktop(string name, int flags, bool inherit, uint access);
    [DllImport("user32", SetLastError = true)] public static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32", SetLastError = true)] public static extern bool SetThreadDesktop(IntPtr h);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lp);
    [DllImport("user32")] public static extern bool EnumDesktopWindows(IntPtr desk, EnumProc cb, IntPtr lp);
    [DllImport("user32")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lp);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateProcess(string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32")] public static extern uint GetCurrentThreadId();
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32")] public static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] public static extern IntPtr SendMessageStr(IntPtr h, uint msg, IntPtr w, string l);
    [DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] public static extern IntPtr SendMessageSb(IntPtr h, uint msg, IntPtr w, StringBuilder l);
    [DllImport("user32")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32")] public static extern bool SetKeyboardState(byte[] state);
    [DllImport("user32")] public static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO info);
    [DllImport("user32")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32")] public static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32")] public static extern IntPtr SetActiveWindow(IntPtr h);
    [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32")] public static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
    [DllImport("user32")] public static extern IntPtr GetMenu(IntPtr h);
    [DllImport("user32")] public static extern int GetMenuItemCount(IntPtr m);
    [DllImport("user32")] public static extern uint GetMenuItemID(IntPtr m, int pos);
    [DllImport("user32")] public static extern IntPtr GetSubMenu(IntPtr m, int pos);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetMenuString(IntPtr m, uint item, StringBuilder sb, int n, uint flags);
    [DllImport("user32")] public static extern uint GetMenuState(IntPtr m, uint item, uint flags);
    [DllImport("user32")] public static extern bool PeekMessage(out MSG msg, IntPtr h, uint min, uint max, uint remove);
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr w, l; public uint time; public int x, y; }

    public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    public static List<IntPtr> TopWindows(IntPtr desk)
    {
        var list = new List<IntPtr>();
        EnumDesktopWindows(desk, (h, _) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static List<IntPtr> Children(IntPtr parent)
    {
        var list = new List<IntPtr>();
        EnumChildWindows(parent, (h, _) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
}
