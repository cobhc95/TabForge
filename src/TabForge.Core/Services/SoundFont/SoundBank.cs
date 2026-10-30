namespace TabForge.Services;

// Owns: one downloadable Sound bank the Settings list can offer: its identity, official address, checksum, size and licence texts.
// Does not own: where the file is kept (SoundFontStore), fetching it (SoundFontDownloader) or the Settings window (PreferencesWindow).
// Tests: SoundBankCatalogTests.
/// <summary>One Sound bank TabForge may download: the official file, its SHA-256 and the licence it is offered under.</summary>
public sealed record SoundBank(
    string Id,
    string Name,
    string Author,
    string Copyright,
    string Url,
    string Sha256,
    long Bytes,
    string LicenceName,
    string SourcePage,
    string FileName,
    string LicenceResource)
{
    /// <summary>The size as the Settings list shows it ("30.8 MB").</summary>
    public string SizeText => SoundBanks.FormatSize(Bytes);

    /// <summary>The file name extension holds only .sf2 files.</summary>
    public bool IsVerified => Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && Sha256.Length == 64 && Bytes > 0;
}

/// <summary>Small shared helpers for the Sound bank list and the licence texts.</summary>
public static class SoundBanks
{
    /// <summary>A byte count as the Settings list shows it, in MB with one decimal (1024-based, like the download totals).</summary>
    public static string FormatSize(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " MB";
}
