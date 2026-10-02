using System.Diagnostics;
using System.Threading;
using System.Windows;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// R3 (the R2 review's leftovers): the draw path never waits on the file system, a render in progress is respected by the engine's clip refresh,
/// Preferences merges instead of overwriting the approvals, a closed song's outlines leave the cache, and the slow plug-in prompt exists once.
/// </summary>
public static partial class SelfTest
{
    private static void TestDocumentContextLeftovers()
    {
        RunDcCase("the draw path and the approval-change handler never wait on the file system", DcDrawPathNeverBlocksCase);
        RunDcCase("the UI thread never waits for a path to resolve", DcUiThreadNeverWaitsCase);
        RunDcCase("a render classifies every clip before its sync", DcRenderResolvesFirstCase);
        RunDcCase("a closed song's outlines are evicted, other songs' are not", DcEvictionCase);
        RunDcCase("the engine's clip refresh respects a render in progress", DcRefreshDuringRenderCase);
        RunDcCase("Preferences merges the approvals", DcApprovalMergeCase);
    }

    /// <summary>The window-based leftovers (run inside the real-window fixture).</summary>
    private static void DcLeftoversInWindows(MainWindow a, LifetimeContext context)
    {
        RunDcCase("Preferences Apply keeps an approval another window gave while the dialog was open", () => DcPreferencesApplyKeepsApprovalCase(a, context));
        RunDcCase("the slow plug-in prompt is shown once for the application", () => DcSlowPromptOnceCase(a));
    }

    private static void DcDrawPathNeverBlocksCase()
    {
        using var probe = new DcProbe();
        var settings = new AppSettings();
        var song = TestMediaContext(settings, @"C:/songs/a/a.tforge");
        const string file = @"C:\songs\a\take.wav";
        _ = WaveformCache.Get(file, song); WaveformCache.WaitIdle(3000);
        Check("document context: the local outline is read once", WaveformCache.Get(file, song) is not null && probe.OpensOf(file) == 1);

        // From here every link resolution takes 1.2 s (a link into an unreachable share). The cache's own worker may wait; nobody else may.
        probe.Fs.OnFinalPath = _ => Thread.Sleep(1200);
        MediaAccess.ClearCache();
        var longest = 0L;
        var watch = new Stopwatch();
        for (var i = 0; i < 40; i++)
        {
            watch.Restart();
            var peaks = WaveformCache.Get(file, song);
            _ = WaveformCache.StatusOf(file, song);
            longest = Math.Max(longest, watch.ElapsedMilliseconds);
            if (peaks is null) { longest = long.MaxValue; break; }
        }
        Check("document context: drawing an already-read clip never waits for the file system (permission is re-checked from the verdict the worker obtained)", longest < 250, $"longest {longest} ms");

        watch.Restart();
        MediaAccess.ClearCache();
        MediaAccess.RaiseChanged();   // an approval changed somewhere: the cache re-judges its outlines on the caller's thread
        var changed = watch.ElapsedMilliseconds;
        Check("document context: an approval change does not wait for the file system either", changed < 250, $"{changed} ms");
        probe.Fs.OnFinalPath = null;
        WaveformCache.WaitIdle(6000);

        // Permission is still honoured without the file system: an unsaved song's session approval, revoked, takes its outline away at once.
        var session = TestMediaContext(settings, null);
        var network = @"\\tf-dc-server\share\held.wav";
        DcApprove(network, session);
        _ = WaveformCache.Get(network, session); WaveformCache.WaitIdle(3000);
        var held = WaveformCache.Get(network, session) is not null;
        MediaAccess.Revoke(MediaAccess.ApprovalsOf(session).First(), session);
        Check("document context: a revoked approval still withdraws a read outline (decided from the stored verdict)", held && WaveformCache.Get(network, session) is null && WaveformCache.StatusOf(network, session).State == WaveState.NeedsApproval);
    }

