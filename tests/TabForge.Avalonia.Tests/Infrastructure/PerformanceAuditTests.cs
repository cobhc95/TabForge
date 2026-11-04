// Owns: regression checks for the audit's lookup and cache policies.
// Does not own: UI appearance, plugin probing or production benchmark thresholds.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

public sealed class PerformanceAuditTests(ITestOutputHelper output)
{
    [Fact]
    public void PluginJoinKeepsFirstPathAndExistingMetadataForEverySort()
    {
        VstPluginInfo[] all = [new("One", "same.vst3", "VST3", "", ""), new("Two", "same.vst3", "VST3", "Own", "Instrument"), new("Three", "other.vst3", "VST3", "", "")];
        (string Path, string Vendor, string Role)[] known = [("same.vst3", "First", "Effect"), ("same.vst3", "Second", "Instrument")];
        for (var column = 0; column < 4; column++)
        foreach (var ascending in new[] { true, false })
        {
            var rows = PluginBrowserListPresenter.Rows(all, known, new HashSet<string>(), "", column, ascending);
            Assert.Equal("First", rows.Single(r => r.Name == "One").Vendor);
            Assert.Equal("Effect", rows.Single(r => r.Name == "One").Role);
            Assert.Equal("Own", rows.Single(r => r.Name == "Two").Vendor);
            Assert.Equal("Instrument", rows.Single(r => r.Name == "Two").Role);
        }
    }

    [Fact]
    public void LargePluginCatalogEnumeratesKnownRolesOnlyOnce()
    {
        var all = Enumerable.Range(0, 5000).Select(i => new VstPluginInfo($"Plugin {i:D5}", $"{i}.vst3", "VST3", "", "")).ToArray();
        var enumerations = 0;
        IEnumerable<(string Path, string Vendor, string Role)> Known()
        {
            enumerations++;
            foreach (var p in all) yield return (p.Path, "Vendor", "Effect");
        }
        var watch = Stopwatch.StartNew();
        var rows = PluginBrowserListPresenter.Rows(all, Known(), new HashSet<string>(), "Plugin", 0, true);
        watch.Stop();
        Assert.Equal(1, enumerations);
        Assert.Equal(all.Length, rows.Count);
        output.WriteLine($"5k indexed filter (includes sorting, no UI): {watch.Elapsed.TotalMilliseconds:F3} ms");
        var warm = new double[12];
        for (var i = 0; i < warm.Length; i++)
        {
            watch.Restart();
            PluginBrowserListPresenter.Rows(all, Known(), new HashSet<string>(), "Plugin", 0, true);
            warm[i] = watch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(warm);
        output.WriteLine($"5k indexed filter warm median: {warm[warm.Length / 2]:F3} ms (12 samples; no UI)");
    }

    [Fact]
    public void ParsedSettingsQueryPreservesMatchingAcrossRegistry()
    {
        var registry = SettingsCatalog.Registry(SettingsCatalog.Build(new AppSettings()), new HotkeySettings());
        foreach (var text in new[] { "", "dark", "mic input", "  font  size ", "plugin audio", "not-a-setting" })
        {
            var query = SettingsCatalog.ParseQuery(text);
            foreach (var entry in registry)
                Assert.Equal(SettingsCatalog.Matches(entry, text), query.Matches(entry));
        }
    }
}
