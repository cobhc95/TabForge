using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Audio;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using Xunit;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge.Avalonia.Tests;

// Owns: the engine side of dragging a take between tracks while the song plays: every drop sends the clip list of the changed tracks again, so the engine must survive hundreds of
//   list swaps across slots (clips moving tracks, appearing, vanishing, a chain reloaded under them) with no fault, no leaked player and no retire-queue growth, and a list that did not
//   change must keep its players (no file reopened, no dropout under the playhead).
// Does not own: what the app sends after a drag (SplitMovePlaybackSyncTests) or the player itself (the engine clip tests).
// Tests: this file.
[Collection("EngineHeadless")]
public class EngineClipResyncStressTests
{
    private static string Wav(double seconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tf-resync-{Guid.NewGuid():N}.wav");
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var block = new float[4800 * 2];
        for (var done = 0; done < (int)(seconds * 48000); done += 4800)
        {
            for (var i = 0; i < 4800; i++) block[i * 2] = block[i * 2 + 1] = 0.3f * MathF.Sin((done + i) * 0.05f);
            writer.WriteSamples(block, 0, block.Length);
        }
        return path;
    }

    [Fact]
    public void HundredsOfClipListSwapsBetweenTracksWhilePlayingLeakNothingAndFaultNothing()
    {
        var files = new[] { Wav(6), Wav(5), "missing-" + Guid.NewGuid().ToString("N") + ".wav" };
        using var shared = SharedBlock.Create($"tf-resync-{Guid.NewGuid():N}");
        EH.Attach(shared, (_, _, _) => throw new InvalidOperationException("no plug-ins here"));
        try
        {
            EH.Configure(new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, 256, false));
            for (var slot = 0; slot < 3; slot++)
            {
                EH.LoadChain(slot, "audio", false, new List<PluginSpec>());
                EH.Command(EngineCommand.SetTrackMix, w => { w.Write(slot); w.Write(100); w.Write(64); });
            }
            EH.Collect();
            var faults = DiskStreamer.Faults;
            EH.SetPlaying(true, 1.0);
            var random = new Random(7);
            var peakRetiring = 0;
            for (var pass = 0; pass < 400; pass++)
            {
                // The three clips land on random tracks at random times (a track may get none): the shape of a drop that moves a take.
                var lists = new List<ClipSpec>[] { new(), new(), new() };
                for (var c = 0; c < 4; c++)
                    lists[random.Next(3)].Add(new ClipSpec(files[random.Next(files.Length)], random.Next(0, 8), 0, 1 + random.Next(3), 0, 0, 1, random.Next(3) == 0 ? 0.2 : 0, 0));
                for (var slot = 0; slot < 3; slot++) EH.SetClips(slot, lists[slot]);
                if (pass % 17 == 0) { EH.RemoveChain(2); EH.LoadChain(2, "audio", false, new List<PluginSpec>()); }
                if (pass % 11 == 0) EH.SetPlaying(true, random.Next(0, 8));
                EH.Pump(random.Next(0, 4));
                EH.Collect();
                peakRetiring = Math.Max(peakRetiring, EH.Retiring);
            }
            for (var i = 0; i < 50; i++) { EH.Pump(2); EH.Collect(); Thread.Sleep(2); }
            Assert.Equal(faults, DiskStreamer.Faults);
            var held = Enumerable.Range(0, 3).Sum(slot => EH.ChainAt(slot)?.Clips.Length ?? 0);
            Assert.True(DiskStreamer.Count <= held + 4, $"players leaked on the disk thread: {DiskStreamer.Count} streamed for {held} held");
            Assert.True(peakRetiring < 64, $"the retire queue grew to {peakRetiring}");
            Assert.True(EH.Retiring <= 8, $"{EH.Retiring} players are still waiting to be disposed");
        }
        finally
        {
            EH.Detach();
            Thread.Sleep(100);
            foreach (var file in files) try { File.Delete(file); } catch (IOException) { }
        }
    }

    [Fact]
    public void AClipListThatDidNotChangeKeepsItsPlayersWhileOneThatMovedGetsANewOne()
    {
        var wav = Wav(6);
        using var shared = SharedBlock.Create($"tf-resync-{Guid.NewGuid():N}");
        EH.Attach(shared, (_, _, _) => throw new InvalidOperationException("no plug-ins here"));
        try
        {
            EH.Configure(new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, 256, false));
            EH.LoadChain(0, "audio", false, new List<PluginSpec>());
            var still = new ClipSpec(wav, 0, 0, 4, 0, 0, 1);
            var moving = new ClipSpec(wav, 1, 0, 2, 0, 0, 1);
            EH.SetClips(0, new List<ClipSpec> { still, moving });
            var before = EH.ChainAt(0)!.Clips.ToArray();
            EH.SetClips(0, new List<ClipSpec> { still, moving with { StartSec = 3 } });
            var after = EH.ChainAt(0)!.Clips;
            Assert.Equal(2, after.Length);
            Assert.Same(before[0], after[0]);   // the clip that stayed keeps its player: no reopen, no dropout
            Assert.NotSame(before[1], after[1]);
            Assert.Equal(3, after[1].Spec.StartSec);
            EH.Collect();
        }
        finally
        {
            EH.Detach();
            Thread.Sleep(100);
            try { File.Delete(wav); } catch (IOException) { }
        }
    }
}
