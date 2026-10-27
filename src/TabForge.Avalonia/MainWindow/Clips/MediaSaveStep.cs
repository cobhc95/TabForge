using TabForge.Avalonia.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Avalonia;

/// <summary>What the person answered to the copy question: <paramref name="Copy"/> true copies, false keeps the links; <paramref name="Remember"/> stores the answer (and the layout) as the settings.</summary>
/// <param name="Cancel">The question was closed: the save is not done.</param>
/// <param name="Layout">The layout picked; null uses the Collected audio layout setting.</param>
public readonly record struct MediaChoiceAnswer(bool Copy, bool Remember, bool Cancel = false, MediaCollectLayout? Layout = null);

// Owns: the step a save takes before it writes: find the audio files outside the song folder, ask (or follow the settings), copy them in the background,
//   and point the clips at the copies as one undo step; for the one-song-folder layout it also moves the song file into its new folder (and says so when it cannot); also makes the clips that stay relative absolute when Save As moves the song to another folder.
// Does not own: the dialogs and the progress window (the callbacks), the file write (MainWindowSaving, DocumentSaveFlow), the plan and the copy (Core).
// Tests: TabForge.Avalonia.Tests.MediaSaveStepTests.
/// <summary>The save-time media step of one window. UI thread only; the plan and the copy run on workers.</summary>
public sealed class MediaSaveStep
{
    private readonly MediaSavePolicy _policy;
    private readonly Func<ExternalMediaChoice> _setting;
    private readonly Action<ExternalMediaChoice> _remember;
    private readonly Func<SongDocument, MediaCollectPlan, MediaCollectLayout, Task<MediaChoiceAnswer>> _ask;
    private readonly Func<MediaCollectLayout>? _layout;
    private readonly Action<MediaCollectLayout>? _rememberLayout;
    private readonly Func<MediaCollectPlan, Task<MediaCollectResult>> _copy;
    private readonly Action<SongDocument> _changed;
    private readonly Action<string> _report;

    /// <param name="policy">The memory of known files and the trigger rules.</param>
    /// <param name="setting">The External media setting, read at each save.</param>
    /// <param name="remember">Stores the choice made with "Remember my choice".</param>
    /// <param name="ask">Shows the copy question for (document, plan, the layout the question starts on).</param>
    /// <param name="copy">Runs the copy with its progress window and returns what was copied.</param>
    /// <param name="changed">The clip paths of the document changed (refresh the timeline and the engine).</param>
    /// <param name="report">Shows a short notice.</param>
    /// <param name="layout">The Collected audio layout setting; null (tests) keeps the Media folder beside the song.</param>
    /// <param name="rememberLayout">Stores the layout chosen with "Remember my choice".</param>
    public MediaSaveStep(MediaSavePolicy policy, Func<ExternalMediaChoice> setting, Action<ExternalMediaChoice> remember,
        Func<SongDocument, MediaCollectPlan, MediaCollectLayout, Task<MediaChoiceAnswer>> ask, Func<MediaCollectPlan, Task<MediaCollectResult>> copy,
        Action<SongDocument> changed, Action<string> report, Func<MediaCollectLayout>? layout = null, Action<MediaCollectLayout>? rememberLayout = null)
    {
        _layout = layout;
        _rememberLayout = rememberLayout;
        _policy = policy;
        _setting = setting;
        _remember = remember;
        _ask = ask;
        _copy = copy;
        _changed = changed;
        _report = report;
    }

