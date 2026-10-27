using System.Linq;
using System.Text;
using AlphaTab.Model;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the settings check of saved plug-in chains (AutoChains, StartupTracks: a plug-in format is "", VST2 or VST3) and the
/// refusal of a DTD in a Guitar Pro bend-curve part (GuitarProBendCurve keeps the unshaped curve).
/// Does not own: the settings file (AppSettingsStore) or the score import (GuitarProImporter).
/// Tests: this class.
/// </summary>
public sealed class SettingsAndXmlSafetyTests
{
    [Fact]
    public void SavedChainPluginWithAnUnknownFormatReadsAsNoFormat()
    {
        var settings = new AppSettings();
        settings.Plugins.AutoChains.Add(new AutoChain { Key = "gm:1", Plugins = new() { new PluginSlot { Name = "Odd", Path = "", Format = "VST9" } } });

        var normalized = SettingsValidator.Normalize(settings);

        Assert.Equal("", normalized.Plugins.AutoChains.Single().Plugins.Single().Format);
    }

    [Fact]
    public void BendCurvePartWithADtdIsLeftUnshaped()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE GPIF [<!ENTITY bend \"x\">]><GPIF/>";
        var bytes = Encoding.UTF8.GetBytes(xml);

        var result = GuitarProBendCurve.Patch(bytes, new Score());

        Assert.Same(bytes, result);
    }
}
