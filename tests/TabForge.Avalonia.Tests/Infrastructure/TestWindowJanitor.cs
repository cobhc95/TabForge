using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit.Sdk;

[assembly: TabForge.Avalonia.Tests.TestWindowJanitor]

namespace TabForge.Avalonia.Tests;

/// <summary>
/// After every test: closes the windows it left open and puts the shared app state back, so one headless app can serve the whole run
/// without later tests slowing down or seeing an earlier test's scale, density or theme. Owns: between-test clean-up. Does not own: any test's own asserts.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class TestWindowJanitorAttribute : BeforeAfterTestAttribute
{
    private static readonly List<Window> Open = new();
    private static bool _watching;
    private static int _tests;

    // AlphaTab fills its engraving defaults lazily and not thread-safely; parallel plain [Fact]s then fail with "Collection was modified". Fill them before any test runs.
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void WarmAlphaTab() => _ = new AlphaTab.RenderingResources();

    /// <summary>Called once the headless app exists.</summary>
    public static void Watch()
    {
        if (_watching) return;
        _watching = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Open.Add(window), RoutingStrategies.Direct);
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => Open.Remove(window), RoutingStrategies.Direct);
    }

    public override void After(MethodInfo methodUnderTest)
    {
        // A plain [Fact] runs on a pool thread and opened no window: touching Dispatcher.UIThread from there would create the dispatcher on the wrong thread.
        if (!methodUnderTest.GetCustomAttributes(true).Any(a => a.GetType().Name.StartsWith("Avalonia", StringComparison.Ordinal))) return;
        if (!Dispatcher.UIThread.CheckAccess()) return;
        foreach (var window in Open.AsEnumerable().Reverse().ToList())
        {
            try
            {
                window.Close();
                if (window.IsVisible) window.Hide(); // its closing handler said no (a save prompt): at least stop it laying out and painting
            }
            catch (Exception)
            {
                // a window that throws on close belongs to a test that already ended; the next test must still get a clean app
            }
            Open.Remove(window);
        }
        WindowUiScale.CurrentAppearance = new TabForge.Services.AppearanceSettings();
        if (global::Avalonia.Application.Current is { } app)
        {
            UiDensityResources.Publish(app.Resources, new TabForge.Services.AppearanceSettings { Density = "Comfortable" });
            app.RequestedThemeVariant = ThemeVariant.Default;
        }
        if (++_tests % 100 == 0)
        {
            // Native Skia surfaces are only freed by finalizers, which the GC does not hurry: without this each test process grows by gigabytes.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
