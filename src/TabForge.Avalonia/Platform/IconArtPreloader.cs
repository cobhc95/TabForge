using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Loads the track, tool and transport icon art once at App start-up (H11-icon), so the first paint does no file or SVG reads.
/// The load runs on the UI thread: Avalonia geometry must be built there, and parsing one before the platform is set up (a module
/// initializer, a worker thread) resolves Dispatcher.UIThread without the Win32 dispatcher, so the app dies on start.
/// Owns: only the one start-up load, the joint readiness flag and the single repaint of the hosts that painted before the load.
/// Does not own: the art itself (TrackIconArt, ToolIconArt, TransportIconArt).
/// Tests: TabForge.Avalonia.Tests.IconArtPreloadTests.
/// </summary>
public static class IconArtPreloader
{
    /// <summary>True once all three icon sets are loaded and paints draw real icons.</summary>
    public static bool IsReady => TrackIconArt.IsReady && ToolIconArt.IsReady && TransportIconArt.IsReady;

    /// <summary>Loads all three icon sets. Call on the UI thread after the Avalonia platform is set up (App.Initialize).</summary>
    public static void Preload()
    {
        try { TrackIconArt.Preload(); }
        catch (Exception ex) { Console.Error.WriteLine("track icons: " + ex.Message); }
        try { ToolIconArt.PreloadAll(); }
        catch (Exception ex) { Console.Error.WriteLine("tool icons: " + ex.Message); }
        try { TransportIconArt.Preload(); }
        catch (Exception ex) { Console.Error.WriteLine("transport icons: " + ex.Message); }
    }

    /// <summary>Loads all three icon sets after the first frame: startup paints first, then one idle post loads them. Call on the UI thread (Avalonia geometry must be built on it).</summary>
    public static void PreloadAfterFirstFrame(Window window)
    {
        var ui = AvaloniaUiThread.ForApplication();
        if (!ui.CheckAccess()) throw new InvalidOperationException("IconArtPreloader.PreloadAfterFirstFrame must be called on the UI thread.");
        if (window.IsVisible)
        {
            ui.Post(() => PreloadAndRepaint(window), TabForge.Platform.UiPriority.ApplicationIdle);
            return;
        }
        EventHandler? shown = null;
        shown = (_, _) =>
        {
            window.Opened -= shown;
            ui.Post(() => PreloadAndRepaint(window), TabForge.Platform.UiPriority.ApplicationIdle);
        };
        window.Opened += shown;
    }

    /// <summary>Builds the instrument catalogue on a worker, then prepares one badge on the UI thread, so the Choose instrument window's first open does not pay the file reads and JIT.</summary>
    private static async System.Threading.Tasks.Task WarmInstrumentPicker()
    {
        try
        {
            var first = await System.Threading.Tasks.Task.Run(() => TabForge.Services.InstrumentCatalog.All[0]);
            AvaloniaUiThread.ForApplication().Post(() => _ = new InstrumentBadgeView(56) { Entry = first }.HasArt, TabForge.Platform.UiPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            TabForge.Services.Trace.Error(TabForge.Services.Trace.Ui, "instrument picker warm-up: " + ex.Message);
        }
    }

    /// <summary>Loads the icon sets, then repaints the icon hosts once: hosts that painted before the load (blank) show the art at once, not on hover.</summary>
    private static void PreloadAndRepaint(Window window)
    {
        Preload();
        _ = WarmInstrumentPicker();
        RefreshIconHosts(window);
    }

    /// <summary>Invalidates the visible icon hosts under <paramref name="window"/> once, so their next render draws the loaded art. Single-shot: no timer.</summary>
    internal static void RefreshIconHosts(Window window)
    {
        // The four surfaces that draw the preloaded art: each painted nothing before the load, and nothing else repays them until hover.
        foreach (var host in window.GetVisualDescendants().OfType<Control>())
            if (host is ToolIconView || host is TrackIconPicture || host is TrackHeaderColumn || host is TransportBarView.TransportButton)
                host.InvalidateVisual();
    }
}