    /// <summary>The engine's clip sync and the approval notice run on the UI thread: a link into a share that does not answer must not stall them.</summary>
    private static void DcUiThreadNeverWaitsCase()
    {
        using var probe = new DcProbe();
        var previousPlayAll = MixerGroups.PlayAllThroughEngine;
        MixerGroups.PlayAllThroughEngine = false;
        var settings = new AppSettings();
        var client = new AudioEngineClient();
        var sent = new Dictionary<int, List<string>>();
        try
        {
            client.AttachFakeForTest();
            client.ClipsSentForTest = (slot, clips) => sent[slot] = clips.Select(c => c.File).ToList();
            const string file = @"C:\songs\a\slow.wav";
            var media = TestMediaContext(settings, @"C:/songs/a/a.tforge");
            var song = DcAudioSong(file);
            object owner = new();
            probe.Fs.OnFinalPath = _ => Thread.Sleep(1200);   // resolving the link takes 1.2 s
            var watch = Stopwatch.StartNew();
            client.Sync(song.Tracks, settings.Plugins, null, song, owner, media);
            var syncMs = watch.ElapsedMilliseconds;
            var slot = client.SlotOf(song.Tracks[0]);
            Check("document context: the engine's clip sync never waits for the file system (a 1.2 s link resolution costs the UI thread under 250 ms)", syncMs < 250 && slot >= 0, $"{syncMs} ms");
            Check("document context: until the path is resolved its clip is not sent (nothing is opened or decided from a guess)", sent.TryGetValue(slot, out var early) && early.Count == 0);
            watch.Restart();
            var waiting = MediaAccess.UnapprovedNoWait(song.Tracks.SelectMany(t => t.AudioClips), media);
            Check("document context: the approval notice's evaluation never waits either", watch.ElapsedMilliseconds < 250 && waiting.Count == 0, $"{watch.ElapsedMilliseconds} ms");
            probe.Fs.OnFinalPath = null;
            MediaAccess.WaitResolved(6000);
            PumpUi();
            Check("document context: when the resolution finishes the clip is sent, judged with its own document's context", sent[slot].SequenceEqual(new[] { file }), string.Join(",", sent[slot]));

            // A document that closed while its path was being resolved gets nothing afterwards.
            var closing = TestMediaContext(settings, @"C:/songs/b/b.tforge");
            const string other = @"C:\songs\b\slow2.wav";
            var closingSong = DcAudioSong(other);
            object closingOwner = new();
            probe.Fs.OnFinalPath = _ => Thread.Sleep(600);
            client.Sync(closingSong.Tracks, settings.Plugins, null, closingSong, closingOwner, closing);
            var closingSlot = client.SlotOf(closingSong.Tracks[0]);
            closing.Close();
            probe.Fs.OnFinalPath = null;
            MediaAccess.WaitResolved(6000);
            PumpUi();
            Check("document context: a document closed before its path resolved is not given the clip", closingSlot >= 0 && sent[closingSlot].Count == 0, string.Join(",", sent[closingSlot]));
        }
        finally { probe.Fs.OnFinalPath = null; client.ClipsSentForTest = null; client.Dispose(); MixerGroups.PlayAllThroughEngine = previousPlayAll; }
    }

    /// <summary>File > Render syncs the engine and then ignores late clip refreshes: an allowed clip whose path was not resolved yet must still be in the render.</summary>
    private static void DcRenderResolvesFirstCase()
    {
        using var probe = new DcProbe();
        var previousPlayAll = MixerGroups.PlayAllThroughEngine;
        MixerGroups.PlayAllThroughEngine = false;
        var settings = new AppSettings();
        var client = new AudioEngineClient();
        var sent = new Dictionary<int, List<string>>();
        try
        {
            client.AttachFakeForTest();
            client.ClipsSentForTest = (slot, clips) => sent[slot] = clips.Select(c => c.File).ToList();
            const string file = @"C:\songs\render\never-seen.wav";
            var media = TestMediaContext(settings, @"C:/songs/render/r.tforge");
            var song = DcAudioSong(file);
            probe.Fs.OnFinalPath = _ => Thread.Sleep(300);   // a slow link: the background resolver would still be busy when the sync runs
            MediaAccess.ResolveNow(song.Tracks.SelectMany(t => t.AudioClips), media);   // what RenderJob does before its sync
            client.Sync(song.Tracks, settings.Plugins, null, song, new object(), media);
            var slot = client.SlotOf(song.Tracks[0]);
            Check("document context: a render's sync sends every allowed clip at once (its paths were classified first; nothing waits for a late refresh)",
                slot >= 0 && sent.TryGetValue(slot, out var clips) && clips.SequenceEqual(new[] { file }), slot >= 0 && sent.TryGetValue(slot, out var got) ? string.Join(",", got) : "nothing sent");
        }
        finally { probe.Fs.OnFinalPath = null; MediaAccess.WaitResolved(3000); client.ClipsSentForTest = null; client.Dispose(); MixerGroups.PlayAllThroughEngine = previousPlayAll; }
    }

