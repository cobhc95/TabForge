using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Media;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The owner's track-row instrument line art (Assets/TrackRowIcons/owner-icons.json, the file the track list reads) drawn on its 24 x 24 grid
/// in three layers: faint shading and the line work in the text colour, and the accent detail in the fixed accent colour.
/// Owns: loading the art once and drawing one icon. Does not own: the program-to-icon mapping (Core <c>TrackIconKeys</c>) or the row layout.
/// Perf (H11-icon): loaded once on the UI thread after the first frame (Avalonia geometry must be built on the UI thread), so the first paint does no IO;
/// a <see cref="Draw"/> before that paints nothing.
/// <see cref="Has"/> keeps its blocking read for logic and tests (never called on a paint path).
/// Tests: TabForge.Avalonia.Tests.ArrangementViewTests, TabForge.Avalonia.Tests.IconArtPreloadTests.
/// </summary>
public static class TrackIconArt
{
    private sealed record Art(AvaloniaPath Ink, AvaloniaPath? Accent, AvaloniaPath? Shade);

    // Cache: key = icon name, ceiling = the icons in owner-icons.json (about 60), filled once and kept for the process;
    // a missing or unreadable file caches an empty set. Thread: any (guarded by Sync; Draw takes only the snapshot).
    private static readonly object Sync = new();
    private static Dictionary<string, Art>? _art;

    /// <summary>True once the art is loaded and <see cref="Draw"/> paints real icons.</summary>
    public static bool IsReady
    {
        get { lock (Sync) return _art is not null; }
    }

    /// <summary>True when <paramref name="key"/> has artwork. Blocking (logic and tests only; the paint path uses <see cref="Draw"/>).</summary>
    public static bool Has(string key) => EnsureLoaded().ContainsKey(key);

    /// <summary>Draws the icon <paramref name="key"/> at (<paramref name="x"/>, <paramref name="y"/>) scaled to <paramref name="size"/> DIPs. Paints nothing before the idle preload (never loads here).</summary>
    public static void Draw(IDrawContext d, string key, double x, double y, double size, UiColor ink, UiColor accent)
    {
        Dictionary<string, Art>? snapshot;
        lock (Sync) snapshot = _art;
        if (snapshot is null || !snapshot.TryGetValue(key, out var icon)) return;
        d.PushTranslate(x, y);
        d.PushScale(size / 24, size / 24);
        if (icon.Shade is { } shade) d.Path(ink with { A = (byte)(ink.A * 0.3) }, null, shade);
        d.Path(ink, null, icon.Ink);
        if (icon.Accent is { } detail) d.Path(accent, null, detail);
        d.Pop();
        d.Pop();
    }

    /// <summary>Loads the art synchronously on the UI thread (App start-up, or a test); a second call is free.</summary>
    public static void Preload() => EnsureLoaded();

    private static Dictionary<string, Art> EnsureLoaded()
    {
        lock (Sync)
        {
            if (_art is not null) return _art;
        }
        var built = ReadAll();
        lock (Sync)
        {
            _art ??= built;
            return _art;
        }
    }

    private static Dictionary<string, Art> ReadAll()
    {
        var art = new Dictionary<string, Art>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "TrackRowIcons", "owner-icons.json")));
            foreach (var icon in doc.RootElement.EnumerateObject())
                if (Layer(icon.Value, "ink") is { } ink) art[icon.Name] = new Art(ink, Layer(icon.Value, "accent"), Layer(icon.Value, "shade"));
        }
        // Without the art file the rows show no icon; nothing else depends on it.
        // This read runs off the paint path (background preload, or Has in logic/tests) with no window to show a message in, so the console line is the only record.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException) { Console.Error.WriteLine("track icons: " + ex.Message); }
        return art;
    }

    private static AvaloniaPath? Layer(JsonElement icon, string name) =>
        icon.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } data ? new AvaloniaPath(Geometry.Parse(data)) : null;
}
