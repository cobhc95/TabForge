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
    private WindowDropKind? _lastTracedKind;

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        _hintKey = "";
        if (Trace.IsOn(Trace.Ui)) DroppedFileUris.TraceDrag("enter", e, LocalFiles(e.Data).ToList(), "n/a");
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => _hintKey = "";

    /// <summary>The paths of the song files among the given paths, in their order.</summary>
    public static string[] SongPaths(IEnumerable<string> paths) => paths.Where(path => FileTypes.IsOpenable(Path.GetExtension(path))).ToArray();

    /// <summary>The local file paths carried by a drag, in their order.</summary>
    internal static IEnumerable<string> LocalFiles(IDataObject data)
    {
        var found = false;
        foreach (var item in data.GetFiles() ?? Array.Empty<IStorageItem>())
            if (item.Path.IsFile)
            {
                found = true;
                yield return item.Path.LocalPath;
            }
        if (found) yield break;
        // X11 file managers may offer only text/uri-list: parse it when the storage list is empty.
        foreach (var path in DroppedFileUris.FromData(data)) yield return path;
    }

    /// <summary>The local folders carried by a drag, in their order (a dropped folder is a storage item whose path is a directory).</summary>
    internal static IEnumerable<string> LocalFolders(IDataObject data) =>
        FoldersAmong(LocalFiles(data));

    /// <summary>The paths among <paramref name="paths"/> that are existing folders, in their order (null entries are not local paths).</summary>
    internal static IEnumerable<string> FoldersAmong(IEnumerable<string?> paths) => paths.OfType<string>().Where(Directory.Exists);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var files = LocalFiles(e.Data).ToList();
        e.DragEffects = HoverFiles(files, out var kind);
        if (kind != _lastTracedKind) { _lastTracedKind = kind; DroppedFileUris.TraceDrag("over", e, files, kind.ToString()); }
        e.Handled = true;
    }

    /// <summary>A hover that did not come through Avalonia drag events (the Linux X11 drop target): true when the window would take these files (the same test and new-track hint as a window drag).</summary>
    internal bool HoverPaths(IReadOnlyList<string> files) => HoverFiles(files, out _) != DragDropEffects.None;

    /// <summary>Ends a hover: the next drag shows its hint again.</summary>
    internal void EndHover() => _hintKey = "";

    private DragDropEffects HoverFiles(IReadOnlyList<string> files, out WindowDropKind kind)
    {
        kind = Decide(SongPaths(files), _audio?.Accepts(files) == true);
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
        return kind == WindowDropKind.None ? DragDropEffects.None : DragDropEffects.Copy;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _lastTracedKind = null;
        var files = LocalFiles(e.Data).ToList();
        if (Trace.IsOn(Trace.Ui)) DroppedFileUris.TraceDrag("drop", e, files, SongPaths(files).Length > 0 ? "songs accepted" : "no song; audio/refusal path");
        if (DropFiles(files, e)) e.Handled = true;
    }

    /// <summary>A drop that did not come through Avalonia's drag events (the Linux X11 drop target): the same song / audio / refusal path as a window drop.</summary>
    internal void DropPaths(IReadOnlyList<string> files) => DropFiles(files, null);

    // True when the drop was taken (songs opened, or audio dropped); a refused one reports and leaves the event unhandled, as before.
    private bool DropFiles(IReadOnlyList<string> files, DragEventArgs? e)
    {
        var songs = SongPaths(files);
        DroppedFileUris.Note($"dispatch: MainWindowFileDrop.DropFiles ({DroppedFileUris.Describe(files)}) songs={songs.Length} audioHandler={_audio is not null}");
        if (songs.Length > 0)
        {
            if (e is not null) e.DragEffects = DragDropEffects.Copy;
            _ = OpenAll(songs);
            return true;
        }
        if (files.Count > 0 && _audio?.Accepts(files) == true)
        {
            if (e is not null) e.DragEffects = DragDropEffects.Copy;
            _hintKey = "";
            _audio.Drop(files);
            return true;
        }
        // A file dropped outside the arrangement (the score, the panes) is refused with the same notice as an empty timeline drop,
        // so an unsupported drop is never a silent no-op.
        DroppedFileUris.Note(files.Count == 0 ? "dispatch: nothing done: no files" : "dispatch: nothing done: neither a song nor audio the importer accepts; notice shown: " + AudioDropPlanner.NoTargetText);
        if (files.Count > 0) _report?.Invoke(AudioDropPlanner.NoTargetText);
        return false;
    }

    private async Task OpenAll(string[] songs)
    {
        var newTab = _dropOpensInNewTab();
        for (var index = 0; index < songs.Length; index++)
        {
            var opened = await _files.OpenPath(songs[index], ReplacesSelectedTab(index, newTab));
            DroppedFileUris.Note($"dispatch: open {Path.GetFileName(songs[index])} replace={ReplacesSelectedTab(index, newTab)} -> opened={opened}");
            if (!opened && index == 0 && !newTab) return;
        }
    }
}
#pragma warning restore CS0618
