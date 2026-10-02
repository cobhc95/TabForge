using System.IO;
using System.Runtime.CompilerServices;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Audit 6 A6-01 / A6-05: linked audio goes through the media policy; waveform decoding is bounded, cancellable and does not leak.</summary>
public static partial class SelfTest
{
    private sealed class FakeMediaFs : IMediaFileSystem
    {
        public int FinalPathCalls;
        public readonly Dictionary<string, string> Links = new(StringComparer.OrdinalIgnoreCase);
        public DriveType DriveTypeOf(string root) => root.ToUpperInvariant() switch
        {
            @"Z:\" => DriveType.Network, @"R:\" => DriveType.Removable, _ => DriveType.Fixed,
        };
        /// <summary>Runs inside every link resolution (a test makes it stall, as a link into an unreachable share does).</summary>
        public Action<string>? OnFinalPath;
        public string? FinalPathOf(string fullPath) { FinalPathCalls++; OnFinalPath?.Invoke(fullPath); return Links.TryGetValue(fullPath, out var f) ? f : null; }
    }

    /// <summary>A fake decoder: <paramref name="seconds"/> long, plays <paramref name="frames"/> samples of noise (then ends), optionally slowly.</summary>
    private sealed class FakeMediaSource : IMediaSource
    {
        private long _left;
        private readonly int _sleepMs;
        public int Reads;
        public volatile bool Disposed;
        public FakeMediaSource(double seconds, long frames, int sleepMs = 0) { TotalSeconds = seconds; _left = frames; _sleepMs = sleepMs; }
        public int SampleRate => 8000;
        public int Channels => 1;
        public double TotalSeconds { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            Interlocked.Increment(ref Reads);
            if (_sleepMs > 0) Thread.Sleep(_sleepMs);
            var n = (int)Math.Min(count, _left);
            for (var i = 0; i < n; i++) buffer[offset + i] = 0.5f;
            _left -= n;
            return n;
        }
        public void Dispose() => Disposed = true;
    }

    /// <summary>A media context over a scratch settings object (its approvals), saved at <paramref name="savedPath"/> (null: an unsaved song).</summary>
    private static MediaContext TestMediaContext(AppSettings settings, string? savedPath)
    {
        var context = new MediaContext(() => settings.Audio, persist: () => { });
        context.SetSavedPath(savedPath);
        return context;
    }

    // The waveform tests below read through one context (a saved song); these keep their call sites short.
    private static MediaContext _wfCtx = MediaContext.Anonymous;
    private static float[]? WfGet(string file) => WaveformCache.Get(file, _wfCtx);
    private static WaveStatus WfStatus(string file) => WaveformCache.StatusOf(file, _wfCtx);
    private static double WfLength(string file, bool userPicked = false) => WaveformCache.LengthOf(file, _wfCtx, userPicked);
    private static void WfCancelUnused(IEnumerable<string> wanted) => WaveformCache.CancelUnused(wanted, _wfCtx);

