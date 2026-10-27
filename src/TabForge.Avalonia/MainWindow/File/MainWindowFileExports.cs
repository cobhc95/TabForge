using System.Collections.Generic;
using System.Linq;
using System;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;

namespace TabForge.Avalonia;

// Owns: the File menu's Render to file and Export video (MP4) commands. Each opens its real dialog (RenderWindow, VideoExportWindow) over the
//   shown song, offering the selection range when the score has a note selection and a folder picker for Browse; the Render dialog's Video (MP4) button opens the video dialog for the same song.
// Does not own: the menu rows (MainMenuTable in Core), the commands table (MainWindowCommands), the dialogs (Views), the render and video
//   rules (RenderDialogPresenter, VideoExportPresenter and the Core exporters).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (every menu id is handled or disabled).
/// <summary>The File menu's render and video export commands, bound to the shown song. The window keeps its render and video settings.</summary>
public sealed class MainWindowFileExports
{
    /// <summary>The menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "File.Render", "File.ExportVideo" };

    private readonly RenderSettings _renderSettings;
    private readonly VideoSettings _videoSettings;
    private readonly Func<bool> _hasSelection;
    private readonly IFileDialogs _dialogs = new AvaloniaFileDialogs();

    /// <param name="hasSelection">Whether the score has a note selection when a dialog opens.</param>
    /// <param name="settings">The shared settings whose Render and Video sections the dialogs edit; null keeps private defaults.</param>
    public MainWindowFileExports(Func<bool> hasSelection, AppSettings? settings = null)
    {
        _hasSelection = hasSelection;
        _renderSettings = settings?.Render ?? new();
        _videoSettings = settings?.Video ?? new();
    }

    /// <summary>The settings the Render dialog opens with.</summary>
    public RenderSettings RenderSettings => _renderSettings;

    /// <param name="songs">The tabs; the commands act on the shown song.</param>
    /// <param name="owner">The window the dialogs open over.</param>
    /// <param name="score">The score view whose note selection offers the dialogs' selection range.</param>
    /// <param name="playback">The window's playback sessions and engine; renders run through it (null: Render says it needs the full app).</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, Window owner, ScorePreviewView score, MainWindowSettings? settings = null,
        Transport.AvaloniaPlaybackSessions? playback = null, Action<string, string?, NotificationKind>? notice = null, StatusActivity? activity = null)
    {
        var exports = new MainWindowFileExports(() => score.Editor.HasSelection, settings?.Settings)
        {
            Runner = playback is not null && settings is not null ? new AvaloniaRenderRunner(playback, settings.Settings) : null,
            Exporter = playback is not null && settings is not null ? new AvaloniaVideoExporter(playback, settings.Settings) : null,
            Selection = () => score.Editor.SelectionCellRange,
            Notation = () => score.Notation,
            Notice = notice,
            Activity = activity,
        };
        commands.Add("File.Render", () => exports.ShowRender(songs.Active, owner));
        commands.Add("File.ExportVideo", () => exports.ShowVideo(songs.Active, owner));
    }

    /// <summary>Shows a Render notice (text, folder for an Open folder button, kind); null shows nothing.</summary>
    public Action<string, string?, NotificationKind>? Notice { get; init; }

    /// <summary>The status bar's activities: Rendering and Exporting video show while a render or an export runs; null shows nothing.</summary>
    public StatusActivity? Activity { get; init; }

    private static string? FolderOf(string? path) { try { return string.IsNullOrEmpty(path) ? null : System.IO.Path.GetDirectoryName(path); } catch (ArgumentException) { return null; } }

    // Started / finished / failed notices around a render or export; a cancel says nothing. The exception goes on to the dialog.
    private async System.Threading.Tasks.Task<T> Announce<T>(string what, string? file, Func<System.Threading.Tasks.Task<T>> run, Func<T, string?> finishedFile)
    {
        Notice?.Invoke(what + " started", null, NotificationKind.Info);
        SetBusy(what, true);
        try
        {
            var result = await run();
            var done = finishedFile(result) ?? file;
            Notice?.Invoke(what + " finished", FolderOf(done), NotificationKind.Info);
            return result;
        }
        catch (TabForge.Audio.Contracts.RenderException ex) when (ex.Cancelled) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Notice?.Invoke(what + " failed: " + ex.Message, null, NotificationKind.Error);
            throw;
        }
        finally { SetBusy(what, false); }
    }

    private void SetBusy(string what, bool on)
    {
        if (Activity is null) return;
        if (what == "Render") Activity.Rendering = on; else Activity.ExportingVideo = on;
    }

    /// <summary>Runs renders through the window's engine; null without one (Render then says it needs the full app).</summary>
    public AvaloniaRenderRunner? Runner { get; init; }

    /// <summary>The score's note selection (bars and cells) when a dialog opens; null without one.</summary>
    public Func<(int StartBar, int StartCell, int EndBar, int EndCell)?> Selection { get; init; } = () => null;

    private void ShowRender(SongDocument song, Window owner)
    {
        var project = song.Project;
        RenderWindow? window = null;
        Func<RenderPlan, IProgress<TabForge.Audio.Contracts.RenderProgressInfo>, System.Threading.CancellationToken, System.Threading.Tasks.Task<TabForge.Audio.Contracts.RenderResult>>? run =
            Runner is { Engine: not null } runner ? (plan, progress, cancel) => Announce("Render", plan.MasterFile ?? plan.Stems.Values.FirstOrDefault(), () => runner.Run(song, plan, _renderSettings, window ?? owner, progress, cancel), r => r.Files.Count > 0 ? r.Files[0].Path : null) : null;
        window = new RenderWindow(project, _renderSettings, hasSelection: _hasSelection(), openVideoExport: () => ShowVideo(song, owner),
            pickFolder: start => _dialogs.PickFolder(new FileDialogOptions { Title = "Choose the render folder", InitialDirectory = start }, owner),
            run: run, mp3Available: run is null || RenderJob.Mp3Available(), selection: _hasSelection() ? Selection() : null, loop: song.Selection.LoopSnapshot(), snip: song.Selection.Snapshot());
        window.ShowDialog(owner);
    }

    /// <summary>Runs video exports through the window's engine; null without one (Export then says it needs the full app).</summary>
    public AvaloniaVideoExporter? Exporter { get; init; }

    /// <summary>The score's notation, which the video's score follows.</summary>
    public Func<NotationMode> Notation { get; init; } = () => NotationMode.TabAndStaff;

    private void ShowVideo(SongDocument song, Window owner)
    {
        VideoExportWindow? window = null;
        Func<VideoExportPlan, IProgress<VideoExportProgress>, System.Threading.CancellationToken, System.Threading.Tasks.Task<VideoExportResult>>? run =
            Exporter is { Engine: not null } exporter ? (plan, progress, cancel) => Announce("Video export", plan.Path, () => exporter.Run(song, plan, window ?? owner, progress, cancel), _ => null) : null;
        window = new VideoExportWindow(song.Project, _videoSettings, hasSelection: _hasSelection(), run: run,
            saveFile: options => _dialogs.SaveFile(options, window ?? owner), selection: _hasSelection() ? Selection() : null, notation: Notation());
        window.ShowDialog(owner);
    }
}
