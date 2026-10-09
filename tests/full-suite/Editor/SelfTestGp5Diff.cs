using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.EffectEditors;
using TabForge.Views.Score;

namespace TabForge;

// Owns: the TabForge side of the GP5 differential harness (tools/gp5diff/README.md): replays action sequences through the window's
//     key routing on a fresh one-bar song, imports the files GP5 saved for the same sequences, and writes both as normalized JSON
//     (one shape for both, made by GdDump) for the comparator. Without TABFORGE_GP5DIFF_DIR it checks itself on a short sequence.
// Does not own: the GP5 driver, the comparator, the generator (tools/gp5diff), product behaviour.
// Tests: TestGp5DiffReplay.
public static partial class SelfTest
{
    private static void TestGp5DiffReplay()
    {
        var dir = Environment.GetEnvironmentVariable("TABFORGE_GP5DIFF_DIR");
        if (string.IsNullOrEmpty(dir)) { GdSelfCheck(); return; }
        if (Environment.GetEnvironmentVariable("TABFORGE_GP5DIFF_MODE") == "import") GdImportAll(dir);
        else GdReplayAll(dir);
    }

    /// <summary>A short sequence in, the expected bar out: frets, a duration change, a dot, a rest and undo through the real key routing.</summary>
    private static void GdSelfCheck()
    {
        var map = JsonNode.Parse(GdDefaultMap)!.AsObject();
        var seq = JsonNode.Parse("""{"id":"self","actions":[{"a":"fret","n":5},{"a":"right"},{"a":"fret","n":12},{"a":"shorter"},{"a":"right"},{"a":"rest"},{"a":"dot"},{"a":"right"},{"a":"fret","n":3},{"a":"undo"}]}""")!.AsObject();
        var w = SmNewWindow();
        try
        {
            var (result, log) = GdReplay(w, map, seq);
            var beats = result["bars"]![0]!["beats"]!.AsArray();
            var text = string.Join(" ", beats.Select(b => b!["rest"]!.GetValue<bool>() ? $"r/{b["d"]}{new string('.', b["dots"]!.GetValue<int>())}"
                : string.Join(",", b["notes"]!.AsArray().Select(n => $"{n!["f"]}@{n["s"]}")) + $"/{b["d"]}"));
            // The bar's automatic rest fill after the last entered beat, and whether "." dotted the rest, are not part of the check.
            Check("gp5diff: replay of a short sequence", text.StartsWith("5@1/4 12@1/8 r/8", StringComparison.Ordinal), text);
            Check("gp5diff: the replay logs no unhandled action", !log.Any(l => l.StartsWith("unhandled")), string.Join("; ", log));
        }
        finally { SmCloseWindow(w); }
    }

    // The mapping the self-check uses when no actions.json is given (the subset it needs).
    private const string GdDefaultMap = """
        {"fret":{"tf":{"digits":true}},"right":{"tf":{"keys":["right"]}},"shorter":{"tf":{"keys":["numadd"]}},
         "rest":{"tf":{"keys":["r"]}},"dot":{"tf":{"keys":["period"]}},"undo":{"tf":{"keys":["ctrl+z"]}}}
        """;

