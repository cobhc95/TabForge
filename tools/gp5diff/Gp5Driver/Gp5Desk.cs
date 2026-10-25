using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using static Gp5Driver.Native;

namespace Gp5Driver;

// Owns: one GP5 instance on one hidden desktop: launching it, finding its windows, keystrokes with modifiers (attached input +
//     keyboard state, then posted key messages), menu commands, window captures.
// Does not own: what a test sequence means (Gp5Actions.cs), batch scheduling (Program.cs).
internal sealed class Gp5Desk : IDisposable
{
    /// <summary>The GP5 to start: by default the installed one; the runner gives each worker its own copy (GP5 keeps its clipboard in a file of its folder).</summary>
    public string Gp5Exe = @"C:\Program Files (x86)\Guitar Pro 5\GP5.exe";
    public readonly string Name;
    public IntPtr Desk;
    public int Pid;
    private uint _gp5Tid;
    /// <summary>A window that receives keys instead of the focused one (exploration: "target:&lt;hex&gt;").</summary>
    public IntPtr KeyTarget;
    private bool _attached;

    public Gp5Desk(string name, bool create)
    {
        Name = name;
        Desk = create ? CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, DESKTOP_ALL, IntPtr.Zero) : OpenDesktop(name, 0, false, DESKTOP_ALL);
        if (Desk == IntPtr.Zero) throw new InvalidOperationException($"desktop {name}: error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
    }

    /// <summary>Binds the calling thread to this desktop (a fresh thread without windows) so it can attach to GP5's input queue.</summary>
    public void BindThread()
    {
        if (!SetThreadDesktop(Desk)) throw new InvalidOperationException($"SetThreadDesktop: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);   // creates this thread's message queue
    }

    public int Launch(string args = "")
    {
        var si = new STARTUPINFO { cb = System.Runtime.InteropServices.Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"WinSta0\" + Name };
        var cmd = new StringBuilder($"\"{Gp5Exe}\" {args}");
        if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, Path.GetDirectoryName(Gp5Exe), ref si, out var pi))
            throw new InvalidOperationException($"CreateProcess: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
        Pid = pi.dwProcessId;
        return Pid;
    }

    /// <summary>Visible top-level windows of GP5 on this desktop.</summary>
    public List<IntPtr> Windows()
    {
        var all = TopWindows(Desk).Where(h => IsWindowVisible(h)).ToList();
        if (Pid != 0) all = all.Where(h => { GetWindowThreadProcessId(h, out var p); return p == Pid; }).ToList();
        return all;
    }

    public IntPtr MainWindow => Windows().FirstOrDefault(h => ClassOf(h) == "TMainForm");
    private static readonly HashSet<string> NotDialogs = new() { "TMainForm", "TApplication", "tooltips_class32", "THintWindow", "TTBFloatingWindowParent", "SysShadow", "TTBPopupWindow" };
    /// <summary>A modal dialog (a visible, enabled GP5 window other than the main window, toolbars and hints), or zero.</summary>
    public IntPtr Dialog()
    {
        if (Pid == 0 && MainWindow is var m && m != IntPtr.Zero) { GetWindowThreadProcessId(m, out var p); Pid = p; }
        return Windows().FirstOrDefault(h => IsWindowEnabled(h) && !NotDialogs.Contains(ClassOf(h)) && !ClassOf(h).StartsWith("UAC") && !ClassOf(h).StartsWith("TTBPopup"));
    }

    public static string TextOfWindow(IntPtr h) { var sb = new StringBuilder(512); SendMessageTimeout(h, WM_NULL, IntPtr.Zero, IntPtr.Zero, 2, 200, out _); GetWindowText(h, sb, 512); return sb.ToString(); }
    public static string ControlText(IntPtr h) { var sb = new StringBuilder(1024); SendMessageSb(h, WM_GETTEXT, (IntPtr)1024, sb); return sb.ToString(); }

    public void AttachInput()
    {
        var main = MainWindow;
        if (main == IntPtr.Zero) throw new InvalidOperationException("no main window");
        var tid = GetWindowThreadProcessId(main, out _);
        if (_attached && tid != _gp5Tid) { AttachThreadInput(GetCurrentThreadId(), _gp5Tid, false); _attached = false; }
        _gp5Tid = tid;
        if (!_attached) _attached = AttachThreadInput(GetCurrentThreadId(), _gp5Tid, true);
        if (!_attached) throw new InvalidOperationException("AttachThreadInput failed");
    }

    /// <summary>
    /// GP5 takes score keys through an off-screen TEdit on the score panel (the window is never activated on a hidden desktop, so
    /// nothing has the focus at start): give it the focus so keys reach the score the way they do in a normal session.
    /// </summary>
    public bool FocusScore()
    {
        var edit = ScoreEdit;
        if (edit == IntPtr.Zero) return false;
        SetFocus(edit);
        Sync(edit);
        return Focus() == edit;
    }

    public IntPtr ScoreEdit => Children(MainWindow).FirstOrDefault(c => ClassOf(c) == "TEdit" && GetWindowRect(c, out var r) && r.Left < -100 && IsWindowVisible(c));

    public IntPtr Focus()
    {
        var gi = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        GetGUIThreadInfo(_gp5Tid, ref gi);
        if (KeyTarget != IntPtr.Zero) return KeyTarget;
        if (gi.hwndFocus != IntPtr.Zero && ClassOf(gi.hwndFocus) != "TApplication") return gi.hwndFocus;
        if (gi.hwndActive != IntPtr.Zero && ClassOf(gi.hwndActive) != "TApplication") return gi.hwndActive;
        return MainWindow;
    }

    /// <summary>Waits until GP5's GUI thread has handled what was posted so far (two sent no-op messages; also returns while a modal loop runs).</summary>
    public bool Sync(IntPtr h, uint timeoutMs = 5000)
    {
        for (var i = 0; i < 2; i++)
            if (SendMessageTimeout(h == IntPtr.Zero ? MainWindow : h, WM_NULL, IntPtr.Zero, IntPtr.Zero, 0, timeoutMs, out _) == IntPtr.Zero) return false;
        return true;
    }

    private static readonly Dictionary<string, (byte Vk, bool Ext)> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = (0x25, true), ["up"] = (0x26, true), ["right"] = (0x27, true), ["down"] = (0x28, true),
        ["home"] = (0x24, true), ["end"] = (0x23, true), ["ins"] = (0x2D, true), ["del"] = (0x2E, true),
        ["pgup"] = (0x21, true), ["pgdn"] = (0x22, true), ["enter"] = (0x0D, false), ["esc"] = (0x1B, false), ["tab"] = (0x09, false),
        ["space"] = (0x20, false), ["back"] = (0x08, false), ["period"] = (0xBE, false), ["comma"] = (0xBC, false),
        ["minus"] = (0xBD, false), ["plus"] = (0xBB, false), ["slash"] = (0xBF, false),
        ["numadd"] = (0x6B, false), ["numsub"] = (0x6D, false), ["nummul"] = (0x6A, false), ["numdiv"] = (0x6F, true), ["numdec"] = (0x6E, false),
        ["f1"] = (0x70, false), ["f2"] = (0x71, false), ["f4"] = (0x73, false), ["f5"] = (0x74, false), ["f6"] = (0x75, false), ["f12"] = (0x7B, false),
    };

