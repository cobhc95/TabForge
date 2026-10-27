#pragma warning disable CS0618 // Avalonia 11.3 marks the IDataObject drag API obsolete; it still delivers the dropped file list.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: files dropped on the main window (the tab strip included): the first dropped song file (.tforge or Guitar Pro) replaces the selected tab
//   unless Tabs.DropOpensInNewTab is on; the rest open in new tabs through MainWindowFile.OpenPath, in drop order. A refused replace stops the rest.
//   The drag is accepted only while it carries a song file, except an audio drag outside the arrangement: that is accepted with the new-track hint
//   and lands as a new track at the playback position (the arrangement takes its own audio drops first).
// Does not own: opening the files (MainWindowFile, DocumentController), audio and MIDI drops on the timeline (MainWindowAudioDrop), the tabs (MainWindowTabs).
// Tests: TabForge.Avalonia.Tests.MainWindowFileDropTests.
/// <summary>Accepts song files dropped on the window. UI thread only.</summary>
public sealed class MainWindowFileDrop
{
    private readonly Window _window;
    private readonly MainWindowFile _files;
    private readonly Func<bool> _dropOpensInNewTab;
    private readonly Action<string>? _report;
    private readonly WindowAudioDrop? _audio;

    /// <summary>An audio drag outside the arrangement: what it accepts, the hint while it hovers, and where it lands.</summary>
    public sealed record WindowAudioDrop(Func<IReadOnlyList<string>, bool> Accepts, string Hint, Action<IReadOnlyList<string>> Drop);

    /// <param name="window">The main window that accepts the drops.</param>
    /// <param name="files">Opens each dropped song file.</param>
    /// <param name="dropOpensInNewTab">The Tabs.DropOpensInNewTab setting, read at each drop.</param>
    /// <param name="report">Shows why a non-song drop was refused (the notice row); null shows nothing.</param>
    /// <param name="audio">Accepts an audio drag over the score with the new-track hint; null: audio drags are refused as before.</param>
    public MainWindowFileDrop(Window window, MainWindowFile files, Func<bool> dropOpensInNewTab, Action<string>? report = null, WindowAudioDrop? audio = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(files);
        _window = window;
        _files = files;
        _dropOpensInNewTab = dropOpensInNewTab;
        _report = report;
        _audio = audio;
    }

    /// <summary>Whether the dropped song at <paramref name="index"/> replaces the selected tab: only the first, and only with the setting off.</summary>
    public static bool ReplacesSelectedTab(int index, bool dropOpensInNewTab) => index == 0 && !dropOpensInNewTab;

    /// <summary>What a window drag carries: songs first (the tab drop), else the audio fallback, else nothing of ours.</summary>
    internal enum WindowDropKind { None, Songs, Audio }

    /// <summary>Which drop <paramref name="songs"/> and the audio fallback resolve to (the drag tests read this; the handlers act on it).</summary>
    internal static WindowDropKind Decide(IReadOnlyList<string> songs, bool audioAccepted) =>
        songs.Count > 0 ? WindowDropKind.Songs : audioAccepted ? WindowDropKind.Audio : WindowDropKind.None;

    /// <summary>Turns the window on for file drops and routes its drag and drop events here.</summary>
    public void Attach()
    {
        DragDrop.SetAllowDrop(_window, true);
        _window.AddHandler(DragDrop.DragEnterEvent, new EventHandler<DragEventArgs>(OnDragEnter));
        _window.AddHandler(DragDrop.DragOverEvent, new EventHandler<DragEventArgs>(OnDragOver));
        _window.AddHandler(DragDrop.DragLeaveEvent, new EventHandler<DragEventArgs>(OnDragLeave));
        _window.AddHandler(DragDrop.DropEvent, new EventHandler<DragEventArgs>(OnDrop));
    }

    private string _hintKey = "";

    private void OnDragEnter(object? sender, DragEventArgs e) => _hintKey = "";

    private void OnDragLeave(object? sender, DragEventArgs e) => _hintKey = "";

    /// <summary>The paths of the song files among the given paths, in their order.</summary>
    public static string[] SongPaths(IEnumerable<string> paths) => paths.Where(path => FileTypes.IsOpenable(Path.GetExtension(path))).ToArray();

    /// <summary>The local file paths carried by a drag, in their order.</summary>
    internal static IEnumerable<string> LocalFiles(IDataObject data)
    {
        var items = data.GetFiles();
        if (items is null) yield break;
        foreach (var item in items)
            if (item.Path.IsFile) yield return item.Path.LocalPath;
    }

    /// <summary>The local folders carried by a drag, in their order (a dropped folder is a storage item whose path is a directory).</summary>
    internal static IEnumerable<string> LocalFolders(IDataObject data) =>
        FoldersAmong((data.GetFiles() ?? Array.Empty<IStorageItem>()).Select(item => item.TryGetLocalPath()));

    /// <summary>The paths among <paramref name="paths"/> that are existing folders, in their order (null entries are not local paths).</summary>
    internal static IEnumerable<string> FoldersAmong(IEnumerable<string?> paths) => paths.OfType<string>().Where(Directory.Exists);

    private static string[] DroppedSongs(DragEventArgs e) => SongPaths(LocalFiles(e.Data));

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = LocalFiles(e.Data).ToList();
        var kind = Decide(SongPaths(files), _audio?.Accepts(files) == true);
        e.DragEffects = kind == WindowDropKind.None ? DragDropEffects.None : DragDropEffects.Copy;
        // The new-track hint shows once per drag (every DragOver would repeat it onto the notice row).
        if (kind == WindowDropKind.Audio && _audio is { } audio)
        {
            var key = string.Join("\n", files);
            if (key != _hintKey)
            {
                _hintKey = key;
                _report?.Invoke(audio.Hint);
            }
        }
        else _hintKey = "";
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var songs = DroppedSongs(e);
        if (songs.Length > 0)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            _ = OpenAll(songs);
            return;
        }
        var files = LocalFiles(e.Data).ToList();
        if (files.Count > 0 && _audio?.Accepts(files) == true)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            _hintKey = "";
            _audio.Drop(files);
            return;
        }
        // A file dropped outside the arrangement (the score, the panes) is refused with the same notice as an empty timeline drop,
        // so an unsupported drop is never a silent no-op.
        if (files.Count > 0) _report?.Invoke(AudioDropPlanner.NoTargetText);
    }

    private async Task OpenAll(string[] songs)
    {
        var newTab = _dropOpensInNewTab();
        for (var index = 0; index < songs.Length; index++)
            if (!await _files.OpenPath(songs[index], ReplacesSelectedTab(index, newTab)) && index == 0 && !newTab) return;
    }
}
#pragma warning restore CS0618
