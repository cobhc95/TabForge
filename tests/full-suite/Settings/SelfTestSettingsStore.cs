using System.IO;
using System.Linq;
using System.Windows.Media;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge;

/// <summary>
/// WP-4 (engineering audit 2026-09-29): one settings store shared by every window (R-09), one explicit owner of a warm
/// audio engine (R-10) and per-control text DPI (A-03). All UI-free: no window, no engine process.
/// </summary>
public static partial class SelfTest
{
    /// <summary>R-09: two "windows" change different settings through the one store; the last close keeps both.</summary>
    private static void TestSettingsStoreSharedAcrossWindows()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-store-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            SettingsFileService.SaveAtomic(path, new AppSettings());
            using var store = AppSettingsStore.Open(path, TimeSpan.FromMinutes(10));   // debounced: nothing is written until a flush
            object windowA = new(), windowB = new();
            var seenByA = new List<object?>();
            store.Changed += source => { if (!ReferenceEquals(source, windowA)) seenByA.Add(source); };

            // Both windows opened before either change (the old failure: each held its startup copy).
            var viewA = store.Settings;
            var viewB = store.Settings;
            const string plugin = @"C:\Plugins\R09 Test.vst3";
            viewB.Plugins.ApprovedPluginPaths.Add(plugin);      // B approves a plug-in ...
            viewB.Hotkeys["File.Save"] = "Ctrl+Alt+F9";          // ... and rebinds a key
            store.MarkChanged(windowB);
            var writtenEarly = SettingsFileService.Load(path).Hotkeys["File.Save"] == "Ctrl+Alt+F9";
            viewA.WindowWidth = 1234;                            // A changes something else, then closes last
            store.MarkChanged(windowA);
            var flushed = store.Flush();

            var reloaded = SettingsFileService.Load(path);
            Check("R-09: two windows share one settings object; the last close keeps both windows' changes (plug-in approval, hotkey, size)",
                ReferenceEquals(viewA, viewB) && flushed && !store.HasPendingSave &&
                reloaded.Plugins.ApprovedPluginPaths.Contains(plugin, StringComparer.OrdinalIgnoreCase) &&
                reloaded.Hotkeys["File.Save"] == "Ctrl+Alt+F9" && Math.Abs(reloaded.WindowWidth - 1234) < 0.5,
                $"same object {ReferenceEquals(viewA, viewB)}, flushed {flushed}, approved {reloaded.Plugins.ApprovedPluginPaths.Count}, hotkey '{reloaded.Hotkeys["File.Save"]}', width {reloaded.WindowWidth}");
            Check("R-09: saves are debounced (nothing written before the delay or a flush) and other windows hear of each change",
                !writtenEarly && seenByA.Count == 1 && ReferenceEquals(seenByA[0], windowB), $"written early {writtenEarly}, A saw {seenByA.Count}");

            // Preferences apply swaps the object: every window sees the new one (none keeps the old one).
            var applied = SettingsMigration.Clone(store.Settings);
            applied.Hotkeys["File.Open"] = "Ctrl+Alt+F8";
            store.Replace(applied, windowA);
            Check("R-09: a replaced settings object is what every window reads next", ReferenceEquals(store.Settings, applied));

