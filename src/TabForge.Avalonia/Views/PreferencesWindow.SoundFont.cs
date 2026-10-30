using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the "Sound banks" list under the Sound engine row: one row per catalog bank (name, status badge, size, author, licence,
//   View licence, Download / Download again / Delete / Use) and the "Open your own .sf2…" row. Downloads and deletes run off the UI
//   thread; a delete asks first and switches the engine away when the bank is in use.
// Does not own: the catalog and licence texts (SoundBankCatalog), the files (SoundFontStore), the download (SoundFontDownloader)
//   or the licence window (SoundBankLicenceWindow).
// Tests: SoundBankCatalogTests (the rules); the list is checked by hand.
public sealed partial class PreferencesWindow
{
    private readonly Dictionary<string, CancellationTokenSource> _bankDownloads = new(StringComparer.Ordinal);

    /// <summary>The downloader a bank's button uses (a test swaps in one with a fake HTTP handler).</summary>
    internal Func<SoundBank, SoundFontDownloader> SoundBankDownloaderFactory { get; set; } = bank => new SoundFontDownloader(bank);

    /// <summary>The card the Sound banks list lives in, added below the Playback engine card.</summary>
    private Control SoundBanksCard()
    {
        var list = new StackPanel { Spacing = 10 };
        foreach (var bank in SoundBankCatalog.All) list.Children.Add(BankRow(bank));
        list.Children.Add(OwnRow());
        return PreferencesSpecialPages.Card("Sound banks", list);
    }

    /// <summary>One bank: its details on the left and its buttons on the right.</summary>
    private Control BankRow(SoundBank bank)
    {
        var downloaded = SoundFontStore.IsDownloaded(bank);
        var builtIn = !downloaded && SoundFontStore.IsBuiltIn(bank);
        var state = SoundBankRowState.Of(bank, Working.Plugins.SoundEngine, OperatingSystem.IsWindows(), downloaded, builtIn);

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = bank.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(Badge(state.Badge, state.Status == SoundBankStatus.InUse));
        text.Children.Add(title);
        text.Children.Add(ToolWindowParts.Small($"{bank.SizeText} · {bank.Author} · {bank.LicenceName}"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(LinkButton("View licence", () => _ = ShowLicenceAsync(bank)));
        if (state.ShowDownloadAgain)
        {
            Button? again = null;
            again = BankButton(DownLabel(bank), () => _ = RunBankDownloadAsync(bank, downloaded, again));
            buttons.Children.Add(again);
        }
        if (state.ShowDelete) buttons.Children.Add(BankButton("Delete", () => _ = DeleteBankAsync(bank)));
        if (state.ShowUse) buttons.Children.Add(BankButton("Use", () => UseBank(bank.Name)));
        if (state.ShowDownload)
        {
            Button? first = null;
            first = BankButton("Download", () => _ = RunBankDownloadAsync(bank, downloaded: false, first));
            buttons.Children.Add(first);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(text);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    /// <summary>The user's own .sf2: TabForge never copies, alters or deletes it.</summary>
    private Control OwnRow()
    {
        var own = Working.Plugins.UserSoundFont;
        var exists = own.Length > 0 && File.Exists(own);
        var inUse = SoundEngines.Resolve(Working.Plugins.SoundEngine) == SoundEngines.OwnFile;
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = "Your own .sf2", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(Badge(inUse ? "In use" : exists ? "Chosen" : "Not chosen", inUse));
        text.Children.Add(title);
        text.Children.Add(ToolWindowParts.Small(exists
            ? $"{Path.GetFileName(own)} · {SoundBanks.FormatSize(new FileInfo(own).Length)} · never copied or deleted by TabForge"
            : "Choose any .sf2 file on this computer; TabForge plays it in place and never copies or deletes it."));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (exists && !inUse) buttons.Children.Add(BankButton("Use", () => UseBank(SoundEngines.OwnFile)));
        buttons.Children.Add(BankButton("Choose file…", () => _ = ChooseOwnFileAsync()));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(text);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    private static Border Badge(string label, bool accent)
    {
        var block = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(accent ? "OnAccentBrush" : "TextMuted");
        var border = new Border { CornerRadius = new CornerRadius(6), Child = block, VerticalAlignment = VerticalAlignment.Center };
        border[!Border.BackgroundProperty] = new DynamicResourceExtension(accent ? "AccentBrush" : "Panel2Brush");
        return border;
    }

    private Button BankButton(string label, Action click)
    {
        var button = new Button { Content = label, Classes = { AvaloniaFormControlStyles.PrefsFieldClass }, VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => click();
        return button;
    }

    private static Button LinkButton(string label, Action click)
    {
        var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => click();
        return button;
    }

    private static string DownLabel(SoundBank bank) => "Download again";

    /// <summary>Switches the sound engine to the row's bank (or the user's file) and shows the new badge at once.</summary>
    private void UseBank(string engine)
    {
        Working.Plugins.SoundEngine = engine;
        Write("vst.soundengine", engine);
        Rebuild();
    }

    /// <summary>Downloads a bank with progress and cancel on its own button; a verified file is the only thing written.</summary>
    private async Task RunBankDownloadAsync(SoundBank bank, bool downloaded, Button? button)
    {
        if (_bankDownloads.TryGetValue(bank.Id, out var running)) { running.Cancel(); return; }   // the second click is Cancel
        using var cancel = _bankDownloads[bank.Id] = new CancellationTokenSource();
        var progress = new Progress<(long Received, long Total)>(p =>
        {
            if (button is null) return;
            button.Content = p.Total > 0 ? $"Cancel ({p.Received * 100 / p.Total}%)" : $"Cancel ({p.Received / 1_000_000} MB)";
        });
        string? error;
        try { error = await SoundBankDownloaderFactory(bank).DownloadAsync(SoundFontStore.PathOf(bank), progress, cancel.Token); }
        finally { _bankDownloads.Remove(bank.Id); }
        if (error is null)
        {
            if (SoundEngines.Resolve(Working.Plugins.SoundEngine) == bank.Name) WriteLive(Working);   // the engine switches to the new bank now
            _status.Text = $"{bank.Name} downloaded.";
        }
        else _status.Text = error;
        Rebuild();
    }

    /// <summary>The delete question, then the engine switch (when in use) and the file delete, all off the UI thread.</summary>
    private async Task DeleteBankAsync(SoundBank bank)
    {
        var answer = await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), this,
            $"Delete \"{bank.Name}\"?\n\n{bank.SizeText}. You can download it again whenever you like.",
            "Delete sound bank", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxAnswer.No);
        if (answer != MessageBoxAnswer.Yes) return;
        var plugins = Working.Plugins;
        var previousEngine = plugins.SoundEngine;
        var error = await Task.Run(() => SoundFontStore.Delete(bank, plugins, OperatingSystem.IsWindows()));
        if (error is not null) { plugins.SoundEngine = previousEngine; _status.Text = error; Rebuild(); return; }
        if (!string.Equals(previousEngine, plugins.SoundEngine, StringComparison.Ordinal)) Write("vst.soundengine", plugins.SoundEngine);
        WriteLive(Working);
        _status.Text = $"{bank.Name} deleted.";
        Rebuild();
    }

    /// <summary>The licence window: the text comes from the downloaded package file when it has one, else the app's resources.</summary>
    private async Task ShowLicenceAsync(SoundBank bank)
    {
        var path = SoundFontStore.PathOf(bank);
        var text = SoundBankCatalog.LicenceText(bank, File.Exists(path) ? path : null);
        await new SoundBankLicenceWindow(bank, text).ShowDialog(this);
    }

    /// <summary>Lets the user pick any .sf2 and plays it in place, without copying it.</summary>
    private async Task ChooseOwnFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose your own .sf2 SoundFont", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("SoundFont") { Patterns = new[] { "*.sf2" } } },
        });
        var path = files.Select(file => file.TryGetLocalPath()).FirstOrDefault(p => p is { Length: > 0 } && File.Exists(p));
        if (path is null) return;
        Working.Plugins.UserSoundFont = path;
        UseBank(SoundEngines.OwnFile);
        _status.Text = $"Using {Path.GetFileName(path)}.";
    }
}
