using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Views.Fx;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the FX chain import/export review fixes: file-name and vendor matching, one spelling of a VST3 class id, Reaper's folder list,
/// duplicate plug-ins in an export, the plug-in info wait for a slow plug-in, and "remember plug-ins" being on by default.
/// Does not own: the basic matching (RfxImportMatchTests) or the engine's answers.
/// Tests: this class.
/// </summary>
public class RfxReviewFixTests
{
    private const string Id = "56534450726F3351756F6E7461000000", Dashed = "56534450-726F-3351-756F-6E7461000000";

    private static ReaperChainImporter.Entry Entry(string file, string vendor = "Acme") =>
        new("Plug", vendor, file, "VST2", false, true, null, null);

    [Fact]
    public void ABare64IsPartOfTheNameButAnX64SuffixIsNot()
    {
        var m = new VstPluginInfo("M", @"C:\vst\M.dll", "VST2", "Acme");
        var bulldozer = new VstPluginInfo("Bulldozer", @"C:\vst\Bulldozer_x64.dll", "VST2", "Acme");
        Assert.Null(ReaperChainImporter.Match(Entry("M64.dll"), new[] { m }));
        Assert.Same(bulldozer, ReaperChainImporter.Match(Entry("Bulldozer.dll"), new[] { bulldozer }));
    }

    [Fact]
    public void TheBareNameMatchNeedsTheSameVendor()
    {
        var other = new VstPluginInfo("Bulldozer", @"C:\vst\Bulldozer_x64.dll", "VST2", "Someone Else");
        Assert.Null(ReaperChainImporter.Match(Entry("Bulldozer.dll"), new[] { other }));
        Assert.False(ReaperChainImporter.SameVendor("Acme", "Acme Audio Ltd."));
        Assert.True(ReaperChainImporter.SameVendor(" ACME ", "acme"));
    }

    [Fact]
    public void TheOtherFormatFallbackNeedsBothVendorsKnownAndEqual()
    {
        var vst3 = new VstPluginInfo("Plug", @"C:\vst\Plug.vst3", "VST3", "");
        Assert.Null(ReaperChainImporter.Match(Entry("Plug.dll", ""), new[] { vst3 }));   // neither maker is known: the name alone is no proof
        Assert.Null(ReaperChainImporter.Match(Entry("Plug.dll", "Acme"), new[] { vst3 }));
        var acme = vst3 with { Vendor = "ACME" };
        Assert.Same(acme, ReaperChainImporter.Match(Entry("Plug.dll", "Acme"), new[] { acme }));
        Assert.False(ReaperChainImporter.SameVendor("", ""));
        Assert.False(ReaperChainImporter.SameVendor("Acme", ""));
    }

    [Fact]
    public void ASameFormatBareNameMatchStillAllowsOneUnknownVendor()
    {
        var bulldozer = new VstPluginInfo("Bulldozer", @"C:\vst\Bulldozer_x64.dll", "VST2", "Acme");
        Assert.Same(bulldozer, ReaperChainImporter.Match(Entry("Bulldozer.dll", ""), new[] { bulldozer }));
    }

    [Theory]
    [InlineData("", "", false)]      // same file stem, no maker named on either side
    [InlineData("Acme", "", false)]
    [InlineData("", "Acme", false)]
    [InlineData("Acme", "Other", false)]
    [InlineData("Acme", "acme", true)]
    public void SavedSettingsComeInOnlyForTheSamePluginOfTheSameKnownMaker(string fileVendor, string catalogVendor, bool kept)
    {
        var entry = new ReaperChainImporter.Entry("Plug", fileVendor, "Plug.dll", "VST2", false, true, new byte[] { 1, 2, 3, 4 }, null);
        var built = ReaperChainImporter.Build(new ReaperChainImporter.Parsed(new List<ReaperChainImporter.Entry> { entry }, new List<string>()),
            new[] { new VstPluginInfo("Plug", @"C:\vst\Plug.dll", "VST2", catalogVendor) });

        Assert.Equal(kept, built.Chain.Single().State is not null);
        Assert.Equal(!kept, built.Report.Contains("Loaded with default settings"));
    }

    [Fact]
    public void ASameVst3ClassIdKeepsItsSettingsWhateverTheVendorsSay()
    {
        var entry = new ReaperChainImporter.Entry("Plug", "", "Plug.vst3", "VST3", false, true, new byte[] { 1, 2, 3, 4 }, null, ClassId: Id);
        var built = ReaperChainImporter.Build(new ReaperChainImporter.Parsed(new List<ReaperChainImporter.Entry> { entry }, new List<string>()),
            new[] { new VstPluginInfo("Plug", @"C:\vst\Plug.vst3", "VST3", "", "Effect", Id) });

        Assert.NotNull(built.Chain.Single().State);
    }

    [Theory]
    [InlineData(Id, Id)]
    [InlineData(Dashed, Id)]
    [InlineData("{" + Dashed + "}", Id)]
    [InlineData("56534450726f3351756f6e7461000000", Id)]
    [InlineData("1234", "")]
    [InlineData("", "")]
    public void AClassIdHasOneSpelling(string text, string expected) => Assert.Equal(expected, Vst3ClassId.Normalize(text));

