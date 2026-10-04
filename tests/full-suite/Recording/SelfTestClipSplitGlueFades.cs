using System.IO;
using System.Text.Json;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Services;
using EA = TabForge.AudioEngine.Audio;

namespace TabForge;

/// <summary>
/// Clip split, glue and fades (part of <see cref="SelfTest"/>): the split keeps offset, length, gain, pitch and speed on both parts; glue joins
/// only pieces that continue each other; fades persist (old files load with none), cross the engine protocol, and the offline clip player ramps
/// the gain. The undo steps run in TestClipAndSectionEdits; the key routing is checked here against the catalog.
/// </summary>
public static partial class SelfTest
{
    private static void TestClipSplitGlueFades()
    {
        SplitCase();
        GlueCase();
        FadePersistenceCase();
        FadeEngineCase();
        var clipMap = HotkeyCatalog.BuildMap(new HotkeySettings(), clipContext: true);
        var noteMap = HotkeyCatalog.BuildMap(new HotkeySettings());
        Check("clip split/glue: S and Ctrl+Shift+G are clip-context keys only (note editing keeps S)",
            clipMap.GetValueOrDefault("S") == "Clip.Split" && clipMap.GetValueOrDefault("Ctrl+Shift+G") == "Clip.Glue"
            && noteMap.GetValueOrDefault("S") != "Clip.Split" && noteMap.GetValueOrDefault("Ctrl+Shift+G") != "Clip.Glue",
            $"clip S={clipMap.GetValueOrDefault("S")}, G={clipMap.GetValueOrDefault("Ctrl+Shift+G")}; notes S={noteMap.GetValueOrDefault("S")}");
    }

    private static AudioClip SgClip(double start = 1, double offset = 0.5, double length = 4) =>
        new() { File = @"C:\songs\a\x.wav", Name = "x", StartSec = start, OffsetSec = offset, SourceLengthSec = length, FileLengthSec = 10, GainDb = -3, Pitch = 2, Speed = 2, FadeInSec = 0.2, FadeOutSec = 0.3 };

    private static void SplitCase()
    {
        var track = new TrackModel();
        var clip = SgClip();   // speed 2: 4 s of file plays in 2 s of timeline, 1.0 .. 3.0
        track.AudioClips.Add(clip);
        var outside = ClipSplitGlue.Split(track, clip, 3.5);
        var second = ClipSplitGlue.Split(track, clip, 1.5);   // 0.5 s of timeline = 1 s of file
        Check("clip split: a time outside the clip does nothing; inside it cuts at that time, not in half",
            outside is null && second is not null && track.AudioClips.Count == 2 && Math.Abs(clip.EndSec - 1.5) < 1e-9 && Math.Abs(second!.StartSec - 1.5) < 1e-9 && Math.Abs(second.EndSec - 3.0) < 1e-9,
            $"first {clip.StartSec}-{clip.EndSec}, second {second?.StartSec}-{second?.EndSec}");
        Check("clip split: both parts keep file, gain, pitch and speed; the second continues the file where the first stops",
            second is not null && second.File == clip.File && second.GainDb == -3 && second.Pitch == 2 && second.Speed == 2 && clip.GainDb == -3
            && Math.Abs(clip.OffsetSec - 0.5) < 1e-9 && Math.Abs(clip.SourceLengthSec - 1) < 1e-9 && Math.Abs(second.OffsetSec - 1.5) < 1e-9 && Math.Abs(second.SourceLengthSec - 3) < 1e-9,
            $"first offset {clip.OffsetSec} len {clip.SourceLengthSec}; second offset {second?.OffsetSec} len {second?.SourceLengthSec}");
        Check("clip split: the fade-in stays with the first part, the fade-out goes to the second",
            second is not null && clip.FadeInSec == 0.2 && clip.FadeOutSec == 0 && second.FadeInSec == 0 && second.FadeOutSec == 0.3, $"{clip.FadeInSec}/{clip.FadeOutSec} {second?.FadeInSec}/{second?.FadeOutSec}");
    }

