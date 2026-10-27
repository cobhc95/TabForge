using System.Linq;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Settings inputs answer a real pointer the same way: each kind takes the accent edge on hover in both themes, every input shares one
/// height and one right edge, and a number's text is right-aligned.
/// Tests: this class (the window is the real PreferencesWindow; the pointer is a headless MouseMove on the control's centre).
/// </summary>
public class PreferencesControlHoverTests
{
    // The Settings field height and the rows' shared right edge, in DIP; a number box's rounding is within one DIP.
    private const double FieldHeight = 32, RightEdgeTolerance = 1.5;

    [AvaloniaFact]
    public void EveryInputKindTakesTheAccentEdgeOnPointerOver()
    {
        foreach (var light in new[] { false, true })
        {
            using var theme = UseTheme(light);
            var window = Open(SettingsCatalog.Appearance);
            var accent = AccentOf(light);
            foreach (var (name, edgeOf) in Kinds(window))
            {
                var editor = edgeOf.Editor;
                var rest = Colour(edgeOf.Edge());
                PointAt(window, editor);
                var hover = Colour(edgeOf.Edge());
                PointAway(window);
                Assert.True(rest != hover, $"{name} ({(light ? "light" : "dark")}): the edge does not change on hover.");
                Assert.Equal(accent, hover);
            }
        }
    }

    [AvaloniaFact]
    public void InputsShareOneHeightOneRightEdgeAndRightAlignedNumbers()
    {
        var window = Open(SettingsCatalog.Appearance);
        var editors = window.ShownRows.Select(row => window.EditorOf(row.Key))
            .Where(editor => editor.IsEffectivelyVisible && editor.Parent is Grid)
            .ToList();
        Assert.NotEmpty(editors);
        foreach (var field in editors.Where(editor => editor is ComboBox or NumericUpDown or TextBox))
            Assert.True(field.Bounds.Height == FieldHeight, $"{field.GetType().Name} {field.Name} is {field.Bounds.Height} DIP high.");
        var right = editors.Select(e => RightEdgeOf(window, e)).ToList();
        Assert.All(right, edge => Assert.InRange(edge, right[0] - RightEdgeTolerance, right[0] + RightEdgeTolerance));
        foreach (var number in editors.OfType<NumericUpDown>())
            Assert.Equal(TextAlignment.Right, number.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "PART_TextBox").TextAlignment);
    }

    private static PreferencesWindow Open(string page)
    {
        var window = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
        window.Show();
        window.ShowPage(page);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // One entry per input kind on the Appearance page: its editor, and the edge brush the kind paints on hover.
    private static (string Name, EdgeProbe Probe)[] Kinds(PreferencesWindow window)
    {
        var rows = window.ShownRows.Where(row => row.Kind is SettingKind.Choice or SettingKind.Number or SettingKind.Bool && window.EditorOf(row.Key).IsEffectivelyVisible).ToList();
        var choice = window.EditorOf(rows.First(r => r.Kind == SettingKind.Choice).Key);
        var number = window.EditorOf(rows.First(r => r.Kind == SettingKind.Number).Key);
        var check = window.EditorOf(rows.First(r => r.Kind == SettingKind.Bool).Key);
        var search = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "SearchBox");
        return new[]
        {
            ("ComboBox", new EdgeProbe(choice, () => PartBorder(choice, "Background"))),
            ("NumericUpDown", new EdgeProbe(number, () => ((TemplatedControl)number).BorderBrush)),
            ("CheckBox", new EdgeProbe(check, () => PartBorder(check, "box"))),
            ("TextBox", new EdgeProbe(search, () => PartBorder(search, "PART_BorderElement"))),
        };
    }

    private sealed record EdgeProbe(Control Editor, System.Func<IBrush?> Edge);

    private static IBrush? PartBorder(Control host, string part) =>
        (host.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == part)
            ?? throw new System.InvalidOperationException($"{host.GetType().Name} has no border {part}; its borders are {string.Join(", ", host.GetVisualDescendants().OfType<Border>().Select(b => b.Name))}.")).BorderBrush;

    private static uint Colour(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color.ToUInt32() : 0;

    private static uint AccentOf(bool light) =>
        Application.Current!.TryGetResource("AccentBrush", light ? ThemeVariant.Light : ThemeVariant.Dark, out var resource) && resource is ISolidColorBrush accent ? accent.Color.ToUInt32() : 0;

    private static double RightEdgeOf(Window window, Control editor) => editor.TranslatePoint(new Point(editor.Bounds.Width, 0), window)?.X ?? -1;

    private static void PointAt(Window window, Control control)
    {
        control.BringIntoView();   // the page scrolls: a row below the fold is not under the pointer until it is shown
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.MouseMove(control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? new Point(0, 0));
        Dispatcher.UIThread.RunJobs();
    }

    private static void PointAway(Window window)
    {
        window.MouseMove(new Point(1, 1));
        Dispatcher.UIThread.RunJobs();
    }
}
