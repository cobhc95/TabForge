using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>The scanner's session cache is one process-wide entry: these tests must not race another class's scan.</summary>
[CollectionDefinition("Vst32Catalog", DisableParallelization = true)]
public sealed class Vst32CatalogCollectionDefinition { }

/// <summary>
/// Owns: a 32-bit VST2 DLL as a first-class bridged plug-in: the scan label, scan/trust fingerprinting, the session and persisted
/// caches, old-label migration, the known-list fallback, the catalog memory, the hand-picked file and the browser row.
/// Does not own: engine routing or the helper process (Vst32RoutingTests), the scanner itself (RfxVstFixTests).
/// Tests: this class.
/// </summary>
[Collection("Vst32Catalog")]
public sealed class Vst32CatalogTests
{
    [Vst32FixtureFact]
    public void EveryReal32BitFixtureIsDetectedAndShownAsBridged()
    {
        var paths = Vst32TestAssets.RealFixtures.Select(name => Path.Combine(Vst32TestAssets.FixturesFolder, name + ".dll")).ToList();
        foreach (var path in paths) Assert.True(File.Exists(path), $"missing fixture: {path}");

        var trust = new PluginTrust();
        var found = VstScannerService.Scan([Vst32TestAssets.FixturesFolder], CancellationToken.None, trust, new Vst32TestAssets.NoFiles(), forceRefresh: true);

        foreach (var path in paths)
        {
            Assert.True(PeExports.IsVst2Plugin32(path), $"{Path.GetFileName(path)} must be a 32-bit VST2 DLL");
            Assert.False(PeExports.IsVst2Plugin(path), $"{Path.GetFileName(path)} must not read as a 64-bit DLL");
            var row = found.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(row);
            Assert.Equal(PluginFormats.Bridged32, row!.Format);
            Assert.True(row.IsBridged32);
            var handPicked = PluginBrowserSession.FromFile(path, out var problem);
            Assert.Null(problem);
            Assert.Equal(PluginFormats.Bridged32, handPicked!.Format);
        }
    }

    [Vst32BridgeAvailableFact]
    public void AScanListsA32BitDllAsBridgedAndFingerprintsIt()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var file = folder.Write("Old32.dll", Vst32TestAssets.PeImage(x86: true));
        var trust = new PluginTrust();

        var found = VstScannerService.Scan(new[] { folder.Path }, CancellationToken.None, trust, new Vst32TestAssets.NoFiles(), forceRefresh: true);

