using System.Linq;
using System.Threading;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

/// <summary>A bar added at the song's end while playing joins the running song: playback carries on into it (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static void TestAppendBarWhilePlaying()
    {
        var project = SingleTrack(bars: 3, bpm: 400);
        Beat(project, 0, 0, 0, 1, 60);
        Beat(project, 0, 1, 0, 4, 65);
        Beat(project, 0, 2, 0, 4, 72);
        var doc = DocumentSession.FromProject(project, null);
        doc.Playback.IsPlayingVisual = true;
        doc.Playback.PlayheadBar = 0;
        var output = new RecordingMidiOutput();
        using var engine = new PlaybackEngine(output);
        using var finished = new ManualResetEventSlim();
        engine.Start(project, new PlaybackOptions { Speed = 4 }, _ => { }, finished.Set);
        var started = SpinWait.SpinUntil(() => output.Messages.Any(m => (m.Status & 0xF0) == 0x90 && m.Data1 == 60 && m.Data2 > 0), TimeSpan.FromSeconds(2));
        Check("append while playing: playback started", started);
        if (!started) return;

        project.Tracks[0].Measures.Add(new MeasureModel { Number = 4 });
        Beat(project, 0, 3, 0, 4, 77);
        var applied = doc.Playback.ApplyStructureEdit(new[] { 0, 1, 2 }, 4);
        Check("append while playing: the bar joins the live remap and the playhead bar stays", applied && doc.Playback.PlayheadBar == 0 && doc.Playback.PlaybackBarRemap!.Length == 4);
        engine.RefreshArrangement(project, doc.Playback.PlaybackBarRemap!);
        Check("append while playing: playback runs on into the new bar and finishes", finished.Wait(TimeSpan.FromSeconds(4)));
        var attacks = output.Messages.Where(m => (m.Status & 0xF0) == 0x90 && m.Data2 > 0).Select(m => m.Data1).Where(p => p is 60 or 65 or 72 or 77).ToArray();
        Check("append while playing: the running note is not restarted and the new bar's note follows", attacks.SequenceEqual(new[] { 60, 65, 72, 77 }), string.Join(" ", attacks));
    }
}
