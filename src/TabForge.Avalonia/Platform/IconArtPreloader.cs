using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Loads icon art after startup: file/model parsing on a worker, bounded geometry batches on the UI thread after platform setup.
/// Avalonia geometry must never be prepared on the worker (it can resolve the wrong dispatcher before the Win32 platform is ready).
/// Owns: startup scheduling, batch boundaries, readiness and repaints after each icon set becomes ready.
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

    /// <summary>Starts worker reads and bounded UI preparation after the window opens. Call after Avalonia platform setup, on the UI thread.</summary>
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
    private static void PreloadAndRepaint(Window window) => UiTasks.Forget(PreloadAndRepaintAsync(window), "Icon preload", text => TabForge.Services.Trace.Error(TabForge.Services.Trace.Ui, text));

    private static async Task PreloadAndRepaintAsync(Window window)
    {
        // Transport is visible first; the broad catalogues follow. IO/model parsing is off-thread, geometry stays on the UI thread.
        await TransportIconArt.PreloadBatched();
        RefreshIconHosts(window);
        await TrackIconArt.PreloadBatched();
        RefreshIconHosts(window);
        await ToolIconArt.PreloadBatched();
        _ = WarmInstrumentPicker();
        RefreshIconHosts(window);
    }

    /// <summary>At most four icon geometries per UI dispatch; input/render can run between batches. No UI timer or background Avalonia access.</summary>
    internal static Task PrepareBatches<T>(IReadOnlyList<T> items, Action<T> prepare)
    {
        const int maxIconsPerBatch = 4;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ui = AvaloniaUiThread.ForApplication();
        var at = 0;
        void Next()
        {
            try
            {
                var end = Math.Min(items.Count, at + maxIconsPerBatch);
                while (at < end) prepare(items[at++]);
                if (at < items.Count) ui.Post(Next, TabForge.Platform.UiPriority.ApplicationIdle);
                else done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        }
        ui.Post(Next, TabForge.Platform.UiPriority.ApplicationIdle);
        return done.Task;
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