        var row = Assert.Single(found);
        Assert.Equal(PluginFormats.Bridged32, row.Format);
        Assert.True(row.IsBridged32);
        // A 32-bit entry is part of the scan baseline now: the trust check knows it was scanned (approval is still needed).
        Assert.Equal(PluginTrust.ReasonNeedsApproval, trust.Check(file, new PluginSettings(), new Vst32TestAssets.NoFiles()).Reason);
    }

    [Vst32BridgeAvailableFact]
    public void TheSessionCacheKeepsABridgedRowUntilAForcedRescan()
    {
        VstScannerService.ClearSessionCache();
        using var folder = new Vst32TestAssets.TempFolder();
        var file = folder.Write("Cached32.dll", Vst32TestAssets.PeImage(x86: true));
        try
        {
            var trust = new PluginTrust();
            var settings = new PluginSettings { RememberScan = false };
            Assert.Single(VstScannerService.Scan(new[] { folder.Path }, CancellationToken.None, null, trust, new Vst32TestAssets.NoFiles(), settings, forceRefresh: true));

            File.Delete(file);
            var cached = VstScannerService.Scan(new[] { folder.Path }, CancellationToken.None, null, trust, new Vst32TestAssets.NoFiles(), settings, forceRefresh: false);
            Assert.Equal(PluginFormats.Bridged32, Assert.Single(cached).Format);

            Assert.Empty(VstScannerService.Scan(new[] { folder.Path }, CancellationToken.None, null, trust, new Vst32TestAssets.NoFiles(), settings, forceRefresh: true));
        }
        finally { VstScannerService.ClearSessionCache(); }
    }

    [Fact]
    public void APersistedScanCacheKeepsTheBridgedLabelThroughJsonAndTheValidator()
    {
        var settings = new PluginSettings
        {
            RememberScan = true,
            ScanCache = [new KnownPlugin { Name = "Old32", Path = @"C:\vst\Old32.dll", Format = PluginFormats.Bridged32 }],
        };

        var json = JsonSerializer.Serialize(settings);
        var loaded = JsonSerializer.Deserialize<PluginSettings>(json)!;
        var app = SettingsValidator.Normalize(new AppSettings { Plugins = loaded });

        Assert.Equal(PluginFormats.Bridged32, Assert.Single(app.Plugins.ScanCache).Format);
    }

    [Fact]
    public void ThePersistedCacheKeepsThe64BitBridgedLabelThroughJsonAndTheValidator()
    {
        var settings = new PluginSettings
        {
            RememberScan = true,
            ScanCache = [new KnownPlugin { Name = "New64", Path = @"C:\vst\New64.dll", Format = PluginFormats.Bridged64 }],
        };

        var json = JsonSerializer.Serialize(settings);
        var loaded = JsonSerializer.Deserialize<PluginSettings>(json)!;
        var app = SettingsValidator.Normalize(new AppSettings { Plugins = loaded });

        Assert.Equal(PluginFormats.Bridged64, Assert.Single(app.Plugins.ScanCache).Format);
        Assert.True(PluginFormats.IsKnown(PluginFormats.Bridged64));
    }

    [Fact]
    public void TheOldUnsupportedLabelLoadsAsBridgedEverywhere()
    {
        const string old = @"C:\vst\Old32.dll";
        var plugins = new PluginSettings
        {
            RememberScan = true,
            ScanCache = [new KnownPlugin { Name = "Old32", Path = old, Format = PluginFormats.LegacyUnsupported32 }],
            Probed = [new KnownPlugin { Name = "Old32", Path = old, Format = PluginFormats.LegacyUnsupported32, Role = "Effect" }],
            LastScan = [new VstPluginInfo("Old32", old, PluginFormats.LegacyUnsupported32)],
            AutoChains = [new AutoChain { Plugins = [new PluginSlot { Name = "Old32", Path = old, Format = PluginFormats.LegacyUnsupported32 }] }],
        };
        var app = SettingsValidator.Normalize(new AppSettings { Plugins = plugins });

        Assert.All(app.Plugins.ScanCache.Concat(app.Plugins.Probed), p => Assert.Equal(PluginFormats.Bridged32, p.Format));
        Assert.Equal(PluginFormats.Bridged32, Assert.Single(app.Plugins.LastScan).Format);
        Assert.Equal(PluginFormats.Bridged32, Assert.Single(app.Plugins.AutoChains).Plugins.Single().Format);

        // The known list normalises a raw LastScan entry too, before any validator has run.
        var known = KnownPluginList.Of(new PluginSettings { LastScan = [new VstPluginInfo("Old32", old, PluginFormats.LegacyUnsupported32)] }, Array.Empty<PluginSlot>());
        Assert.Equal(PluginFormats.Bridged32, Assert.Single(known).Format);

        // A raw persisted cache read by the scanner (before any validator) maps the old label too.
        var raw = new PluginSettings { RememberScan = true, ScanCache = [new KnownPlugin { Name = "Old32", Path = old, Format = PluginFormats.LegacyUnsupported32 }] };
        var rows = VstScannerService.Scan(Array.Empty<string>(), CancellationToken.None, null, new PluginTrust(), new Vst32TestAssets.NoFiles(), raw, forceRefresh: false);
        Assert.Equal(PluginFormats.Bridged32, Assert.Single(rows).Format);

        // The Add plug-in window's own cache load maps it before the validator ever runs.
        var browser = new PluginBrowserSession(raw, new PluginTrust(), new Vst32TestAssets.NoFiles(), () => { });
        Assert.Equal(PluginFormats.Bridged32, Assert.Single(browser.All).Format);
    }

    [Fact]
    public void PluginTrustApprovesAndFingerprintsA32BitPath()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var file = folder.Write("Trusted32.dll", Vst32TestAssets.PeImage(x86: true));
        var settings = new PluginSettings();
        var trust = new PluginTrust();

        Assert.False(trust.IsTrusted(file, settings, new Vst32TestAssets.NoFiles()));
        trust.Approve(settings, file, new Vst32TestAssets.NoFiles());

        Assert.True(trust.IsTrusted(file, settings, new Vst32TestAssets.NoFiles()));
        Assert.Contains(settings.TrustRecords, r => string.Equals(r.Path, file, StringComparison.OrdinalIgnoreCase) && r.Sha256.Length == 64);
        Assert.Equal(64, trust.ProbeHash(file, settings, new Vst32TestAssets.NoFiles()).Length);
    }

    [Vst32BridgeAvailableFact]
    public void KnownPluginListShowsBridgedForAnApproved32BitPathWithoutAStoredFormat()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var file = folder.Write("Approved32.dll", Vst32TestAssets.PeImage(x86: true));
        var settings = new PluginSettings();
        settings.ApprovedPluginPaths.Add(file);

        var row = Assert.Single(KnownPluginList.Of(settings, Array.Empty<PluginSlot>()));
        Assert.Equal(PluginFormats.Bridged32, row.Format);
        Assert.True(row.IsBridged32);
    }

    [Vst32BridgeAvailableFact]
    public void PluginCatalogRemembersTheBridgedFormat()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var file = folder.Write("Catalog32.dll", Vst32TestAssets.PeImage(x86: true));
        var settings = new PluginSettings();
        var trust = new PluginTrust();
        trust.Approve(settings, file, new Vst32TestAssets.NoFiles());
        PluginCatalog.ProbeOverride = (_, hash, _) => hash.Length == 64 ? ("Effect", "Acme", "Catalog32") : null;
        try
        {
            var described = PluginCatalog.Describe(file, settings, trust, new Vst32TestAssets.NoFiles());
            Assert.Equal(("Effect", "Acme"), (described.Role, described.Vendor));
            Assert.Equal(PluginFormats.Bridged32, Assert.Single(settings.Probed).Format);
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }

    [Vst32BridgeAvailableFact]
    public void FromFileAcceptsA32BitDllAndNamesItBridged()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var file = folder.Write("Hand32.dll", Vst32TestAssets.PeImage(x86: true));

        var info = PluginBrowserSession.FromFile(file, out var problem);

        Assert.Null(problem);
        Assert.NotNull(info);
        Assert.Equal(("Hand32", PluginFormats.Bridged32), (info!.Name, info.Format));
        Assert.True(info.IsBridged32);
    }

    [AvaloniaFact]
    public void TheBrowserShowsAndChoosesA32BitRowWithoutRejectingIt()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var file = folder.Write("Browser32.dll", Vst32TestAssets.PeImage(x86: true));
        var chosen = new VstPluginInfo("Browser32", file, PluginFormats.Bridged32, "Acme", "Effect");
        var settings = new PluginSettings { LastScan = [chosen], RememberScan = false };
        var session = new PluginBrowserSession(settings, new PluginTrust(), new Vst32TestAssets.NoFiles(), () => { });
        var window = new PluginBrowserWindow(session, new TabForge.Avalonia.Platform.AvaloniaFileDialogs(), _ => { });
        try
        {
            window.Show();
            var list = window.GetLogicalDescendants().OfType<ListBox>().Single(c => c.Name == "PluginList");
            var rows = ((IEnumerable<PluginBrowserRow>)list.ItemsSource!).ToList();
            var index = rows.FindIndex(r => r.Info.Path == file);
            Assert.True(index >= 0);
            Assert.Equal(PluginFormats.Bridged32, rows[index].Format);
            list.SelectedIndex = index;
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = list });
            Assert.Equal(chosen, window.Chosen);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }

}