    private static void TestMediaPathPolicy()
    {
        var fs = new FakeMediaFs();
        var settings = new AppSettings();
        var savedFs = MediaPathPolicy.FileSystem;
        MediaPathPolicy.FileSystem = fs;
        var songA = TestMediaContext(settings, @"C:/songs/a/song.tforge"); var songB = TestMediaContext(settings, @"C:/songs/b/song.tforge");
        MediaAccess.ClearCache();
        try
        {
            MediaVerdict C(string p, string? folder = @"C:\songs\a") => MediaPathPolicy.Classify(p, folder);
            foreach (var bad in new[] { @"\\.\PhysicalDrive0\x.wav", @"\\?\C:\a\x.wav", @"\\?\UNC\srv\sh\x.wav", "//./pipe/x.wav", @"C:\a\NUL.wav", @"C:\a\com1\x.wav", @"C:\a\x.wav:stream.wav", @"C:\GLOBALROOT\x.wav" })
                Check($"device / stream / reserved path refused: {bad}", C(bad).Location == MediaLocation.Device || C(bad).Location == MediaLocation.Invalid, C(bad).Location.ToString());
            Check("\\\\?\\ and \\\\.\\ forms are Device", C(@"\\?\C:\a\x.wav").Location == MediaLocation.Device && C(@"\\.\x\y.wav").Location == MediaLocation.Device);
            Check("a non-audio extension is refused", C(@"C:\a\payload.exe").Refused && C(@"C:\a\x.dll").Refused && C(@"C:\a\noext").Refused);
            Check("an empty path and a relative path without a project folder are refused", C("").Refused && C(@"clips\x.wav", null).Refused && C(@"\x.wav").Refused);
            Check("a relative path resolves inside the project folder", C(@"clips\x.wav") is { Location: MediaLocation.Local, InProject: true } v && v.FullPath == @"C:\songs\a\clips\x.wav");
            Check("a local fixed-drive path is Local, outside the project", C(@"C:\music\x.wav") is { Location: MediaLocation.Local, InProject: false });
            fs.FinalPathCalls = 0;
            Check("UNC is Network and is classified without touching the file system", C(@"\\srv\share\x.wav").Location == MediaLocation.Network && fs.FinalPathCalls == 0);
            Check("a mapped network drive is Network, an optical / removable drive is Removable", C(@"Z:\x.wav").Location == MediaLocation.Network && C(@"R:\x.wav").Location == MediaLocation.Removable);
            fs.Links[@"C:\songs\a\link.wav"] = @"\\?\UNC\srv\sh\real.wav";
            Check("a local link that leads to a network file is judged by its target", C("link.wav") is { Location: MediaLocation.Network, InProject: false } l && l.FullPath == @"\\srv\sh\real.wav");
            fs.Links[@"C:\songs\a\dev.wav"] = @"\\.\pipe\x";
            Check("a link to a device path is refused", C("dev.wav").Location == MediaLocation.Device);
            fs.Links[@"C:\songs\a\out.wav"] = @"D:\elsewhere\out.wav";
            Check("a link leading out of the project folder is no longer 'in project'", C("out.wav") is { Location: MediaLocation.Local, InProject: false });

            // Approval: per project and per folder, revocable.
            fs.FinalPathCalls = 0;
            var unc = MediaAccess.Evaluate(@"\\srv\share\x.wav", songA);
            Check("a network file needs approval before anything is opened", unc.State == MediaAccessState.NeedsApproval && fs.FinalPathCalls == 0 && unc.Message.Contains("network location", StringComparison.Ordinal), unc.Message);
            MediaAccess.Approve(unc.Verdict, songA);
            MediaAccess.ClearCache();
            Check("approving the folder allows files in it and its subfolders for that song",
                MediaAccess.Evaluate(@"\\srv\share\y.wav", songA).Allowed && MediaAccess.Evaluate(@"\\srv\share\sub\y.wav", songA).Allowed);
            Check("...but not another folder, and not another song",
                !MediaAccess.Evaluate(@"\\srv\other\y.wav", songA).Allowed && !MediaAccess.Evaluate(@"\\srv\share\y.wav", songB).Allowed);
            Check("a removable drive needs approval too", MediaAccess.Evaluate(@"R:\x.wav", songA).State == MediaAccessState.NeedsApproval);
            Check("local and in-project files load without approval", MediaAccess.Evaluate(@"C:\music\x.wav", songA).Allowed && MediaAccess.Evaluate("clips\\q.wav", songA).Allowed);
            MediaAccess.Revoke(settings.Audio.ApprovedMedia[0], songA);
            MediaAccess.ClearCache();
            Check("revoking the approval blocks the folder again", settings.Audio.ApprovedMedia.Count == 0 && !MediaAccess.Evaluate(@"\\srv\share\y.wav", songA).Allowed);
            Check("the approval list survives settings validation", ValidatedApprovalsKept());

            // Project validation and the engine's own guard.
            var song = new SongProject();
            song.Tracks.Add(new TrackModel { Name = "t" });
            song.Tracks[0].AudioClips.Add(new AudioClip { File = @"\\.\pipe\x.wav", Name = "c", SourceLengthSec = 1, FileLengthSec = 1 });
            Check("a project whose clip names a device path is rejected", ThrowsInvalidData(() => ProjectValidator.Validate(song), out _));
            song.Tracks[0].AudioClips[0].File = @"C:\music\ok.wav";
            Check("the same project with a normal path is accepted (the rejection above was about the path)", !ThrowsInvalidData(() => ProjectValidator.Validate(song), out var why), why);
            Check("the engine refuses device, relative, stream and non-audio clip paths",
                !ClipPathGuard.IsAllowed(@"\\.\pipe\x.wav", out _) && !ClipPathGuard.IsAllowed(@"\\?\C:\x.wav", out _) && !ClipPathGuard.IsAllowed("x.wav", out _)
                && !ClipPathGuard.IsAllowed(@"C:\x.wav:s", out _) && !ClipPathGuard.IsAllowed(@"C:\x.exe", out _) && !ClipPathGuard.IsAllowed(@"C:\NUL.wav", out _)
                && ClipPathGuard.IsAllowed(@"C:\music\x.wav", out _) && ClipPathGuard.IsAllowed(@"\\srv\share\x.flac", out _));
        }
        finally
        {
            MediaPathPolicy.FileSystem = savedFs;
            MediaAccess.ClearCache();
        }
    }

