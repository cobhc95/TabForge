namespace TabForge.Services;

// Owns: the Sound banks TabForge offers, each with its official HTTPS address, verified SHA-256 and byte size, and its licence
//   (the text ships as an embedded resource; a licence file next to a downloaded bank is used instead when one is there).
// Does not own: downloading (SoundFontDownloader), the files on disk (SoundFontStore) or the Settings list (PreferencesWindow).
// Tests: SoundBankCatalogTests.
/// <summary>The verified Sound banks: GeneralUser GS (the app's long-standing bank), FluidR3 GM and MuseScore General.</summary>
public static class SoundBankCatalog
{
    public static readonly SoundBank GeneralUserGs = new(
        "generaluser-gs", "GeneralUser GS", "S. Christian Collins", "Copyright © 2000–2025 S. Christian Collins",
        SoundFontDownloader.Url, SoundFontDownloader.Sha256, 32_319_396, "GeneralUser GS License v2.0",
        "https://github.com/mrbumpy409/GeneralUser-GS", "GeneralUser-GS.sf2", "TabForge.Resources.SoundBanks.generaluser-gs.txt");

    public static readonly SoundBank FluidR3 = new(
        "fluidr3-gm", "FluidR3 GM", "Frank Wen", "Copyright © 2000–2002, 2008 Frank Wen",
        "https://github.com/pianobooster/fluid-soundfont/releases/download/v3.1/FluidR3_GM.sf2",
        "74594E8F4250680ADF590507A306655A299935343583256F3B722C48A1BC1CB0", 148_398_306, "MIT License",
        "https://github.com/pianobooster/fluid-soundfont/releases/tag/v3.1", "FluidR3_GM.sf2", "TabForge.Resources.SoundBanks.fluidr3-gm.txt");

    public static readonly SoundBank MuseScoreGeneral = new(
        "musescore-general", "MuseScore General", "S. Christian Collins and MuseScore contributors", "Copyright © 2018–2020 S. Christian Collins, MuseScore contributors and Frank Wen",
        "https://ftp.osuosl.org/pub/musescore/soundfont/MuseScore_General/MuseScore_General.sf2",
        "EE51D2C4B1525E70F19A45909C4FD7A2E26D91D115FA89DBF5A6BC413D8B9BF3", 215_614_036, "MIT License",
        "https://musescore.org/en/handbook/3/soundfonts-and-sfz-files", "MuseScore_General.sf2", "TabForge.Resources.SoundBanks.musescore-general.txt");

    /// <summary>Every bank, in the order the Settings list shows them.</summary>
    public static IReadOnlyList<SoundBank> All { get; } = new[] { GeneralUserGs, FluidR3, MuseScoreGeneral };

    /// <summary>The bank with an id ("generaluser-gs"), or null.</summary>
    public static SoundBank? ById(string? id) => All.FirstOrDefault(bank => string.Equals(bank.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The bank an engine value names, or null for Windows GM, None and the user's own file.</summary>
    public static SoundBank? ForEngine(string? engine) => ById(SoundEngines.BankIdOf(engine));

    /// <summary>Where the bank's source page is (the View licence window's Open source page button).</summary>
    public static string SourcePageOf(SoundBank bank) => bank.SourcePage;

    /// <summary>The licence text the View licence window shows: a licence file beside the downloaded bank if there is one, else the embedded text.</summary>
    public static string LicenceText(SoundBank bank, string? downloadedPath = null)
    {
        if (downloadedPath is { Length: > 0 })
        {
            foreach (var candidate in SidecarLicences(downloadedPath))
                try { if (File.Exists(candidate)) return File.ReadAllText(candidate); } catch (IOException) { /* swallow: the embedded licence is used */ } catch (UnauthorizedAccessException) { /* swallow: the embedded licence is used */ }
        }
        return EmbeddedLicence(bank);
    }

    /// <summary>A licence text stored in the app's resources (never a file the user could change).</summary>
    public static string EmbeddedLicence(SoundBank bank)
    {
        using var stream = typeof(SoundBankCatalog).Assembly.GetManifestResourceStream(bank.LicenceResource);
        if (stream is null) return $"{bank.Name}\n\n{bank.LicenceName}\n{bank.Copyright}\n{bank.Url}";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The names a package licence file may have beside a downloaded bank ("FluidR3_GM.sf2.txt", "LICENSE.txt").</summary>
    private static IEnumerable<string> SidecarLicences(string bankPath)
    {
        var folder = Path.GetDirectoryName(bankPath) ?? "";
        yield return bankPath + ".txt";
        yield return Path.ChangeExtension(bankPath, ".txt");
        yield return Path.ChangeExtension(bankPath, ".license.txt");
        yield return Path.Combine(folder, "LICENSE.txt");
        yield return Path.Combine(folder, "COPYING");
    }
}