    private static void DcEvictionCase()
    {
        using var probe = new DcProbe();
        var closing = DocumentSession.FromProject(DcAudioSong(@"C:\songs\x\one.wav"), @"C:\songs\x\x.tforge");
        var staying = DocumentSession.FromProject(DcAudioSong(@"C:\songs\y\two.wav"), @"C:\songs\y\y.tforge");
        _ = WaveformCache.Get(@"C:\songs\x\one.wav", closing.Media);
        _ = WaveformCache.Get(@"C:\songs\y\two.wav", staying.Media);
        WaveformCache.WaitIdle(3000);
        var both = WaveformCache.CachedCount;
        closing.DisposePlayback();
        Check("document context: closing a song evicts the outlines it asked for and keeps the other song's",
            both == 2 && WaveformCache.CachedCount == 1 && WaveformCache.Get(@"C:\songs\y\two.wav", staying.Media) is not null, $"before {both}, after {WaveformCache.CachedCount}");
        Check("document context: a closed song's context asks for nothing afterwards", WaveformCache.Get(@"C:\songs\x\one.wav", closing.Media) is null && WaveformCache.CachedCount == 1);
        staying.DisposePlayback();
        Check("document context: the last song's outlines go with it", WaveformCache.CachedCount == 0);
    }

    private static void DcRefreshDuringRenderCase()
    {
        using var probe = new DcProbe();
        var previousPlayAll = MixerGroups.PlayAllThroughEngine;
        MixerGroups.PlayAllThroughEngine = false;
        var settings = new AppSettings();
        var client = new AudioEngineClient();
        var sent = new Dictionary<int, List<string>>();
        try
        {
            client.AttachFakeForTest();
            client.ClipsSentForTest = (slot, clips) => sent[slot] = clips.Select(c => c.File).ToList();
            var media = TestMediaContext(settings, @"C:/songs/a/a.tforge");
            DcApprove(DcUnc, media);
            var song = DcAudioSong(DcUnc);
            object owner = new();
            client.Sync(song.Tracks, settings.Plugins, null, song, owner, media);
            MediaAccess.WaitResolved(3000); PumpUi();
            var slot = client.SlotOf(song.Tracks[0]);
            Check("document context: the approved network clip is on the engine before the render", slot >= 0 && sent.TryGetValue(slot, out var before) && before.Count == 1);

            client.Rendering = true;
            MediaAccess.Revoke(DcApprovalOf(settings, media), media);
            PumpUi();
            Check("document context: an approval change during a render sends nothing to the engine (no reload mid-render)", sent[slot].Count == 1);
            client.Rendering = false;
            PumpUi();
            Check("document context: when the render ends the live clips are judged again with the changed approvals", sent[slot].Count == 0);
        }
        finally { client.Rendering = false; client.Dispose(); MixerGroups.PlayAllThroughEngine = previousPlayAll; }
    }

    private static void DcApprovalMergeCase()
    {
        static MediaApproval Ap(string project, string folder) => new() { Project = project, Folder = folder };
        var live = new List<MediaApproval> { Ap("a", @"\\s\a") };
        var merge = new MediaApprovalMerge(() => live);
        live.Add(Ap("b", @"\\s\b"));   // another window allows it while the dialog is open
        var staged = new AppSettings(); staged.Audio.ApprovedMedia = new() { Ap("a", @"\\s\a") };
        merge.Stage(staged);
        Check("document context: an approval given while Preferences is open survives the preview", staged.Audio.ApprovedMedia.Count == 2 && staged.Audio.ApprovedMedia.Any(x => x.Project == "b"));
        var again = new AppSettings(); again.Audio.ApprovedMedia = new() { Ap("a", @"\\s\a") };   // the dialog's own copy never receives the merge
        merge.Stage(again);
        Check("document context: staging again with the dialog's unchanged copy keeps it (the dialog did not remove it)", again.Audio.ApprovedMedia.Count == 2);

        var withNew = new AppSettings(); withNew.Audio.ApprovedMedia = new() { Ap("a", @"\\s\a"), Ap("c", @"\\s\c") };   // allowed through the dialog's own Linked audio window
        live.Add(Ap("c", @"\\s\c"));
        merge.Stage(withNew);
        Check("document context: what the dialog added is kept without a duplicate", withNew.Audio.ApprovedMedia.Count(x => x.Project == "c") == 1 && withNew.Audio.ApprovedMedia.Count == 3);

        var removed = new AppSettings(); removed.Audio.ApprovedMedia = new() { Ap("b", @"\\s\b"), Ap("c", @"\\s\c") };   // the dialog dropped "a" (revoked in its own window or reset)
        live.RemoveAll(x => x.Project == "a");
        merge.Stage(removed);
        Check("document context: what the dialog removed stays removed, and the others stay", removed.Audio.ApprovedMedia.Count == 2 && removed.Audio.ApprovedMedia.All(x => x.Project != "a"));

        live.Add(Ap("d", @"\\s\d"));
        var reset = new AppSettings(); reset.Audio.ApprovedMedia = new();   // "Reset all settings" while "d" was just allowed elsewhere
        merge.Stage(reset);
        Check("document context: a reset removes what the dialog showed but not an approval it never saw", reset.Audio.ApprovedMedia.Count == 1 && reset.Audio.ApprovedMedia[0].Project == "d");
    }

