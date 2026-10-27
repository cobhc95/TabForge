using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TabForge.Avalonia.Tests.TestAppBuilder))]

namespace TabForge.Avalonia.Tests;

/// <summary>The headless Avalonia platform for the tests. Owns: test app set-up. Does not own: the app (App).</summary>
public static class TestAppBuilder
{
    // Spacing is Comfortable (factor 1.0) for the tests: the layout numbers are written for it; Compact is the product default and a test that needs it sets it explicitly.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .AfterSetup(_ => TabForge.Avalonia.UiDensityResources.Publish(global::Avalonia.Application.Current!.Resources, new TabForge.Services.AppearanceSettings { Density = "Comfortable" }));
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
