using System.Text.Json;
using FlaUI.Core.AutomationElements;

namespace ParityRecorder;

/// <summary>
/// Runs the TabForge sessions and collects the text files. Every session is off-screen (the app's capture placement):
/// one for the main window and three tool windows, one for the Shortcuts window (it shares its title with Preferences,
/// so it gets its own process), one for the function scenarios (UI Automation patterns only), and one on-screen live
/// session with real mouse and keyboard input, which is run only inside the test VM.
/// Each session is killed when its work is done.
/// Owns: the capture scripts, the scratch folder and the file set. Does not own: the text formats (TreeDump, HitMap, Scenarios, LiveScenarios, FunctionScenarios).
/// </summary>
internal static class Recorder
{
    /// <summary>The main window plus three tool windows, opened together so their trees come from one state. The wait keeps the session alive until it is killed.</summary>
    private const string MainScript =
        "[{\"window\":\"Mixer\"},{\"window\":\"Preferences\"},{\"window\":\"SongInfo\"},{\"wait\":600000}]";

    private const string ShortcutsScript = "[{\"window\":\"Shortcuts\"},{\"wait\":600000}]";

    /// <summary>Records every file from one build. <paramref name="scratchRoot"/> holds the scratch profiles (default: a new folder in %TEMP%, deleted afterwards).</summary>
    /// With <paramref name="keepScratch"/>, the scratch folder (profiles with their Diagnostics crash logs) is kept and its path printed.
    public static SortedDictionary<string, string> RecordAll(string exe, bool keepScratch = false, bool functionsOnly = false, string? scratchRoot = null)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        // A fresh folder per run, so concurrent runs never share scratch profiles; only this run's folder is deleted.
        var created = scratchRoot is null;
        var scratch = scratchRoot ?? Path.Combine(Path.GetTempPath(), "tf-parity-run-" + Guid.NewGuid().ToString("N"));
        try
        {
            return RecordInto(exe, scratch, files, functionsOnly);
        }
        finally
        {
            if (keepScratch) Console.WriteLine($"SCRATCH kept: {scratch}");
            else if (created) TryDelete(scratch);
        }
    }

    private static SortedDictionary<string, string> RecordInto(string exe, string scratch, SortedDictionary<string, string> files, bool functionsOnly)
    {
        var outDir = Directory.CreateDirectory(Path.Combine(scratch, "out")).FullName;
        var mainScript = Path.Combine(scratch, "main.json");
        var shortcutsScript = Path.Combine(scratch, "shortcuts.json");
        File.WriteAllText(mainScript, MainScript);
        File.WriteAllText(shortcutsScript, ShortcutsScript);

        // With functionsOnly (a diagnosis run), only the function session is recorded.
        if (functionsOnly)
        {
            RecordFunctions(exe, scratch, files);
            return files;
        }
        RecordMainCapture(exe, Directory.CreateDirectory(Path.Combine(scratch, "profile-main")).FullName, mainScript, outDir, files);
        RecordShortcuts(exe, Directory.CreateDirectory(Path.Combine(scratch, "profile-shortcuts")).FullName, shortcutsScript, outDir, files);
        RecordLive(exe, Directory.CreateDirectory(Path.Combine(scratch, "live-profile")).FullName, files);
        RecordFunctions(exe, scratch, files);
        return files;
    }

    /// <summary>
    /// The function scenarios: an off-screen capture session that opens a scratch copy of the demo song (kept under the
    /// capture output folder, which the app allows to open). Only UI Automation patterns are used; no input is sent.
    /// </summary>
    private static void RecordFunctions(string exe, string scratch, IDictionary<string, string> files)
    {
        var outDir = Directory.CreateDirectory(Path.Combine(scratch, "fn-out")).FullName;
        var demo = FindDemo();
        var copy = Path.Combine(outDir, Path.GetFileName(demo));
        File.Copy(demo, copy, overwrite: true);
        var script = Path.Combine(scratch, "fn.json");
        File.WriteAllText(script, "[{\"open\":" + JsonSerializer.Serialize(copy) + "},{\"wait\":600000}]");
        var profile = Directory.CreateDirectory(Path.Combine(scratch, "fn-profile")).FullName;
        using var tf = TabForgeProcess.Capture(exe, profile, script, outDir);
        files["scenarios-function.txt"] = FunctionScenarios.Run(tf, files);
    }

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { } // Not logged: a file still held by an ending process stays in %TEMP%.
        catch (UnauthorizedAccessException) { } // Not logged: as above.
    }

    /// <summary>The bundled demo song: the "samples" folder found by searching up from the current folder (the repository root).</summary>
    private static string FindDemo()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var up = 0; up < 8 && dir is not null; up++, dir = dir.Parent)
        {
            var samples = Path.Combine(dir.FullName, "samples");
            if (!Directory.Exists(samples)) continue;
            var file = Directory.EnumerateFiles(samples, "*Ashen Meridian*.gp").FirstOrDefault();
            if (file is not null) return file;
        }
        throw new FileNotFoundException("The demo song (samples/*Ashen Meridian*.gp) was not found above the current folder.");
    }

    private static void RecordMainCapture(string exe, string profile, string script, string outDir, IDictionary<string, string> files)
    {
        using var tf = TabForgeProcess.Capture(exe, profile, script, outDir);
        var windows = tf.WaitForWindows(ws => ws.Count >= 4);
        var mainHwnd = MainHandleOf(tf, windows);
        var main = windows.First(w => ElementText.HandleOf(w) == mainHwnd);
        files["tree-main.txt"] = TreeDump.File("main", main, mainHwnd);
        files["tree-tool-mixer.txt"] = ToolTree(windows, mainHwnd, "Mixer");
        files["tree-tool-preferences.txt"] = ToolTree(windows, mainHwnd, "TabForge Settings");
        files["tree-tool-songinfo.txt"] = ToolTree(windows, mainHwnd, "Score information");
        files["scenarios.txt"] = Scenarios.RunMain(tf, main, mainHwnd, windows);
    }

    /// <summary>The Shortcuts window alone: its title is the same as Preferences', so it is never matched against it.</summary>
    private static void RecordShortcuts(string exe, string profile, string script, string outDir, IDictionary<string, string> files)
    {
        using var tf = TabForgeProcess.Capture(exe, profile, script, outDir);
        var windows = tf.WaitForWindows(ws => ws.Count >= 2);
        var mainHwnd = MainHandleOf(tf, windows);
        files["tree-tool-shortcuts.txt"] = ToolTree(windows, mainHwnd, null);
    }

    /// <summary>The on-screen session with real mouse and keyboard input; run it only inside the test VM (tests/parity/vm).</summary>
    private static void RecordLive(string exe, string profile, IDictionary<string, string> files)
    {
        using var tf = TabForgeProcess.Live(exe, profile);
        tf.WaitForWindows(ws => ws.Count >= 1);
        files["scenarios-live.txt"] = LiveScenarios.Run(tf, files);
    }

    /// <summary>The tree of the tool window whose title contains <paramref name="keyword"/> (any non-main window when null).</summary>
    private static string ToolTree(IReadOnlyList<AutomationElement> windows, IntPtr mainHwnd, string? keyword)
    {
        var tool = windows.FirstOrDefault(w => ElementText.HandleOf(w) != mainHwnd
            && (keyword is null || ElementText.NameOf(w).Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        return tool is null
            ? $"# tool window not found: {keyword ?? "any"}; windows open: {string.Join(" | ", windows.Select(w => TreeDump.Clean(ElementText.NameOf(w))))}{Environment.NewLine}"
            : TreeDump.File("tool", tool, ElementText.HandleOf(tool));
    }

    /// <summary>The process's main window when Windows reports one, else the largest top-level window of the process.</summary>
    private static IntPtr MainHandleOf(TabForgeProcess tf, IReadOnlyList<AutomationElement> windows)
    {
        var reported = tf.MainHandle();
        if (reported != IntPtr.Zero && windows.Any(w => ElementText.HandleOf(w) == reported)) return reported;
        return windows.OrderByDescending(w => { var b = ElementText.BoundsOf(w); return b.Width * b.Height; })
            .Select(ElementText.HandleOf).First();
    }
}
