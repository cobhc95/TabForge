using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
/// R2 (remediation plan section 3): media, approval and settings operations take the owning document's context explicitly. The matrix:
/// per-document relative paths, approval isolation (network, cache, unsaved songs, legacy empty-path approvals), imported songs without a
/// save path, background playback, stale and queued work (close, Save As, revoke), Save As / duplicate / move / reopen, and a window-close
/// that asks about every dirty document. File-system, probe and decoder seams count every attempt; fixtures are synthetic and the remote
/// paths are fake (nothing on them is ever touched).
/// </summary>
public static partial class SelfTest
{
    private const string DcUnc = @"\\tf-dc-server\share\take.wav";

    /// <summary>Counts what the media layer actually does: file-system classification probes, size/last-write probes, decoder opens. Restores the seams.</summary>
    private sealed class DcProbe : IDisposable
    {
        public readonly FakeMediaFs Fs = new();
        public readonly List<string> Stats = new(), Opens = new();
        public Action<string>? BeforeOpen;
        private readonly IMediaFileSystem _savedFs = MediaPathPolicy.FileSystem;
        private readonly long _savedBudget = WaveformCache.BudgetBytes;

        public DcProbe()
        {
            MediaPathPolicy.FileSystem = Fs;
            MediaAccess.ClearCache();
            WaveformCache.ClearAll();
            WaveformCache.StatOverride = path => { lock (Stats) Stats.Add(path); return (1000, 1); };
            WaveformCache.OpenOverride = path =>
            {
                lock (Opens) Opens.Add(path);
                BeforeOpen?.Invoke(path);
                return new FakeMediaSource(1, 8000);
            };
        }

        public int OpensOf(string path) { lock (Opens) return Opens.Count(o => string.Equals(o, path, StringComparison.OrdinalIgnoreCase)); }
        public int StatsOf(string path) { lock (Stats) return Stats.Count(o => string.Equals(o, path, StringComparison.OrdinalIgnoreCase)); }

        public void Dispose()
        {
            WaveformCache.OpenOverride = null;
            WaveformCache.StatOverride = null;
            WaveformCache.ClearAll();
            WaveformCache.BudgetBytes = _savedBudget;
            MediaPathPolicy.FileSystem = _savedFs;
            MediaAccess.ClearCache();
        }
    }

    private static MediaApproval DcApprovalOf(AppSettings settings, MediaContext context) =>
        settings.Audio.ApprovedMedia.First(a => string.Equals(a.Project, context.ScopeKey, StringComparison.OrdinalIgnoreCase));

    private static void DcApprove(string file, MediaContext context)
    {
        MediaAccess.Approve(MediaAccess.Evaluate(file, context).Verdict, context);
        MediaAccess.ClearCache();
    }

    private static void TestDocumentContext()
    {
        RunDcCase("relative media resolves per document, whatever the creation order or focus", DcRelativePathsCase);
        RunDcCase("network approval is per document and the cache never bypasses it", DcApprovalIsolationCase);
        RunDcCase("unsaved songs have their own session scope; empty-path approvals match nothing", DcUnsavedScopeCase);
        RunDcCase("an imported Guitar Pro song resolves relative media against its source folder", DcImportedSongCase);
        RunDcCase("stale and queued work is rejected", DcStaleWorkCase);
        RunDcCase("Save As, duplicate, move and reopen keep scopes explicit", DcSaveAsCase);
        RunDcCase("the shared engine judges every document's clips with its own context", DcEngineCase);
        RunDcCase("closure (a): an imported long song with linked audio keeps its identity across a window transfer and Save As", ClosureImportedLongSongIdentityCase);
        TestDocumentContextLeftovers();   // R3: the R2 review's leftovers
        RunInWindowFixture((a, context) =>
        {
            RunDcCase("the linked-audio review is bound to the song it opened for", () => DcReviewTargetCase(context));
            RunDcCase("closing a window asks about every dirty document", () => DcCloseAllDirtyCase(context));
            DcLeftoversInWindows(a, context);
        });
    }

