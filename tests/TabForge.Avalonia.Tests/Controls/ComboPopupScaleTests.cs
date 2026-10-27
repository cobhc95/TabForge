using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the combo box dropdown scale: the template hook puts the popup under the window's UI scale and raises its minimum
/// width to the closed box's width on every open, while a wider minimum stays untouched.
/// Does not own: the window scale itself (WindowUiScaleTests) or the menu popups (ContextMenuTests).
/// Tests: this class.
/// </summary>
public class ComboPopupScaleTests
{
    // One root across calls, like the main window's own: ApplyWindow sees the previous scale and raises UiScaleChanged on every real change.
    private static LayoutTransformControl? _scaleRoot;

    private static void SetScale(double scale)
    {
        WindowUiScale.CurrentAppearance.UiScale = scale;
        _scaleRoot ??= new LayoutTransformControl();
        ThemeSettingsBinding.ApplyWindow(new Window(), _scaleRoot, WindowUiScale.CurrentAppearance);
    }

    private static Popup PopupOf(ComboBox combo)
    {
        combo.ApplyTemplate();
        var popup = AvaloniaFormControlStyles.ComboPopupFor(combo);
        Assert.NotNull(popup);
        return popup!;
    }

    private static ComboBox ComboInWindow(Window window)
    {
        var combo = new ComboBox { Width = 300, ItemsSource = new[] { "One", "Two", "Three" } };
        window.Content = combo;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return combo;
    }

    [AvaloniaFact]
    public void TheComboPopupInheritsTheWindowScale()
    {
        Window? window = null;
        try
        {
            SetScale(1.5);
            window = new Window { Width = 400, Height = 300 };
            var combo = ComboInWindow(window);

            Assert.True(PopupOf(combo).InheritsTransform);
        }
        finally
        {
            window?.Close();
            SetScale(1.0);
        }
    }

    [AvaloniaFact]
    public void ThePopupMinimumWidthIsRaisedToTheComboWidth()
    {
        Window? window = null;
        try
        {
            SetScale(1.5);
            window = new Window { Width = 400, Height = 300 };
            var combo = ComboInWindow(window);
            var popup = PopupOf(combo);

            Assert.Equal(300, combo.Bounds.Width, 1);
            AvaloniaFormControlStyles.ApplyComboPopupWidth(combo, popup);
            Assert.True(popup.MinWidth >= 300, $"popup minimum {popup.MinWidth}");
        }
        finally
        {
            window?.Close();
            SetScale(1.0);
        }
    }

    [AvaloniaFact]
    public void AWiderPopupMinimumIsNeverLowered()
    {
        Window? window = null;
        try
        {
            SetScale(0.8);
            window = new Window { Width = 400, Height = 300 };
            var combo = ComboInWindow(window);
            var popup = new Popup { MinWidth = 500 };

            AvaloniaFormControlStyles.ApplyComboPopupWidth(combo, popup);
            Assert.Equal(500, popup.MinWidth);
        }
        finally
        {
            window?.Close();
            SetScale(1.0);
        }
    }
}
