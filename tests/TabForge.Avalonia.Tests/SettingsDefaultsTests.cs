using TabForge.Avalonia.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fallback settings helper's contract, so a reader with no store gets a fresh default object and never a shared one that another
/// reader's change would leak into.
/// Does not own: the settings values themselves (AppSettings) or the settings file (AppSettingsStore).
/// Tests: this class.
/// </summary>
public class SettingsDefaultsTests
{
    [Fact]
    public void FallbackIsAFreshDefaultInstanceEachTime()
    {
        var first = SettingsDefaults.Fallback();
        var second = SettingsDefaults.Fallback();
        Assert.NotSame(first, second);
        Assert.NotSame(first, new AppSettings());
        var defaults = new AppSettings();
        Assert.Equal(defaults.InstrumentHeight, first.InstrumentHeight);
        Assert.Equal(defaults.ShowArrangement, first.ShowArrangement);
    }
}
