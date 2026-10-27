using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks the dialog test classes share: switching the theme, saving a window's frame as a PNG under the temp folder, reading a
/// resolved brush as a colour and the contrast of text over a surface.
/// Does not own: each dialog's expected structure and texts (its test class), the palette and the presenters.
/// Tests: the dialog test classes that call it.
/// </summary>
internal static class DialogTestKit
{
    /// <summary>
    /// The render root of this test process, under the temp folder. Key: one GUID per process, fixed when the class first loads.
    /// Ceiling: the folders are never cleared, so a run never reuses an earlier run's PNGs. Thread: any; written once, then read.
    /// </summary>
    public static readonly string PngDirectory = Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The folder one test class renders into (<see cref="PngDirectory"/>\<paramref name="className"/>), created on demand, so classes never share file names.</summary>
    public static string RenderDirectory(string className) => Directory.CreateDirectory(Path.Combine(PngDirectory, className)).FullName;

    /// <summary>The path of a render named <paramref name="fileName"/> in the folder of the test class <paramref name="className"/>.</summary>
    public static string RenderPath(string className, string fileName) => Path.Combine(RenderDirectory(className), fileName);

    /// <summary>Switches the theme until the returned scope is disposed (<c>using var theme = UseTheme(light)</c>), so a failing test still resets it.</summary>
    public static IDisposable UseTheme(bool light)
    {
        Application.Current!.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        return new ThemeScope();
    }

    private sealed class ThemeScope : IDisposable
    {
        public void Dispose() => Application.Current!.RequestedThemeVariant = null;
    }

    /// <summary>Shows the window, lets it lay out and saves its frame as <paramref name="fileName"/> in the folder of <paramref name="className"/> (<see cref="RenderPath"/>).</summary>
    public static string SavePng(Window window, string className, string fileName)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window rendered no frame.");
        var path = RenderPath(className, fileName);
        frame.Save(path);
        return path;
    }

    /// <summary>The controls the window's layout declares, in tree order: the parts inside a button, combo box or text box template are left out.</summary>
    public static IEnumerable<Control> LayoutControls(Window window) => window.GetLogicalDescendants().OfType<Control>()
        .Where(c => !c.GetLogicalAncestors().Any(a => a is Button or ComboBox or TextBox));

    /// <summary>The control of type T named <paramref name="name"/> in the window's logical tree; code-built windows have no name scope, so FindControl cannot see them.</summary>
    // Distinct: a control shown in a TabControl's selected tab is reached both through its TabItem and through the tab's content presenter.
    public static T Named<T>(Window window, string name) where T : Control =>
        window.GetLogicalDescendants().Concat(FlyoutParts(window)).OfType<T>().Distinct().Single(c => c.Name == name);

    /// <summary>The controls inside the flyouts of the window's buttons (a flyout's content is not in the window's logical tree until it opens).</summary>
    private static IEnumerable<ILogical> FlyoutParts(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Select(b => b.Flyout).OfType<Flyout>().Select(f => f.Content).OfType<Control>()
            .SelectMany(content => content.GetLogicalDescendants().Prepend(content)).ToList();

    public static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Upper bound on the dispatcher passes that drain queued UI work; each pass runs every job posted so far, and an empty pass costs nothing.</summary>
    private const int QueuedUiPasses = 50;

    /// <summary>Runs queued UI work to the end, including the background slices a window posts while it builds its content.</summary>
    public static void RunQueuedUiWork()
    {
        for (var pass = 0; pass < QueuedUiPasses; pass++) Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The colour a resolved brush paints; fails when the brush is missing or not solid.</summary>
    public static UiColor SolidColour(IBrush? brush, string what)
    {
        if (brush is not ISolidColorBrush solid) throw new InvalidOperationException($"{what} does not resolve to a solid brush.");
        return new UiColor(solid.Color.A, solid.Color.R, solid.Color.G, solid.Color.B);
    }

    /// <summary>The surface a text block sits on: the opaque fill of the button that holds it (the accent fill of an OK button), otherwise the window.</summary>
    public static IBrush? SurfaceBehind(TextBlock text, Window window) =>
        text.GetLogicalAncestors().OfType<Button>().Select(b => b.Background).FirstOrDefault(f => f is ISolidColorBrush { Color.A: 255 }) ?? window.Background;

    /// <summary>The WCAG contrast ratio of a resolved text brush over a resolved surface brush.</summary>
    public static double Contrast(IBrush? text, IBrush? surface, string what) =>
        ThemeColours.Contrast(SolidColour(text, what + " text"), SolidColour(surface, what + " surface"));
}
