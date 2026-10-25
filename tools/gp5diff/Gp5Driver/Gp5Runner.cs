using System.Diagnostics;
using System.Text.Json.Nodes;
using static Gp5Driver.Native;

namespace Gp5Driver;

// Owns: running sequence files through one GP5 instance on one hidden desktop: File > New, the actions (keys, menu paths, the
//     dialogs they open), File > Save As into out/<id>.gp5, closing the document; restarting GP5 when it hangs.
// Does not own: the Win32 mechanics (Gp5Desk.cs), the action vocabulary (tools/gp5diff/actions.json).
// Usage: Gp5Driver run <desktop> <dir> <shard>/<shards> [restartEvery] [GP5.exe of this worker]
internal static class Gp5Runner
{
    public static int Run(string[] args)
    {
        var name = args[0];
        var dir = args[1];
        var sh = args[2].Split('/');
        int shard = int.Parse(sh[0]), shards = int.Parse(sh[1]);
        var restartEvery = args.Length > 3 ? int.Parse(args[3]) : 150;
        var exe = args.Length > 4 ? args[4] : null;
        return Program.OnThread(() => new Worker(name, dir, shard, shards, restartEvery, exe).Loop());
    }

    private sealed class Worker(string name, string dir, int shard, int shards, int restartEvery, string? exe)
    {
        private Gp5Desk _d = null!;
        private readonly JsonObject _map = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "actions.json")))!.AsObject();
        private int _sinceStart;
        private readonly string _logDir = Path.Combine(dir, "gp5log");
        private int _shots;

        public int Loop()
        {
            Directory.CreateDirectory(Path.Combine(dir, "out"));
            Directory.CreateDirectory(_logDir);
            if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true);
            StartJudge();
            Directory.CreateDirectory(_workDir);
            var files = Directory.GetFiles(Path.Combine(dir, "seq"), "*.json").OrderBy(f => f, StringComparer.Ordinal).Where((_, i) => i % shards == shard).ToList();
            _d = new Gp5Desk(name, true);
            if (exe is not null) _d.Gp5Exe = exe;
            _d.BindThread();
            int done = 0, failed = 0;
            try
            {
                foreach (var file in files)
                {
                    // <run>/STOP asks every worker to finish its current sequence and close its GP5.
                    if (File.Exists(Path.Combine(dir, "STOP"))) break;
                    var id = Path.GetFileNameWithoutExtension(file);
                    var outPath = Path.Combine(dir, "out", id + ".gp5");
                    if (File.Exists(outPath) || File.Exists(Path.Combine(_logDir, id + ".fail"))) continue;
                    var seq = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                    var log = new List<string>();
                    var sw = Stopwatch.StartNew();
                    var ok = false;
                    for (var attempt = 0; attempt < 2 && !ok; attempt++)
                    {
                        try
                        {
                            EnsureRunning(log);
                            ok = RunOne(seq, outPath, log);
                        }
                        catch (Exception ex) { log.Add("exception " + ex.Message); ok = false; }
                        if (!ok)
                        {
                            log.Add($"attempt {attempt} failed; restarting GP5");
                            // Every failed attempt is a hang or a blocked prompt for the watchdog: the worker's own GP5 is killed and restarted.
                            File.AppendAllText(Path.Combine(dir, "..", "hangs.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {name} {id} attempt {attempt}: {log.LastOrDefault(l => !l.StartsWith("attempt"))}{Environment.NewLine}");
                            Restart(log);
                        }
                    }
                    var entry = new JsonObject { ["id"] = id, ["ok"] = ok, ["ms"] = sw.ElapsedMilliseconds, ["log"] = new JsonArray(log.Select(l => (JsonNode)l).ToArray()) };
                    File.WriteAllText(Path.Combine(_logDir, id + (ok ? ".json" : ".fail")), entry.ToJsonString());
                    if (ok) done++; else failed++;
                    // A GP5 that reported an access violation is not trusted for the next sequence: start a fresh one.
                    if (ok && log.Any(l => l.Contains("gp5_crash"))) Restart(log);
                    if (++_sinceStart >= restartEvery) { Close(log); _sinceStart = 0; }
                }
            }
            finally { Close(new List<string>()); _d.Dispose(); try { _judge?.Kill(); } catch (InvalidOperationException) { } }
            Console.WriteLine($"{name}: {done} done, {failed} failed");
            return 0;
        }

        private void EnsureRunning(List<string> log)
        {
            if (_d.MainWindow != IntPtr.Zero && _d.Pid != 0) return;
            if (_d.MainWindow == IntPtr.Zero)
            {
                _d.Pid = 0;
                _d.Launch();
                for (var i = 0; i < 150 && _d.MainWindow == IntPtr.Zero; i++) Thread.Sleep(200);
                if (_d.MainWindow == IntPtr.Zero) throw new InvalidOperationException("GP5 did not start");
                log.Add("launched GP5 pid " + _d.Pid);
            }
            else { GetWindowThreadProcessId(_d.MainWindow, out var p); _d.Pid = p; }
            _d.AttachInput();
            // Startup prompts: "restore the last backup?" -> No; anything else -> its default.
            for (var i = 0; i < 50; i++)
            {
                Thread.Sleep(100);
                var dl = _d.Dialog();
                if (dl == IntPtr.Zero) { if (i > 10) break; continue; }
                try { if (!_d.ClickButton("No") && !_d.ClickButton("OK")) _d.Key("esc"); } catch (TimeoutException) { Thread.Sleep(1000); }
            }
            _d.Shrink();
        }

        private void Restart(List<string> log)
        {
            _d.Kill();
            Thread.Sleep(1000);
            _d.Pid = 0;
            _sinceStart = 0;
        }

        private void Close(List<string> log)
        {
            if (_d.MainWindow == IntPtr.Zero) return;
            PostMessage(_d.MainWindow, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            for (var i = 0; i < 50 && _d.MainWindow != IntPtr.Zero; i++)
            {
                Thread.Sleep(100);
                if (_d.Dialog() != IntPtr.Zero) _d.ClickButton("No");
            }
            if (_d.MainWindow != IntPtr.Zero) _d.Kill();
            _d.Pid = 0;
        }

        /// <summary>Waits for a dialog (or none) for up to <paramref name="ms"/>.</summary>
        private IntPtr WaitDialog(int ms)
        {
            var end = Environment.TickCount64 + ms;
            do { var dl = _d.Dialog(); if (dl != IntPtr.Zero) return dl; Thread.Sleep(20); } while (Environment.TickCount64 < end);
            return IntPtr.Zero;
        }

        /// <summary>File > Save As through the common dialog (an untitled score's Ctrl+S opens it).</summary>
        private bool SaveAs(string outPath, List<string> log)
        {
            if (File.Exists(outPath)) File.Delete(outPath);
            var save = WaitDialog(10000);
            if (save == IntPtr.Zero || ClassOf(save) != "#32770") { if (_shots++ < 200) _d.Capture(_d.MainWindow, Path.Combine(_logDir, $"nosave_{Path.GetFileName(outPath)}.png")); log.Add("save: no file dialog (" + (save == IntPtr.Zero ? "none" : ClassOf(save)) + ")"); return false; }
            _d.FileDialog(outPath);
            for (var i = 0; i < 100 && (!File.Exists(outPath) || _d.Dialog() != IntPtr.Zero); i++)
            {
                Thread.Sleep(50);
                var dl = _d.Dialog();
                if (dl != IntPtr.Zero && ClassOf(dl) != "#32770") { log.Add("save: dialog " + ClassOf(dl)); Accept(); }
            }
            if (!File.Exists(outPath)) { log.Add("save: file not written"); return false; }
            // GP5 writes the file on its UI thread after the dialog closes: wait until it answers again (the file is complete), and
            // let it finish moving the focus back before giving it to the score (an arrow sent to the form is lost).
            _d.Sync(_d.MainWindow, 20000);
            Thread.Sleep(80);
            _d.Sync(_d.MainWindow, 20000);
            _d.FocusScore();
            return true;
        }

        private string _cur = "";
        private readonly string _workDir = Path.Combine(dir, "work", name);
        private int _fallbacks;
        private readonly Stopwatch _seqClock = new();
        // Side-by-side pictures: <dir>/shots.txt lists "id step" pairs to photograph GP5's window after (into <dir>/side_gp5).
        private readonly HashSet<(string, int)> _shotSteps = File.Exists(Path.Combine(dir, "shots.txt"))
            ? File.ReadAllLines(Path.Combine(dir, "shots.txt")).Select(l => l.Split(' ')).Where(p => p.Length == 2).Select(p => (p[0], int.Parse(p[1]))).ToHashSet()
            : new HashSet<(string, int)>();
        private readonly Stopwatch _actionsMs = new(), _saveMs = new();

        /// <summary>
        /// Writes this step's file with File > Save As into a fresh work file, then copies it. Ctrl+S cannot be used: GP5 writes only
        /// when it counts the score as modified, and typing a fret, an undo or a redo does not set that. Save As keeps the cursor, the
        /// selection, the undo history and a pending two-digit fret. A crash report during the save is logged and accepted.
        /// </summary>
        private bool SaveStep(string copy, int step, bool moveOnly, List<string> log)
        {
            // Ctrl+S first: when GP5 counts the score as modified it writes at once (cheap); otherwise Save As (a file dialog, slow).
            var before = File.GetLastWriteTimeUtc(_cur);
            _d.Key("ctrl+s");
            if (_d.Dialog() == IntPtr.Zero && File.GetLastWriteTimeUtc(_cur) != before) { File.Copy(_cur, copy, true); return true; }
            // Not written after a cursor move, a selection or a copy: GP5 counts every edit those make (new beats or bars) as a
            // modification, so the score is as the file already holds it. Typed frets, undo and redo are the ones it does not count.
            if (_d.Dialog() == IntPtr.Zero && moveOnly) { File.Copy(_cur, copy, true); return true; }
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (_d.Dialog() is var pending && pending != IntPtr.Zero)
                {
                    var ptext = DialogText(pending);
                    log.Add($"{step}: save: {(ptext.Contains("Violation", StringComparison.OrdinalIgnoreCase) ? "gp5_crash" : "dialog")} {ClassOf(pending)} \"{ptext}\"");
                    if (!Accept()) _d.Key("enter");
                    _d.FocusScore();
                }
                _fallbacks++;
                _d.Key("alt+f");
                for (var i = 0; i < 5; i++) _d.Key("down");
                _d.Key("enter");
                var work = Path.Combine(_workDir, Path.GetFileName(Path.GetDirectoryName(copy)!) + $"_{step:000}_{attempt}.gp5");
                if (SaveAs(work, log))
                {
                    File.Delete(_cur);
                    _cur = work;
                    File.Copy(_cur, copy, true);
                    return true;
                }
                var dl = _d.Dialog();
                if (dl == IntPtr.Zero) continue;
                var text = DialogText(dl);
                log.Add($"{step}: save: {(text.Contains("Violation", StringComparison.OrdinalIgnoreCase) ? "gp5_crash" : "dialog")} {ClassOf(dl)} \"{text}\"");
                if (_shots++ < 200) _d.Capture(dl, Path.Combine(_logDir, $"save_{Path.GetFileName(copy)}_{step}_{attempt}.png"));
                if (!_d.ClickButton("Cancel") && !Accept()) _d.Key("esc");
                _d.FocusScore();
            }
            return false;
        }
        private static string DialogText(IntPtr dl) =>
            string.Join(" | ", Children(dl).Where(c => ClassOf(c) is "Static" or "TLabel" or "TStaticText").Select(Gp5Desk.ControlText).Where(t => t.Length > 0));

        /// <summary>
        /// Gives the score's key sink the focus and checks it stays there: after a dialog closes GP5 can move the focus a moment later,
        /// and an arrow key sent to the form instead moves the focus between controls (digits still reach the score).
        /// </summary>
        private bool EnsureScoreFocus()
        {
            for (var i = 0; i < 25; i++)
            {
                var edit = _d.ScoreEdit;
                if (edit != IntPtr.Zero && _d.Focus() == edit && i > 0) return true;
                _d.FocusScore();
                Thread.Sleep(i == 0 ? 0 : 20);
                if (edit != IntPtr.Zero && _d.Focus() == edit && _d.Sync(edit) && _d.Focus() == edit) return true;
            }
            return false;
        }

        private System.Diagnostics.Process? _judge;

        private void StartJudge()
        {
            var script = Path.Combine(AppContext.BaseDirectory, "py", "judge.py");
            const string python = @"C:\Users\COBHC\Downloads\TabForge\work\tools\venv\Scripts\python.exe";
            if (!File.Exists(script) || !File.Exists(python) || !Directory.Exists(Path.Combine(dir, "tf"))) return;
            _judge = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(python, $"\"{script}\" \"{dir}\"")
            { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
        }

        private string Judge(string id, int step, string file)
        {
            if (_judge is null || _judge.HasExited) return "unknown";
            _judge.StandardInput.WriteLine($"{id} {step} {file}");
            _judge.StandardInput.Flush();
            return _judge.StandardOutput.ReadLine() ?? "unknown";
        }

        private bool Popup() => _d.Windows().Any(h => ClassOf(h) == "TTBPopupWindow");

        private bool Accept() => _d.ClickButton("OK") || _d.ClickButton("Yes") || _d.ClickButton("Save");

        private bool RunOne(JsonObject seq, string outPath, List<string> log)
        {
            if (_d.Dialog() != IntPtr.Zero) { log.Add("stray dialog " + ClassOf(_d.Dialog())); if (!_d.ClickButton("Cancel") && !_d.ClickButton("No")) _d.Key("esc"); }
            // File > New: a modified untitled score asks to be saved (No); then the score information dialog (OK).
            _d.FocusScore();
            _d.Key("ctrl+n");
            var sawInfo = false;
            for (var i = 0; i < 20; i++)
            {
                var dl = WaitDialog(sawInfo ? 300 : 1000);
                if (dl == IntPtr.Zero) { if (sawInfo) break; continue; }
                var cls = ClassOf(dl);
                if (cls == "TFPartitionProprietes") { sawInfo = true; if (!_d.ClickButton("OK")) Thread.Sleep(100); }
                else if (!_d.ClickButton("No")) Accept();
                Thread.Sleep(50);
            }
            if (_d.Dialog() != IntPtr.Zero) { log.Add("new: dialog left open " + ClassOf(_d.Dialog())); return false; }
            if (!_d.FocusScore()) { log.Add("new: score focus failed"); return false; }

            // Save As once at the start; after every action Ctrl+S writes the same file and a copy keeps that step's state
            // (saving changes neither the cursor, the selection nor the pending two-digit fret).
            _d.Key("ctrl+s");
            // The score's own file lives in this worker's small work folder: the file dialog opens there quickly (it lists the folder).
            _cur = Path.Combine(_workDir, (string)seq["id"]! + "_init.gp5");
            if (!SaveAs(_cur, log)) return false;
            var stepDir = Path.Combine(dir, "steps", (string)seq["id"]!);
            if (Directory.Exists(stepDir)) foreach (var f in Directory.GetFiles(stepDir)) File.Delete(f);
            Directory.CreateDirectory(stepDir);

            var step = 0;
            _fallbacks = 0; _actionsMs.Reset(); _saveMs.Reset(); _seqClock.Restart();
            foreach (var node in seq["actions"]!.AsArray())
            {
                _actionsMs.Start();
                var a = node!["a"]!.GetValue<string>();
                if (_seqClock.Elapsed > TimeSpan.FromMinutes(3)) { log.Add($"{step}: hang: sequence over 3 minutes"); return false; }
                // A message box that came up late (GP5 reports some of its own errors after the save): logged and accepted.
                for (var n = 0; n < 3 && _d.Dialog() is var late && late != IntPtr.Zero; n++)
                {
                    var text = DialogText(late);
                    log.Add($"{step - 1}: {(text.Contains("Violation", StringComparison.OrdinalIgnoreCase) ? "gp5_crash" : "late dialog")} {ClassOf(late)} \"{text}\"");
                    if (!Accept()) _d.Key("enter");
                    Thread.Sleep(50);
                }
                // GP5 crashed (access violation): its state is no reference any more; end the sequence here, keeping the steps so far.
                if (log.Any(l => l.Contains("gp5_crash"))) { _actionsMs.Stop(); log.Add($"{step}: stopped after a GP5 crash"); break; }
                if (!EnsureScoreFocus())
                {
                    var fw = _d.Focus();
                    log.Add($"{step}: score focus lost (focus {ClassOf(fw)} \"{Gp5Desk.TextOfWindow(fw)}\", dialog {ClassOf(_d.Dialog())}, edit {_d.ScoreEdit != IntPtr.Zero})");
                    if (_shots++ < 200) _d.Capture(_d.Dialog() is var fd && fd != IntPtr.Zero ? fd : _d.MainWindow, Path.Combine(_logDir, $"{seq["id"]}_{step}_focus.png"));
                    if (_d.Dialog() is var fdl && fdl != IntPtr.Zero) log.Add($"{step}: blocking dialog \"{Gp5Desk.TextOfWindow(fdl)}\": {DialogText(fdl)} | buttons {string.Join(",", Children(fdl).Where(c => ClassOf(c).Contains("Button") || ClassOf(c).Contains("Btn")).Select(Gp5Desk.TextOfWindow))}");
                    return false;
                }
                var spec = _map[a]?["gp5"];
                if (spec is null) log.Add($"{step}: skipped {a} (no GP5 mapping)");
                else if (spec["digits"] is not null) foreach (var ch in node["n"]!.GetValue<int>().ToString()) _d.Key(ch.ToString());
                else if (spec["menu"] is JsonArray menu)
                {
                    _d.Key("alt+f");
                    for (var i = 0; i < menu[0]!.GetValue<int>(); i++) _d.Key("right");
                    for (var i = 0; i < menu[1]!.GetValue<int>(); i++) _d.Key("down");
                    _d.Key("enter");
                    for (var i = 0; i < 50 && Popup(); i++) Thread.Sleep(20);
                    if (Popup()) { log.Add($"{step}: menu stayed open after {a}"); _d.Key("esc"); _d.Key("esc"); _d.Key("esc"); }
                }
                else foreach (var chord in spec["keys"]!.AsArray()) _d.Key(chord!.GetValue<string>());
                // Dialogs the action opened: the expected one is accepted with its default; anything else is logged and accepted too.
                for (var n = 0; n < 3; n++)
                {
                    var dl = _d.Dialog();
                    if (dl == IntPtr.Zero) break;
                    var cls = ClassOf(dl);
                    var expected = spec?["dialog"] is not null;
                    var text = DialogText(dl);
                    // GP5's own crash reports (access violations): the comparator ignores this step and everything after it.
                    if (text.Contains("Violation", StringComparison.OrdinalIgnoreCase)) log.Add($"{step}: gp5_crash \"{text}\" after {a}");
                    else log.Add($"{step}: dialog {cls} \"{Gp5Desk.TextOfWindow(dl)}\"{(expected ? "" : " (unexpected)")} after {a}");
                    if (!expected && _shots++ < 200) _d.Capture(dl, Path.Combine(_logDir, $"{seq["id"]}_{step}_{cls}.png"));
                    if (!Accept()) _d.Key("enter");
                    Thread.Sleep(30);
                }
                if (_d.Dialog() is var stuck && stuck != IntPtr.Zero) { log.Add($"{step}: dialog stuck after {a}: {ClassOf(stuck)} \"{Gp5Desk.TextOfWindow(stuck)}\" {DialogText(stuck)}"); if (_shots++ < 200) _d.Capture(stuck, Path.Combine(_logDir, $"{seq["id"]}_{step}_stuck.png")); return false; }
                if (_d.Focus() != _d.ScoreEdit) _d.FocusScore();
                _actionsMs.Stop();
                _saveMs.Start();
                // Snapshots: every action of the first 15, then every 5th and the last (a later divergence is placed in a window of 5).
                var snapshot = _judge is not null || step < 15 || step % 5 == 4 || step == seq["actions"]!.AsArray().Count - 1;
                var saved = !snapshot || SaveStep(Path.Combine(stepDir, $"{step:000}.gp5"), step, a is "left" or "right" or "up" or "down" or "home" or "end" or "first_bar" or "last_bar" or "prev_bar" or "next_bar" or "sel_left" or "sel_right" or "select_all" or "copy", log);
                _saveMs.Stop();
                if (!saved) return false;
                // With the judge (judge.py) every action is saved and the sequence ends at the first difference from TabForge.
                if (snapshot && _judge is not null && Judge((string)seq["id"]!, step, Path.Combine(stepDir, $"{step:000}.gp5")) == "diff" && _shotSteps.Count == 0)
                {
                    log.Add($"{step}: first difference from TabForge; sequence ends here");
                    break;
                }
                if (_shotSteps.Contains(((string)seq["id"]!, step)))
                {
                    Directory.CreateDirectory(Path.Combine(dir, "side_gp5"));
                    Thread.Sleep(150);
                    _d.Capture(_d.MainWindow, Path.Combine(dir, "side_gp5", $"{seq["id"]}_{step:000}.png"));
                }
                step++;
                // After GP5 reports an access violation its state is not a reference any more: stop the sequence (the comparator stops there too).
                if (log.Any(l => l.Contains("gp5_crash"))) { log.Add($"{step - 1}: stopped after a GP5 crash"); break; }
            }
            log.Add($"timing: actions {_actionsMs.ElapsedMilliseconds} ms, saves {_saveMs.ElapsedMilliseconds} ms, save-as fallbacks {_fallbacks}");
            File.Copy(_cur, outPath, true);
            _d.Key("ctrl+f4");
            if (WaitDialog(300) != IntPtr.Zero) { log.Add("close: prompt " + ClassOf(_d.Dialog())); _d.ClickButton("No"); }
            File.Delete(_cur);
            return true;
        }
    }
}
