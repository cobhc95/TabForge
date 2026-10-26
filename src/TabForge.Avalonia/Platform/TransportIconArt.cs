using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The owner's transport icons (Assets/TransportIcons/*.svg, the files the playbar reads) drawn on their 96 x 96 grid without the frame,
/// as the transport buttons show them (ShowFrame off). The files are read by the Core icon reader (SvgIcon) and drawn by AvaloniaSvgIcon;
/// on the light theme the colours take the deeper shade of the same hue. The SVG glow filter is not drawn.
/// Owns: loading the SVGs once and drawing one icon. Does not own: the buttons or their layout (TransportBarView).
/// Perf (H11-icon): loaded once on the UI thread after the first frame (Avalonia geometry must be built on the UI thread), so the first paint does no IO;
/// a <see cref="Draw"/> before that paints nothing.
/// <see cref="Has"/> keeps its blocking read for logic and tests (never called on a paint path).
/// Tests: TabForge.Avalonia.Tests.TransportBarViewTests, TabForge.Avalonia.Tests.SvgIconTests, TabForge.Avalonia.Tests.IconArtPreloadTests.
/// </summary>
public static class TransportIconArt
{
    // Cache: key = icon file name without extension, ceiling = the SVGs in Assets/TransportIcons (about 12);
    // a missing or unreadable folder caches an empty set. Thread: any (guarded by Sync; Draw takes only the snapshot).
    private static readonly object Sync = new();
    private static Dictionary<string, AvaloniaSvgIcon.Prepared>? _art;

    /// <summary>True once the art is loaded and <see cref="Draw"/> paints real icons.</summary>
    public static bool IsReady
    {
        get { lock (Sync) return _art is not null; }
    }

    /// <summary>True when <paramref name="name"/> has artwork. Blocking (logic and tests only; the paint path uses <see cref="Draw"/>).</summary>
    public static bool Has(string name) => EnsureLoaded().ContainsKey(name);

    /// <summary>Draws the icon <paramref name="name"/> at (<paramref name="x"/>, <paramref name="y"/>) scaled to <paramref name="size"/> DIPs. Paints nothing before the idle preload (never loads here).</summary>
    public static void Draw(IDrawContext d, string name, double x, double y, double size, bool light)
    {
        Dictionary<string, AvaloniaSvgIcon.Prepared>? snapshot;
        lock (Sync) snapshot = _art;
        if (snapshot is null || !snapshot.TryGetValue(name, out var icon)) return;
        d.PushTranslate(x, y);
        d.PushScale(size / 96, size / 96);
        AvaloniaSvgIcon.Draw(d, icon, null, light);
        d.Pop();
        d.Pop();
    }

    /// <summary>Loads the art synchronously on the UI thread (App start-up, or a test); a second call is free.</summary>
    public static void Preload() => EnsureLoaded();

    private static Dictionary<string, AvaloniaSvgIcon.Prepared> EnsureLoaded()
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

    private static Dictionary<string, AvaloniaSvgIcon.Prepared> ReadAll()
    {
        var art = new Dictionary<string, AvaloniaSvgIcon.Prepared>(StringComparer.Ordinal);
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "TransportIcons"), "*.svg"))
                if (SvgIcon.Read(file) is { } svg) art[Path.GetFileNameWithoutExtension(file)] = AvaloniaSvgIcon.Prepare(svg);
        }
        // Without the icon files the transport buttons show no icon; nothing else depends on them.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or FormatException) { Console.Error.WriteLine("transport icons: " + ex.Message); }
        return art;
    }
}