    /// <summary>One key chord such as "ctrl+shift+left", "alt+s", "7", "numadd", "L".</summary>
    public void Key(string chord, int holdMs = 0)
    {
        var parts = chord.Split('+', StringSplitOptions.RemoveEmptyEntries);
        // "shift++" style is not used; "numadd" names the plus key.
        bool ctrl = false, shift = false, alt = false;
        foreach (var p in parts[..^1])
            switch (p.ToLowerInvariant()) { case "ctrl": ctrl = true; break; case "shift": shift = true; break; case "alt": alt = true; break; default: throw new ArgumentException(chord); }
        var k = parts[^1];
        byte vk; bool ext = false;
        if (Keys.TryGetValue(k, out var e)) (vk, ext) = e;
        else if (k.Length == 1 && char.IsLetterOrDigit(k[0])) vk = (byte)char.ToUpperInvariant(k[0]);
        else throw new ArgumentException("unknown key " + k);
        var target = Focus();
        var state = new byte[256];
        GetKeyboardState(state);
        void Mod(int vkm, int l, bool on) { state[vkm] = state[l] = (byte)(on ? 0x80 : 0); }
        Mod(0x10, 0xA0, shift); Mod(0x11, 0xA2, ctrl); Mod(0x12, 0xA4, alt);
        state[vk] = 0x80;
        SetKeyboardState(state);
        var scan = MapVirtualKey(vk, 0);
        long l = 1 | (scan << 16) | (ext ? 1L << 24 : 0) | (alt ? 1L << 29 : 0);
        var sys = alt && !ctrl;
        PostMessage(target, sys ? WM_SYSKEYDOWN : WM_KEYDOWN, (IntPtr)vk, (IntPtr)l);
        if (!Sync(target)) throw new TimeoutException($"GP5 did not answer after {chord}");
        if (holdMs > 0) Thread.Sleep(holdMs);
        state[vk] = 0;
        SetKeyboardState(state);
        // The key-up is waited for too: the WM_CHAR / WM_SYSCHAR that TranslateMessage posts while GP5 handles the key-down
        // (digits, Alt+letter menus) runs after the sent no-op messages, so without this wait the next key can overtake it.
        PostMessage(target, sys ? WM_SYSKEYUP : WM_KEYUP, (IntPtr)vk, (IntPtr)(l | (1L << 30) | (1L << 31)));
        Sync(target);
        Mod(0x10, 0xA0, false); Mod(0x11, 0xA2, false); Mod(0x12, 0xA4, false);
        SetKeyboardState(state);
    }