    private static void RunDcCase(string name, Action body)
    {
        try { body(); }
        catch (Exception ex) { Check($"document context: {name} completed without throwing", false, $"{ex.GetType().Name}: {ex.Message} at {string.Join(" <- ", (ex.StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim()))}"); }
    }

    // ---------- 1: two songs, the same relative path ----------

    private static void DcRelativePathsCase()
    {
        using var probe = new DcProbe();
        var settings = new AppSettings();
        var a = TestMediaContext(settings, @"C:/songs/a/a.tforge");
        var b = TestMediaContext(settings, @"C:/songs/b/b.tforge");
        // B asks first, A second: neither creation order nor "who is active" is consulted.
        _ = WaveformCache.Get(@"clips\take.wav", b);
        _ = WaveformCache.Get(@"clips\take.wav", a);
        WaveformCache.WaitIdle(3000);
        Check("document context: the same relative clip path resolves to each song's own file", probe.OpensOf(@"C:\songs\a\clips\take.wav") == 1 && probe.OpensOf(@"C:\songs\b\clips\take.wav") == 1 && probe.Opens.Count == 2,
            string.Join(" | ", probe.Opens));
        Check("document context: both songs get their own outline (no cross-talk through a shared slot)",
            WaveformCache.Get(@"clips\take.wav", a) is not null && WaveformCache.Get(@"clips\take.wav", b) is not null);
        Check("document context: evaluating the same relative path with each context gives each song's folder",
            MediaAccess.Evaluate(@"clips\take.wav", a).Verdict.FullPath == @"C:\songs\a\clips\take.wav" && MediaAccess.Evaluate(@"clips\take.wav", b).Verdict.FullPath == @"C:\songs\b\clips\take.wav");
    }

    // ---------- 2 and 3: approval isolation, the waveform cache, revocation ----------

    private static void DcApprovalIsolationCase()
    {
        using var probe = new DcProbe();
        var settings = new AppSettings();
        var a = TestMediaContext(settings, @"C:/songs/a/a.tforge");
        var b = TestMediaContext(settings, @"C:/songs/b/b.tforge");
        DcApprove(DcUnc, a);

        _ = WaveformCache.Get(DcUnc, a); WaveformCache.WaitIdle(3000);
        Check("document context: a network folder approved for song A is read for A (one probe, one decode)", probe.StatsOf(DcUnc) == 1 && probe.OpensOf(DcUnc) == 1 && WaveformCache.Get(DcUnc, a) is not null);

        var decodes = WaveformCache.DecodeCount;
        Check("document context: song B asking for the same unapproved file gets no outline", WaveformCache.Get(DcUnc, b) is null);
        WaveformCache.WaitIdle(3000);
        Check("document context: B causes no file probe, no open and no decode of the remote file (A's cached outline does not bypass B's approval)",
            probe.StatsOf(DcUnc) == 1 && probe.OpensOf(DcUnc) == 1 && WaveformCache.DecodeCount == decodes && WaveformCache.StatusOf(DcUnc, b).State == WaveState.NeedsApproval,
            $"stats {probe.StatsOf(DcUnc)}, opens {probe.OpensOf(DcUnc)}, state {WaveformCache.StatusOf(DcUnc, b).State}");
        Check("document context: a length probe for B (not picked by the user) touches nothing", WaveformCache.LengthOf(DcUnc, b) == 0 && probe.StatsOf(DcUnc) == 1 && probe.Fs.FinalPathCalls == 0);

        // Once B is approved too, the decoded outline is reused (permission was checked first; nothing is decoded again).
        DcApprove(DcUnc, b);
        _ = WaveformCache.Get(DcUnc, b); WaveformCache.WaitIdle(3000);
        Check("document context: after B's own approval it shares A's decoded outline (no second decode)", WaveformCache.Get(DcUnc, b) is not null && WaveformCache.DecodeCount == decodes && probe.OpensOf(DcUnc) == 1,
            $"decodes {WaveformCache.DecodeCount - decodes}, opens {probe.OpensOf(DcUnc)}");

        // Revoking A: A loses its outline at once (decoded data does not keep granting access); B keeps its own.
        MediaAccess.Revoke(DcApprovalOf(settings, a), a);
        MediaAccess.ClearCache();
        Check("document context: revoking A's approval takes A's outline away immediately, B's stays", WaveformCache.Get(DcUnc, a) is null && WaveformCache.StatusOf(DcUnc, a).State == WaveState.NeedsApproval && WaveformCache.Get(DcUnc, b) is not null);
    }