    private static void GdReplayAll(string dir)
    {
        var map = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "actions.json")))!.AsObject();
        var (shard, shards) = GdShard();
        // TABFORGE_GP5DIFF_TF names the output folder (default "tf"): a newer build can replay into its own folder against the same GP5 results.
        var tfDir = Path.Combine(dir, Environment.GetEnvironmentVariable("TABFORGE_GP5DIFF_TF") is { Length: > 0 } tfName ? tfName : "tf");
        Directory.CreateDirectory(tfDir);
        var files = Directory.GetFiles(Path.Combine(dir, "seq"), "*.json").OrderBy(f => f, StringComparer.Ordinal).Where((_, i) => i % shards == shard).ToList();
        MainWindow? w = null;
        int done = 0, failed = 0, sinceNew = 0;
        try
        {
            foreach (var file in files)
            {
                var outPath = Path.Combine(tfDir, Path.GetFileName(file));
                if (File.Exists(outPath)) continue;
                if (w is null || sinceNew++ >= 25) { if (w is not null) { try { SmCloseWindow(w); } catch (Exception) { } } w = SmNewWindow(); sinceNew = 0; }
                var seq = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                JsonObject result;
                List<string> log;
                var progress = new int[] { -1 };
                try { (result, log) = GdReplay(w, map, seq, progress); }
                catch (Exception ex) { result = new JsonObject(); log = new List<string> { $"{progress[0]}: exception (outside the action, while the window settled) " + ex.GetType().Name + ": " + ex.Message + " at " + string.Join(" < ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim())) }; failed++; }
                // A sequence that threw can leave the window in a broken state: the next one starts in a fresh window.
                if (log.Any(l => l.Contains("exception"))) sinceNew = int.MaxValue - 1;
                result["id"] = seq["id"]!.GetValue<string>();
                result["log"] = new JsonArray(log.Select(l => (JsonNode)l).ToArray());
                File.WriteAllText(outPath, result.ToJsonString());
                done++;
            }
        }
        finally { if (w is not null) SmCloseWindow(w); }
        Log.Add($"  info  gp5diff replay: shard {shard}/{shards}, {done} sequences replayed, {failed} threw");
        Check("gp5diff: replay ran", true);
    }

    private static void GdImportAll(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "gp5"));
        var (shard, shards) = GdShard();
        int done = 0, failed = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(dir, "out"), "*.gp5").OrderBy(f => f, StringComparer.Ordinal).Where((_, i) => i % shards == shard))
        {
            var outPath = Path.Combine(dir, "gp5", Path.GetFileNameWithoutExtension(file) + ".json");
            if (File.Exists(outPath) && File.GetLastWriteTimeUtc(outPath) >= File.GetLastWriteTimeUtc(file)) continue;
            var id = Path.GetFileNameWithoutExtension(file);
            var result = GdImport(file);
            if (result["error"] is not null) failed++;
            // The file GP5 saved after each action (steps/<id>/<n>.gp5), in action order.
            var stepDir = Path.Combine(dir, "steps", id);
            if (Directory.Exists(stepDir))
                result["steps"] = new JsonArray(Directory.GetFiles(stepDir, "*.gp5").OrderBy(f => f, StringComparer.Ordinal).Select(f => (JsonNode)GdImport(f)).ToArray());
            result["id"] = id;
            File.WriteAllText(outPath, result.ToJsonString());
            done++;
        }
        Log.Add($"  info  gp5diff import: {done} files, {failed} failed");
        Check("gp5diff: import ran", true);
    }

    private static JsonObject GdImport(string file)
    {
        try { return GdDump(GuitarProImporter.Import(file)); }
        catch (Exception ex) { return new JsonObject { ["error"] = ex.GetType().Name + ": " + ex.Message }; }
    }

    private static (int Shard, int Shards) GdShard()
    {
        var s = Environment.GetEnvironmentVariable("TABFORGE_GP5DIFF_SHARD") ?? "0/1";
        var parts = s.Split('/');
        return (int.Parse(parts[0]), Math.Max(1, int.Parse(parts[1])));
    }

    /// <summary>A song like GP5's File > New: one track, standard tuning, 120 bpm, one empty 4/4 bar.</summary>
    private static SongProject GdSong()
    {
        var song = new SongProject { Tempo = 120 };
        song.Tracks.Add(new TrackModel { Name = "Track 1", Measures = TemplateFactory.Measures(1) });
        song.IsDirty = false;
        return song;
    }

    /// <summary>Replays one sequence in a new tab of the window and returns the song's dump and a log of what each action did.</summary>
    private static (JsonObject Result, List<string> Log) GdReplay(MainWindow w, JsonObject map, JsonObject seq, int[]? progress = null)
    {
        var log = new List<string>();
        ClipboardService.Compose(null);   // a fresh in-memory clipboard per sequence: parallel replays must not share the system clipboard
        var doc = SmOpenSong(w, GdSong());
        var ed = SmField<TabEditorControl>(w, "Editor")!;
        ed.SelectedTrackIndex = 0;
        ed.AutoAdvanceAfterEntry = false;
        ed.SelectForEdit(0, 0, 0);
        SmCall(w, "ToolsPaletteButton_Click", new Button { Tag = "duration:quarter" }, new RoutedEventArgs());
        SmSettle();
        var hotkeys = SmField<HotkeyMaps>(w, "_hotkeys")!;
        var previousCapture = DialogHost.Capture;
        var previousMessage = DialogHost.MessageCapture;
        var step = 0;
        var steps = new JsonArray();
        // Side-by-side pictures: <dir>/shots.txt lists "id step" pairs to photograph the score after (written to <dir>/side_tf).
        var shotDir = Environment.GetEnvironmentVariable("TABFORGE_GP5DIFF_DIR") is { Length: > 0 } root ? Path.Combine(root, "side_tf") : null;
        var shots = shotDir is null || !File.Exists(Path.Combine(shotDir, "..", "shots.txt")) ? new HashSet<int>()
            : File.ReadAllLines(Path.Combine(shotDir, "..", "shots.txt")).Select(l => l.Split(' ')).Where(p => p.Length == 2 && p[0] == seq["id"]!.GetValue<string>())
                .Select(p => int.Parse(p[1])).ToHashSet();
        if (shots.Count > 0) { Directory.CreateDirectory(shotDir!); SmCall(w, "SetPaper", false); }
        DialogHost.Capture = d => GdAnswer(d, log, step);
        DialogHost.MessageCapture = (caption, text) => log.Add($"{step}: message {caption}: {text}");
        try
        {
            foreach (var node in seq["actions"]!.AsArray())
            {
                var a = node!["a"]!.GetValue<string>();
                var spec = map[a]?["tf"];
                if (progress is not null) progress[0] = step;
                try
                {
                if (spec is null) log.Add($"{step}: skipped {a} (no TabForge mapping)");
                else if (spec["digits"] is not null)
                {
                    GdResetDigits(ed);   // a separate fret action is typed after a pause: it never extends the previous number
                    foreach (var ch in node["n"]!.GetValue<int>().ToString())
                        GdKey(w, ed, hotkeys, Key.D0 + (ch - '0'), ModifierKeys.None, log, step, a);
                }
                else if (spec["cmd"] is { } cmd)
                {
                    if (!(bool)SmCall(w, "RunHotkey", cmd.GetValue<string>())!) log.Add($"{step}: unhandled command {cmd}");
                    SmSettle();
                }
                else foreach (var chord in spec["keys"]!.AsArray())
                {
                    var (key, mods) = GdParse(chord!.GetValue<string>());
                    GdKey(w, ed, hotkeys, key, mods, log, step, a);
                }
                }
                catch (Exception ex)
                {
                    // A TabForge exception is a finding: logged with where it was thrown, and the replay goes on as the app would.
                    log.Add($"{step}: exception {ex.GetType().Name}: {ex.Message} at {string.Join(" < ", (ex.StackTrace ?? "").Split('\n').Take(6).Select(l => l.Trim()))}");
                }
                JsonObject state;
                try { state = GdDump(doc.Project); }
                catch (Exception ex) { state = new JsonObject { ["error"] = ex.GetType().Name + ": " + ex.Message }; log.Add($"{step}: dump failed {ex.GetType().Name}: {ex.Message} at {string.Join(" < ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim()))}"); }
                state["cursor"] = $"{ed.SelectedMeasure}:{ed.SelectedCell}:{ed.SelectedString}";
                steps.Add(state);   // the song after each action: the comparator finds the first action after which the apps differ
                if (shots.Contains(step)) GdShot(w, Path.Combine(shotDir!, $"{seq["id"]}_{step:000}.png"));
                step++;
            }
        }
        finally { DialogHost.Capture = previousCapture; DialogHost.MessageCapture = previousMessage; }
        JsonObject result;
        try { result = GdDump(doc.Project); }
        catch (Exception ex) { result = new JsonObject { ["error"] = ex.GetType().Name + ": " + ex.Message }; }
        result["cursor"] = $"{ed.SelectedMeasure}:{ed.SelectedCell}:{ed.SelectedString}";
        result["steps"] = steps;
        return (result, log);
    }

    /// <summary>The score viewport as the user sees it (light paper, scrolled to the top), as a PNG.</summary>
    private static void GdShot(MainWindow w, string path)
    {
        var scroll = SmField<ScrollViewer>(w, "ScoreScroll")!;
        var height = w.Height;
        w.Height = 1500;   // tall enough that a few systems show whole (the default test window cuts the TAB of the first system)
        scroll.ScrollToVerticalOffset(0);
        w.UpdateLayout(); SmSettle();
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1, (int)scroll.ActualWidth), Math.Max(1, (int)scroll.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(scroll);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using (var stream = File.Create(path)) png.Save(stream);
        w.Height = height;
        w.UpdateLayout(); SmSettle();
    }

    private static void GdKey(MainWindow w, TabEditorControl ed, HotkeyMaps hotkeys, Key key, ModifierKeys mods, List<string> log, int step, string action)
    {
        var target = WindowKeyRouter.Dispatch(key, Key.None, mods, ed, hotkeys.Global, id => (bool)SmCall(w, "RunHotkey", id)!);
        SmSettle();
        if (target == WindowKeyRouter.Target.None) log.Add($"{step}: unhandled key {mods}+{key} ({action})");
    }

    private static void GdResetDigits(TabEditorControl ed)
    {
        var f = typeof(ScoreEditCommands).GetField("_lastDigitMeasure", BindingFlags.Instance | BindingFlags.NonPublic)!;
        f.SetValue(ed.Effects, -1);
    }

    /// <summary>Answers a dialog the way pressing its default button does: OK for the effect editors, the default for prompts.</summary>
    private static bool? GdAnswer(Window d, List<string> log, int step)
    {
        log.Add($"{step}: dialog {d.GetType().Name} \"{d.Title}\"");
        switch (d)
        {
            case ThemedEditorDialog e: e.AnswerForTest(EditorAnswer.Ok); break;
            case ThemedConfirmDialog c:
                var no = typeof(ThemedConfirmDialog).GetField("_defaultIsNo", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(c) as bool? == true;
                c.AnswerForTest(no ? MessageBoxResult.No : MessageBoxResult.Yes);
                break;
        }
        return true;
    }

    private static (Key, ModifierKeys) GdParse(string chord)
    {
        var parts = chord.Split('+');
        var mods = ModifierKeys.None;
        foreach (var p in parts[..^1])
            mods |= p switch { "ctrl" => ModifierKeys.Control, "shift" => ModifierKeys.Shift, "alt" => ModifierKeys.Alt, _ => throw new ArgumentException(chord) };
        var k = parts[^1];
        Key key = k switch
        {
            "left" => Key.Left, "right" => Key.Right, "up" => Key.Up, "down" => Key.Down, "home" => Key.Home, "end" => Key.End,
            "ins" => Key.Insert, "del" => Key.Delete, "back" => Key.Back, "enter" => Key.Enter, "esc" => Key.Escape,
            "numadd" => Key.Add, "numsub" => Key.Subtract, "nummul" => Key.Multiply, "numdiv" => Key.Divide, "numdec" => Key.Decimal,
            "period" => Key.OemPeriod, "slash" => Key.OemQuestion, "oem1" => Key.Oem1, "minus" => Key.OemMinus, "plus" => Key.OemPlus,
            _ when k.Length == 1 && char.IsDigit(k[0]) => Key.D0 + (k[0] - '0'),
            _ when k.Length == 1 && char.IsLetter(k[0]) => Key.A + (char.ToUpperInvariant(k[0]) - 'A'),
            _ => throw new ArgumentException("unknown key " + chord)
        };
        return (key, mods);
    }

    /// <summary>
    /// The first track as bars of beats (voice 1): time signature, then per beat its duration, dots, tuplet, rest flag, notes
    /// (string 1 = highest, fret, sorted effect names) and beat effects. Both apps' results go through this one dump.
    /// </summary>
    private static JsonObject GdDump(SongProject song)
    {
        var track = song.Tracks[0];
        var bars = new JsonArray();
        int num = 4, den = 4;
        foreach (var m in track.Measures)
        {
            num = m.TimeSigNum ?? num; den = m.TimeSigDenom ?? den;
            var beats = new JsonArray();
            foreach (var i in MusicTime.BeatSlots(m))
            {
                var c = m.Cells[i];
                var bx = new List<string>();
                if (c.Accent == 1) bx.Add("accent");
                if (c.Accent == 2) bx.Add("heavy_accent");
                if (c.Staccato) bx.Add("staccato");
                if (c.Tenuto) bx.Add("tenuto");
                if (c.Fermata) bx.Add("fermata");
                if (c.IsGrace) bx.Add("grace_beat");
                if (c.IsTied) bx.Add("tied_beat");
                if (c.WhammyPoints.Count > 0) bx.Add("whammy");
                if (!string.IsNullOrEmpty(c.ChordName)) bx.Add("chord");
                if (!string.IsNullOrEmpty(c.Text)) bx.Add("text");
                var notes = new JsonArray(c.Notes.OrderBy(n => n.StringIndex).Select(n => (JsonNode)new JsonObject
                {
                    ["s"] = n.StringIndex + 1,
                    ["f"] = n.Fret,
                    ["x"] = new JsonArray(GdNoteEffects(n).Select(x => (JsonNode)x).ToArray())
                }).ToArray());
                var (tn, td) = c.Tuplet;
                beats.Add(new JsonObject
                {
                    ["d"] = c.DurationDenominator,
                    ["dots"] = c.Dots,
                    ["tup"] = tn > 0 ? $"{tn}:{td}" : "",
                    ["rest"] = c.IsRest || c.Notes.Count == 0,
                    ["notes"] = notes,
                    ["bx"] = new JsonArray(bx.OrderBy(x => x, StringComparer.Ordinal).Select(x => (JsonNode)x).ToArray())
                });
            }
            bars.Add(new JsonObject { ["ts"] = $"{num}/{den}", ["beats"] = beats, ["state"] = GdBarState(song, track, track.Measures.IndexOf(m)) });
        }
        return new JsonObject { ["bars"] = bars, ["tempo"] = song.Tempo, ["tuning"] = string.Join(",", track.StringTunings) };
    }

    private static string GdBarState(SongProject song, TrackModel track, int bar)
    {
        var s = MusicTime.AnalyzeBar(song, bar, track);
        return s.Error ? "over" : s.Short ? "short" : s.Complete ? "ok" : "partial";
    }

    private static IEnumerable<string> GdNoteEffects(TabNote n)
    {
        var x = new SortedSet<string>(StringComparer.Ordinal);
        if (n.Tied) x.Add("tie");
        if (n.Ghost) x.Add("ghost");
        if (n.Dead) x.Add("dead");
        if (n.IsGraceNote) x.Add("grace_note");
        if (n.BendPoints.Count > 0) x.Add("bend");
        if (n.HarmonicFret is not null) x.Add("harmonic");
        foreach (var t in n.Techniques) x.Add("t:" + t);
        return x;
    }
}