    private static bool ValidatedApprovalsKept()
    {
        var s = new AppSettings();
        s.Audio.ApprovedMedia.Add(new MediaApproval { Project = @"C:\a\s.tforge", Folder = @"\\srv\share" });
        s.Audio.ApprovedMedia.Add(new MediaApproval { Project = "", Folder = "" });
        SettingsValidator.Normalize(s);
        return s.Audio.ApprovedMedia.Count == 1 && s.Audio.ApprovedMedia[0].Folder == @"\\srv\share";
    }

    private static void TestWaveformCacheBounds()
    {
        var fs = new FakeMediaFs();
        var settings = new AppSettings();
        var savedFs = MediaPathPolicy.FileSystem;
        var savedBudget = WaveformCache.BudgetBytes; var savedRecheck = WaveformCache.RecheckMs;
        MediaPathPolicy.FileSystem = fs;
        _wfCtx = TestMediaContext(settings, Path.Combine(Path.GetTempPath(), "tf-wave-song.tforge"));
        MediaAccess.ClearCache();
        WaveformCache.ClearAll();
        var temp = Path.Combine(Path.GetTempPath(), "TabForge-wave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            // A network path is not opened before approval; after approval it is read once.
            var opened = new List<string>();
            WaveformCache.OpenOverride = p => { lock (opened) opened.Add(p); return new FakeMediaSource(1, 8000); };
            var before = WaveformCache.DecodeCount;
            var unc = @"\\srv\share\take.wav";
            Check("a network clip starts as 'not loaded'", WfGet(unc) is null);
            WaveformCache.WaitIdle(3000);
            var status = WfStatus(unc);
            Check("a network clip is not opened without approval (no decoder call, status asks for approval)",
                WaveformCache.DecodeCount == before && opened.Count == 0 && status.State == WaveState.NeedsApproval && status.Message!.Contains("approve", StringComparison.Ordinal), $"{status.State} {status.Message}");
            MediaAccess.Approve(MediaAccess.Evaluate(unc, _wfCtx).Verdict, _wfCtx);
            MediaAccess.ClearCache();
            _ = WfGet(unc);
            WaveformCache.WaitIdle(3000);
            Check("after approval the clip is read", WfGet(unc) is { Length: > 0 } && opened.Count == 1 && opened[0] == unc, $"{opened.Count}");
            // Device paths: never.
            before = WaveformCache.DecodeCount;
            _ = WfGet(@"\\.\pipe\x.wav"); _ = WfGet(@"\\?\C:\x.wav");
            WaveformCache.WaitIdle(3000);
            Check("device paths are refused and never decoded", WaveformCache.DecodeCount == before && WfStatus(@"\\.\pipe\x.wav").State == WaveState.Failed);
            Check("LengthOf refuses device paths and unapproved network paths; a picked network file is fine",
                WfLength(@"\\.\pipe\x.wav", userPicked: true) == 0 && WfLength(@"\\srv\nope\x.wav") == 0 && WfLength(@"\\srv\nope\x.wav", userPicked: true) > 0);

            // Limits.
            var local = Path.Combine(temp, "a.wav");
            WaveformCache.OpenOverride = _ => new FakeMediaSource(3 * 3600, 8000);
            _ = WfGet(local); WaveformCache.WaitIdle(3000);
            Check("audio longer than the duration limit is refused with a clear error", WfStatus(local) is { State: WaveState.Failed } d && d.Message!.Contains("too long", StringComparison.Ordinal));
            var endless = Path.Combine(temp, "endless.wav");
            WaveformCache.OpenOverride = _ => new FakeMediaSource(10, long.MaxValue);   // claims 10 s, never ends
            var savedMax = WaveformCache.MaxSecondsOverride; WaveformCache.MaxSecondsOverride = 10;
            try { _ = WfGet(endless); Check("a source that never ends stops at the output-sample limit", WaveformCache.WaitIdle(10000) && WfStatus(endless).State == WaveState.Failed); }
            finally { WaveformCache.MaxSecondsOverride = savedMax; }
            var big = Path.Combine(temp, "big.wav");
            File.WriteAllBytes(big, new byte[4096]);
            WaveformCache.OpenOverride = null;
            var savedBytes = WaveformCache.MaxFileBytesOverride; WaveformCache.MaxFileBytesOverride = 1000;
            var decodes = WaveformCache.DecodeCount;
            try
            {
                _ = WfGet(big); WaveformCache.WaitIdle(3000);
                Check("a file over the size limit is refused before decoding", WaveformCache.DecodeCount == decodes && WfStatus(big) is { State: WaveState.Failed } z && z.Message!.Contains("too large", StringComparison.Ordinal));
                Check("LengthOf applies the size limit", WfLength(big, userPicked: true) == 0);
            }
            finally { WaveformCache.MaxFileBytesOverride = savedBytes; }
            var broken = Path.Combine(temp, "broken.wav");
            File.WriteAllBytes(broken, new byte[2048]);
            _ = WfGet(broken); WaveformCache.WaitIdle(5000);
            Check("a corrupt file shows a clip error, never a crash", WfStatus(broken) is { State: WaveState.Failed } b && !string.IsNullOrEmpty(b.Message), WfStatus(broken).State.ToString());

            // Cancellation: a slow decode stops when its timeline goes away (or the clip is removed).
            WaveformCache.ClearAll();
            var slow = new FakeMediaSource(1000, long.MaxValue, sleepMs: 5);
            var slowPath = Path.Combine(temp, "slow.wav");
            WaveformCache.OpenOverride = _ => slow;
            _ = WfGet(slowPath);
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (slow.Reads < 3 && wait.ElapsedMilliseconds < 3000) Thread.Sleep(5);
            Check("the slow decode is running", slow.Reads >= 3);
            WfCancelUnused(Array.Empty<string>());
            wait.Restart();
            while (!slow.Disposed && wait.ElapsedMilliseconds < 3000) Thread.Sleep(5);
            Check("a removed clip / closed timeline cancels its decode (the file is released)", slow.Disposed);
            Check("the cancelled file is forgotten", WaveformCache.CachedCount == 0);

            // Bounded queue, deduplicated.
            WaveformCache.ClearAll();
            var blocker = new FakeMediaSource(1000, long.MaxValue, sleepMs: 5);
            WaveformCache.OpenOverride = p => p.EndsWith("block.wav", StringComparison.Ordinal) ? blocker : new FakeMediaSource(1, 8000, 1);
            _ = WfGet(Path.Combine(temp, "block.wav"));
            Thread.Sleep(50);
            for (var i = 0; i < 200; i++) _ = WfGet(Path.Combine(temp, $"q{i}.wav"));
            for (var i = 0; i < 5; i++) _ = WfGet(Path.Combine(temp, "q0.wav"));
            Check($"the waiting queue is bounded ({WaveformCache.CachedCount} entries for 201 files)", WaveformCache.CachedCount <= WaveformCache.MaxPending + 2);
            WaveformCache.ClearAll();

            // Memory budget: least recently drawn outlines go first.
            WaveformCache.BudgetBytes = 2000;   // each fake file = 100 peaks = 400 bytes
            WaveformCache.OpenOverride = _ => new FakeMediaSource(1, 8000);
            var files = Enumerable.Range(0, 12).Select(i => Path.Combine(temp, $"e{i}.wav")).ToList();
            foreach (var f in files) { _ = WfGet(f); WaveformCache.WaitIdle(3000); Thread.Sleep(3); }
            Check($"the cache stays within its budget ({WaveformCache.CachedBytes} bytes)", WaveformCache.CachedBytes <= 2000 && WaveformCache.CachedBytes > 0);
            Check("the most recently read outline is kept", WfGet(files[^1]) is not null);
            WaveformCache.BudgetBytes = savedBudget;

            // A file replaced at the same path is read again (key: path + size + last-write).
            WaveformCache.ClearAll();
            WaveformCache.OpenOverride = null;
            WaveformCache.RecheckMs = 0;
            var wav = Path.Combine(temp, "replace.wav");
            WriteTestWav(wav, 0.5);
            _ = WfGet(wav); WaveformCache.WaitIdle(5000);
            var first = WfGet(wav)?.Length ?? -1;
            WriteTestWav(wav, 1.5);
            File.SetLastWriteTimeUtc(wav, DateTime.UtcNow.AddMinutes(1));
            Thread.Sleep(10);
            // Notices the change and reads again in the background: poll (the re-read is queued asynchronously, so a single
            // WaitIdle can return before it is scheduled on a fast machine).
            var second = -1;
            for (var tries = 0; tries < 100 && second is < 145 or > 155; tries++)
            {
                _ = WfGet(wav);
                WaveformCache.WaitIdle(200);
                second = WfGet(wav)?.Length ?? -1;
                if (second is < 145 or > 155) Thread.Sleep(50);
            }
            Check($"a file replaced at the same path shows the new data ({first} -> {second} peaks)", first is >= 45 and <= 55 && second is >= 145 and <= 155);
        }
        finally
        {
            WaveformCache.ClearAll();
            WaveformCache.OpenOverride = null; WaveformCache.BudgetBytes = savedBudget; WaveformCache.RecheckMs = savedRecheck;
            MediaPathPolicy.FileSystem = savedFs;
            MediaAccess.ClearCache();
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }

    private static void WriteTestWav(string path, double seconds)
    {
        using var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(8000, 16, 1));
        var samples = new short[(int)(8000 * seconds)];
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)(10000 * Math.Sin(i * 0.3));
        writer.WriteSamples(samples, 0, samples.Length);
    }

    private static void TestClosedTimelineIsCollected()
    {
        var weak = MakeTimelineWeak();
        for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Check("a timeline that is no longer used is collected (the static waveform event does not keep it alive)", !weak.TryGetTarget(out _));
        var owner = MakeOwnerWeak(out var hits);
        for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Check("a weak subscription to a collected owner is never called", !owner.TryGetTarget(out _) && hits[0] == 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TrackTimeline> MakeTimelineWeak()
    {
        var t = new TrackTimeline { Project = new SongProject(), MeasureWidth = 24 };
        return new WeakReference<TrackTimeline>(t);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<object> MakeOwnerWeak(out int[] hits)
    {
        var counter = new int[1];
        var o = new object();
        WaveformCache.SubscribeWeak(o, (_, _) => counter[0]++);
        hits = counter;
        return new WeakReference<object>(o);
    }
}
