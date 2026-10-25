using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TabForge.Avalonia.Tests.TestAppBuilder))]

namespace TabForge.Avalonia.Tests;

/// <summary>The headless Avalonia platform for the tests. Owns: test app set-up. Does not own: the app (App).</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Owns: the preview window check. Tests: the window opens headless with the preview title.</summary>
public class MainWindowTests
{
    [AvaloniaFact]
    public void MainWindowOpensWithPreviewTitle()
    {
        var window = new MainWindow();
        window.Show();
        Assert.True(window.IsVisible);
        Assert.Equal("TabForge", window.Title);
        window.Close();
    }
}