    private sealed class DcPrefsHost : ISettingsWindowHost
    {
        public required Func<AppSettings, Action<AppSettings>, Action<AppSettings>, SettingsShowResult> OnShow;
        public SettingsShowResult Show(AppSettings current, Action<AppSettings> apply, Action<AppSettings> preview) => OnShow(current, apply, preview);
    }

    private static void DcPreferencesApplyKeepsApprovalCase(MainWindow a, LifetimeContext context)
    {
        var real = LtField<ISettingsWindowHost>(a, "_settingsWindowHost");
        var store = context.Store;
        store.Settings.Audio.ApprovedMedia = new() { new MediaApproval { Project = @"C:\p\a.tforge", Folder = @"\\srv\a" } };
        var added = new MediaApproval { Project = @"C:\p\b.tforge", Folder = @"\\srv\b" };
        try
        {
            LtSet(a, "_settingsWindowHost", new DcPrefsHost
            {
                OnShow = (current, apply, preview) =>
                {
                    var staged = SettingsMigration.Clone(current);   // what the dialog edits
                    lock (store.Settings.Audio.ApprovedMedia) store.Settings.Audio.ApprovedMedia.Add(added);   // another window's "Allow" while the dialog is open
                    staged.General.ShowStatusBar = !staged.General.ShowStatusBar;   // some unrelated setting changes in the dialog
                    preview(SettingsMigration.Clone(staged));
                    apply(SettingsMigration.Clone(staged));
                    return SettingsShowResult.Applied;
                },
            });
            var statusBar = store.Settings.General.ShowStatusBar;
            LtCall(a, "Prefs_Click", a, new RoutedEventArgs());
            var approvals = store.Settings.Audio.ApprovedMedia;
            Check("document context: Apply keeps the approval that was given while Preferences was open", approvals.Any(x => x.Project == added.Project && x.Folder == added.Folder) && approvals.Count == 2,
                string.Join(" | ", approvals.Select(x => x.Project)));
            Check("document context: the dialog's own change was applied", store.Settings.General.ShowStatusBar != statusBar);
        }
        finally { LtSet(a, "_settingsWindowHost", real); }
    }

    private static void DcSlowPromptOnceCase(MainWindow a)
    {
        const string path = @"C:\NoSuch\Slow.vst3";
        var b = NewLifetimeWindow();
        try
        {
            foreach (var window in new[] { a, b })
                window.OpenDocuments[0].Project.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "Slow", Path = path, Format = "VST3", Type = PluginSlotType.Instrument });
            SettleLifetimeDispatcher();
            var engine = AudioEngineClient.Instance;
            var raise = LtField<Action<string, PluginCallKind, int>>(engine, "PluginSlow");
            raise?.Invoke(path, PluginCallKind.Load, (int)EngineWatchdog.LongNoticeSec + 1);   // the shared engine reports it once: every window hears it
            SettleLifetimeDispatcher();
            var prompts = Application.Current.Windows.OfType<ThemedConfirmDialog>().Where(w => w.Title == "Plug-in loading slowly").ToList();
            Check("document context: two windows whose songs use the slow plug-in show one prompt, not two", prompts.Count == 1, $"{prompts.Count} prompt(s)");
            foreach (var prompt in prompts) { try { prompt.Close(); } catch (InvalidOperationException) { } }
            SettleLifetimeDispatcher();
            raise?.Invoke(path, PluginCallKind.Load, (int)EngineWatchdog.LongNoticeSec + 5);
            SettleLifetimeDispatcher();
            var next = Application.Current.Windows.OfType<ThemedConfirmDialog>().Where(w => w.Title == "Plug-in loading slowly").ToList();
            Check("document context: after the prompt was answered a later slow load asks again", next.Count == 1, $"{next.Count} prompt(s)");
            foreach (var prompt in next) { try { prompt.Close(); } catch (InvalidOperationException) { } }
        }
        finally { try { b.Close(); } catch (InvalidOperationException) { } SettleLifetimeDispatcher(); }
    }
}
