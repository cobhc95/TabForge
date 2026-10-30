using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Sound bank catalogue (each entry verified and complete), old engine values mapping, the row state per status, the
//   delete safety rules (outside the folder, the user's own file, the in-use switch) and the licence text source.
// Does not own: the code it checks.
// Tests: SoundBankCatalog, SoundEngines, SoundFontStore, SoundFontDownloader.
public sealed class SoundBankCatalogTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "tf-banks-" + Guid.NewGuid().ToString("N"));

    private static string MakeFile(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    [Fact]
    public void EveryCatalogEntryIsComplete()
    {
        Assert.Equal(new[] { "GeneralUser GS", "FluidR3 GM", "MuseScore General" }, SoundBankCatalog.All.Select(bank => bank.Name));
        foreach (var bank in SoundBankCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(bank.Id), bank.Name + " id");
            Assert.False(string.IsNullOrWhiteSpace(bank.Name));
            Assert.False(string.IsNullOrWhiteSpace(bank.Author));
            Assert.False(string.IsNullOrWhiteSpace(bank.LicenceName));
            Assert.StartsWith("https://", bank.Url);
            Assert.Equal(64, bank.Sha256.Length);
            Assert.All(bank.Sha256, c => Assert.True(Uri.IsHexDigit(c), bank.Name + " hash"));
            Assert.True(bank.Bytes > 0);
            Assert.True(bank.IsVerified);
            var licence = SoundBankCatalog.EmbeddedLicence(bank);
            Assert.True(licence.Length > 100, bank.Name + " licence text");
            Assert.Contains(bank.LicenceName.Split(' ')[0], licence);
        }
        Assert.DoesNotContain(SoundBankCatalog.All, bank => bank.Name.Contains("Timbres", StringComparison.OrdinalIgnoreCase)
            || bank.Name.Contains("Arachno", StringComparison.OrdinalIgnoreCase) || bank.Name.Contains("SGM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheUserFileIsRememberedAndOnlyPlayedWhenItExists()
    {
        var plugins = new PluginSettings { UserSoundFont = @"C:\Music\mine.sf2", SoundEngine = SoundEngines.FluidR3 };
        var text = System.Text.Json.JsonSerializer.Serialize(plugins);
        var back = System.Text.Json.JsonSerializer.Deserialize<PluginSettings>(text)!;
        Assert.Equal(@"C:\Music\mine.sf2", back.UserSoundFont);
        Assert.Equal(SoundEngines.FluidR3, back.SoundEngine);
        Assert.Empty(SoundFontStore.UserPath(back));   // the file does not exist here: nothing is played
    }

    [Fact]
    public void OldSettingsValuesKeepWorking()
    {
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve("TabForge SoundFont"));
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve("TabForge SoundFont", windowsHost: false));
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve("bank:generaluser-gs"));
        Assert.Equal(SoundEngines.WindowsGm, SoundEngines.Resolve("Windows GM"));
        Assert.Equal(SoundEngines.SoundFont, SoundEngines.Resolve("Windows GM", windowsHost: false));
        Assert.Equal(SoundEngines.FluidR3, SoundEngines.Resolve("bank:fluidr3-gm"));
        Assert.Equal(SoundEngines.MuseScoreGeneral, SoundEngines.Resolve("MuseScore General"));
        Assert.Equal(SoundEngines.None, SoundEngines.Resolve("None"));
        Assert.Equal(OperatingSystem.IsWindows() ? SoundEngines.WindowsGm : SoundEngines.SoundFont, SoundEngines.Resolve(""));
        Assert.Contains(SoundEngines.None, SoundEngines.All);
        Assert.Contains(SoundEngines.OwnFile, SoundEngines.All);
        Assert.Equal(OperatingSystem.IsWindows() ? SoundEngines.WindowsGm : SoundEngines.SoundFont, SoundEngines.Default);
    }

    [Fact]
    public void RowStateFollowsTheFilesAndTheChosenEngine()
    {
        var bank = SoundBankCatalog.FluidR3;
        var fresh = SoundBankRowState.Of(bank, SoundEngines.None, windowsHost: true, downloaded: false, builtIn: false);
        Assert.Equal(SoundBankStatus.NotDownloaded, fresh.Status);
        Assert.True(fresh.ShowDownload);
        Assert.False(fresh.ShowUse || fresh.ShowDelete || fresh.ShowDownloadAgain);
        Assert.Equal("Not downloaded", fresh.Badge);

        var builtIn = SoundBankRowState.Of(bank, SoundEngines.None, windowsHost: false, downloaded: false, builtIn: true);
        Assert.Equal(SoundBankStatus.BuiltIn, builtIn.Status);
        Assert.True(builtIn.ShowUse);
        Assert.False(builtIn.ShowDelete);

        var downloaded = SoundBankRowState.Of(bank, SoundEngines.None, windowsHost: true, downloaded: true, builtIn: false);
        Assert.Equal(SoundBankStatus.Downloaded, downloaded.Status);
        Assert.True(downloaded.ShowUse && downloaded.ShowDownloadAgain && downloaded.ShowDelete);
        Assert.False(downloaded.ShowDownload);

        var inUse = SoundBankRowState.Of(bank, SoundEngines.FluidR3, windowsHost: true, downloaded: true, builtIn: false);
        Assert.Equal(SoundBankStatus.InUse, inUse.Status);
        Assert.Equal("In use", inUse.Badge);
        Assert.False(inUse.ShowUse);
    }

    [Fact]
    public void DeleteRefusesAPathOutsideTheSoundFontsFolder()
    {
        var folder = TempFolder();
        var outsideFolder = Path.Combine(Path.GetTempPath(), "tf-banks-outside-" + Guid.NewGuid().ToString("N"));
        var outside = MakeFile(outsideFolder, "bank.sf2");
        var error = SoundFontStore.DeleteFile(outside, folder);
        Assert.Contains("outside", error);
        Assert.True(File.Exists(outside));
        Assert.False(SoundFontStore.IsInsideFolder(outside, folder));
        Assert.True(SoundFontStore.IsInsideFolder(Path.Combine(folder, "bank.sf2"), folder));
    }

    [Fact]
    public void DeleteNeverRemovesTheUsersOwnFile()
    {
        var folder = TempFolder();
        var own = MakeFile(folder, SoundBankCatalog.FluidR3.FileName);
        var settings = new PluginSettings { SoundEngine = SoundEngines.FluidR3, UserSoundFont = own };
        var error = SoundFontStore.DeleteIn(SoundBankCatalog.FluidR3, folder, settings, windowsHost: true);
        Assert.Contains("your own", error);
        Assert.True(File.Exists(own));
        Assert.Equal(SoundEngines.FluidR3, settings.SoundEngine);   // nothing was switched either
    }

    [Fact]
    public void DeletingTheBankInUseSwitchesTheEngineFirst()
    {
        Assert.Equal(SoundEngines.WindowsGm, SoundFontStore.EngineAfterDelete(SoundBankCatalog.FluidR3, windowsHost: true, _ => true));
        Assert.Equal(SoundEngines.MuseScoreGeneral, SoundFontStore.EngineAfterDelete(SoundBankCatalog.FluidR3, windowsHost: false,
            other => other.Id == SoundBankCatalog.MuseScoreGeneral.Id));
        Assert.Equal(SoundEngines.None, SoundFontStore.EngineAfterDelete(SoundBankCatalog.FluidR3, windowsHost: false, _ => false));
        Assert.Equal(SoundEngines.SoundFont, SoundFontStore.EngineAfterDelete(SoundBankCatalog.MuseScoreGeneral, windowsHost: false,
            other => other.Id == SoundBankCatalog.GeneralUserGs.Id));

        var folder = TempFolder();
        var path = MakeFile(folder, SoundBankCatalog.FluidR3.FileName);
        var settings = new PluginSettings { SoundEngine = SoundEngines.FluidR3 };
        var error = SoundFontStore.DeleteIn(SoundBankCatalog.FluidR3, folder, settings, windowsHost: false, _ => false);
        Assert.Null(error);
        Assert.Equal(SoundEngines.None, settings.SoundEngine);
        Assert.False(File.Exists(path));

        var windowsFolder = TempFolder();
        MakeFile(windowsFolder, SoundBankCatalog.FluidR3.FileName);
        var windowsSettings = new PluginSettings { SoundEngine = SoundEngines.FluidR3 };
        Assert.Null(SoundFontStore.DeleteIn(SoundBankCatalog.FluidR3, windowsFolder, windowsSettings, windowsHost: true));
        Assert.Equal(SoundEngines.WindowsGm, windowsSettings.SoundEngine);
        Assert.False(File.Exists(Path.Combine(windowsFolder, SoundBankCatalog.FluidR3.FileName)));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) => answer(request, cancel);
    }

    [Fact]
    public async Task AWrongSizeOrHashLeavesNoPartialFile()
    {
        var target = Path.Combine(TempFolder(), "bank.sf2");
        var bytes = new byte[1000];
        var right = Convert.ToHexString(SHA256.HashData(bytes));
        var wrongSize = new SoundBank("test", "Test", "A", "C", "https://fake.test/a.sf2", right, Bytes: 2000,
            "MIT", "https://fake.test", "bank.sf2", "TabForge.Resources.SoundBanks.fluidr3-gm.txt");
        var sizeError = await new SoundFontDownloader(wrongSize, new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })))
            .DownloadAsync(target, null, CancellationToken.None);
        Assert.Contains("instead of the expected", sizeError);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));

        var wrongHash = new SoundBank("test2", "Test", "A", "C", "https://fake.test/a.sf2", new string('0', 64), bytes.Length,
            "MIT", "https://fake.test", "bank.sf2", "TabForge.Resources.SoundBanks.fluidr3-gm.txt");
        var hashError = await new SoundFontDownloader(wrongHash, new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })))
            .DownloadAsync(target, null, CancellationToken.None);
        Assert.Contains("checksum", hashError);
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public void LicenceTextComesFromThePackageFileWhenThereIsOne()
    {
        var folder = TempFolder();
        var bankPath = MakeFile(folder, SoundBankCatalog.FluidR3.FileName);
        Assert.Contains("Fluid (R3)", SoundBankCatalog.LicenceText(SoundBankCatalog.FluidR3, bankPath));   // the embedded text
        File.WriteAllText(bankPath + ".txt", "Licence shipped in the package");
        Assert.Equal("Licence shipped in the package", SoundBankCatalog.LicenceText(SoundBankCatalog.FluidR3, bankPath));
    }
}
