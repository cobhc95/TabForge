using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Diagnostics;

/// <summary>
/// The score editor's pixel identity: renders a fixed set of songs off-screen (scale x paper x state) and records a SHA-256 of the
/// pixel bytes per case. `--render-identity` writes the manifest, `--render-identity-compare` lists the cases that differ between two
/// manifests. Fonts and DPI are machine-specific, so the golden is always a run of an earlier build on the same machine.
/// </summary>
internal static class ScoreRenderIdentity
{
    internal sealed record CaseResult(string Name, int Width, int Height, string Sha256);

    internal static readonly double[] Scales = { 1.0, 1.25, 1.5 };
    internal static readonly string[] States = { "plain", "edit", "play", "horizontal" };

    private const int PageWidth = 1280;
    private const int MaxPixels = 3000;

    /// <summary>The repository's own songs: the diagnostic songs, the demo song and (when found) the sample file.</summary>
    internal static IReadOnlyList<(string Name, SongProject Song)> BuiltInSongs(Func<string, SongProject> load)
    {
        var songs = new List<(string, SongProject)>();
        foreach (var (name, song) in DiagnosticSongFactory.All()) songs.Add(("diag-" + name, song));
        songs.Add(("demo", DemoSongFactory.Create()));
        songs.Add(("technique", GmSongAudit.TechniqueSong()));
        var sample = FindSample();
        if (sample is not null) songs.Add(("sample", load(sample)));
        return songs;
    }

