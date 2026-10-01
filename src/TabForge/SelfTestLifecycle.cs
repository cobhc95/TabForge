using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Views;

namespace TabForge;

/// <summary>Repeated open / close of a document window set (area "leaks"): closed windows and timelines must be collectable and the managed heap must not keep growing.</summary>
public static partial class SelfTest
{
    private const int LifecycleCycles = 20;
    private const long LifecycleGrowthLimitBytes = 48L * 1024 * 1024;

    private static void TestRepeatedOpenCloseReleasesWindows()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var wav = Path.Combine(folder, "clip.wav");
        WriteTinyWav(wav);
        // An earlier window test may already have let the application begin its shutdown; the windows still work headlessly.
        IDisposable? alive = null;
        try { alive = KeepAlive(); } catch (InvalidOperationException) { }
        var mixers = new List<WeakReference>();
        var panels = new List<WeakReference>();
        var timelines = new List<WeakReference>();
        var shells = new List<WeakReference>();
        try
        {
            long afterWarmUp = 0;
            var readyBefore = WaveformReadySubscribers();
            for (var i = 0; i < LifecycleCycles; i++)
            {
                OpenAndCloseDocumentWindows(wav, mixers, panels, timelines, shells);
                if (i == 0) Thread.Sleep(80);   // the first cycle lets the waveform read finish and raise its redraw
                PumpUi();
                if (i == 2) afterWarmUp = CollectFully();
            }
            var finalHeap = CollectFully();
            for (var round = 0; round < 4 && (Alive(mixers) > 0 || Alive(timelines) > 0 || Alive(shells) > 0); round++) { PumpUi(); CollectFully(); }

            var growth = finalHeap - afterWarmUp;
            var readyGrowth = WaveformReadySubscribers() - readyBefore;
            Check($"open/close x{LifecycleCycles}: closed mixer windows and document windows are collected",
                Alive(mixers) == 0 && Alive(shells) == 0, $"{Alive(mixers)} of {mixers.Count} mixer windows, {Alive(shells)} of {shells.Count} document windows still alive");
            var timelinesAlive = Alive(timelines) + Alive(panels);
                Check($"open/close x{LifecycleCycles}: closed arrangement panels and timelines are collected",
                    timelinesAlive == 0, $"{Alive(timelines)} of {timelines.Count} timelines and {Alive(panels)} of {panels.Count} panels still alive; {readyGrowth} new WaveformCache.Ready subscribers");
            Check($"open/close x{LifecycleCycles}: the managed heap grows by less than {LifecycleGrowthLimitBytes / (1024 * 1024)} MiB after warm-up",
                growth < LifecycleGrowthLimitBytes, $"{growth / 1024.0 / 1024:0.0} MiB (from {afterWarmUp / 1024.0 / 1024:0.0} to {finalHeap / 1024.0 / 1024:0.0} MiB)");
            Log.Add($"  info  open/close x{LifecycleCycles}: heap growth after warm-up {growth / 1024.0 / 1024:0.0} MiB, {Alive(timelines)} timelines / {Alive(mixers)} mixers alive, {readyGrowth} new Ready subscribers");
        }
        finally
        {
            try { alive?.Dispose(); } catch (InvalidOperationException) { }
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Everything the cycle creates lives in this frame, so no local keeps a closed window reachable when the collection runs.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OpenAndCloseDocumentWindows(string wav, List<WeakReference> mixers, List<WeakReference> panels, List<WeakReference> timelines, List<WeakReference> shells)
    {
        var host = new FakeMixerHost();
        foreach (var t in BuildSyntheticGpSong().Tracks) host.Project.Tracks.Add(t);
        host.Project.Tracks[0].AudioClips.Add(new AudioClip { File = wav, Name = "clip", StartSec = 0, SourceLengthSec = 0.25, FileLengthSec = 0.25 });
        host.Project.Tracks[1].AudioClips.Add(new AudioClip { File = wav, Name = "clip 2", StartSec = 0.1, SourceLengthSec = 0.25, FileLengthSec = 0.25 });

        var panel = new ArrangementPanel();
        panel.Bind(host.Project, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var shell = new Window { Content = panel, Width = 900, Height = 500, ShowInTaskbar = false, Left = -5000, Top = -5000 };
        var mixer = new MixerWindow(host, null);
        mixer.SizeToContent = SizeToContent.Manual; mixer.Width = 800; mixer.Height = 600;
        ShowTestWindow(shell);
        ShowTestWindow(mixer);
        shell.UpdateLayout(); mixer.UpdateLayout();
        PumpUi();

        mixers.Add(new WeakReference(mixer));
        panels.Add(new WeakReference(panel));
        shells.Add(new WeakReference(shell));
        foreach (var timeline in VisualDescendants<TrackTimeline>(panel)) timelines.Add(new WeakReference(timeline));
        mixer.Close();
        shell.Close();
        shell.Content = null;
    }

    /// <summary>
    /// Audit 7 memory trend: a closed tab's plug-in chains were parked like a background tab's (5 min warm period), so opening and
    /// closing a song with a large sampler every 30 s held ~10 loaded copies (engine ~3.9 GB, rising callback load). Closing a document
    /// must unload its chains at once and drop the engine client's reference to it; a background (switched-away) tab still parks.
    /// </summary>
    private static void TestClosedDocumentChainsReleased()
    {
        var previousPlayAll = MixerGroups.PlayAllThroughEngine;
        MixerGroups.PlayAllThroughEngine = false;
        static SongProject WithPlugin(string name)
        {
            var song = TemplateFactory.Blank();
            song.Tracks[0].SoundSource = SoundSources.Plugins;
            song.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = name, Path = $@"C:\NoSuch\{name}.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
            return song;
        }
        var settings = new Services.PluginSettings();

        // ReleaseOwner on its own client: parked chains of the closed owner go now; another parked owner and the active one stay.
        var client = new Audio.AudioEngineClient();
        try
        {
            client.AttachFakeForTest();
            var songA = WithPlugin("CloseA"); var songB = WithPlugin("CloseB"); var songC = WithPlugin("CloseC");
            object ownerA = new(), ownerB = new(), ownerC = new();
            var removed = new List<TabForge.Audio.Contracts.EngineCommand>();
            client.Sync(songA.Tracks, settings, null, songA, ownerA);
            client.Sync(songB.Tracks, settings, null, songB, ownerB);
            client.Sync(songC.Tracks, settings, null, songC, ownerC);
            var parkedBefore = client.IsParkedForTest(songA.Tracks[0]) && client.IsParkedForTest(songB.Tracks[0]);
            client.SentForTest = c => { if (c is TabForge.Audio.Contracts.EngineCommand.RemoveTrack or TabForge.Audio.Contracts.EngineCommand.GetStates) removed.Add(c); };
            client.ReleaseOwner(ownerA);
            // A startup track's chain window closes later in the same close and asks for its plug-in states: the request must go out
            // before the chain is removed (the removal is posted), or the startup template keeps stale states.
            _ = client.CollectStatesAsync(new[] { songA.Tracks[0] }, 1);
            PumpUi();
            var order = string.Join(",", removed);
            removed.RemoveAll(c => c != TabForge.Audio.Contracts.EngineCommand.RemoveTrack);
            Check("closed tab: its parked plug-in chain is unloaded at once; another background tab stays parked, the active tab stays live",
                parkedBefore && client.SlotOf(songA.Tracks[0]) == -1 && removed.Count == 1 && client.IsParkedForTest(songB.Tracks[0])
                && client.SlotOf(songC.Tracks[0]) >= 0 && !client.IsParkedForTest(songC.Tracks[0]),
                $"parked before {parkedBefore}, A slot {client.SlotOf(songA.Tracks[0])}, RemoveTrack x{removed.Count}, B parked {client.IsParkedForTest(songB.Tracks[0])}");
            Check("closed tab: a chain window's state request sent while the tab closes reaches the engine before the chain is removed",
                order == "GetStates,RemoveTrack", $"commands {order}");

            // The last document's tracks go: its bus chains go too and the warm period starts (nothing left would ever re-sync them).
            client.ReleaseOwner(ownerB);
            client.ReleaseOwner(ownerC);
            PumpUi();
            Check("closed tab: once no document's track is left, no bus chain stays loaded and the engine's warm period runs",
                client.SlotOf(songC.Tracks[0]) == -1 && client.SlotOf(songB.Tracks[0]) == -1 && client.IdleForTest,
                $"B slot {client.SlotOf(songB.Tracks[0])}, C slot {client.SlotOf(songC.Tracks[0])}, idle {client.IdleForTest}");
        }
        finally
        {
            client.SentForTest = null;
            client.Stop();
        }

        // The real close path: DocumentSession.DisposePlayback releases the document on the shared client.
        var shared = Audio.AudioEngineClient.Instance;
        if (shared.IsRunning) { Log.Add("  info  closed tab chains: the shared engine runs, wiring check skipped"); MixerGroups.PlayAllThroughEngine = previousPlayAll; return; }
        try
        {
            shared.AttachFakeForTest();
            var closing = Documents.DocumentSession.FromProject(WithPlugin("CloseDoc"), null);
            var other = Documents.DocumentSession.FromProject(WithPlugin("OtherDoc"), null);
            Audio.AudioRouting.Apply(closing.Project, closing.Playback.Routing, shared, settings, owner: closing);
            Audio.AudioRouting.Apply(other.Project, other.Playback.Routing, shared, settings, owner: other);
            var parked = shared.IsParkedForTest(closing.Project.Tracks[0]);
            closing.DisposePlayback();
            PumpUi();
            Check("closed tab: DocumentSession.DisposePlayback unloads the document's engine chains (not parked for the warm period)",
                parked && shared.SlotOf(closing.Project.Tracks[0]) == -1 && shared.SlotOf(other.Project.Tracks[0]) >= 0,
                $"parked before {parked}, closed slot {shared.SlotOf(closing.Project.Tracks[0])}, other slot {shared.SlotOf(other.Project.Tracks[0])}");
            other.DisposePlayback();
        }
        finally
        {
            shared.Stop();
            MixerGroups.PlayAllThroughEngine = previousPlayAll;
        }
    }

    private static int Alive(List<WeakReference> references) => references.Count(r => r.IsAlive);

    private static long CollectFully()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        return GC.GetTotalMemory(true);
    }

    /// <summary>How many handlers the static WaveformCache.Ready event has (each open timeline adds one today).</summary>
    private static int WaveformReadySubscribers()
    {
        var field = typeof(Audio.WaveformCache).GetField("Ready", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (field?.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
    }

    private static void WriteTinyWav(string path)
    {
        const int rate = 8000; const int samples = rate / 4;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + samples * 2); w.Write("WAVEfmt "u8.ToArray());
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(samples * 2);
        for (var i = 0; i < samples; i++) w.Write((short)(Math.Sin(i * 0.2) * 12000));
    }
}
