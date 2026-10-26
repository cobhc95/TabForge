using System;
using System.IO;
using System.Linq;
using TabForge.Avalonia.Playback;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that the Avalonia composition's engine client asks the fail-closed plug-in trust: a file nobody approved is sent as Skip.
/// Does not own: the trust rules (PluginTrust, self-tests in the app) or the engine itself.
/// Tests: this class.
/// </summary>
public class AvaloniaPlaybackCompositionTests
{
    [Fact]
    public void EngineClientSendsAnUnapprovedPluginAsSkip()
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-avalonia-trust-" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A });
        try
        {
            using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
            var spec = client.Trust.BuildSpecs(new[] { new PluginSlot { Name = "unapproved", Path = path, Format = "VST2" } }, Array.Empty<string>(), new PluginSettings()).Single();
            Assert.True(spec.Skip);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MidiOutputListsTheMapperFirst()
    {
        using var output = AvaloniaPlaybackComposition.CreateMidiOutput();
        Assert.Equal(TabForge.Playback.WinMmMidiOutput.MapperName, output.Devices[0].Name);
    }
}