    // ---------- 4: unsaved songs ----------

    private static void DcUnsavedScopeCase()
    {
        using var probe = new DcProbe();
        var settings = new AppSettings();
        var u1 = TestMediaContext(settings, null);
        var u2 = TestMediaContext(settings, null);
        DcApprove(DcUnc, u1);
        Check("document context: approving one unsaved song leaves the other unapproved", MediaAccess.Evaluate(DcUnc, u1).Allowed && !MediaAccess.Evaluate(DcUnc, u2).Allowed && u1.ScopeKey != u2.ScopeKey);
        Check("document context: nothing is written under an empty or shared key for an unsaved song", settings.Audio.ApprovedMedia.Count == 0 && u1.ScopeKey.StartsWith("session:", StringComparison.Ordinal));
        // An approval stored with an empty path (from before unsaved songs had their own scope) is inert.
        settings.Audio.ApprovedMedia.Add(new MediaApproval { Project = "", Folder = @"\\tf-dc-server\share" });
        var u3 = TestMediaContext(settings, null);
        var saved = TestMediaContext(settings, @"C:/songs/c/c.tforge");
        MediaAccess.ClearCache();
        Check("document context: a legacy empty-path approval is no blanket trust for unsaved or saved songs", !MediaAccess.Evaluate(DcUnc, u3).Allowed && !MediaAccess.Evaluate(DcUnc, saved).Allowed);
        Check("document context: an identifier named inside a song cannot confer approval (the scope is made at run time)",
            !MediaAccess.Evaluate(DcUnc, TestMediaContext(settings, null)).Allowed && settings.Audio.ApprovedMedia.All(x => !x.Project.StartsWith("session:", StringComparison.Ordinal)));
    }

    // ---------- 5: imported song without a native save path ----------