            // Load failure lives in the store: nothing is written over the unreadable file until Preferences are applied.
            var brokenPath = Path.Combine(folder, "broken.json");
            File.WriteAllText(brokenPath, "{ not json");
            using var broken = AppSettingsStore.Open(brokenPath, TimeSpan.FromMinutes(10));
            var failedAtLoad = broken.LoadFailed;
            broken.Settings.WindowWidth = 999;
            broken.MarkChanged(windowA);
            var refused = !broken.Flush() && File.ReadAllText(brokenPath) == "{ not json";
            broken.AcceptCurrentAsReplacement();
            var replaced = broken.Flush() && Math.Abs(SettingsFileService.Load(brokenPath).WindowWidth - 999) < 0.5;
            Check("R-09: after a failed load the store keeps the unreadable file until the user applies settings", failedAtLoad && !broken.LoadFailed && refused && replaced,
                $"failed at load {failedAtLoad}, refused {refused}, replaced {replaced}");
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    /// <summary>The audio engine plays every song by default (any driver, no plug-ins needed); only the user's own Settings toggle turns it off, and that sticks.</summary>
    private static void TestEngineDefaultOnAndManualOffSticks()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-engdef-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var previous = TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine;
        try
        {
            var fresh = new AppSettings();
            Check("Engine default: new settings play the whole song through the audio engine", fresh.Plugins.PlayAllThroughEngine);

            TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = fresh.Plugins.PlayAllThroughEngine;
            var created = TemplateFactory.Blank();          // a new song
            Check("Engine default: a new song's tracks use the engine (no plug-ins)",
                created.Tracks.All(t => MixerGroups.RouteOf(t, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.MidiThroughEffects));

            fresh.Plugins.Driver = AudioDrivers.Asio;       // ASIO and no plug-ins
            Check("Engine default: ASIO with no plug-ins uses the engine",
                TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine && created.Tracks.All(t => t.Rig.Plugins.Count == 0 && MixerGroups.RouteOf(t, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.MidiThroughEffects));

            // Old file that stored null (auto): on. A stored explicit false is kept by the migration (no rule can tell it from a choice).
            var nullPath = Path.Combine(folder, "null.json");
            File.WriteAllText(nullPath, "{\"Plugins\":{\"PlayAllThroughEngine\":null}}");
            var offPath = Path.Combine(folder, "off.json");
            File.WriteAllText(offPath, "{\"Plugins\":{\"PlayAllThroughEngine\":false}}");
            Check("Engine default: a settings file with no explicit value loads as on, an explicit off stays off",
                SettingsFileService.Load(nullPath).Plugins.PlayAllThroughEngine && !SettingsFileService.Load(offPath).Plugins.PlayAllThroughEngine);

            // The user switches it off: it survives save + reload, including with ASIO selected.
            var path = Path.Combine(folder, "settings.json");
            var manual = new AppSettings();
            manual.Plugins.PlayAllThroughEngine = false;
            manual.Plugins.Driver = AudioDrivers.Asio;
            SettingsFileService.SaveAtomic(path, manual);
            var reloaded = SettingsFileService.Load(path);
            TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = reloaded.Plugins.PlayAllThroughEngine;
            Check("Engine default: a manual off stays off after save and reload",
                !reloaded.Plugins.PlayAllThroughEngine && created.Tracks.All(t => MixerGroups.RouteOf(t, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.WindowsMidi));
        }
        finally
        {
            TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = previous;
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    /// <summary>The fretboard size is unlocked by default; an old stored lock (the old default) is cleared once, a later lock persists.</summary>
    private static void TestInstrumentSizeUnlockedByDefault()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tabforge-lock-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            Check("Fretboard lock: new settings are unlocked", !new AppSettings().Appearance.LockInstrumentSize);
            var oldLocked = Path.Combine(folder, "old-locked.json");
            File.WriteAllText(oldLocked, "{\"Appearance\":{\"LockInstrumentSize\":true}}");
            var oldUnlocked = Path.Combine(folder, "old-unlocked.json");
            File.WriteAllText(oldUnlocked, "{\"Appearance\":{\"LockInstrumentSize\":false}}");
            Check("Fretboard lock: a lock stored by an older file (the old default) is cleared once, an unlocked value stays",
                !SettingsFileService.Load(oldLocked).Appearance.LockInstrumentSize && !SettingsFileService.Load(oldUnlocked).Appearance.LockInstrumentSize);
            var path = Path.Combine(folder, "settings.json");
            var settings = new AppSettings();
            settings.Appearance.LockInstrumentSize = true;
            SettingsFileService.SaveAtomic(path, settings);
            var again = SettingsFileService.Load(path);
            SettingsFileService.SaveAtomic(path, again);
            Check("Fretboard lock: locking it persists across reloads", SettingsFileService.Load(path).Appearance.LockInstrumentSize);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    /// <summary>R-10: the engine has one owner and stays warm; switching back within the warm period reloads no plug-in.</summary>
    private static void TestEngineWarmOwnership()
    {
        var previousPlayAll = TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine;
        TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = false;
        var client = new AudioEngineClient();
        client.Mixer.PlayAllThroughEngine = false;
        try
        {
            var settings = new PluginSettings();
            SongProject WithPlugin(string name)
            {
                var song = TemplateFactory.Blank();
                var track = song.Tracks[0];
                track.SoundSource = SoundSources.Plugins;
                track.Rig.Plugins.Add(new PluginSlot { Name = name, Path = $@"C:\NoSuch\{name}.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
                return song;
            }
            var docA = WithPlugin("R10A");
            var docB = TemplateFactory.Blank();   // no engine tracks
            var docC = WithPlugin("R10C");
            object ownerA = new(), ownerB = new(), ownerC = new();
            var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
            client.WarmClock = () => now;
            client.AttachFakeForTest();
            var trackA = docA.Tracks[0];

            client.Sync(docA.Tracks, settings, null, docA, ownerA);
            var loadsA = client.ChainLoadsSentForTest;
            var slotA = client.SlotOf(trackA);
            client.Sync(docB.Tracks, settings, null, docB, ownerB);
            var warm = client.IsRunning && client.IsParkedForTest(trackA);
            now += TimeSpan.FromMinutes(4);
            client.Sync(docA.Tracks, settings, null, docA, ownerA);
            Check("R-10: Sync(A) -> Sync(B without engine tracks) -> Sync(A) within the warm period: the engine keeps running and no LoadChain is sent again",
                loadsA == 1 && warm && client.ChainLoadsSentForTest == loadsA && client.SlotOf(trackA) == slotA && !client.IsParkedForTest(trackA),
                $"loads {loadsA} -> {client.ChainLoadsSentForTest}, warm {warm}, slot {slotA} -> {client.SlotOf(trackA)}");

            // Two documents with engine tracks: the inactive one is parked (loaded, silent) on its own slot; no collision, no reload.
            client.Sync(docC.Tracks, settings, null, docC, ownerC);
            var slotC = client.SlotOf(docC.Tracks[0]);
            var parkedA = client.IsParkedForTest(trackA);
            var parkedNotRequested = client.RequestedChains.All(r => r.Slot != slotA);
            client.Sync(docA.Tracks, settings, null, docA, ownerA);
            Check("R-10: switching between two plug-in documents parks the other one on its own slot and reloads nothing",
                parkedA && parkedNotRequested && slotC >= 0 && slotC != slotA && client.ChainLoadsSentForTest == loadsA + 1 && client.IsParkedForTest(docC.Tracks[0]),
                $"parked {parkedA}, slots A {slotA} C {slotC}, loads {client.ChainLoadsSentForTest}");

            // Parked chains are dropped after the warm period; the owner's stay.
            now += TimeSpan.FromMinutes(AudioEngineClient.DefaultWarmIdleMinutes) + TimeSpan.FromSeconds(1);
            client.ExpireWarmForTest();
            Check("R-10: a chain parked longer than the warm period is removed; the owner's chain stays",
                client.IsRunning && client.SlotOf(docC.Tracks[0]) == -1 && client.SlotOf(trackA) == slotA);

            // Idle: stays warm until the period ends, then stops.
            client.Sync(docB.Tracks, settings, null, docB, ownerB);
            now += TimeSpan.FromMinutes(AudioEngineClient.DefaultWarmIdleMinutes - 1);
            client.ExpireWarmForTest();
            var stillWarm = client.IsRunning;
            now += TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1);
            client.ExpireWarmForTest();
            Check("R-10: without engine tracks the engine stops only after the warm period",
                stillWarm && !client.IsRunning, $"warm at 4 min {stillWarm}, running after 5 min {client.IsRunning}");

            // WarmIdle 0 (headless probes): the old behaviour, Sync without engine tracks stops at once.
            client.WarmIdle = TimeSpan.Zero;
            client.AttachFakeForTest();
            client.Sync(docA.Tracks, settings, null, docA, ownerA);
            client.Sync(docB.Tracks, settings, null, docB, ownerB);
            Check("R-10: with no warm period a Sync without engine tracks stops the engine at once", !client.IsRunning);
        }
        finally
        {
            client.Stop();
            TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = previousPlayAll;
        }
    }

    /// <summary>
    /// Multi-tab playback (beta.4): two open songs play through the engine at once. Focusing the second tab keeps the first one's
    /// tracks live on their own slots, both songs' notes reach the engine, and stopping one silences only its own slots.
    /// </summary>
    private static void TestEngineMultiTabPlayback()
    {
        var previousPlayAll = TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine;
        TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = false;
        var client = new AudioEngineClient();
        client.Mixer.PlayAllThroughEngine = false;
        try
        {
            var settings = new PluginSettings();
            SongProject WithPlugin(string name)
            {
                var song = TemplateFactory.Blank();
                var track = song.Tracks[0];
                track.SoundSource = SoundSources.Plugins;
                track.Rig.Plugins.Add(new PluginSlot { Name = name, Path = $@"C:\NoSuch\{name}.vst3", Format = "VST3", Type = PluginSlotType.Instrument });
                return song;
            }
            var songA = WithPlugin("TabA");
            var songB = WithPlugin("TabB");
            object ownerA = new(), ownerB = new();
            var playing = new HashSet<object>();
            client.IsOwnerPlaying = playing.Contains;
            client.AttachFakeForTest();
            var routingA = new RoutedMidiOutput(new TabForge.Playback.NullMidiOutput(), client);
            var routingB = new RoutedMidiOutput(new TabForge.Playback.NullMidiOutput(), client);
            var written = new List<TabForge.Audio.Contracts.TimedMidi>();
            var sent = new List<TabForge.Audio.Contracts.EngineCommand>();
            client.WrittenForTest = m => written.Add(m);
            client.SentForTest = c => sent.Add(c);

            // Tab A plays, then tab B is focused (becomes the engine owner) and plays too.
            AudioRouting.Apply(songA, routingA, client, settings, owner: ownerA);
            playing.Add(ownerA);
            AudioRouting.Apply(songB, routingB, client, settings, owner: ownerB);
            playing.Add(ownerB);
            var slotA = client.SlotOf(songA.Tracks[0]);
            var slotB = client.SlotOf(songB.Tracks[0]);
            Check("multi-tab: focusing tab B keeps playing tab A's engine track live on its own slot",
                slotA >= 0 && slotB >= 0 && slotA != slotB && !client.IsParkedForTest(songA.Tracks[0]) && !client.IsParkedForTest(songB.Tracks[0]),
                $"slots A {slotA} B {slotB}, A parked {client.IsParkedForTest(songA.Tracks[0])}");

            var chA = TabForge.Playback.ChannelAllocator.Assign(songA)[0];
            var chB = TabForge.Playback.ChannelAllocator.Assign(songB)[0];
            routingA.Send(0, 0x90 | chA, 60, 100);
            routingB.Send(0, 0x90 | chB, 64, 100);
            Check("multi-tab: the engine receives notes from both tabs while B is focused, each on its own slot",
                written.Any(m => m.Slot == slotA && m.Data1 == 60) && written.Any(m => m.Slot == slotB && m.Data1 == 64),
                string.Join(", ", written.Select(m => $"{m.Slot}:{m.Data1}")));

            // Stopping tab A silences only A's slots (no engine-wide panic), and B keeps playing.
            sent.Clear(); written.Clear();
            routingA.ResetAll();
            playing.Remove(ownerA);
            routingB.Send(0, 0x90 | chB, 67, 100);
            Check("multi-tab: stopping tab A sends an ordered slot panic (in the note ring), never an engine-wide one; tab B still reaches the engine",
                written.Any(m => m.Slot == slotA && (m.Flags & TabForge.Audio.Contracts.TimedMidi.PanicFlag) != 0) && !written.Any(m => m.Slot == slotB && (m.Flags & TabForge.Audio.Contracts.TimedMidi.PanicFlag) != 0) && !sent.Contains(TabForge.Audio.Contracts.EngineCommand.Panic) && written.Any(m => m.Slot == slotB && m.Data1 == 67),
                string.Join(",", sent));

            // Once A has stopped, the next sync parks it as before (R-10) while B stays live.
            AudioRouting.Apply(songB, routingB, client, settings, owner: ownerB);
            Check("multi-tab: a stopped background tab is parked on the next sync, the focused one stays live",
                client.IsParkedForTest(songA.Tracks[0]) && !client.IsParkedForTest(songB.Tracks[0]));

            // Each song has its own transport id: A reporting and stopping never touches B's position (clips follow their own song).
            var idA = client.ExistingOwnerId(ownerA); var idB = client.ExistingOwnerId(ownerB);
            Check("multi-tab: each open song has its own engine transport id", idA >= 0 && idB >= 0 && idA != idB, $"A {idA}, B {idB}");
            var clockA = new SongClock(client) { OwnerKey = ownerA };
            var clockB = new SongClock(client) { OwnerKey = ownerB };
            _ = clockA.BarStartSec(songA, 0); _ = clockB.BarStartSec(songB, 0);
            clockA.Report(songA, new TabForge.Playback.PlaybackPosition { Bar = 1, BarFraction = 0 }, playing: true);
            clockB.Report(songB, new TabForge.Playback.PlaybackPosition { Bar = 2, BarFraction = 0 }, playing: true);
            var posA = client.PositionSentForTest(idA); var posB = client.PositionSentForTest(idB);
            Check("multi-tab: two playing songs each report their own position to the engine",
                posA is { Playing: true } && posB is { Playing: true } && posA.Value.SongSec < posB.Value.SongSec, $"A {posA}, B {posB}");
            clockA.Stopped();
            Check("multi-tab: stopping song A leaves song B playing at its own position",
                client.PositionSentForTest(idA) is { Playing: false } && client.PositionSentForTest(idB) == posB, $"A {client.PositionSentForTest(idA)}, B {client.PositionSentForTest(idB)}");
            // Audio clips play from the engine transport: a playhead reported late after the stop must not restart it, and pause stops it too.
            clockA.Report(songA, new TabForge.Playback.PlaybackPosition { Bar = 2, BarFraction = 0 }, playing: true);
            Check("transport: a playhead reported after Stop does not restart the song's audio clips", client.PositionSentForTest(idA) is { Playing: false }, $"A {client.PositionSentForTest(idA)}");
            clockB.Paused();
            var pausedB = client.PositionSentForTest(idB);
            clockB.Report(songB, new TabForge.Playback.PlaybackPosition { Bar = 2, BarFraction = 0.5 }, playing: true);
            Check("transport: Pause stops the song's audio clips and the next playhead resumes them",
                pausedB is { Playing: false } && client.PositionSentForTest(idB) is { Playing: true }, $"paused {pausedB}, resumed {client.PositionSentForTest(idB)}");
            client.ReleaseOwner(ownerA);
            PumpUi();
            Check("multi-tab: a released document gives its id back, the new document takes it, and the other song keeps its own",
                client.ExistingOwnerId(ownerA) < 0 && client.OwnerIdOf(new object()) == idA && client.ExistingOwnerId(ownerB) == idB);
        }
        finally
        {
            client.SentForTest = null;
            client.WrittenForTest = null;
            client.Stop();
            TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = previousPlayAll;
        }
    }

    /// <summary>A-03: text is shaped for the DPI of the control drawing it (a nested scope), not one app-wide value.</summary>
    private static void TestPerControlTextDpi()
    {
        var brush = Draw.Solid(Colors.White);
        double outer, inner, restored;
        FormattedText atOneAndHalf, atTwo;
        using (Draw.UseDpi(1.5))
        {
            atOneAndHalf = Draw.Text("A-03 dpi", 11, brush);
            outer = Draw.PixelsPerDip;
            using (Draw.UseDpi(2.0))
            {
                atTwo = Draw.Text("A-03 dpi", 11, brush);
                inner = Draw.PixelsPerDip;
            }
            restored = Draw.PixelsPerDip;
        }
        var outside = Draw.Text("A-03 dpi", 11, brush);
        Check("A-03: each control's text is shaped for its own DPI (scoped, nested, restored); none leaks to other windows",
            Math.Abs(atOneAndHalf.PixelsPerDip - 1.5) < 1e-6 && Math.Abs(atTwo.PixelsPerDip - 2.0) < 1e-6 &&
            Math.Abs(outside.PixelsPerDip - 1.0) < 1e-6 && outer == 1.5 && inner == 2.0 && restored == 1.5 &&
            Draw.PixelsPerDip == 1.0 && !ReferenceEquals(atOneAndHalf, atTwo) && !ReferenceEquals(atOneAndHalf, outside),
            $"1.5 -> {atOneAndHalf.PixelsPerDip}, 2.0 -> {atTwo.PixelsPerDip}, outside -> {outside.PixelsPerDip}, restored {restored}");
    }
}