    /// <summary>
    /// Runs before <paramref name="document"/> is written to <paramref name="targetPath"/>. Returns the path to write (the same, or inside the new song folder when
    /// the song moved there), or null when the person closed the question, so the save is not done.
    /// </summary>
    public async Task<string?> BeforeSave(SongDocument document, string targetPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (folder is null) return targetPath;
        var oldBase = document.Media.BaseDirectory;
        var firstOrSaveAs = document.IsNew || document.Path is null || !string.Equals(Path.GetFullPath(document.Path), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase);
        var files = MediaCollectPlan.Files(document.Project);   // copied here: the worker never reads the live song
        var media = document.Media;
        var plan = await Task.Run(() => MediaCollectPlan.Build(files, media, folder, MediaFolders.ForSong(targetPath)));
        var action = MediaSavePolicy.Decide(_setting(), plan.ExternalCount, firstOrSaveAs, _policy.HasNewFile(document, plan.Sources));
        var layout = _layout?.Invoke() ?? MediaCollectLayout.MediaBeside;
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (action == MediaSaveAction.Ask)
        {
            var answer = await _ask(document, plan, layout);
            if (answer.Cancel) return null;
            if (answer.Layout is { } picked) layout = picked;
            if (answer.Remember) { _remember(answer.Copy ? ExternalMediaChoice.AlwaysCopy : ExternalMediaChoice.NeverCopy); _rememberLayout?.Invoke(layout); }
            action = answer.Copy ? MediaSaveAction.Copy : MediaSaveAction.Nothing;
        }
        if (action == MediaSaveAction.Copy && plan.ExternalCount > 0)
        {
            if (layout == MediaCollectLayout.SongFolder)
            {
                var (path, rebuilt) = await IntoSongFolder(document, targetPath, files, media);
                if (rebuilt is not null) { targetPath = path; folder = Path.GetDirectoryName(path)!; plan = rebuilt; }
            }
            var result = await _copy(plan);
            foreach (var (source, target) in result.Done) moved[source] = target;
            if (result.Cancelled) _report($"Copy cancelled: {result.Done.Count} of {plan.ExternalCount} file{(plan.ExternalCount == 1 ? "" : "s")} copied, the rest stay linked");
            else if (result.Failed.Count > 0) _report.Warning($"{result.Failed.Count} file{(result.Failed.Count == 1 ? "" : "s")} could not be copied and stay linked");
        }
        Apply(document, moved, oldBase, folder);
        return targetPath;
    }

    // The one-song-folder layout: the song file moves into "<song>/" (when it is being saved where it already is) and the plan is made again for the "Audio" folder
    // beside it. A move that fails leaves the song where it was and the Media folder layout in force (an error notice says so): the plan is then null.
    private async Task<(string Path, MediaCollectPlan? Plan)> IntoSongFolder(SongDocument document, string targetPath, List<string> files, MediaContext media)
    {
        var target = MediaLayouts.SongFolderFor(targetPath);
        if (!string.Equals(target.SongPath, Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
        {
            var inPlace = document.Path is { } current && string.Equals(Path.GetFullPath(current), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath);
            var failure = await Task.Run(() => inPlace ? MediaLayouts.MoveSong(targetPath, target.SongPath) : CreateFolder(target.Folder));
            if (failure is not null)
            {
                _report.Error($"Could not put the song in its own folder ({failure}); the audio goes into the Media folder beside it instead");
                return (targetPath, null);
            }
            if (inPlace) document.Path = target.SongPath;   // the song is there now: a save that fails afterwards still finds it
        }
        var plan = await Task.Run(() => MediaCollectPlan.Build(files, media, target.Folder, target.AudioFolder));
        return (target.SongPath, plan);
    }

    private static string? CreateFolder(string folder)
    {
        try { Directory.CreateDirectory(folder); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { return ex.Message; }
    }

    /// <summary>
    /// File > Collect media: copies the song's external files into its Media folder at any time. The song must be saved (the folder is beside it).
    /// Returns the number of files brought in.
    /// </summary>
    public async Task<int> CollectNow(SongDocument document)
    {
        if (document.Path is not { } path) { _report.Warning("Save the song first: media is collected into a folder beside it"); return 0; }
        var plan = await Task.Run(() => MediaCollectPlan.Build(MediaCollectPlan.Files(document.Project), document.Media, Path.GetDirectoryName(Path.GetFullPath(path))!, MediaFolders.ForSong(path)));
        if (plan.ExternalCount == 0) { _report("All audio is already inside the song's folder" + (plan.Skipped.Count > 0 ? $" ({plan.Skipped.Count} linked file{(plan.Skipped.Count == 1 ? "" : "s")} cannot be copied)" : "")); return 0; }
        var result = await _copy(plan);
        Apply(document, new Dictionary<string, string>(result.Done, StringComparer.OrdinalIgnoreCase), document.Media.BaseDirectory, null);
        _report($"Copied {result.Done.Count} file{(result.Done.Count == 1 ? "" : "s")} into {Path.GetFileName(plan.MediaFolder)}; save the song to keep the new links");
        return result.Done.Count;
    }

    /// <summary>A save was written: its files are known now, so only a later addition asks again.</summary>
    public void AfterSave(SongDocument document) => _policy.Saved(document);

    // One undo step for all path changes (the copies and, on Save As, the links made absolute).
    private void Apply(SongDocument document, IReadOnlyDictionary<string, string> moved, string? oldBase, string? newBase)
    {
        if (!MediaCollectApply.WouldChange(document.Project, moved, oldBase, newBase)) return;
        var result = DocumentEdits.Run(document, project => MediaCollectApply.Apply(project, moved, oldBase, newBase) > 0, invalidatesTimeline: false, label: "Collect media");
        if (result.Changed) _changed(document);
    }
}