    private static void DcImportedSongCase()
    {
        using var probe = new DcProbe();
        var settings = new AppSettings();
        var imported = TestMediaContext(settings, null);
        imported.SetSourceDirectory(@"C:\imports\song");
        var verdict = MediaAccess.Evaluate(@"audio\drums.wav", imported);
        Check("document context: relative media of an imported song resolves against its source folder (no save path yet)", verdict.Allowed && verdict.Verdict.FullPath == @"C:\imports\song\audio\drums.wav", verdict.Message);
        Check("document context: without a source folder or save path a relative path is refused (never resolved against another folder)", !MediaAccess.Evaluate(@"audio\drums.wav", TestMediaContext(settings, null)).Allowed);
        // The network-project policy is not broadened: only a saved song's own folder counts as "the project"; a song imported from a share
        // (or a duplicate inheriting that folder) resolves there but still needs its own approval, as before R2.
        var remoteImport = TestMediaContext(settings, null);
        remoteImport.SetSourceDirectory(@"\\tf-dc-server\share");
        var remote = MediaAccess.Evaluate("take.wav", remoteImport);
        var savedOnShare = TestMediaContext(settings, @"\\tf-dc-server\share\song.tforge");
        Check("document context: a song imported from a network folder needs approval for media beside it (a saved song opened there does not)",
            remote.State == MediaAccessState.NeedsApproval && remote.Verdict.FullPath == DcUnc && MediaAccess.Evaluate("take.wav", savedOnShare).Allowed, $"{remote.State} {remote.Verdict.FullPath}");

        // The real open path reports the folder the song came from.
        var folder = Path.Combine(Path.GetTempPath(), "tf-dc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var gp = Path.Combine(folder, "synthetic.gp");
            GuitarProExporter.Save(BuildSyntheticGpSong(), gp);
            var opened = new DocumentController().Open(gp);
            Check("document context: opening a Guitar Pro file hands the window its source path while the save path stays empty", opened is { ImportedFromGuitarPro: true, SessionPath: null } && opened.SourcePath == gp, $"{opened.SessionPath} / {opened.SourcePath}");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // ---------- 8 and 9: stale completions, revoke while queued ----------

    private static void DcStaleWorkCase()
    {
        using var probe = new DcProbe();
        var settings = new AppSettings();
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        probe.BeforeOpen = path => { if (path.EndsWith("slow.wav", StringComparison.OrdinalIgnoreCase)) { started.Set(); gate.Wait(5000); } };

        // A delayed read when the song closes: the result is dropped, nothing of it lands on another song.
        var closing = TestMediaContext(settings, @"C:/songs/x/x.tforge");
        var other = TestMediaContext(settings, @"C:/songs/y/y.tforge");
        _ = WaveformCache.Get(@"C:\audio\slow.wav", closing);
        var reading = started.Wait(3000);
        closing.Close();
        gate.Set();
        WaveformCache.WaitIdle(3000);
        Check("document context: a read that completes after its song closed is rejected (no outline kept for it)",
            reading && WaveformCache.CachedCount == 0 && WaveformCache.Get(@"C:\audio\slow.wav", closing) is null && WaveformCache.StatusOf(@"C:\audio\slow.wav", closing).State == WaveState.Failed);
        _ = WaveformCache.Get(@"C:\audio\slow.wav", other); WaveformCache.WaitIdle(3000);
        Check("document context: another song asking for that file afterwards reads it normally", WaveformCache.Get(@"C:\audio\slow.wav", other) is not null);

        // Save As during a delayed read: the old request is over (new revision), a new one starts for the new scope.
        WaveformCache.ClearAll(); started.Reset(); gate.Reset();
        var moving = TestMediaContext(settings, @"C:/songs/m/m.tforge");
        _ = WaveformCache.Get(@"C:\audio\slow.wav", moving);
        started.Wait(3000);
        moving.SetSavedPath(@"C:/songs/m2/m2.tforge");
        gate.Set();
        WaveformCache.WaitIdle(3000);
        Check("document context: a read that finished after Save As is not applied to the song's new scope", WaveformCache.CachedCount == 0);

        // Revoke while the request waits in the queue: the file is never probed or opened with the stale consent.
        WaveformCache.ClearAll(); started.Reset(); gate.Reset();
        var blocker = TestMediaContext(settings, @"C:/songs/b1/b1.tforge");
        var waiting = TestMediaContext(settings, @"C:/songs/w/w.tforge");
        DcApprove(DcUnc, waiting);
        _ = WaveformCache.Get(@"C:\audio\slow.wav", blocker);   // the single worker is busy inside this read
        started.Wait(3000);
        _ = WaveformCache.Get(DcUnc, waiting);                   // queued behind it, approved at this moment
        MediaAccess.Revoke(DcApprovalOf(settings, waiting), waiting);
        MediaAccess.ClearCache();
        gate.Set();
        WaveformCache.WaitIdle(3000);
        Check("document context: an approval revoked while the request was queued means the file is never probed or opened", probe.StatsOf(DcUnc) == 0 && probe.OpensOf(DcUnc) == 0 && WaveformCache.Get(DcUnc, waiting) is null,
            $"stats {probe.StatsOf(DcUnc)}, opens {probe.OpensOf(DcUnc)}");
    }

    // ---------- 10: Save As, duplicate, move, reopen ----------

    private static void DcSaveAsCase()
    {
        var settings = new AppSettings();
        var unsaved = TestMediaContext(settings, null);
        DcApprove(DcUnc, unsaved);
        var revisionBefore = unsaved.Revision;
        unsaved.SetSavedPath(@"C:/songs/first/first.tforge");
        MediaAccess.ClearCache();
        Check("document context: the first save of an unsaved song carries that session's own approvals to its path, and starts a new revision",
            MediaAccess.Evaluate(DcUnc, unsaved).Allowed && settings.Audio.ApprovedMedia.Count == 1 && settings.Audio.ApprovedMedia[0].Project == @"C:\songs\first\first.tforge" && unsaved.Revision != revisionBefore && !unsaved.IsCurrent(revisionBefore));

        unsaved.SetSavedPath(@"C:/songs/second/second.tforge");   // Save As from a saved path
        MediaAccess.ClearCache();
        Check("document context: Save As to another path does not copy approvals (the new path is evaluated again); the old path keeps its own",
            !MediaAccess.Evaluate(DcUnc, unsaved).Allowed && settings.Audio.ApprovedMedia.Count == 1 && settings.Audio.ApprovedMedia[0].Project == @"C:\songs\first\first.tforge");
        var reopened = TestMediaContext(settings, @"C:/songs/first/first.tforge");
        Check("document context: reopening the first path finds its approval (a new session, the same scope)", MediaAccess.Evaluate(DcUnc, reopened).Allowed);

        // Duplicate and move: a duplicate is a new song (its own scope, no approvals) that still resolves the original's relative media; a moved tab keeps its context.
        var store = AppSettingsStore.Open(Path.Combine(Path.GetTempPath(), "tf-dc-store-" + Guid.NewGuid().ToString("N")[..8] + ".json"), TimeSpan.FromMinutes(30));
        using var scope = AppSettingsStore.OverrideSharedForTest(store);
        var manager = new DocumentManager();
        var original = DocumentSession.FromProject(TemplateFactory.Blank(), @"C:\songs\orig\orig.tforge");
        manager.Add(original);
        var copy = manager.Duplicate(0)!;
        var mover = new DocumentManager();
        var media = original.Media;
        mover.Insert(manager.Detach(0)!, 0);
        Check("document context: a duplicate has its own scope and no approvals but the original's media folder",
            copy.Media.ScopeKey != original.Media.ScopeKey && copy.Media.ScopeKey.StartsWith("session:", StringComparison.Ordinal) && copy.Media.BaseDirectory == @"C:\songs\orig");
        Check("document context: a tab moved to another window keeps its media context (same scope, same approvals)", ReferenceEquals(mover.Documents[0].Media, media) && media.ScopeKey == @"C:\songs\orig\orig.tforge");
    }

    // ---------- 6 and 13: the shared engine, background playback, skipped plug-ins ----------

    private static SongProject DcAudioSong(string file)
    {
        var song = TemplateFactory.Blank();
        song.Tracks[0].AudioClips.Add(new AudioClip { File = file, Name = "take", StartSec = 0, SourceLengthSec = 1, FileLengthSec = 1 });
        return song;
    }

    private static void DcEngineCase()
    {
        using var probe = new DcProbe();
        var previousPlayAll = TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine;
        TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = false;
        var settings = new AppSettings();
        var client = new AudioEngineClient();
        client.Mixer.PlayAllThroughEngine = false;   // these cases describe the engine-off routing
        var sent = new Dictionary<int, List<string>>();
        try
        {
            client.AttachFakeForTest();
            client.ClipsSentForTest = (slot, clips) => sent[slot] = clips.Select(c => c.File).ToList();
            var mediaA = TestMediaContext(settings, @"C:/songs/a/a.tforge");
            var mediaB = TestMediaContext(settings, @"C:/songs/b/b.tforge");
            DcApprove(DcUnc, mediaA);
            var songA = DcAudioSong(DcUnc); var songB = DcAudioSong(DcUnc);
            object ownerA = new(), ownerB = new();
            client.IsOwnerPlaying = o => ReferenceEquals(o, ownerA);   // A keeps playing in the background while B is active

            client.Sync(songA.Tracks, settings.Plugins, null, songA, ownerA, mediaA);
            var slotA = client.SlotOf(songA.Tracks[0]);
            client.Sync(songB.Tracks, settings.Plugins, null, songB, ownerB, mediaB);
            MediaAccess.WaitResolved(3000); PumpUi();   // the classification is resolved off the UI thread; the clips follow
            var slotB = client.SlotOf(songB.Tracks[0]);
            Check("document context: the engine sends the approved network clip for A and none for B (B's context decides B's clips)",
                slotA >= 0 && slotB >= 0 && slotA != slotB && sent.TryGetValue(slotA, out var forA) && forA.Count == 1 && forA[0] == DcUnc && sent.TryGetValue(slotB, out var forB) && forB.Count == 0,
                $"A slot {slotA}: {(sent.TryGetValue(slotA, out var x) ? string.Join(",", x) : "-")}; B slot {slotB}: {(sent.TryGetValue(slotB, out var y) ? string.Join(",", y) : "-")}");
            Check("document context: A keeps its own slot live and unparked while B syncs (background playback keeps A's media and permissions)", !client.IsParkedForTest(songA.Tracks[0]) && sent[slotA].Count == 1);

            // Revoking A's approval (while B is the active document): A's live slot is judged again with A's context at once.
            MediaAccess.Revoke(DcApprovalOf(settings, mediaA), mediaA);
            PumpUi();
            Check("document context: revoking A's approval removes A's clip from the engine even though B synced last; B's slot is untouched", sent[slotA].Count == 0 && sent[slotB].Count == 0 && client.SlotOf(songB.Tracks[0]) == slotB);

            // Skipped plug-ins belong to a document: the same plug-in is unavailable in A and not in B.
            var plugin = new PluginSlot { Name = "Slow", Path = @"C:\NoSuch\Slow.vst3", Format = "VST3", Type = PluginSlotType.Instrument };
            var pluginA = TemplateFactory.Blank(); pluginA.Tracks[0].Rig.Plugins.Add(plugin);
            var pluginB = TemplateFactory.Blank(); pluginB.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "Slow", Path = plugin.Path, Format = "VST3", Type = PluginSlotType.Instrument });
            PluginTrust.Approve(settings.Plugins, plugin.Path);   // trusted: only the per-document skip can make it unavailable
            var skippedForA = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { plugin.Path };
            client.RefreshAvailability(pluginA.Tracks, settings.Plugins, skippedForA);
            client.RefreshAvailability(pluginB.Tracks, settings.Plugins, new HashSet<string>());
            Check("document context: a plug-in skipped for one document is skipped only in that document's tracks", pluginA.Tracks[0].Rig.Plugins[0].Unavailable && !pluginB.Tracks[0].Rig.Plugins[0].Unavailable,
                $"A unavailable {pluginA.Tracks[0].Rig.Plugins[0].Unavailable}, B unavailable {pluginB.Tracks[0].Rig.Plugins[0].Unavailable}");
        }
        finally
        {
            client.ClipsSentForTest = null;
            client.Dispose();
            TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = previousPlayAll;
        }
    }