    private static void GlueCase()
    {
        var track = new TrackModel();
        var clip = SgClip();
        track.AudioClips.Add(clip);
        var second = ClipSplitGlue.Split(track, clip, 1.5)!;
        var third = ClipSplitGlue.Split(track, second, 2.25)!;
        var other = SgClip(5, 0.5, 4);   // not touching
        track.AudioClips.Add(other);
        var glued = ClipSplitGlue.Glue(track, second);   // from the middle piece: the whole chain joins
        Check("clip glue: contiguous pieces of one file join into the first piece, restoring the original span, offset and fades",
            glued == clip && track.AudioClips.Count == 2 && Math.Abs(clip.StartSec - 1) < 1e-9 && Math.Abs(clip.EndSec - 3) < 1e-9 && Math.Abs(clip.OffsetSec - 0.5) < 1e-9
            && Math.Abs(clip.SourceLengthSec - 4) < 1e-9 && clip.FadeInSec == 0.2 && clip.FadeOutSec == 0.3 && !track.AudioClips.Contains(third),
            $"{clip.StartSec}-{clip.EndSec} offset {clip.OffsetSec} len {clip.SourceLengthSec}, clips {track.AudioClips.Count}");
        var apart = SgClip(3.5, 0.5, 4);
        var differentFile = SgClip(3, 4.5, 4); differentFile.File = @"C:\songs\a\y.wav";
        var differentSpeed = SgClip(3, 4.5, 4); differentSpeed.Speed = 1;
        var gap = SgClip(3, 5.0, 4);
        Check("clip glue: a gap, another file, another speed or a skipped part of the file is never merged",
            !ClipSplitGlue.Continues(clip, apart) && !ClipSplitGlue.Continues(clip, differentFile) && !ClipSplitGlue.Continues(clip, differentSpeed) && !ClipSplitGlue.Continues(clip, gap)
            && ClipSplitGlue.Glue(track, other) is null, "a non-continuing clip was merged");
    }

    private static void FadePersistenceCase()
    {
        var clip = SgClip();
        var back = JsonSerializer.Deserialize<AudioClip>(JsonSerializer.Serialize(clip))!;
        var plain = new AudioClip { File = "a.wav", SourceLengthSec = 1 };
        var json = JsonSerializer.Serialize(plain);
        var old = JsonSerializer.Deserialize<AudioClip>("""{"File":"a.wav","SourceLengthSec":1,"Speed":1}""")!;
        Check("clip fades: saved with the clip and copied by Clone; a clip without fades writes nothing and an old file loads with none",
            back.FadeInSec == 0.2 && back.FadeOutSec == 0.3 && clip.Clone().FadeOutSec == 0.3 && !json.Contains("Fade") && old.FadeInSec == 0 && old.FadeOutSec == 0, json);
        var spec = new ClipSpec("a.wav", 1, 0, 2, 0, 0, 1, 0.25, 0.5);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) writer.Write(new List<ClipSpec> { spec });
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var read = reader.ReadClips();
        Check("clip fades: cross the engine protocol", read.Count == 1 && read[0] == spec, $"{read.FirstOrDefault()}");
    }

    private static void FadeEngineCase()
    {
        var wav = Path.Combine(Path.GetTempPath(), $"tf-selftest-{Guid.NewGuid():N}.wav");
        try
        {
            using (var writer = new NAudio.Wave.WaveFileWriter(wav, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
            {
                var block = new float[4800 * 2];
                Array.Fill(block, 0.5f);
                for (var i = 0; i < 20; i++) writer.WriteSamples(block, 0, block.Length);   // 2 s of constant 0.5
            }
            float[] Render(ClipSpec spec)
            {
                using var player = new EA.ClipPlayer(spec, 48000, offline: true);
                var all = new float[48000 * 2];
                var left = new float[4800]; var right = new float[4800];
                for (var b = 0; b < 20; b++)
                {
                    Array.Clear(left); Array.Clear(right);
                    player.Mix(left, right, 4800, b * 0.1, true);
                    Array.Copy(left, 0, all, b * 4800, 4800);
                }
                return all;
            }
            var plain = Render(new ClipSpec(wav, 0, 0, 2, 0, 0, 1));
            var faded = Render(new ClipSpec(wav, 0, 0, 2, 0, 0, 1, 0.5, 0.5));
            bool Near(float v, double expected) => Math.Abs(v - expected) < 0.01;
            Check("clip fades: the offline player ramps the gain in and out (0 at the edges, half way at the quarter points, untouched between) and a clip without fades is unchanged",
                Near(plain[1000], 0.5) && Near(plain[96000 - 1000], 0.5) && Near(faded[0], 0) && Near(faded[12000], 0.25) && Near(faded[48000], 0.5) && Near(faded[84000], 0.25) && Near(faded[95999], 0),
                $"plain {plain[1000]:0.000}; faded {faded[0]:0.000} {faded[12000]:0.000} {faded[48000]:0.000} {faded[84000]:0.000} {faded[95999]:0.000}");
            Check("clip fades: two fades longer than the clip are scaled to fit and the gain stays in 0..1",
                Near(EA.ClipPlayer.FadeGain(0.25, 1, 1, 1), 0.5) && Near(EA.ClipPlayer.FadeGain(0, 1, 2, 2), 0) && Near(EA.ClipPlayer.FadeGain(0.25, 1, 0, 0), 1), "fade scaling");
        }
        finally { try { File.Delete(wav); } catch (IOException) { } }
    }
}
