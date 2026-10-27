using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: ThemeColourCache keeps a looked-up colour until a resource of the owner changes, then reads the new one.
/// Tests: TabForge.Avalonia.Platform.ThemeColourCache.
/// </summary>
public class ThemeColourCacheTests
{
    [AvaloniaFact]
    public void ColourIsKeptUntilTheResourcesChange()
    {
        var owner = new Border();
        owner.Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(1, 2, 3));
        var cache = new ThemeColourCache(owner);
        Assert.Equal(1, cache.Get("AccentBrush").R);
        owner.Resources["AccentBrush"] = new SolidColorBrush(Color.FromRgb(9, 2, 3));
        owner.Resources["Other"] = new SolidColorBrush(Colors.Red);
        Assert.Equal(9, cache.Get("AccentBrush").R);
    }
}