    // ---------- 12: the review window belongs to the song it was opened for ----------

    private static void DcReviewTargetCase(LifetimeContext context)
    {
        var w = NewLifetimeWindow();
        AddUnapprovedClip(w, 0);
        LtCall(w, "NewTab");
        AddUnapprovedClip(w, 1);
        var first = w.OpenDocuments[0]; var second = w.OpenDocuments[1];
        LtCall(w, "ActivateTabAt", 0);
        var before = context.Captured.Count;
        LtCall(w, "ReviewLinkedAudio", w);                    // opened for the first tab
        var review = context.Captured.Skip(before).FirstOrDefault(x => x.Title == "Linked audio");
        LtCall(w, "ActivateTabAt", 1);                        // focus moves to the second tab while the review is open
        var box = review is null ? null : Logical<CheckBox>(review).FirstOrDefault();
        if (box is not null) box.IsChecked = true;
        var allow = review is null ? null : Logical<Button>(review).FirstOrDefault(x => x.Content as string == "Allow selected");
        allow?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        MediaAccess.ClearCache();
        Check("document context: Allow in a review opened for one song applies to that song after the focus moved to another tab",
            review is not null && box is not null && allow is not null && MediaAccess.Evaluate(LifetimeUncFile, first.Media).Allowed && !MediaAccess.Evaluate(LifetimeUncFile, second.Media).Allowed,
            $"review {review is not null}, first allowed {MediaAccess.Evaluate(LifetimeUncFile, first.Media).Allowed}, second allowed {MediaAccess.Evaluate(LifetimeUncFile, second.Media).Allowed}");
        foreach (var dialog in context.Captured.ToArray()) { try { dialog.Close(); } catch (InvalidOperationException) { } }
        context.Captured.Clear();
        foreach (var document in w.OpenDocuments) { document.Project.Tracks[0].AudioClips.Clear(); document.MarkClean(); }
        w.Close();
        SettleLifetimeDispatcher();
    }

