using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the window side of media collect and relink: the save-time step with its question and progress windows, File > Collect media and
//   File > Find missing media, the "files not found" notice on the top bar for the song shown, and the relink as one undo step.
// Does not own: the plan, the copy and the search (Core: MediaCollectPlan, MediaCollector, MediaRelink), the trigger rules (MediaSavePolicy), the hatched clip (TimelineWaveforms).
// Tests: TabForge.Avalonia.Tests.MediaSaveStepTests (the save step), MediaRelinkTests (the search).
/// <summary>Wires the media collect / relink features into the main window. UI thread only.</summary>
public sealed class MainWindowMediaCollect
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "File.CollectMedia", "File.FindMissingMedia" };

    private readonly Window _owner;
    private readonly IFileDialogs _dialogs;
    private readonly MainWindowSettings? _settings;
    private readonly NotificationBar _bar;
    private readonly Action<string> _report;
    private readonly MediaSavePolicy _policy = new();
    private MainWindowTabs? _songs;
    private MainWindowDocumentPanes? _panes;
    private int _scan;

    public MainWindowMediaCollect(Window owner, IFileDialogs dialogs, MainWindowSettings? settings, NotificationBar bar, Action<string> report)
    {
        _owner = owner;
        _dialogs = dialogs;
        _settings = settings;
        _bar = bar;
        _report = report;
        Step = new MediaSaveStep(_policy, () => MediaSavePolicy.Parse(settings?.Settings.General.ExternalMedia), Remember, AskAsync,
            plan => new MediaCopyProgressWindow().RunAsync(_owner, plan), Changed, report);
    }

    /// <summary>The step the save runs before it writes.</summary>
    public MediaSaveStep Step { get; }

    /// <summary>Connects the tabs and the arrangement (created after the saving flow): the song shown is watched for missing files.</summary>
    public void Bind(MainWindowTabs songs, MainWindowDocumentPanes panes)
    {
        _songs = songs;
        _panes = panes;
        _policy.Observe(songs.Active);
        _ = ScanAsync();
        songs.ActiveChanged += () => { _policy.Observe(songs.Active); _ = ScanAsync(); };
        MediaAccess.Changed += () => Dispatcher.UIThread.Post(() => _ = ScanAsync());   // an approval given: a file that waited can now be judged
    }

    public void Register(MainWindowCommands commands)
    {
        commands.Add("File.CollectMedia", () => _ = CollectAsync());
        commands.Add("File.FindMissingMedia", () => _ = RelinkAsync());
    }

    private void Remember(ExternalMediaChoice choice)
    {
        if (_settings is null) return;
        _settings.Settings.General.ExternalMedia = MediaSavePolicy.TextOf(choice);
        _settings.SaveSettings();
    }

    private async Task<MediaChoiceAnswer> AskAsync(SongDocument document, MediaCollectPlan plan, string mediaFolderName)
    {
        var name = Path.GetFileNameWithoutExtension(document.LabelPath) is { Length: > 0 } file ? file : document.Project.Title;
        var window = new MediaCollectPromptWindow(name, plan, mediaFolderName);
        await window.ShowDialog(_owner);
        return new MediaChoiceAnswer(window.Answer == MediaPromptAnswer.Copy, window.Remember && window.Answer != MediaPromptAnswer.Cancel, window.Answer == MediaPromptAnswer.Cancel);
    }

    // The clip paths changed: the engine and the timeline follow, and the notice is checked again.
    private void Changed(SongDocument document)
    {
        _panes?.Arrangement?.Timeline.ClipEdited?.Invoke(document);
        _ = ScanAsync();
    }

    private async Task CollectAsync()
    {
        if (_songs is null) return;
        await Step.CollectNow(_songs.Active);
    }

    /// <summary>Looks for missing files of the song shown (on a worker) and shows or clears the notice; a result for a song no longer shown is dropped.</summary>
    private async Task ScanAsync()
    {
        if (_songs is null) return;
        var document = _songs.Active;
        var scan = ++_scan;
        var files = MediaCollectPlan.Files(document.Project);
        var missing = await Task.Run(() => MediaRelink.FindMissing(files, document.Media));
        if (scan != _scan || !ReferenceEquals(document, _songs.Active)) return;
        if (missing.Count == 0) { _bar.HideSticky(NotificationBar.MissingMediaSource); return; }
        _bar.Show($"{missing.Count} audio file{(missing.Count == 1 ? "" : "s")} for this song {(missing.Count == 1 ? "was" : "were")} not found.", NotificationKind.Warn,
            ("Find missing media…", () => _ = RelinkAsync()), source: NotificationBar.MissingMediaSource, tooltip: string.Join("\n", missing.Take(10).Select(m => m.FullPath)));
    }

    /// <summary>File > Find missing media: the relink window; its fixes are one undo step.</summary>
    public async Task RelinkAsync()
    {
        if (_songs is null) return;
        var document = _songs.Active;
        var files = MediaCollectPlan.Files(document.Project);
        var missing = await Task.Run(() => MediaRelink.FindMissing(files, document.Media));
        if (missing.Count == 0) { _report("No audio files are missing"); _bar.HideSticky(NotificationBar.MissingMediaSource); return; }
        var window = new MediaRelinkWindow(missing, _dialogs);
        await window.ShowDialog(_owner);
        if (window.Fixes.Count == 0) return;
        var fixes = new Dictionary<string, string>(window.Fixes, StringComparer.OrdinalIgnoreCase);
        var oldBase = document.Media.BaseDirectory;
        var result = DocumentEdits.Run(document, project => MediaCollectApply.Apply(project, fixes, oldBase, null) > 0, invalidatesTimeline: false, label: "Relink media");
        if (!result.Changed) return;
        _report($"Relinked {fixes.Count} file{(fixes.Count == 1 ? "" : "s")}");
        Changed(document);
    }
}
