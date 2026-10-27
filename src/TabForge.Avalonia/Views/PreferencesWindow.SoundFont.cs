using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the "Download SoundFont..." button of the Settings window: the asynchronous download with progress and cancel, and its result on the button.
// Does not own: the download itself (SoundFontDownloader) or choosing the engine (the Sound engine row).
// Tests: TabForge.Avalonia.Tests.SoundEngineTests (the downloader); the button is checked by hand.
public sealed partial class PreferencesWindow
{
    private CancellationTokenSource? _soundFontDownload;

    /// <summary>The downloader the button uses (a test swaps in one with a fake HTTP handler).</summary>
    internal Func<SoundFontDownloader> SoundFontDownloaderFactory { get; set; } = () => new SoundFontDownloader();

    private Button SoundFontButton(SettingDescriptor row)
    {
        var button = RowButton(row, SoundFontStore.IsInstalled ? "Download again…" : "Download SoundFont…", true, () => { });
        if (SoundFontStore.IsInstalled) ToolTip.SetTip(button, "The SoundFont is installed: " + SoundFontStore.FilePath);
        button.Click += async (_, _) => await RunSoundFontDownloadAsync(button);
        return button;
    }

    /// <summary>Starts the SoundFont download; the button becomes Cancel and shows the percentage until it ends. Never blocks the UI.</summary>
    private async Task RunSoundFontDownloadAsync(Button button)
    {
        if (_soundFontDownload is { } running) { running.Cancel(); return; }   // the second click is Cancel
        using var cancel = _soundFontDownload = new CancellationTokenSource();
        button.Content = "Cancel (0%)";
        var progress = new Progress<(long Received, long Total)>(p =>
            button.Content = p.Total > 0 ? $"Cancel ({p.Received * 100 / p.Total}%)" : $"Cancel ({p.Received / 1_000_000} MB)");
        string? error;
        try { error = await SoundFontDownloaderFactory().DownloadAsync(SoundFontStore.FilePath, progress, cancel.Token); }
        finally { _soundFontDownload = null; }
        button.Content = error is null ? "Download again…" : "Download SoundFont…";
        ToolTip.SetTip(button, error is null ? "The SoundFont is installed: " + SoundFontStore.FilePath : error);
        AutomationProperties.SetHelpText(button, error ?? "Installed");
        if (error is null && SoundEngines.Resolve(Working.Plugins.SoundEngine) == SoundEngines.SoundFont) WriteLive(Working);   // the engine switches to the new bank now
    }
}
