using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Keyboard mode pop-out window checks (presenter texts and tooltip, the full screen button and its text, the F11 and Esc keys,
/// dark and light PNGs, contrast, the preview's menu item).
/// Does not own: the window, the presenter or the palette. The control bar and the keyboard view are the app's Keyboard mode and are not
/// in this window (see its header), so the structure check covers only the full screen button and the empty host.
/// Tests: this class.
/// </summary>
public class KeyboardModePopoutWindowTests
{
    // The window (src/TabForge/KeyboardMode/KeyboardModePopoutWindow.cs), written as the controls it owns: the full screen button (its
    // automation name, text and tooltip) after the app's control bar, then the keyboard view. The bar and the view are not ported.
    [AvaloniaFact]
    public void FullScreenButtonMatchesWpfWindowNamesTextAndTooltip()
    {
        var window = new KeyboardModePopoutWindow();
        window.Show();

        Assert.Equal(KeyboardModePopoutPresenter.Title, window.Title);
        Assert.Equal((1100d, 680d, 520d, 360d), (window.Width, window.Height, window.MinWidth, window.MinHeight));
        Assert.Equal("Keyboard mode keyboard view", AutomationProperties.GetName(window));
        var button = window.FindControl<Button>("FullScreenButton")!;
        Assert.Equal("Toggle full screen", AutomationProperties.GetName(button));
        Assert.Equal(KeyboardModePopoutPresenter.FullScreenButtonText(false), (string)button.Content!);
        Assert.Equal(KeyboardModePopoutPresenter.FullScreenTooltip, ToolTip.GetTip(button));
        Assert.NotNull(window.FindControl<Panel>("KeysHost"));
        window.Close();
    }

    [AvaloniaFact]
    public void FullScreenButtonTogglesTheWindowAndItsText()
    {
        var window = new KeyboardModePopoutWindow();
        window.Show();
        var button = window.FindControl<Button>("FullScreenButton")!;

        DialogTestKit.Click(button);
        Assert.True(window.IsFullScreen);
        Assert.Equal(KeyboardModePopoutPresenter.FullScreenButtonText(true), (string)button.Content!);

        DialogTestKit.Click(button);
        Assert.False(window.IsFullScreen);
        Assert.Equal(KeyboardModePopoutPresenter.FullScreenButtonText(false), (string)button.Content!);
        window.Close();
    }

    [AvaloniaFact]
    public void F11TogglesFullScreenAndEscOnlyLeavesIt()
    {
        var window = new KeyboardModePopoutWindow();
        window.Show();

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = window });
        Assert.False(window.IsFullScreen);

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F11, Source = window });
        Assert.True(window.IsFullScreen);

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = window });
        Assert.False(window.IsFullScreen);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false, "keyboard-mode-popout-dark.png")]
    [InlineData(true, "keyboard-mode-popout-light.png")]
    public void RendersPngInEachTheme(bool light, string fileName)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new KeyboardModePopoutWindow();
        var path = DialogTestKit.SavePng(window, nameof(KeyboardModePopoutWindowTests), fileName);
        Assert.True(new System.IO.FileInfo(path).Length > 0);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullScreenTextReadsAtLeastFourAndAHalfToOneOnTheWindow(bool light)
    {
        using var theme = DialogTestKit.UseTheme(light);
        var window = new KeyboardModePopoutWindow();
        window.Show();
        var button = window.FindControl<Button>("FullScreenButton")!;
        Assert.True(DialogTestKit.Contrast(button.Foreground, window.Background, "Full screen button") >= 4.5);
        window.Close();
    }

    [AvaloniaFact]
    public void PreviewToolsMenuOpensKeyboardModeView()
    {
        var main = new MainWindow();
        main.Show();
        var item = main.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Keyboard mode: view in its own window");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var opened = main.OwnedWindows.OfType<KeyboardModePopoutWindow>().ToList();
        Assert.Single(opened);
        foreach (var window in opened) window.Close();
        main.Close();
    }
}
