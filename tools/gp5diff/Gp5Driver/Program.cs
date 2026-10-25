using Gp5Driver;

// Owns: the command line of the GP5 driver. "do <desktop> <op>..." runs exploration ops in order on a fresh thread bound to the
//     desktop: launch, tree, menu, shot:<png>, dshot:<png> (dialog), key:<chord>, keys:<chord,chord>, cmd:<id>, sleep:<ms>, focus, close.
// "run" executes sequence files (see Gp5Runner.cs).
// Does not own: the Win32 work (Gp5Desk.cs).
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "do") return OnThread(() => Do(args[1], args[2..]));
        if (args.Length >= 1 && args[0] == "run") return Gp5Runner.Run(args[1..]);
        Console.Error.WriteLine("usage: Gp5Driver do <desktop> <ops...> | run ...");
        return 2;
    }

    public static int OnThread(Func<int> body)
    {
        var rc = 1;
        var t = new Thread(() => { try { rc = body(); } catch (Exception ex) { Console.Error.WriteLine(ex); rc = 1; } });
        t.Start(); t.Join();
        return rc;
    }

    private static int Do(string name, string[] ops)
    {
        var create = ops.Length > 0 && ops[0].StartsWith("launch");
        using var d = new Gp5Desk(name, create);
        d.BindThread();
        foreach (var op in ops)
        {
            var (verb, arg) = op.Contains(':') ? (op[..op.IndexOf(':')], op[(op.IndexOf(':') + 1)..]) : (op, "");
            switch (verb)
            {
                case "launch":
                    Console.WriteLine("pid " + d.Launch(arg));
                    for (var i = 0; i < 100 && d.MainWindow == IntPtr.Zero; i++) Thread.Sleep(200);
                    Console.WriteLine("main " + d.MainWindow.ToString("X"));
                    break;
                case "tree": Console.WriteLine(d.Tree()); break;
                case "menu": d.AttachInput(); foreach (var m in d.Menu()) Console.WriteLine($"{m.Id,6} {(m.Checked ? "[x]" : "   ")}{(m.Grayed ? "(g)" : "   ")} {m.Path}"); break;
                case "shot": d.Capture(d.MainWindow, arg); break;
                case "dshot": { var dl = d.Dialog(); Console.WriteLine("dialog " + dl.ToString("X") + " " + Native.ClassOf(dl)); if (dl != IntPtr.Zero) d.Capture(dl, arg); break; }
                case "key": d.AttachInput(); d.Key(arg); break;
                case "keys": d.AttachInput(); foreach (var k in arg.Split(',')) d.Key(k); break;
                case "cmd": d.AttachInput(); d.Command(uint.Parse(arg)); break;
                case "click": Console.WriteLine("click " + d.ClickButton(arg)); break;
                case "dtree": { var dl = d.Dialog(); foreach (var c in Native.Children(dl)) Console.WriteLine($"  {c:X} [{Native.ClassOf(c)}] \"{Gp5Desk.TextOfWindow(c)}\" / \"{Gp5Desk.ControlText(c)}\""); break; }
                case "score": d.AttachInput(); Console.WriteLine("score focus " + d.FocusScore()); break;
                case "file": Console.WriteLine("file dialog " + d.FileDialog(arg)); break;
                case "pshot":
                {
                    var n = 0;
                    foreach (var pw in d.Windows().Where(h => Native.ClassOf(h) == "TTBPopupWindow"))
                    { d.Capture(pw, n == 0 ? arg : arg.Replace(".png", $"_{n}.png")); Console.WriteLine("popup " + pw.ToString("X")); n++; }
                    break;
                }
                case "small": d.Shrink(); break;
                case "target": d.KeyTarget = (IntPtr)Convert.ToInt64(arg, 16); break;
                case "sleep": Thread.Sleep(int.Parse(arg)); break;
                case "focus": { d.AttachInput(); var f = d.Focus(); Console.WriteLine($"focus {f:X} [{Native.ClassOf(f)}]"); break; }
                case "close": Native.PostMessage(d.MainWindow, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); break;
                default: Console.Error.WriteLine("unknown op " + op); return 2;
            }
        }
        return 0;
    }
}
