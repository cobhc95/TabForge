using System.IO;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;
using EH = TabForge.AudioEngine.EngineHost.Headless;
using EM = TabForge.AudioEngine.Mixing;
using EP = TabForge.AudioEngine.Plugins;

namespace TabForge;

/// <summary>
/// Mute / solo holds for every kind of track and across every way playback starts or jumps (0.5.2): the one shared rule
/// (<see cref="MixerGroups.IsAudible"/>), the engine's mute gate that a seek's MIDI volume message cannot lift, and the offline render.
/// Part of <see cref="SelfTest"/>.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The rule, written out independently: any solo -> only soloed tracks play (even if also muted); no solo -> unmuted tracks play.</summary>
    private static void TestMuteSoloTruthTable()
    {
        var song = SingleTrack(1);
        song.Tracks.Add(new TrackModel { Name = "B", Measures = TemplateFactory.Measures(1) });
        song.Tracks.Add(new TrackModel { Name = "C", Measures = TemplateFactory.Measures(1) });
        var bad = new List<string>();
        var checkedCases = 0;
        for (var bits = 0; bits < 64; bits++)
        {
            for (var i = 0; i < 3; i++) { song.Tracks[i].Mute = (bits & (1 << i)) != 0; song.Tracks[i].Solo = (bits & (1 << (i + 3))) != 0; }
            var anySolo = song.Tracks.Any(t => t.Solo);
            var mask = PlaybackEngine.AudibleMask(song);
            for (var i = 0; i < 3; i++)
            {
                var t = song.Tracks[i];
                var expected = anySolo ? t.Solo : !t.Mute;
                checkedCases++;
                if (MixerGroups.IsAudible(song, t) != expected || mask[i] != expected) bad.Add($"mute {t.Mute} solo {t.Solo} anySolo {anySolo} track {i}");
            }
        }
        Check($"Mute/solo: the shared rule matches the truth table for 3 tracks x every mute/solo combination ({checkedCases} cases; MIDI scheduling uses the same rule)",
            bad.Count == 0, string.Join("; ", bad.Take(3)));
        song.Tracks.ForEach(t => { t.Mute = false; t.Solo = false; });
        song.Tracks[0].Mute = true; song.Tracks[0].Solo = true;
        Check("Mute/solo: mute + solo on the same track plays (soloed); the others are silent; solo alone solos; mute alone mutes",
            MixerGroups.IsAudible(song, song.Tracks[0]) && !MixerGroups.IsAudible(song, song.Tracks[1]) && !MixerGroups.IsAudible(song, song.Tracks[2]));
        song.Tracks[0].Mute = false; song.Tracks[0].Solo = false;
        song.Tracks[1].Mute = true; song.Tracks[2].Solo = true; song.Tracks[1].Solo = false;
        Check("Mute/solo: with a solo active a muted, non-soloed track stays silent too",
            !MixerGroups.IsAudible(song, song.Tracks[0]) && !MixerGroups.IsAudible(song, song.Tracks[1]) && MixerGroups.IsAudible(song, song.Tracks[2]));
    }

    /// <summary>The highest post-fader level the slot's meter shows over <paramref name="ms"/> (the clocked null output plays in real time).</summary>
    private static float PeakOver(SharedBlock shared, int slot, int ms)
    {
        var peak = 0f;
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { peak = Math.Max(peak, shared.Peak(slot)); Thread.Sleep(2); }
        return peak;
    }

    /// <summary>The owner's bug: a muted audio track played again after a click on the timeline (the seek re-primes the track's MIDI volume, which overwrote the mute level).</summary>
    private static void TestMuteHoldsAcrossSeekAndRestart()
    {
        var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        EH.Attach(shared, (spec, _, _) => throw new InvalidOperationException($"selftest: unexpected plug-in {spec.Path}"));
        var wav = WriteTestWav(3);
        try
        {
            EH.Configure(NullConfig(48000, 256, manual: false));
            EH.LoadChain(0, "audio", false, new List<PluginSpec>());
            // Two lanes (overlapping clips) on one track.
            var clips = new List<ClipSpec> { new(wav, 0, 0, 3, 0, 0, 1), new(wav, 0, 0, 3, 0, 0, 1) };
            EH.SetClips(0, clips);
            void Mix(int volume) { EH.Command(EngineCommand.SetTrackMix, w => { w.Write(0); w.Write(volume); w.Write(64); }); EH.Collect(); }
            float Heard(double fromSec) { EH.SetPlaying(true, fromSec); Thread.Sleep(350); EH.Collect(); return PeakOver(shared, 0, 200); }

            // An audible track: machine load only delays the disk thread, so wait (with a deadline) until the meter shows the signal, up to
            // 3 seeks. A real regression (the track never plays) stays silent through every attempt and still fails.
            float HeardAudible(double fromSec)
            {
                var best = 0f;
                for (var attempt = 0; attempt < 3 && best <= 0.01f; attempt++)
                {
                    EH.SetPlaying(true, fromSec);
                    var deadline = DateTime.UtcNow.AddMilliseconds(1500);
                    while (DateTime.UtcNow < deadline && best <= 0.01f) { EH.Collect(); best = Math.Max(best, shared.Peak(0)); Thread.Sleep(2); }
                }
                return best;
            }

            Mix(100);
            var open = HeardAudible(0);
            Check("Mute: an unmuted two-lane audio track is audible (baseline)", open > 0.01f, $"peak {open:0.####}");

            Mix(0);   // muted: the client sends level 0
            var muted = Heard(0);
            var afterSeek = 0f; var afterWrap = 0f; var afterJump = 0f;
            var chain = EH.ChainAt(0)!;
            chain.AddEvent(0, 0xB0, 7, 100);   // what the song's channel set-up sends again at every seek / restart / loop wrap
            afterSeek = Heard(1.0);            // click on the timeline
            chain.AddEvent(0, 0xB0, 7, 127);
            afterWrap = Heard(0.0);            // loop wrap back to the start
            chain.AddEvent(0, 0xB0, 7, 90);
            afterJump = Heard(1.5);            // section jump / Go to bar
            Check("Mute: a muted two-lane audio track is silent (all lanes)", muted < 0.002f, $"peak {muted:0.####}");
            Check("Mute: still silent after a seek, a loop wrap and a jump (the song's volume message cannot lift the mute)",
                afterSeek < 0.002f && afterWrap < 0.002f && afterJump < 0.002f, $"seek {afterSeek:0.####}, wrap {afterWrap:0.####}, jump {afterJump:0.####}");

            // Clips replaced (document change / tab switch) and the chain rebuilt (plug-in reload, crash fallback): the level is kept.
            EH.SetClips(0, new List<ClipSpec> { new(wav, 0, 0, 3, 0, 0, 1), new(wav, 0, 0, 3, 0, 0, 1) });
            EH.LoadChain(0, "audio", false, new List<PluginSpec>());
            EH.Collect();
            var afterReload = Heard(0.5);
            Check("Mute: still silent after the clips are replaced and the chain is rebuilt", afterReload < 0.002f, $"peak {afterReload:0.####}");

            // Mute + solo = the shared rule says audible, so the client sends the normal level.
            var song = SingleTrack(1);
            song.Tracks.Add(new TrackModel { Name = "B", Measures = TemplateFactory.Measures(1) });
            song.Tracks[0].Mute = true; song.Tracks[0].Solo = true;
            Mix(MixerGroups.IsAudible(song, song.Tracks[0]) ? 100 : 0);
            var soloed = HeardAudible(0);
            Check("Mute: a track that is both muted and soloed plays", soloed > 0.01f, $"peak {soloed:0.####}");

            Mix(100);
            var unmuted = HeardAudible(0.2);
            Check("Mute: unmuting makes it audible again", unmuted > 0.01f, $"peak {unmuted:0.####}");

            // An audible track with its fader at 0 is not a mute: a Mix Table volume change (CC7) in the song still brings it in.
            Mix(TabForge.Audio.AudioRouting.EngineLevel(true, 0));
            EH.ChainAt(0)!.AddEvent(0, 0xB0, 7, 100);
            var fadedIn = HeardAudible(0.3);
            Check("Mute: a fader at 0 on an audible track is not the mute gate (a Mix Table volume change lifts it); a silenced track sends 0",
                fadedIn > 0.01f && TabForge.Audio.AudioRouting.EngineLevel(false, 100) == 0, $"peak {fadedIn:0.####}");
        }
        finally
        {
            EH.Detach();
            SettleDisk();
            shared.Dispose();
            try { File.Delete(wav); } catch (IOException) { }
        }
    }

    /// <summary>Offline render honours the shared rule: a muted (not-in-master) track leaves no signal in the mix, its stem still renders, and the live mute gate survives the render.</summary>
    private static void TestRenderHonoursMute()
    {
        using var shared = SharedBlock.Create($"tf-selftest-{Guid.NewGuid():N}");
        var mix = new EM.MixEngine(shared, 48000, 64);
        EM.TrackChain Source(int slot, float level) => new(slot, 0, null, new[] { new EM.TrackChain.Effect(new GatedConstInstrument(level, 100000), 1f, 0, IsInstrument: true) }, 64);
        var muted = Source(0, 0.5f); var heard = Source(1, 0.1f);
        mix.SetChain(0, muted); mix.SetChain(1, heard);
        mix.SetGraph(new EM.MixEngine.RenderGraph(new[] { 0, 1 }, EM.MixEngine.RenderGraph.NewDest(), Array.Empty<int>()));
        muted.SetMix(0, 64);   // live: muted
        var dir = Directory.CreateTempSubdirectory("tf-mute-").FullName;
        try
        {
            byte[] Render(bool mutedInMaster, out string stem0, string tag)
            {
                var events = Path.Combine(dir, $"{tag}.events");
                RenderEventFile.Write(events, new List<RenderEvent>());
                var master = Path.Combine(dir, $"{tag}-m.wav"); stem0 = Path.Combine(dir, $"{tag}-s0.wav");
                var spec = new RenderSpec
                {
                    StartFrame = 0, EndFrame = 4800, TailMode = RenderTailMode.Fixed, TailMs = 0, Channels = 2, SafetyLimiter = false,
                    Format = RenderFormat.Float32, MasterPath = master, EventFile = events, Threads = RenderThreads.One,
                    Slots = { new RenderSlot { Slot = 0, Volume = 100, InMaster = mutedInMaster, StemPath = stem0 }, new RenderSlot { Slot = 1, Volume = 100, InMaster = true } },
                    Tempo = { new RenderTempoPoint(0, 120, 0) },
                };
                var renderer = new EM.OfflineRenderer(spec, mix, shared, 48000, 64, () => false, _ => { });
                renderer.Prepare();
                try { renderer.Run(); } finally { renderer.Restore(); }
                return File.ReadAllBytes(master);
            }
            var withMute = Render(false, out var stemMuted, "a");
            var withoutMuted = Render(false, out _, "b");
            var allIn = Render(true, out _, "c");
            float PeakWav(string path)
            {
                using var reader = new NAudio.Wave.AudioFileReader(path);
                var buffer = new float[4096]; var peak = 0f; int n;
                while ((n = reader.Read(buffer, 0, buffer.Length)) > 0) for (var i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
                return peak;
            }
            // Master of the muted-track render must equal what the audible track alone gives, and be quieter than with the muted track included.
            var masterPeak = PeakWav(Path.Combine(dir, "a-m.wav"));
            var allInPeak = PeakWav(Path.Combine(dir, "c-m.wav"));
            var stemPeak = PeakWav(stemMuted);
            Check("Render: a muted track contributes no signal to the mix (same bytes as the audible track alone; the muted one at level 0.5 would be louder)",
                withMute.SequenceEqual(withoutMuted) && masterPeak > 0.01f && allInPeak > masterPeak * 2, $"master {masterPeak:0.####}, with it {allInPeak:0.####}");
            Check("Render: the muted track's stem still renders (stems ignore mute), and the live mute is back after the render",
                stemPeak > 0.05f && muted.Silent && !heard.Silent, $"stem {stemPeak:0.####}, live gate {muted.Silent}");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
