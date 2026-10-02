using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

/// <summary>
/// Closure audit (remediation plan section 10): the interaction cases that connect R1, R2, R4 and R5.
/// (a) an imported long song with linked audio keeps its identity across a window transfer and Save As;
/// (c) closing one window neither clears another window's approval nor stops its playback.
/// (b) (a failed export leaves the original and the unsaved state intact) is covered in SelfTestGpFidelity ("preflight export: a failed write ...").
/// </summary>
public static partial class SelfTest
{
    // ---------- (a) document-context group ----------

    private static void ClosureImportedLongSongIdentityCase()
    {
        using var probe = new DcProbe();
        var store = AppSettingsStore.Open(Path.Combine(Path.GetTempPath(), "tf-closure-store-" + Guid.NewGuid().ToString("N")[..8] + ".json"), TimeSpan.FromMinutes(30));
        using var scope = AppSettingsStore.OverrideSharedForTest(store);
        var folder = Path.Combine(Path.GetTempPath(), "tf-closure-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            const int bars = 1_200;
            var gp = Path.Combine(folder, "long.gp5");
            File.WriteAllBytes(gp, SyntheticGuitarPro35.Write(500, bars));
            var opened = new DocumentController().Open(gp);
            // As MainWindow.OpenDocument does for an imported song: no save path, its source folder for relative media.
            var doc = DocumentSession.FromProject(opened.Project, opened.SessionPath);
            if (opened.SourcePath is not null) doc.Media.SetSourceDirectory(Path.GetDirectoryName(opened.SourcePath));
            var track = doc.Project.Tracks[0];
            track.AudioClips.Add(new AudioClip { File = DcUnc, Name = "net", StartSec = 0, SourceLengthSec = 1, FileLengthSec = 1 });
            var localClip = Path.Combine(folder, "audio", "take.wav");
            track.AudioClips.Add(new AudioClip { File = localClip, Name = "local", StartSec = 1, SourceLengthSec = 1, FileLengthSec = 1 });
            doc.MarkClean();
            DcApprove(DcUnc, doc.Media);
            var project = doc.Project;
            var media = doc.Media;
            var sessionScope = media.ScopeKey;
            Check("closure (a): an imported 1,200-bar song has no save path, its own session scope, and its network clip approved for it",
                opened.ImportedFromGuitarPro && opened.SessionPath is null && track.Measures.Count == bars && sessionScope.StartsWith("session:", StringComparison.Ordinal)
                && MediaAccess.Evaluate(DcUnc, media).Allowed && MediaAccess.Evaluate(localClip, media).Allowed,
                $"imported {opened.ImportedFromGuitarPro}, path {opened.SessionPath}, bars {track.Measures.Count}, scope {sessionScope}");

            // Window transfer (what "Move to new window" does with the document managers).
            var first = new DocumentManager();
            first.Add(doc);
            first.AddNew();
            var second = new DocumentManager();
            second.Insert(first.Detach(0)!, 0);
            var moved = second.Documents[0];
            MediaAccess.ClearCache();
            Check("closure (a): after a window transfer the song is the same document (project, media context, scope, approval, clips)",
                ReferenceEquals(moved, doc) && ReferenceEquals(moved.Project, project) && ReferenceEquals(moved.Media, media) && media.ScopeKey == sessionScope
                && MediaAccess.Evaluate(DcUnc, moved.Media).Allowed && moved.Project.Tracks[0].AudioClips.Count == 2 && !first.Documents.Contains(doc));
            Check("closure (a): the song left behind in the first window did not get the moved song's approval",
                first.Documents.Count == 1 && !MediaAccess.Evaluate(DcUnc, first.Documents[0].Media).Allowed);

            // Save As (the first save of the imported song) through the real save path.
            var savedPath = Path.Combine(folder, "saved", "long.tforge");
            Directory.CreateDirectory(Path.GetDirectoryName(savedPath)!);
            new DocumentController().Save(moved, savedPath, moved.Project.Lyrics);
            MediaAccess.ClearCache();
            var normalized = MediaPathPolicy.Normalize(savedPath);
            Check("closure (a): Save As keeps the same document and media context; the scope becomes the saved path and the session's own approval moves with it",
                ReferenceEquals(moved.Media, media) && string.Equals(media.ScopeKey, normalized, StringComparison.OrdinalIgnoreCase) && !moved.HasUnsavedChanges
                && MediaAccess.Evaluate(DcUnc, media).Allowed && MediaAccess.Evaluate(localClip, media).Allowed
                && (store.Settings.Audio.ApprovedMedia.Any(x => string.Equals(x.Project, normalized, StringComparison.OrdinalIgnoreCase))),
                $"scope {media.ScopeKey}, dirty {moved.HasUnsavedChanges}, approvals {string.Join(";", store.Settings.Audio.ApprovedMedia.Select(x => x.Project + "|" + x.Folder))}");
            var reloaded = ProjectService.Load(savedPath, InputLimits.MaxTforgeFileBytes);
            Check("closure (a): the saved long song holds every bar and both linked clips",
                reloaded.Tracks[0].Measures.Count == bars && reloaded.Tracks[0].AudioClips.Select(c => c.File).SequenceEqual(new[] { DcUnc, localClip }, StringComparer.OrdinalIgnoreCase),
                $"bars {reloaded.Tracks[0].Measures.Count}, clips {string.Join(",", reloaded.Tracks[0].AudioClips.Select(c => c.File))}");
            var reopened = DocumentSession.FromProject(reloaded, savedPath);
            Check("closure (a): reopening the saved song finds the approval it carried (a new session, the same scope)", MediaAccess.Evaluate(DcUnc, reopened.Media).Allowed);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // ---------- (c) window-lifetime group ----------

    private static void ClosureOtherWindowKeepsApprovalAndPlaybackCase(MainWindow a)
    {
        var silent = new SilentMidiOutput();
        var songA = new DocumentSession(new PlaybackEngine(silent)) { Project = TemplateFactory.Blank() };
        songA.Project.Tracks[0].AudioClips.Add(new AudioClip { File = LifetimeUncFile, Name = "take", StartSec = 0, SourceLengthSec = 1, FileLengthSec = 1 });
        songA.MarkClean();
        LtCall(a, "AdoptDroppedDocument", songA, a.OpenDocuments.Count);
        MediaAccess.Approve(MediaAccess.Evaluate(LifetimeUncFile, songA.Media).Verdict, songA.Media);
        MediaAccess.ClearCache();
        songA.LoopEnabled = true; songA.LoopStartBar = 0; songA.LoopEndBar = 0;
        LtCall(a, "StartPlayback");
        var playingBefore = songA.Playback.Engine.IsPlaying;
        var approvedBefore = MediaAccess.Evaluate(LifetimeUncFile, songA.Media).Allowed;

        CloseOtherWindowWithItsOwnApproval();
        SettleLifetimeDispatcher();
        MediaAccess.ClearCache();
        MediaAccess.RaiseChanged();
        SettleLifetimeDispatcher();
        LtCall(a, "UpdateMediaApprovalBar");

        var approvedAfter = MediaAccess.Evaluate(LifetimeUncFile, songA.Media).Allowed;
        var bar = LtField<Border>(a, "_mediaBar");
        var playingAfter = songA.Playback.Engine.IsPlaying;
        Check("closure (c): closing another window (with its own approval of the same file) keeps this window's approval",
            approvedBefore && approvedAfter && !songA.Media.IsClosed, $"before {approvedBefore}, after {approvedAfter}, closed {songA.Media.IsClosed}");
        Check("closure (c): this window's linked-audio notice stays hidden after the other window closed", bar is null || bar.Visibility != Visibility.Visible,
            $"bar {(bar is null ? "missing" : bar.Visibility.ToString())}");
        Check("closure (c): closing another window does not stop this window's playback", playingBefore && playingAfter, $"before {playingBefore}, after {playingAfter}");

        LtCall(a, "StopPlayback");
        LtCall(a, "CloseDocument", a.OpenDocuments.ToList().IndexOf(songA));
        SettleLifetimeDispatcher();
        AudioEngineClient.Instance.ReleaseOwner(songA);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void CloseOtherWindowWithItsOwnApproval()
    {
        var b = NewLifetimeWindow();
        AddUnapprovedClip(b);
        var media = b.OpenDocuments[0].Media;
        MediaAccess.Approve(MediaAccess.Evaluate(LifetimeUncFile, media).Verdict, media);
        MediaAccess.ClearCache();
        b.Close();
    }
}
