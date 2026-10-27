using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the window's side of its autosave: its tabs, the process's windows (for the songs that left a window), the autosave interval, whether a song
//     plays, the recovery question and the tab a recovered song opens in.
// Does not own: the copy rules and the clock (MainWindowAutosave and TabForge.Autosave), the settings file (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.MainWindowAutosaveTests.
/// <summary>The window's answers and actions for <see cref="MainWindowAutosave"/>. UI thread only.</summary>
internal sealed class MainWindowAutosaveHost : MainWindowAutosave.IHost, TabForge.Avalonia.Views.INoticeReceiver
{
    private const string RecoveryCaption = "Recover unsaved songs";

    private readonly Window _owner;
    private readonly MainWindowTabs _tabs;
    private readonly TabWindowRegistry _windows;
    private readonly MainWindowSettings? _settings;
    private readonly AvaloniaPlaybackSessions? _playback;
    private readonly Action<string> _report;

    public MainWindowAutosaveHost(Window owner, MainWindowTabs tabs, TabWindowRegistry windows, MainWindowSettings? settings, AvaloniaPlaybackSessions? playback, Action<string> report)
    {
        _owner = owner;
        _tabs = tabs;
        _windows = windows;
        _settings = settings;
        _playback = playback;
        _report = report;
    }

    public IReadOnlyList<SongDocument> Songs => _tabs.Songs;

    public IReadOnlyList<SongDocument> SongsInAllWindows => _windows.Windows.SelectMany(window => window.Songs.Songs).ToList();

    public int AutosaveMinutes => AutosaveService.NormalizeMinutes(_settings?.Settings.General.AutosaveMinutes ?? AutosaveService.DefaultMinutes);

    public bool IsPlaying(SongDocument song) => _playback?.Existing(song)?.IsPlaying ?? false;

    public async Task<bool> ConfirmRecoveryAsync(int songCount)
    {
        var text = $"TabForge found {songCount} unsaved song(s) from a session that ended unexpectedly.\n\nOpen them now? Choosing No discards them.";
        var answer = await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), _owner, text, RecoveryCaption, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxAnswer.No);
        return answer == MessageBoxAnswer.Yes;
    }

    public void OpenRecoveredSong(SongDocument song) => _tabs.Open(song);

    public void Report(string text) => _report(text);
    public void Receive(string text, TabForge.Avalonia.Views.NoticeCategory category) => TabForge.Avalonia.Views.NoticeChannel.Send(_report, text, category);
}