    /// <summary>Clicks the button whose caption (without '&amp;') starts with <paramref name="caption"/> in the open dialog. False when none.</summary>
    public bool ClickButton(string caption)
    {
        var dl = Dialog();
        if (dl == IntPtr.Zero) return false;
        foreach (var c in Children(dl))
            if (TextOfWindow(c).Replace("&", "").StartsWith(caption, StringComparison.OrdinalIgnoreCase) && IsWindowVisible(c))
            {
                PostMessage(c, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                Sync(dl);
                return true;
            }
        return false;
    }

    /// <summary>Fills the file name of an open Save / Open common dialog and presses its default button. False when no such dialog.</summary>
    public bool FileDialog(string path)
    {
        var dl = Dialog();
        if (dl == IntPtr.Zero || ClassOf(dl) != "#32770") return false;
        var edit = Children(dl).FirstOrDefault(c => ClassOf(c) == "Edit" && IsWindowVisible(c));
        if (edit == IntPtr.Zero) return false;
        SendMessageStr(edit, WM_SETTEXT, IntPtr.Zero, path);
        var ok = Children(dl).FirstOrDefault(c => ClassOf(c) == "Button" && GetDlgCtrlID(c) == 1);
        PostMessage(ok, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        Sync(MainWindow);
        return true;
    }

    /// <summary>Restores the (maximized) main window to a small size: each key repaints less, so GP5 answers faster.</summary>
    public void Shrink()
    {
        var main = MainWindow;
        SendMessageTimeout(main, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)0xF120 /* SC_RESTORE */, IntPtr.Zero, 0, 5000, out _);
        MoveWindow(main, 0, 0, 1100, 800, true);
        Sync(main);
    }

    public void Command(uint id) { PostMessage(MainWindow, WM_COMMAND, (IntPtr)id, IntPtr.Zero); Sync(MainWindow); }

    /// <summary>The main menu as "path = id" lines (GP5 uses a plain Win32 menu).</summary>
    public List<(string Path, uint Id, bool Checked, bool Grayed)> Menu()
    {
        var list = new List<(string, uint, bool, bool)>();
        void Walk(IntPtr m, string prefix)
        {
            var n = GetMenuItemCount(m);
            for (var i = 0; i < n; i++)
            {
                var sb = new StringBuilder(256);
                GetMenuString(m, (uint)i, sb, 256, 0x400);
                var name = prefix + sb.ToString().Replace("&", "");
                var sub = GetSubMenu(m, i);
                var st = GetMenuState(m, (uint)i, 0x400);
                if (sub != IntPtr.Zero) Walk(sub, name + " > ");
                else list.Add((name, GetMenuItemID(m, i), (st & 8) != 0, (st & 3) != 0));
            }
        }
        var menu = GetMenu(MainWindow);
        if (menu != IntPtr.Zero) Walk(menu, "");
        return list;
    }

    public void Capture(IntPtr h, string path)
    {
        GetWindowRect(h, out var r);
        int w = Math.Max(1, r.Right - r.Left), ht = Math.Max(1, r.Bottom - r.Top);
        using var bmp = new Bitmap(w, ht);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            PrintWindow(h, hdc, 2);
            g.ReleaseHdc(hdc);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    public string Tree()
    {
        var sb = new StringBuilder();
        foreach (var h in TopWindows(Desk))
        {
            GetWindowThreadProcessId(h, out var p);
            GetWindowRect(h, out var r);
            sb.AppendLine($"{h:X} pid {p} vis {IsWindowVisible(h)} en {IsWindowEnabled(h)} [{ClassOf(h)}] \"{TextOfWindow(h)}\" {r}");
            if (!IsWindowVisible(h)) continue;
            foreach (var c in Children(h))
            {
                GetWindowRect(c, out var rc);
                sb.AppendLine($"    {c:X} parent {GetParent(c):X} vis {IsWindowVisible(c)} [{ClassOf(c)}] \"{TextOfWindow(c)}\" {rc}");
            }
        }
        return sb.ToString();
    }

    public void Kill() { try { if (Pid != 0) Process.GetProcessById(Pid).Kill(); } catch (ArgumentException) { } }

    public void Dispose()
    {
        if (_attached) AttachThreadInput(GetCurrentThreadId(), _gp5Tid, false);
        if (Desk != IntPtr.Zero) CloseDesktop(Desk);
        Desk = IntPtr.Zero;
    }
}