    private static string? FindSample()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "TabForge Demo - Ashen Meridian.gp5");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>A fresh editor in the given state, laid out and ready to render.</summary>
    internal static TabEditorControl CreateEditor(SongProject project, int track, double zoom, bool dark, string state)
    {
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = track, DarkPaper = dark, Zoom = zoom, PageWidthOverride = PageWidth };
        ApplyState(editor, state);
        return editor;
    }

    internal static void ApplyState(TabEditorControl editor, string state)
    {
        var track = editor.Track;
        if (track is null || track.Measures.Count == 0) return;
        var bars = track.Measures.Count;
        switch (state)
        {
            case "edit":
                editor.SetPosition(bars / 2, 0, 1, seekPlayback: false);
                editor.SelectMeasureRange(Math.Min(bars - 1, bars / 2), Math.Min(bars - 1, bars / 2 + 1));
                break;
            case "play":
                ApplyPlayState(editor, track);
                break;
            case "horizontal":
                editor.HorizontalScroll = true;
                break;
        }
    }

    /// <summary>Sets the playhead on a note a third of the way through the song, with the timeline, sounding notes and the playing-bar band.</summary>
    internal static void ApplyPlayState(TabEditorControl editor, TrackModel track)
    {
        var project = editor.Project!;
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        var notes = timeline.NotesFor(editor.SelectedTrackIndex);
        editor.Timeline = timeline;
        editor.PlaybackTrackIndex = editor.SelectedTrackIndex;
        editor.PlayingBarEnabled = true;
        editor.PlaybackActive = true;
        if (notes.Length == 0) { editor.SetPlayhead(0, 0); return; }
        var note = notes[notes.Length / 3];
        var ms = note.OnsetMs + Math.Min(30, note.DurationMs / 2);
        editor.PlaybackMs = ms;
        editor.PlaybackFraction = timeline.Bars.Count > 0 ? timeline.BarAt(ms).SlotFraction(ms) : 0;
        editor.SetPlayhead(Math.Clamp(note.Bar, 0, track.Measures.Count - 1), note.Cell);
    }

    /// <summary>Lays the editor out and renders its top part (at most 3000 x 3000 pixels) at 96 dpi.</summary>
    internal static RenderTargetBitmap Render(TabEditorControl editor)
    {
        editor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        editor.Arrange(new Rect(0, 0, editor.DesiredSize.Width, editor.DesiredSize.Height));
        editor.UpdateLayout();
        var width = Math.Clamp((int)Math.Ceiling(editor.DesiredSize.Width), 1, MaxPixels);
        var height = Math.Clamp((int)Math.Ceiling(editor.DesiredSize.Height), 1, MaxPixels);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(editor);
        return bitmap;
    }

    internal static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    internal static string Hash(BitmapSource bitmap) =>
        Convert.ToHexString(SHA256.HashData(Pixels(bitmap))).ToLowerInvariant();

    internal static string CaseName(string song, int track, double zoom, bool dark, string state) =>
        string.Create(CultureInfo.InvariantCulture, $"{song}|t{track}|x{zoom:0.00}|{(dark ? "dark" : "light")}|{state}");

    private static string FileSafe(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '_'));

    /// <summary>Every case of one song: all scales, both papers, all states for the first track; a plain render of the last track of a multi-track song.</summary>
    internal static IEnumerable<(string Name, int Track, double Zoom, bool Dark, string State)> CasesOf(string song, SongProject project)
    {
        foreach (var zoom in Scales)
            foreach (var dark in new[] { true, false })
                foreach (var state in States)
                    yield return (CaseName(song, 0, zoom, dark, state), 0, zoom, dark, state);
        if (project.Tracks.Count > 1)
            foreach (var dark in new[] { true, false })
                yield return (CaseName(song, project.Tracks.Count - 1, 1.0, dark, "plain"), project.Tracks.Count - 1, 1.0, dark, "plain");
    }

    /// <summary>`--render-identity &lt;outDir&gt; [--png] [--corpus &lt;dir&gt;]`: writes identity.json (and a PNG per case with --png).</summary>
    internal static int Run(string[] args, Func<string, SongProject> load, Func<string, string> outputDirectory)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: TabForge.exe --render-identity <outDir> [--png] [--corpus <dir>]"); return 2; }
        var outDir = outputDirectory(args[1]);
        var png = args.Skip(2).Any(a => a.Equals("--png", StringComparison.OrdinalIgnoreCase));
        var corpus = ArgAfter(args, "--corpus") ?? Environment.GetEnvironmentVariable("TF_RENDER_CORPUS");
        Directory.CreateDirectory(outDir);

        var songs = BuiltInSongs(load).ToList();
        if (!string.IsNullOrWhiteSpace(corpus) && Directory.Exists(corpus))
            foreach (var file in Directory.EnumerateFiles(corpus).Where(f => f.EndsWith(".gp5", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tforge", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                songs.Add(("corpus-" + Path.GetFileNameWithoutExtension(file), load(file)));

        var rethrow = TabEditorControl.RethrowRenderFailures;
        TabEditorControl.RethrowRenderFailures = true;
        var results = new List<CaseResult>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // The first renders of a process can differ at fractional scales while fonts and glyph caches warm up: render the first song once, unrecorded.
            if (songs.Count > 0)
                foreach (var (_, track, zoom, dark, state) in CasesOf(songs[0].Name, songs[0].Song))
                    Render(CreateEditor(songs[0].Song, track, zoom, dark, state));
            foreach (var (song, project) in songs)
                foreach (var (name, track, zoom, dark, state) in CasesOf(song, project))
                {
                    var editor = CreateEditor(project, track, zoom, dark, state);
                    var bitmap = Render(editor);
                    results.Add(new CaseResult(name, bitmap.PixelWidth, bitmap.PixelHeight, Hash(bitmap)));
                    if (png) SavePng(bitmap, Path.Combine(outDir, FileSafe(name) + ".png"));
                }
        }
        finally { TabEditorControl.RethrowRenderFailures = rethrow; }

        var manifest = results.ToDictionary(r => r.Name, r => new { w = r.Width, h = r.Height, sha256 = r.Sha256 });
        File.WriteAllText(Path.Combine(outDir, "identity.json"), JsonSerializer.Serialize(new { version = 1, cases = manifest }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"render identity: {results.Count} cases from {songs.Count} songs in {watch.Elapsed.TotalSeconds:0.#} s -> {Path.Combine(outDir, "identity.json")}");
        return 0;
    }

    /// <summary>`--render-identity-compare &lt;a.json&gt; &lt;b.json&gt;`: exit 0 when every case is equal, 1 and the list of differences otherwise.</summary>
    internal static int Compare(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: TabForge.exe --render-identity-compare <a.json> <b.json>"); return 2; }
        var a = ReadManifest(args[1]);
        var b = ReadManifest(args[2]);
        var differing = new List<string>();
        foreach (var name in a.Keys.Union(b.Keys).OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!a.TryGetValue(name, out var left)) differing.Add($"only in second: {name}");
            else if (!b.TryGetValue(name, out var right)) differing.Add($"only in first: {name}");
            else if (left != right) differing.Add($"differs: {name}");
        }
        foreach (var line in differing) Console.WriteLine(line);
        Console.WriteLine($"render identity compare: {a.Count} / {b.Count} cases, {differing.Count} differences");
        if (differing.Count > 0) WriteDiffImages(args[1], args[2], differing);
        return differing.Count == 0 ? 0 : 1;
    }

    private static Dictionary<string, string> ReadManifest(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateObject())
            result[c.Name] = $"{c.Value.GetProperty("w").GetInt32()}x{c.Value.GetProperty("h").GetInt32()}:{c.Value.GetProperty("sha256").GetString()}";
        return result;
    }

    /// <summary>When both runs kept their PNGs, writes an amplified absolute-difference image per differing case next to the second manifest.</summary>
    private static void WriteDiffImages(string firstJson, string secondJson, List<string> differing)
    {
        try
        {
            var dirA = Path.GetDirectoryName(Path.GetFullPath(firstJson))!;
            var dirB = Path.GetDirectoryName(Path.GetFullPath(secondJson))!;
            foreach (var line in differing.Where(l => l.StartsWith("differs: ", StringComparison.Ordinal)).Take(40))
            {
                var name = FileSafe(line["differs: ".Length..]);
                string pa = Path.Combine(dirA, name + ".png"), pb = Path.Combine(dirB, name + ".png");
                if (!File.Exists(pa) || !File.Exists(pb)) continue;
                var ia = Decode(pa); var ib = Decode(pb);
                if (ia.PixelWidth != ib.PixelWidth || ia.PixelHeight != ib.PixelHeight) continue;
                var xa = Pixels(ia); var xb = Pixels(ib);
                var diff = new byte[xa.Length];
                for (var i = 0; i < diff.Length; i += 4)
                {
                    var d = Math.Min(255, (Math.Abs(xa[i] - xb[i]) + Math.Abs(xa[i + 1] - xb[i + 1]) + Math.Abs(xa[i + 2] - xb[i + 2])) * 4);
                    diff[i] = diff[i + 1] = diff[i + 2] = (byte)d; diff[i + 3] = 255;
                }
                Directory.CreateDirectory(Path.Combine(dirB, "diff"));
                var image = BitmapSource.Create(ia.PixelWidth, ia.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, diff, ia.PixelWidth * 4);
                SavePng(image, Path.Combine(dirB, "diff", name + ".png"));
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException) { Console.WriteLine("diff images skipped: " + ex.Message); }
    }

    private static BitmapSource Decode(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        return new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string? ArgAfter(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
