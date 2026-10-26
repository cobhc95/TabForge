using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge.Avalonia;

/// <summary>What the person answered to the copy question: <paramref name="Copy"/> true copies, false keeps the links; <paramref name="Remember"/> stores the answer as the setting.</summary>
/// <param name="Cancel">The question was closed: the save is not done.</param>
public readonly record struct MediaChoiceAnswer(bool Copy, bool Remember, bool Cancel = false);

// Owns: the step a save takes before it writes: find the audio files outside the song folder, ask (or follow the setting), copy them in the background,
//   and point the clips at the copies as one undo step; also makes the clips that stay relative absolute when Save As moves the song to another folder.
// Does not own: the dialogs and the progress window (the callbacks), the file write (MainWindowSaving, DocumentSaveFlow), the plan and the copy (Core).
// Tests: TabForge.Avalonia.Tests.MediaSaveStepTests.
/// <summary>The save-time media step of one window. UI thread only; the plan and the copy run on workers.</summary>
public sealed class MediaSaveStep
{
    private readonly MediaSavePolicy _policy;
    private readonly Func<ExternalMediaChoice> _setting;
    private readonly Action<ExternalMediaChoice> _remember;
    private readonly Func<SongDocument, MediaCollectPlan, string, Task<MediaChoiceAnswer>> _ask;
    private readonly Func<MediaCollectPlan, Task<MediaCollectResult>> _copy;
    private readonly Action<SongDocument> _changed;
    private readonly Action<string> _report;

    /// <param name="policy">The memory of known files and the trigger rules.</param>
    /// <param name="setting">The External media setting, read at each save.</param>
    /// <param name="remember">Stores the choice made with "Remember my choice".</param>
    /// <param name="ask">Shows the copy question for (document, plan, media folder name).</param>
    /// <param name="copy">Runs the copy with its progress window and returns what was copied.</param>
    /// <param name="changed">The clip paths of the document changed (refresh the timeline and the engine).</param>
    /// <param name="report">Shows a short notice.</param>
    public MediaSaveStep(MediaSavePolicy policy, Func<ExternalMediaChoice> setting, Action<ExternalMediaChoice> remember,
        Func<SongDocument, MediaCollectPlan, string, Task<MediaChoiceAnswer>> ask, Func<MediaCollectPlan, Task<MediaCollectResult>> copy,
        Action<SongDocument> changed, Action<string> report)
    {
        _policy = policy;
        _setting = setting;
        _remember = remember;
        _ask = ask;
        _copy = copy;
        _changed = changed;
        _report = report;
    }

    /// <summary>
    /// Runs before <paramref name="document"/> is written to <paramref name="targetPath"/>. False: the person closed the question, so the save is not done.
    /// </summary>
    public async Task<bool> BeforeSave(SongDocument document, string targetPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (folder is null) return true;
        var oldBase = document.Media.BaseDirectory;
        var firstOrSaveAs = document.IsNew || document.Path is null || !string.Equals(Path.GetFullPath(document.Path), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase);
        var files = MediaCollectPlan.Files(document.Project);   // copied here: the worker never reads the live song
        var media = document.Media;
        var plan = await Task.Run(() => MediaCollectPlan.Build(files, media, folder, MediaFolders.ForSong(targetPath)));
        var action = MediaSavePolicy.Decide(_setting(), plan.ExternalCount, firstOrSaveAs, _policy.HasNewFile(document, plan.Sources));
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (action == MediaSaveAction.Ask)
        {
            var answer = await _ask(document, plan, Path.GetFileName(plan.MediaFolder));
            if (answer.Cancel) return false;
            if (answer.Remember) _remember(answer.Copy ? ExternalMediaChoice.AlwaysCopy : ExternalMediaChoice.NeverCopy);
            action = answer.Copy ? MediaSaveAction.Copy : MediaSaveAction.Nothing;
        }
        if (action == MediaSaveAction.Copy && plan.ExternalCount > 0)
        {
            var result = await _copy(plan);
            foreach (var (source, target) in result.Done) moved[source] = target;
            if (result.Cancelled) _report($"Copy cancelled: {result.Done.Count} of {plan.ExternalCount} file{(plan.ExternalCount == 1 ? "" : "s")} copied, the rest stay linked");
            else if (result.Failed.Count > 0) _report($"{result.Failed.Count} file{(result.Failed.Count == 1 ? "" : "s")} could not be copied and stay linked");
        }
        Apply(document, moved, oldBase, folder);
        return true;
    }

    /// <summary>
    /// File > Collect media: copies the song's external files into its Media folder at any time. The song must be saved (the folder is beside it).
    /// Returns the number of files brought in.
    /// </summary>
    public async Task<int> CollectNow(SongDocument document)
    {
        if (document.Path is not { } path) { _report("Save the song first: media is collected into a folder beside it"); return 0; }
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
