namespace TabForge.Services;

/// <summary>What a Sound bank row shows: its badge and which buttons it offers.</summary>
public enum SoundBankStatus { NotDownloaded, BuiltIn, Downloaded, InUse }

/// <summary>The state of one Sound bank row in Settings, from the files on disk and the chosen engine.</summary>
public sealed record SoundBankRowState(SoundBankStatus Status, bool ShowDownload, bool ShowUse, bool ShowDownloadAgain, bool ShowDelete)
{
    /// <summary>The badge text ("In use", "Downloaded", "Built in" or "Not downloaded").</summary>
    public string Badge => Status switch
    {
        SoundBankStatus.InUse => "In use",
        SoundBankStatus.Downloaded => "Downloaded",
        SoundBankStatus.BuiltIn => "Built in",
        _ => "Not downloaded",
    };

    /// <summary>The row for a bank: in use wins over downloaded, downloaded over built in. Delete exists only for a file TabForge downloaded.</summary>
    public static SoundBankRowState Of(SoundBank bank, string? engine, bool windowsHost, bool downloaded, bool builtIn)
    {
        var inUse = SoundEngines.Resolve(engine, windowsHost) == bank.Name;
        var status = inUse ? SoundBankStatus.InUse : downloaded ? SoundBankStatus.Downloaded : builtIn ? SoundBankStatus.BuiltIn : SoundBankStatus.NotDownloaded;
        return new SoundBankRowState(status, ShowDownload: !downloaded && !builtIn, ShowUse: (downloaded || builtIn) && !inUse,
            ShowDownloadAgain: downloaded, ShowDelete: downloaded);
    }
}
