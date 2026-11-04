using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Opt-in measurement of a whole test run: set TABFORGE_TEST_PROBE to a file path and the run writes a sample every 5 s (heap, GC pause, threads,
/// handles, windows open) and a table of the windows opened and never closed, by type. Owns: run-level leak numbers. Does not own: any test's result.
/// </summary>
internal static class TestRunProbe
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Window, string> Open = new();
    private static readonly List<(WeakReference<Window> Window, string Type)> Ever = new();
    private static readonly Dictionary<string, int> Opened = new();
    private static string? _path;
    private static int _watching;
    private static int _setups;

    /// <summary>Counts the headless app set-ups, and is written next to the window table.</summary>
    public static void CountSetup() => Interlocked.Increment(ref _setups);

    public static bool Enabled => _path != null;

    [ModuleInitializer]
    internal static void Start()
    {
        _path = Environment.GetEnvironmentVariable("TABFORGE_TEST_PROBE");
        if (_path == null) return;
        var sampler = new Thread(Sample) { IsBackground = true, Name = "TestRunProbe" };
        sampler.Start();
    }

    /// <summary>Called once the headless app exists, so the class handlers see every window.</summary>
    public static void Watch()
    {
        if (_path == null || Interlocked.Exchange(ref _watching, 1) == 1) return; // the headless app can be set up many times; the class handlers are static
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            var type = window.GetType().Name;
            lock (Gate)
            {
                Open[window] = type;
                Ever.Add((new WeakReference<Window>(window), type));
                Opened[type] = Opened.GetValueOrDefault(type) + 1;
            }
        }, RoutingStrategies.Direct);
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) =>
        {
            lock (Gate) Open.Remove(window);
        }, RoutingStrategies.Direct);
    }

    private static void Sample()
    {
        var clock = Stopwatch.StartNew();
        File.WriteAllText(_path!, "sec,heapMB,wsMB,gen2,pauseSec,threads,handles,windowsOpen,windowsOpened\n");
        var next = 60;
        while (true)
        {
            Thread.Sleep(5000);
            using var self = Process.GetCurrentProcess();
            int open, opened;
            lock (Gate) { open = Open.Count; opened = Opened.Values.Sum(); }
            File.AppendAllText(_path!, string.Create(CultureInfo.InvariantCulture,
                $"{clock.Elapsed.TotalSeconds:F0},{GC.GetTotalMemory(false) >> 20},{self.WorkingSet64 >> 20},{GC.CollectionCount(2)},{GC.GetTotalPauseDuration().TotalSeconds:F1},{self.Threads.Count},{self.HandleCount},{open},{opened}\n"));
            if (clock.Elapsed.TotalSeconds >= next) { next += 60; Table(); }
        }
    }

    private static void Table()
    {
        List<string> lines;
        lock (Gate)
        {
            var alive = Ever.Where(e => e.Window.TryGetTarget(out _)).GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count());
            var open = Open.Values.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
            lines = Opened.OrderByDescending(p => open.GetValueOrDefault(p.Key)).ThenByDescending(p => alive.GetValueOrDefault(p.Key))
                .Select(p => $"{p.Key},opened={p.Value},stillOpen={open.GetValueOrDefault(p.Key)},alive={alive.GetValueOrDefault(p.Key)}").ToList();
        }
        lines.Insert(0, $"appSetups={Volatile.Read(ref _setups)}");
        File.WriteAllLines(_path! + ".windows.txt", lines);
    }
}
