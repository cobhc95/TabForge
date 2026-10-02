using System.Diagnostics;
using System.IO;
using System.Text;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Diagnostics;

// `--probe-audio <report>`: end-to-end check of the audio engine process with real plug-ins (ReaEQ VST2,
// ValhallaDelay VST3): start, route notes, state round trip, CPU / memory, and recovery from an engine crash.
internal static partial class DiagnosticCommands
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr param);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out WinRect r);
    private delegate bool EnumProc(IntPtr h, IntPtr param);
    private struct WinRect { public int Left, Top, Right, Bottom; }

    /// <summary>Size of a visible top-level window of the engine whose title contains <paramref name="name"/>.</summary>
    private static (int Width, int Height)? EngineWindow(int? pid, string name)
    {
        if (pid is null) return null;
        (int, int)? result = null;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var owner);
            if (owner != pid || !IsWindowVisible(h)) return true;
            var title = new StringBuilder(256);
            GetWindowText(h, title, 256);
            if (!title.ToString().Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
            GetWindowRect(h, out var r);
            result = (r.Right - r.Left, r.Bottom - r.Top);
            return false;
        }, IntPtr.Zero);
        return result;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    /// <summary>Saves a picture of an engine window (PrintWindow, full content) as PNG.</summary>
    private static void SaveWindowImage(int? pid, string name, string path)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var owner);
            if (owner != pid || !IsWindowVisible(h)) return true;
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            if (!t.ToString().Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
            found = h; return false;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero || !GetWindowRect(found, out var r)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var screen = GetDC(IntPtr.Zero); var dc = CreateCompatibleDC(screen); var bmp = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(dc, bmp);
        PrintWindow(found, dc, 2);
        SelectObject(dc, old);
        var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using (var file = File.Create(path)) encoder.Save(file);
        DeleteObject(bmp); DeleteDC(dc); ReleaseDC(IntPtr.Zero, screen);
    }

    private static List<string> EngineWindowTitles(int? pid)
    {
        var titles = new List<string>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var owner);
            if (owner == pid) { var t = new StringBuilder(256); GetWindowText(h, t, 256); titles.Add($"{t} (visible {IsWindowVisible(h)})"); }
            return true;
        }, IntPtr.Zero);
        return titles;
    }

    private static int RunAudioProbe(string[] args)
    {
        if (args.Length < 2) return Usage("--probe-audio <report.txt>");
        return Guard("Audio engine probe", () =>
        {
            var report = new StringBuilder();
            var client = AudioEngineClient.Instance;
            client.WarmIdle = TimeSpan.Zero;   // the probe checks "stopped when no track needs it" and unloads between cases (R-10's warm period is for the app)
            var quarantine = new List<string>();
            client.Quarantine = () => quarantine;
            var crashed = 0;
            var failures = new List<string>();
            client.PluginCrashed += _ => crashed++;
            client.PluginFailed += (path, why) => failures.Add($"{Path.GetFileName(path)}: {why}");
            client.DeviceError += why => failures.Add($"device: {why}");

            var reaEq = @"C:\Program Files\Steinberg\VSTPlugins\ReaPlugs\reaeq-standalone.dll";
            var valhalla = @"C:\Program Files\Common Files\VST3\ValhallaDSP\ValhallaDelay.vst3";
            var song = TemplateFactory.Blank();
            var guitar = song.Tracks[0];
            guitar.Name = "Guitar (ReaEQ)";
            guitar.SoundSource = SoundSources.Plugins;
            if (File.Exists(reaEq)) guitar.Rig.Plugins.Add(new PluginSlot { Name = "ReaEQ", Path = reaEq, Format = "VST2", Type = PluginSlotType.Effect });
            if (File.Exists(valhalla))
            {
                var second = new TrackModel { Name = "Keys (ValhallaDelay)", Kind = TrackKind.Keys, MidiProgram = 0, MidiChannel = 1, SoundSource = SoundSources.Plugins };
                second.Rig.Plugins.Add(new PluginSlot { Name = "ValhallaDelay", Path = valhalla, Format = "VST3", Type = PluginSlotType.Effect, Wet = 60 });
                song.Tracks.Add(second);
            }
            var settings = new PluginSettings();
            // TABFORGE_PROBE_DRIVER=ASIO (and optionally TABFORGE_PROBE_DEVICE) probes another driver than the default.
            if (Environment.GetEnvironmentVariable("TABFORGE_PROBE_DRIVER") is { Length: > 0 } probeDriver) settings.Driver = probeDriver;
            if (Environment.GetEnvironmentVariable("TABFORGE_PROBE_DEVICE") is { Length: > 0 } probeDevice) settings.Device = probeDevice;
            if (Environment.GetEnvironmentVariable("TABFORGE_PROBE_FOLLOW") == "1") settings.FollowWindowsVolume = true;
            if (int.TryParse(Environment.GetEnvironmentVariable("TABFORGE_PROBE_BUFFER"), out var probeBuffer)) settings.BufferSize = probeBuffer;
            if (Environment.GetEnvironmentVariable("TABFORGE_PROBE_MONO") == "1") { settings.AsioInputChannel = 1; settings.AsioInputLastChannel = 1; settings.AsioOutputChannel = 0; settings.AsioOutputLastChannel = 0; }
            var memoryBefore = Process.GetCurrentProcess().WorkingSet64;

            bool WaitFor(Func<bool> condition, int ms)
            {
                var sw = Stopwatch.StartNew();
                while (!condition() && sw.ElapsedMilliseconds < ms)
                {
                    Thread.Sleep(20);
                    // Engine events are posted to this (UI) thread: let them run.
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                }
                return condition();
            }

            var started = Stopwatch.StartNew();
            client.Sync(song.Tracks, settings);
            var ready = WaitFor(() => client.DeviceDescription is not null, 8000);
            report.AppendLine($"engine running: {client.IsRunning}, ready: {ready} in {started.ElapsedMilliseconds} ms, output: {client.DeviceDescription}");
            report.AppendLine($"output details: {client.Output}");
            report.AppendLine($"latency (Windows MIDI is delayed by this): {client.LatencyTicks * 1000.0 / Stopwatch.Frequency:0.0} ms");
            var slots = song.Tracks.Select(client.SlotOf).ToList();
            report.AppendLine($"slots: {string.Join(", ", song.Tracks.Select((t, i) => $"{t.Name}={slots[i]} route={MixerGroups.RouteOf(t, client.Mixer)}"))}");
            WaitFor(() => false, 800); // plug-ins load on the engine's main thread

            // A few quiet notes on each routed track.
            for (var n = 0; n < 4; n++)
                foreach (var slot in slots.Where(s => s >= 0))
                {
                    var now = Stopwatch.GetTimestamp();
                    client.Write(new TimedMidi { Timestamp = now, Slot = slot, Status = 0x90, Data1 = (byte)(60 + n * 4), Data2 = 45 });
                    client.Write(new TimedMidi { Timestamp = now + Stopwatch.Frequency / 5, Slot = slot, Status = 0x80, Data1 = (byte)(60 + n * 4), Data2 = 0 });
                }
            WaitFor(() => false, 1200);
            report.AppendLine($"audio callback CPU load: {client.CpuLoad:P1} of the block time");
            if (client.EngineProcessId is int pid)
            {
                using var engineProcess = Process.GetProcessById(pid);
                engineProcess.Refresh();
                report.AppendLine($"engine process memory: {engineProcess.WorkingSet64 / 1024 / 1024} MB working set");
            }
            report.AppendLine($"editor process memory change: {(Process.GetCurrentProcess().WorkingSet64 - memoryBefore) / 1024 / 1024} MB");

            // Plug-in windows: open each plug-in's own editor and look for its window in the engine process.
            foreach (var track in song.Tracks)
                foreach (var plugin in track.Rig.Plugins)
                {
                    client.OpenEditor(track, plugin, IntPtr.Zero, dark: true);
                    var found = WaitFor(() => EngineWindow(client.EngineProcessId, plugin.Name) is not null, 4000);
                    var size = EngineWindow(client.EngineProcessId, plugin.Name);
                    report.AppendLine($"editor window of {plugin.Name}: {(found && size is { } s ? $"open, {s.Width}x{s.Height}" : "NOT FOUND")}");
                    if (found)
                    {
                        WaitFor(() => false, 800); // let the plug-in paint
                        SaveWindowImage(client.EngineProcessId, plugin.Name, Path.ChangeExtension(args[1], null) + $"-{plugin.Name}.png");
                    }
                    if (!found) report.AppendLine($"   engine pid {client.EngineProcessId}; its windows: {string.Join(" | ", EngineWindowTitles(client.EngineProcessId))}");
                    if (!found) failures.Add($"{plugin.Name} editor did not open");
                }

            // Docked: the first plug-in's window inside a TabForge window (as in the FX chain window).
            if (song.Tracks[0].Rig.Plugins.FirstOrDefault() is { } dockedPlugin)
            {
                var dock = new Views.PluginDockHost();
                var host = new System.Windows.Window { Title = "Docked probe", Width = 1100, Height = 800, Content = dock, Left = 40, Top = 40, ShowActivated = false };
                host.Show();
                WaitFor(() => dock.Area != IntPtr.Zero, 2000);
                var sized = (0, 0);
                void OnSized(int s, int i, int w, int h) => sized = (w, h);
                client.EditorSized += OnSized;
                client.OpenEditor(song.Tracks[0], dockedPlugin, dock.Area, dark: true, docked: true);
                var ok = WaitFor(() => sized.Item1 > 0, 4000);
                WaitFor(() => false, 800);
                var shot = Path.ChangeExtension(args[1], null) + "-docked.png";
                SaveWindowImage(Environment.ProcessId, "Docked probe", shot);
                report.AppendLine($"docked editor of {dockedPlugin.Name}: {(ok ? $"open inside TabForge, {sized.Item1}x{sized.Item2}" : "NOT OPENED")}");
                if (!ok) failures.Add($"{dockedPlugin.Name} did not dock");
                client.CloseEditor(song.Tracks[0], dockedPlugin);
                client.EditorSized -= OnSized;
                host.Close();
            }

            client.CollectStates(song.Tracks, 3000);
            foreach (var plugin in song.Tracks.SelectMany(t => t.Rig.Plugins))
                report.AppendLine($"state of {plugin.Name}: {(plugin.State is null ? "none" : $"{plugin.State.Length} chars")}");

            // Crash recovery: end the engine as a crashing plug-in would, then sync again (what the app does).
            client.KillEngineForTest();
            WaitFor(() => !client.IsRunning, 3000);
            WaitFor(() => crashed > 0, 2000);
            report.AppendLine($"after engine crash: running={client.IsRunning}, crash reported={crashed > 0}");
            var restart = Stopwatch.StartNew();
            client.Sync(song.Tracks, settings);
            var back = WaitFor(() => client.DeviceDescription is not null && client.IsRunning, 8000);
            report.AppendLine($"restarted: {back} in {restart.ElapsedMilliseconds} ms");

            client.Sync(Array.Empty<TrackModel>(), settings);
            report.AppendLine($"stopped when no track needs it: {!client.IsRunning}");

            // A real song's playback into a VST instrument (Nexus when installed): the path the app uses —
            // document playback → routed MIDI output → engine → instrument — must produce sound on that track.
            var nexus = @"C:\Program Files\Steinberg\VSTPlugins\Nexus.dll";
            if (File.Exists(nexus))
            {
                var doc = new Documents.DocumentSession();
                var played = TemplateFactory.Blank();
                var lead = played.Tracks[0];
                lead.Name = "Lead (Nexus)";
                lead.SoundSource = SoundSources.Plugins;
                lead.MidiSound = false;
                lead.Rig.Plugins.Add(new PluginSlot { Name = "Nexus", Path = nexus, Format = "VST2", Type = PluginSlotType.Instrument });
                foreach (var cell in lead.Measures[0].Cells.Take(8)) cell.Notes.Add(new TabNote { StringIndex = 2, Fret = 5, MidiValue = 60 });
                doc.Project = played;
                AudioRouting.Apply(played, doc.Playback.Routing, client, settings);
                WaitFor(() => client.DeviceDescription is not null, 8000);
                WaitFor(() => false, 3000); // Nexus loads its sounds
                var peak = 0f;
                doc.Playback.Engine.Start(played, new Playback.PlaybackOptions(), _ => { }, () => { });
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 2500) { WaitFor(() => false, 50); peak = Math.Max(peak, client.PeakOf(lead)); }
                doc.Playback.Engine.Stop();
                report.AppendLine($"song playback into Nexus: routed={doc.Playback.Routing is not null}, peak level {peak:0.000} ({(peak > 0.001 ? "sound" : "SILENT")})");
                if (peak <= 0.001) failures.Add("Nexus received no MIDI from playback");

                // Live settings survive chain changes: pick another Nexus preset, then add an effect while it plays.
                // The running Nexus must be kept (same settings), and the change must not stall.
                var dominator = @"C:\Program Files\Steinberg\VSTPlugins\Audio Assault\Dominator.dll";
                if (File.Exists(dominator))
                {
                    client.SetProgram(lead, lead.Rig.Plugins[0], 7);
                    WaitFor(() => false, 1500);
                    client.CollectStates(new[] { lead }, 5000);
                    var before = lead.Rig.Plugins[0].State;
                    doc.Playback.Engine.Start(played, new Playback.PlaybackOptions(), _ => { }, () => { });
                    WaitFor(() => false, 500);
                    var add = Stopwatch.StartNew();
                    lead.Rig.Plugins.Add(new PluginSlot { Name = "Dominator", Path = dominator, Format = "VST2", Type = PluginSlotType.Effect });
                    AudioRouting.Apply(played, doc.Playback.Routing, client, settings);
                    client.CollectStates(new[] { lead }, 8000);   // answered once the new chain is in place
                    var addMs = add.ElapsedMilliseconds;
                    var after = lead.Rig.Plugins[0].State;
                    var peak2 = 0f;
                    var sw2 = Stopwatch.StartNew();
                    while (sw2.ElapsedMilliseconds < 1500) { WaitFor(() => false, 50); peak2 = Math.Max(peak2, client.PeakOf(lead)); }
                    doc.Playback.Engine.Stop();
                    var same = before is not null && before == after;
                    report.AppendLine($"add Dominator while Nexus plays: {addMs} ms, Nexus settings kept: {same} (state {before?.Length ?? 0} chars), sound after: {peak2:0.000}");
                    if (!same) failures.Add("adding a plug-in reset the running Nexus");
                    if (addMs > 5000) failures.Add($"adding a plug-in stalled for {addMs} ms");
                }
                client.Sync(Array.Empty<TrackModel>(), settings);
            }

            // TABFORGE_PROBE_PLUGINS=1: every plug-in of the plug-in folders, one at a time, as a real chain in the engine.
            if (Environment.GetEnvironmentVariable("TABFORGE_PROBE_PLUGINS") == "1")
            {
                var roots = new[] { @"C:\Program Files\Steinberg", @"C:\Program Files\Common Files\VST3" };
                var found = VstScannerService.Scan(roots, System.Threading.CancellationToken.None);
                report.AppendLine($"plug-ins found: {found.Count}");
                var rows = new List<(long Ms, string Line)>();
                foreach (var info in found)
                {
                    var one = TemplateFactory.Blank();
                    one.Tracks[0].SoundSource = SoundSources.Plugins;
                    one.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = info.Name, Path = info.Path, Format = info.Format, Type = info.Role == "Instrument" ? PluginSlotType.Instrument : PluginSlotType.Effect });
                    string? failed = null; var loaded = false;
                    void OnFail(string p, string why) => failed = why;
                    void OnLoad() => loaded = true;
                    client.PluginFailed += OnFail; client.ChainLoaded += OnLoad;
                    var sw = Stopwatch.StartNew();
                    client.Sync(one.Tracks, settings);
                    WaitFor(() => loaded || failed is not null, 30000);
                    var ms = sw.ElapsedMilliseconds;
                    client.PluginFailed -= OnFail; client.ChainLoaded -= OnLoad;
                    rows.Add((ms, $"{ms,6} ms  {(failed is null ? (loaded ? "ok  " : "TIMEOUT") : "FAIL")}  {info.Format} {info.Name}{(failed is null ? "" : "  -> " + failed)}"));
                    client.Sync(Array.Empty<TrackModel>(), settings);   // unload (the engine stops) before the next
                    WaitFor(() => !client.IsRunning, 3000);
                }
                foreach (var row in rows.OrderByDescending(r => r.Ms)) report.AppendLine(row.Line);
                report.AppendLine($"plug-in probe: {rows.Count} plug-ins, total {rows.Sum(r => r.Ms)} ms, slowest {rows.Max(r => r.Ms)} ms");
            }

            // Real input: arm a track, watch the input meter, record 1.5 s and check the take arrives.
            {
                var inputSong = TemplateFactory.Blank();
                var armed = inputSong.Tracks[0];
                armed.RecordArm = true;
                string? inputError = null; var takes = new List<(string File, double Start, double Length)>();
                client.InputError += why => inputError = why;
                client.Recorded += (_, file, start, length) => takes.Add((file, start, length));
                client.Sync(inputSong.Tracks, settings);
                WaitFor(() => client.IsRunning && client.DeviceDescription is not null, 8000);
                var inputPeak = 0f;
                var swIn = Stopwatch.StartNew();
                while (swIn.ElapsedMilliseconds < 1200) { WaitFor(() => false, 40); inputPeak = Math.Max(inputPeak, client.InputPeakOf(armed)); }
                var folder = Path.Combine(Path.GetTempPath(), "tabforge-probe-recordings");
                var recordStarted = client.StartRecording(inputSong.Tracks, folder);
                client.SetPosition(true, 0, Stopwatch.GetTimestamp());
                WaitFor(() => false, 1500);
                client.StopRecording();
                WaitFor(() => takes.Count > 0 || inputError is not null, 5000);
                var length = takes.Count > 0 ? takes[0].Length : 0;
                var fileOk = takes.Count > 0 && File.Exists(takes[0].File) && new FileInfo(takes[0].File).Length > 1000;
                report.AppendLine($"input: opened without error={inputError is null}{(inputError is null ? "" : " (" + inputError + ")")}, record started={recordStarted}, take {length:0.00} s, file ok={fileOk}, input peak {inputPeak:0.000}");
                if (inputError is not null) failures.Add("input: " + inputError);
                else if (!fileOk || length < 1.0) failures.Add($"recording produced no usable take ({length:0.00} s)");
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { }
                client.Sync(Array.Empty<TrackModel>(), settings);
            }

            // Crash tests with the deliberately faulty plug-in (native\crashtest), when its path is given.
            var crashTestsPassed = true;
            if (args.Length > 2 && File.Exists(args[2]))
            {
                var faulty = args[2];
                string? lastCrash = null;
                client.PluginCrashed += path => lastCrash = path;
                SongProject FaultySong()
                {
                    var s = TemplateFactory.Blank();
                    s.Tracks[0].Name = "Faulty";
                    s.Tracks[0].SoundSource = SoundSources.Plugins;
                    s.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "CrashTest", Path = faulty, Format = "VST2", Type = PluginSlotType.Effect });
                    return s;
                }
                bool RunCase(string title, string mode, bool isolated, int waitMs)
                {
                    quarantine.Clear();
                    client.ResetCrashCountForTest();
                    lastCrash = null;
                    Environment.SetEnvironmentVariable("TF_CRASHTEST_MODE", mode); // inherited by the engine / plug-in processes
                    var s = FaultySong();
                    var caseSettings = new PluginSettings { SeparateProcessPerPlugin = isolated };
                    client.Sync(s.Tracks, caseSettings);
                    var caught = WaitFor(() => lastCrash is not null, waitMs);
                    var named = lastCrash is not null && string.Equals(lastCrash, faulty, StringComparison.OrdinalIgnoreCase);
                    var switchedOff = quarantine.Contains(faulty, StringComparer.OrdinalIgnoreCase);
                    if (!client.IsRunning && !client.TooManyCrashes) client.Sync(s.Tracks, caseSettings); // what the app does
                    var recovered = WaitFor(() => client.IsRunning, 5000);
                    report.AppendLine($"{title}: crash caught={caught}, plug-in named={named}, switched off={switchedOff}, engine running afterwards={recovered}");
                    client.Sync(Array.Empty<TrackModel>(), caseSettings);
                    return caught && named && switchedOff && recovered;
                }
                crashTestsPassed &= RunCase("shared engine, plug-in crashes while playing", "process", isolated: false, 8000);
                crashTestsPassed &= RunCase("own process, plug-in crashes while playing", "process", isolated: true, 8000);
                crashTestsPassed &= RunCase("shared engine, plug-in freezes the audio thread", "hang", isolated: false, 12000);
                Environment.SetEnvironmentVariable("TF_CRASHTEST_MODE", null);
            }
            else report.AppendLine("crash tests skipped (no faulty test plug-in given)");
            report.AppendLine(failures.Count == 0 ? "no plug-in or device failures" : "failures: " + string.Join("; ", failures));
            File.WriteAllText(args[1], report.ToString());
            return ready && back && crashTestsPassed && failures.Count == 0 ? Ok : 1;
        });
    }
}
