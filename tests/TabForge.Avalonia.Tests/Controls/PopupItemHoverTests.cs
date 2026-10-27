using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Platform.Theme;
using Xunit;
using static TabForge.Avalonia.Tests.DialogTestKit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// A drop-down item under the pointer paints the master hover surface in both themes, and the item look rules resolve hover to HoverSurface and pressed to PressSurface.
/// Owns: the pointer-over check of an opened combo box and the hover/pressed token check of each popup and list item look. Does not own: the look values (LookTable) or the surfaces (HoverSurfaces).
/// Tests: this class.
/// </summary>
public class PopupItemHoverTests
{
    // Every popup or list item look and the brush its pointer-over and pressed rules must name (null: no pressed rule is required).
    private static readonly (string Look, string? Pressed)[] ItemLooks =
    {
        ("ComboBoxItem", "PressBrush"), ("ListBoxItem", "PressBrush"), ("MenuItem", "PressBrush"), ("PrefsComboItem", null), ("PrefsMenuItem", null),
    };

    [Fact]
    public void EveryItemLookPaintsHoverFromTheHoverSurfaceAndPressedFromThePressSurface()
    {
        foreach (var (look, pressed) in ItemLooks)
        {
            Assert.Equal("HoverSurface", AvaloniaPalette.BrushPaletteNames[BrushOf(look, "IsMouseOver", "IsHighlighted")]);
            if (pressed is not null) Assert.Equal("PressSurface", AvaloniaPalette.BrushPaletteNames[BrushOf(look, "IsPressed")]);
        }
    }

    [AvaloniaFact]
    public void OpenedComboBoxItemUnderThePointerPaintsTheHoverSurfaceInBothThemes()
    {
        foreach (var light in new[] { false, true })
        {
            using var theme = UseTheme(light);
            var combo = new ComboBox { ItemsSource = new[] { "System", "Dark", "Light", "Custom" }, SelectedIndex = 0, Width = 160 };
            var window = new Window { Width = 300, Height = 200, Content = combo };
            window.Show();
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var popup = combo.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.Popup>().Single();
            var item = popup.Child!.GetVisualDescendants().OfType<ComboBoxItem>().ElementAt(1);
            var top = (TopLevel)item.GetVisualRoot()!;
            top.MouseMove(item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), top)!.Value);
            Dispatcher.UIThread.RunJobs();
            var body = item.GetVisualDescendants().OfType<Border>().First(b => b.Name == "bd");
            Assert.True(Application.Current!.TryGetResource("HoverBrush", light ? ThemeVariant.Light : ThemeVariant.Dark, out var token));
            Assert.Equal(((ISolidColorBrush)token!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(body.Background).Color);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MenuItemInASubmenuUnderThePointerPaintsTheHoverSurfaceInBothThemes()
    {
        foreach (var light in new[] { false, true })
        {
            using var theme = UseTheme(light);
            var sub = new MenuItem { Header = "Sub", Items = { new MenuItem { Header = "Leaf" } } };
            var file = new MenuItem { Header = "File", Items = { new MenuItem { Header = "Open" }, sub } };
            var window = new Window { Width = 300, Height = 200, Content = new Menu { Items = { file } } };
            window.Show();
            file.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            sub.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            var popup = sub.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.Popup>().Single();
            var item = popup.Child!.GetVisualDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Leaf");
            var top = (TopLevel)item.GetVisualRoot()!;
            top.MouseMove(item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), top)!.Value);
            Dispatcher.UIThread.RunJobs();
            var body = item.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");
            Assert.True(Application.Current!.TryGetResource("HoverBrush", light ? ThemeVariant.Light : ThemeVariant.Dark, out var token));
            Assert.Equal(((ISolidColorBrush)token!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(body.Background).Color);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HoveringTheSelectedRowShowsAChangeInBothThemes()
    {
        foreach (var light in new[] { false, true })
        {
            using var theme = UseTheme(light);
            var list = new ListBox { ItemsSource = new[] { "One", "Two", "Three" }, SelectedIndex = 0, Width = 160 };
            var window = new Window { Width = 300, Height = 200, Content = list };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var row = list.GetVisualDescendants().OfType<ListBoxItem>().First();
            var body = row.GetVisualDescendants().OfType<Border>().First(b => b.Name == "bd");
            var selected = Assert.IsAssignableFrom<ISolidColorBrush>(body.Background).Color;
            var top = (TopLevel)row.GetVisualRoot()!;
            top.MouseMove(row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), top)!.Value);
            Dispatcher.UIThread.RunJobs();
            var hovered = Assert.IsAssignableFrom<ISolidColorBrush>(body.Background).Color;
            Assert.NotEqual(selected, hovered);
            Assert.True(Application.Current!.TryGetResource("HoverAccentBrush", light ? ThemeVariant.Light : ThemeVariant.Dark, out var token));
            Assert.Equal(((ISolidColorBrush)token!).Color, hovered);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectedComboBoxItemPresenterStaysUnfilledAlsoUnderThePointer()
    {
        foreach (var light in new[] { false, true })
        {
            using var theme = UseTheme(light);
            var combo = new ComboBox { ItemsSource = new[] { "System", "Dark", "Light", "Custom" }, SelectedIndex = 1, Width = 160 };
            var window = new Window { Width = 300, Height = 200, Content = combo };
            window.Show();
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var popup = combo.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.Popup>().Single();
            var item = popup.Child!.GetVisualDescendants().OfType<ComboBoxItem>().ElementAt(1);
            global::Avalonia.Controls.Presenters.ContentPresenter Presenter() => item.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
            Assert.True(item.IsSelected);
            Assert.True(Presenter().Background is null || Presenter().Background is ISolidColorBrush { Color.A: 0 });
            var top = (TopLevel)item.GetVisualRoot()!;
            top.MouseMove(item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), top)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Presenter().Background is null || Presenter().Background is ISolidColorBrush { Color.A: 0 });
            window.Close();
        }
    }

    // The brush a rule of the look names for the first of the conditions, as the palette key it publishes.
    private static string BrushOf(string look, params string[] conditions) =>
        LookTable.Looks[look].Rules.Where(rule => rule.When.Any(c => conditions.Contains(c.Property)))
            .SelectMany(rule => rule.Set).First(setter => setter.Property == "Border.Background").Brush!;
}
