using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio.Wave;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Output;
using Xunit;
using EH = TabForge.AudioEngine.EngineHost.Headless;

namespace TabForge.Avalonia.Tests;

// Owns: the playing engine replacing a track's clip list with a split clip whose pieces were moved apart: the old whole clip stops sounding in the gap, the
//   left piece ends at its end, and the right piece sounds from its own source offset at its new place.
// Does not own: what the app sends after a split or a drag (SplitMovePlaybackSyncTests).
// Tests: this file.
[Collection("EngineHeadless")]
public class EngineSplitClipGapTests
{
    private const int Slot = 0;

    [Fact]
    public void ReplacingAWholeClipWithMovedApartPiecesSilencesTheGapWhilePlaying()
    {
        var wav = Path.Combine(Path.GetTempPath(), $"tf-gap-{Guid.NewGuid():N}.wav");
        using (var writer = new WaveFileWriter(wav, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
        {
            var block = new float[4800 * 2];
            for (var done = 0; done < 12 * 48000; done += 4800)
            {
                for (var i = 0; i < 4800; i++) block[i * 2] = block[i * 2 + 1] = 0.3f * MathF.Sin((done + i) * 0.05f);
                writer.WriteSamples(block, 0, block.Length);
            }
        }
        using var shared = SharedBlock.Create($"tf-gap-{Guid.NewGuid():N}");
        EH.Attach(shared, (_, _, _) => throw new InvalidOperationException("no plug-ins here"));
        try
        {
            EH.Configure(new EngineConfig(AudioOutputFactory.Null, AudioOutputFactory.NullManual, 48000, 256, false));
            EH.LoadChain(Slot, "audio", false, new List<PluginSpec>());
            EH.Command(EngineCommand.SetTrackMix, w => { w.Write(Slot); w.Write(100); w.Write(64); });
            EH.SetClips(Slot, new List<ClipSpec> { new(wav, 0, 0, 12, 0, 0, 1) });
            EH.Collect();

            float Peak(int ms)
            {
                var best = 0f;
                var until = DateTime.UtcNow.AddMilliseconds(ms);
                while (DateTime.UtcNow < until) { EH.Pump(4); EH.Collect(); best = Math.Max(best, shared.Peak(Slot)); Thread.Sleep(2); }
                return best;
            }

            EH.SetPlaying(true, 5.0);
            Assert.True(Peak(1500) > 0.01f, "the whole clip never sounded at 5 s");

            // The clip was split at 4 s and the right piece moved from 4 s to 8 s: the song is at about 6.5 s now, inside the gap.
            EH.SetClips(Slot, new List<ClipSpec> { new(wav, 0, 0, 4, 0, 0, 1), new(wav, 8, 4, 4, 0, 0, 1) });
            EH.SetPlaying(true, 5.5);
            Peak(400);   // the old ring and the meter's hold drain
            Assert.True(Peak(600) < 0.002f, "the gap is not silent after the clip list was replaced");

            EH.SetPlaying(true, 8.5);
            Assert.True(Peak(1500) > 0.01f, "the moved right piece does not sound at its new place");
            EH.SetPlaying(true, 3.0);
            Assert.True(Peak(1500) > 0.01f, "the left piece does not sound before its end");
            EH.SetPlaying(true, 4.5);
            Peak(300);
            Assert.True(Peak(500) < 0.002f, "the left piece sounds past its end");
        }
        finally
        {
            EH.Detach();
            Thread.Sleep(100);
            try { File.Delete(wav); } catch (IOException) { }
        }
    }
}