    [Fact]
    public void ADashedAndAPlainClassIdMatch()
    {
        var entry = ReaperChainImporter.Parse($"<VST \"VST3: Pro-Q (Acme)\" \"Renamed.vst3\" 0 \"\" 0{{{Dashed}}} \"\"\n>\n").Entries.Single();
        Assert.Equal(Id, entry.ClassId);
        var plain = new VstPluginInfo("Pro-Q", @"C:\vst\A.vst3", "VST3", "Acme", "Effect", Id);
        var dashed = new VstPluginInfo("Pro-Q", @"C:\vst\B.vst3", "VST3", "Acme", "Effect", Dashed.ToLowerInvariant());
        Assert.Same(plain, ReaperChainImporter.Match(entry, new[] { plain }));
        Assert.Same(dashed, ReaperChainImporter.Match(entry, new[] { dashed }));
    }

    [Fact]
    public void ReapersFolderListIsCleanedAndFallsBackToTheOlderKey()
    {
        var dir = Directory.CreateTempSubdirectory("rfx-ini2");
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(dir.FullName, "Plugs")).FullName;
            Environment.SetEnvironmentVariable("TF_RFX_TEST_ROOT", dir.FullName);
            var quoted = Path.Combine(dir.FullName, "q.ini");
            File.WriteAllText(quoted, "[REAPER]\nvstpath64=\"%TF_RFX_TEST_ROOT%" + Path.DirectorySeparatorChar + "Plugs\"\n");
            Assert.Equal(new[] { folder }, ReaperPluginFolders.Roots(quoted));
            var old = Path.Combine(dir.FullName, "old.ini");
            File.WriteAllText(old, $"[REAPER]\nvstpath={folder}\n");
            Assert.Equal(new[] { folder }, ReaperPluginFolders.Roots(old));
        }
        finally { Environment.SetEnvironmentVariable("TF_RFX_TEST_ROOT", null); dir.Delete(true); }
    }

    [Fact]
    public void TwoIdenticalPlugInsAreBothExported()
    {
        PluginSlot Slot() => new() { Name = "Same", Path = @"C:\vst\same.dll", Format = "VST2" };
        var slots = new List<PluginSlot> { Slot(), Slot() };
        var info = new LoadedPluginInfo?[] { new(LoadedPluginInfo.Vst2, 1, "", 2, 2, true, "Acme", "Same"), new(LoadedPluginInfo.Vst2, 1, "", 2, 2, true, "Acme", "Same") };
        var items = RfxChainExporter.FromTree(ChainTree.Tree(slots, null), slots, info);
        Assert.Equal(2, items.Count);
        Assert.Equal(2, RfxChainExporter.Export(items).Written);
    }

    [Fact]
    public async Task APlugInThatTakesASecondDoesNotBlankTheOnesAfterIt()
    {
        var request = new AudioEngineClient.InfoRequest { Slot = 0 };
        var info = new LoadedPluginInfo(LoadedPluginInfo.Vst2, 1, "", 2, 2, true, "Acme", "Plug");
        // The engine answers in order and the plug-in in its own process takes 1000 ms to describe itself.
        var engine = Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++) { await Task.Delay(1000); lock (request) request.Replies[i] = info; }
            request.Done.TrySetResult(true);
        });
        await AudioEngineClient.WaitForRepliesAsync(request, 3, AudioEngineClient.DefaultPerPluginMs);
        await engine;
        Assert.Equal(3, request.Replies.Count);
    }

    [Fact]
    public async Task APlugInThatNeverAnswersEndsTheWaitWithWhatCameBefore()
    {
        var request = new AudioEngineClient.InfoRequest { Slot = 0 };
        lock (request) request.Replies[0] = LoadedPluginInfo.Unavailable;
        await AudioEngineClient.WaitForRepliesAsync(request, 3, 100);
        Assert.Single(request.Replies);
    }

    [Fact]
    public void RememberingPlugInsIsOnByDefaultAndAnExplicitOffIsKept()
    {
        Assert.True(new PluginSettings().RememberScan);
        var dir = Directory.CreateTempSubdirectory("rfx-set");
        try
        {
            string Load(string json) { var p = Path.Combine(dir.FullName, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(p, json); return p; }
            Assert.True(SettingsFileService.Load(Load("{}")).Plugins.RememberScan);
            Assert.True(SettingsFileService.Load(Load("{\"Plugins\":{\"RememberScan\":false}}")).Plugins.RememberScan);
            Assert.False(SettingsFileService.Load(Load("{\"Plugins\":{\"RememberScan\":false,\"RememberScanVersion\":1}}")).Plugins.RememberScan);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void PreferencesAndTheBrowserShareTheSameSetting()
    {
        var settings = new AppSettings();
        var row = SettingsCatalog.Build(settings).Single(d => d.Key == "vst.rememberscan");
        Assert.Equal(PluginSettings.RememberScanLabel, row.Title);
        Assert.Equal(true, row.Get());
        row.Set(false);
        Assert.False(settings.Plugins.RememberScan);
        var session = new PluginBrowserSession(settings.Plugins, new PluginTrust(), new TabForge.Platform.Windows.WindowsPluginFileTrust(), () => { });
        Assert.False(session.Settings.RememberScan);
        session.SetRemember(true);
        Assert.Equal(true, row.Get());
    }

    [Fact]
    public void TheScanCacheKeepsItsClassIds()
    {
        var settings = new PluginSettings { RememberScan = true };
        settings.ScanCache.Add(new KnownPlugin { Name = "A", Path = @"C:\vst\a.vst3", Format = "VST3", ClassId = Id });
        var session = new PluginBrowserSession(settings, new PluginTrust(), new TabForge.Platform.Windows.WindowsPluginFileTrust(), () => { });
        session.SetRemember(true);
        Assert.Equal(Id, settings.ScanCache.Single().ClassId);
    }
}
