using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The palette icons (Assets/ToolIcons/&lt;group&gt;/&lt;name&gt;.svg, the owner's files the Tools pane reads) drawn on their 32 x 32 grid
/// without a frame. The file is read by the Core icon reader (SvgIcon) and drawn by AvaloniaSvgIcon;
/// <c>currentColor</c> takes the icon colour. The SVG glow of explicitly coloured shapes on hover is not drawn.
/// Owns: loading the palette SVGs once and drawing one icon in its square. Does not own: the buttons (ToolsPaneView) or the tool rows (Core
/// PaletteToolCatalog).
/// Perf (H11-icon): loaded once on the UI thread after the first frame (Avalonia geometry must be built on the UI thread), so the first paint does no IO;
/// a <see cref="Draw"/> before that paints nothing.
/// Tests: TabForge.Avalonia.Tests.ToolsPaneViewTests, TabForge.Avalonia.Tests.SvgIconTests, TabForge.Avalonia.Tests.IconArtPreloadTests.
/// </summary>
public static class ToolIconArt
{
    // Cache: key = icon path under Assets/ToolIcons without extension ("Edit/select_pointer"), ceiling = the tool SVGs (about 100);
    // a missing or unreadable file caches null. Thread: any (guarded by Sync; Draw takes only the snapshot).
    private static readonly object Sync = new();
    private static readonly Dictionary<string, AvaloniaSvgIcon.Prepared?> Cache = new(StringComparer.Ordinal);
    private static bool _preloadedAll;

    /// <summary>True once every tool SVG has been read and <see cref="Draw"/> paints real icons.</summary>
    public static bool IsReady
    {
        get { lock (Sync) return _preloadedAll; }
    }

    /// <summary>Draws the icon <paramref name="name"/> in a square of <paramref name="size"/> DIPs at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static void Draw(IDrawContext d, string name, double x, double y, double size, UiColor current, bool light)
    {
        // No load on the paint path: before the idle preload this paints nothing (IsReady is false).
        AvaloniaSvgIcon.Prepared? icon = null;
        bool ready;
        lock (Sync)
        {
            ready = _preloadedAll && Cache.TryGetValue(name, out icon);
            if (!ready && _preloadedAll)
            {
                // Unknown name after a full preload: remember the miss so later paints stay IO-free.
                Cache[name] = null;
                return;
            }
        }
        if (!ready || icon is null) return;
        d.PushTranslate(x, y);
        d.PushScale(size / 32, size / 32);
        AvaloniaSvgIcon.Draw(d, icon, current, light);
        d.Pop();
        d.Pop();
    }

    /// <summary>Reads every tool SVG synchronously on the UI thread (App start-up, or a test); a second call is free.</summary>
    public static void PreloadAll()
    {
        lock (Sync)
        {
            if (_preloadedAll) return;
        }
        var read = ReadAll();
        lock (Sync)
        {
            if (_preloadedAll) return;
            foreach (var (name, icon) in read) Cache[name] = icon;
            _preloadedAll = true;
        }
    }

    private static List<(string Name, AvaloniaSvgIcon.Prepared? Icon)> ReadAll()
    {
        var read = new List<(string, AvaloniaSvgIcon.Prepared?)>();
        try
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Assets", "ToolIcons");
            if (!Directory.Exists(root)) return read;
            foreach (var file in Directory.EnumerateFiles(root, "*.svg", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var name = Path.GetRelativePath(root, file);
                name = name[..^".svg".Length].Replace(Path.DirectorySeparatorChar, '/');
                AvaloniaSvgIcon.Prepared? icon = null;
                try
                {
                    if (SvgIcon.Read(file) is { } svg) icon = AvaloniaSvgIcon.Prepare(svg);
                }
                // Without the file the palette button shows no icon; the button still works from its label.
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or FormatException) { Console.Error.WriteLine("tool icons: " + ex.Message); }
                read.Add((name, icon));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("tool icons: " + ex.Message); }
        return read;
    }
}
