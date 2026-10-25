using System;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Reaper-only host function lookup (audioMaster 0xDEADBEEF) a ReaPlug makes: known names answer a safe function, unknown names answer none.
/// Does not own: real plug-ins (none is loaded in tests).
/// Tests: this class.
/// </summary>
public class Vst2ReaperHostApiTests
{
    private static Vst2Plugin Host() => Vst2Plugin.TestEffect.Create(48000, 256, new EngineThreads(new EngineLogWriter()));

    [Fact]
    public void ResourcePathAndIniFileAnswerPaths()
    {
        using var plugin = Host();
        var resource = Vst2Plugin.TestEffect.ProbeReaperString("GetResourcePath");
        var ini = Vst2Plugin.TestEffect.ProbeReaperString("get_ini_file");
        Assert.False(string.IsNullOrEmpty(resource));
        Assert.StartsWith(resource!, ini);
        Assert.EndsWith("reaper.ini", ini);
    }

    [Fact]
    public void UnknownNamesAnswerNoFunction()
    {
        using var plugin = Host();
        Assert.Null(Vst2Plugin.TestEffect.ProbeReaperString("NoSuchReaperFunction"));
        Assert.True(Vst2Plugin.TestEffect.ProbeResolveFn("GetResourcePath"));
        Assert.False(Vst2Plugin.TestEffect.ProbeResolveFn("NoSuchReaperFunction"));
    }
}