    // ---------- 4 (data loss): closing a window asks about every dirty document ----------

    private static void DcCloseAllDirtyCase(LifetimeContext context)
    {
        var previous = DialogHost.Capture;
        var asked = new List<string>();
        var answers = new Queue<MessageBoxResult>();
        DialogHost.Capture = dialog =>
        {
            if (dialog is ThemedConfirmDialog confirm && answers.Count > 0) { asked.Add(confirm.Title ?? ""); confirm.AnswerForTest(answers.Dequeue()); return true; }
            context.Captured.Add(dialog); return false;
        };
        try
        {
            var w = NewLifetimeWindow();
            LtCall(w, "NewTab"); LtCall(w, "NewTab");
            for (var i = 0; i < w.OpenDocuments.Count; i++) { var d = w.OpenDocuments[i]; d.Project.Title = $"Dirty {i}"; d.Project.IsDirty = true; }
            LtCall(w, "ActivateTabAt", 0);
            var dirtyCount = w.OpenDocuments.Count(d => d.HasUnsavedChanges);

            answers.Clear(); asked.Clear();
            foreach (var r in new[] { MessageBoxResult.No, MessageBoxResult.No, MessageBoxResult.Cancel }) answers.Enqueue(r);
            w.Close();
            SettleLifetimeDispatcher();
            var discardSet = LtField<System.Collections.IEnumerable>(w, "_discardOnClose")!.Cast<object>().Count();
            Check("document context: with three dirty tabs the window asks about each; Cancel at the third leaves the window open, every tab dirty and nothing marked as discarded",
                dirtyCount == 3 && asked.Count == 3 && w.IsVisible && w.OpenDocuments.Count == 3 && w.OpenDocuments.All(d => d.HasUnsavedChanges) && discardSet == 0,
                $"dirty {dirtyCount}, asked {asked.Count}, visible {w.IsVisible}, tabs {w.OpenDocuments.Count}, still dirty {w.OpenDocuments.Count(d => d.HasUnsavedChanges)}, discard marks {discardSet}");

            answers.Clear(); asked.Clear();
            foreach (var r in new[] { MessageBoxResult.Yes, MessageBoxResult.Cancel }) answers.Enqueue(r);
            w.Close();
            SettleLifetimeDispatcher();
            Check("document context: Yes for the first tab then Cancel for the second saves nothing and closes nothing (all answers come before any action)",
                asked.Count == 2 && w.IsVisible && w.OpenDocuments.All(d => d.HasUnsavedChanges) && w.OpenDocuments.All(d => d.Path is null));

            answers.Clear(); asked.Clear();
            foreach (var r in new[] { MessageBoxResult.No, MessageBoxResult.No, MessageBoxResult.No }) answers.Enqueue(r);
            w.Close();
            SettleLifetimeDispatcher();
            Check("document context: answering No for every dirty tab closes the window after exactly one question per tab", asked.Count == 3 && !w.IsVisible, $"asked {asked.Count}, visible {w.IsVisible}");
        }
        finally { DialogHost.Capture = previous; }
    }
}
