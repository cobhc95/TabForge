using System.Text.Json;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Band;

namespace TabForge;

/// <summary>Band lanes follow the playhead with the score's follow settings; the Band's own choice and the old saved value still work.</summary>
public static partial class SelfTest
{
    private static void TestBandFollow()
    {
        var project = BandSong(130);
        var host = new FakeBandHost(project);
        using var band = new BandViewController(host);
        BandStage(band, 900, 800);
        band.Tick();
        BandStage(band, 900, 800);
        var follow = host.Settings.Follow;
        var lane = band.View.Rows[0].Lane;

        // Jump (the score's page turn): the strip holds, then turns about half a page near the end of the visible part.
        follow.Mode = FollowModes.Jump; follow.ContinuousScroll = false; follow.HorizontalFollow = true;
        band.Tick();
        band.Apply(0, 0.0, 0, true, false);
        var offsets = new List<double>();
        for (var bar = 0; bar < 40; bar++) { band.Apply(bar, 0.5, bar * 2000.0, true, false); offsets.Add(lane.Offset); }
        var jumps = Enumerable.Range(1, offsets.Count - 1).Where(i => Math.Abs(offsets[i] - offsets[i - 1]) > 0.5).ToList();
        Check("band follow: score jump mode turns now and then, not every bar", jumps.Count > 0 && jumps.Count < 30, jumps.Count.ToString());
        Check("band follow: a jump moves about half a page", jumps.Any(i => Math.Abs(offsets[i] - offsets[i - 1] - lane.ActualWidth * 0.5) < lane.ActualWidth * 0.1) && jumps.All(i => Math.Abs(offsets[i] - offsets[i - 1]) <= lane.ActualWidth), string.Join(",", jumps.Select(i => (offsets[i] - offsets[i - 1]).ToString("0"))));

        // Smooth (the default): the score still turns half a page sideways, so the strip holds between turns.
        follow.Mode = FollowModes.Smooth;
        band.Tick();
        band.Apply(5, 0.0, 10000, true, false);
        var a = lane.Offset;
        band.Apply(5, 0.3, 10600, true, false);
        Check("band follow: score smooth mode turns pages like the score, it does not scroll every frame", Math.Abs(lane.Offset - a) < 1e-6, $"{a:0} {lane.Offset:0}");

        // Off: the strip stays.
        follow.Mode = FollowModes.Off;
        band.Tick();
        band.Apply(5, 0.0, 10000, true, false);
        var b = lane.Offset;
        band.Apply(20, 0.0, 40000, true, false);
        Check("band follow: score follow off holds the strip", Math.Abs(lane.Offset - b) < 1e-6, $"{b:0} {lane.Offset:0}");

        // Override: the Band's own choice wins when it does not follow the score.
        var settings = host.Settings.Timeline.Band!;
        settings.FollowLikeScore = false; settings.SmoothFollow = true;
        band.Tick();
        band.Apply(5, 0.0, 10000, true, false);
        var c = lane.Offset;
        band.Apply(5, 0.3, 10600, true, false);
        Check("band follow: the override toggle uses the Band's own smooth follow", lane.Offset > c + 1e-6);

        // The lane's line stands where the score's own playhead stands for the same bar and fraction.
        var reference = new TabEditorControl { Project = project, SelectedTrackIndex = 0, Notation = NotationMode.TabOnly, HorizontalScroll = true, Zoom = lane.Editor.Zoom };
        reference.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        reference.Playback.Active = true;
        var worst = 0.0;
        foreach (var (bar, fraction) in new[] { (0, 0.0), (3, 0.37), (12, 0.5), (25, 0.93) })
        {
            reference.Playback.Fraction = fraction;
            reference.Playback.SetPlayhead(bar, 0);
            settings.FollowLikeScore = true; follow.Mode = FollowModes.Smooth;
            band.Tick();
            band.Apply(bar, fraction, 0, true, false);
            var score = reference.Playback.PlayheadGeometry();
            worst = Math.Max(worst, Math.Abs((score?.X ?? double.NaN) - lane.PlayheadStripX));
        }
        Check("band follow: the lane's line is where the score's playhead is for the same time", worst < 0.01, worst.ToString("0.000"));
        Check("band follow: the strip moves in whole pixels, in step with the line", Math.Abs(lane.Offset - Math.Round(lane.Offset)) < 1e-9);

        // Long seeks: any distance, either way, shows the asked bar at once (the line and the strip agree with the bar's own position).
        foreach (var glide in new[] { false, true })
        foreach (var playing in new[] { false, true })
        {
        settings.FollowLikeScore = true; follow.Mode = FollowModes.Jump; follow.HorizontalFollow = true; follow.ContinuousScroll = glide;
        band.Tick();
        var seekWorst = 0.0; var seekInView = true; var seekLog = "";
        foreach (var bar in new[] { 5, 9, 14, 19, 41, 3, 120 })
        {
            band.Apply(bar, 0.25, 0, playing, false);
            var want = lane.Editor.PlaybackHorizontalGeometry(bar, 0.25)!.Value.PlayheadX;
            seekWorst = Math.Max(seekWorst, Math.Abs(want - lane.PlayheadStripX));
            var onLane = lane.PlayheadStripX - lane.Offset;
            seekInView &= onLane >= 0 && onLane <= lane.ActualWidth;
            seekLog += $" {bar}:{lane.Offset:0}/{lane.PlayheadStripX:0}/{want:0}";
        }
        Check($"band follow: a long seek forward or back puts the line and the strip on the asked bar (glide {glide}, playing {playing})", seekWorst < 0.01 && seekInView, seekLog);
        }

        // The shipped demo songs, seeking while stopped and while playing.
        foreach (var (name, demo) in new[] { ("demo", DemoSongFactory.Create()), ("full demo", FullDemoSongFactory.Create()) })
        {
            var dHost = new FakeBandHost(demo);
            using var dBand = new BandViewController(dHost);
            BandStage(dBand, 900, 800);
            dBand.Tick();
            BandStage(dBand, 900, 800);
            var dLane = dBand.View.Rows[0].Lane;
            var last = demo.Tracks[0].Measures.Count - 1;
            var worstDemo = 0.0; var logDemo = "";
            foreach (var playing in new[] { false, true })
                foreach (var bar in new[] { 4, Math.Min(40, last), 2, last, 4 })
                {
                    dBand.Apply(bar, 0.25, 0, playing, false);
                    var want = dLane.Editor.PlaybackHorizontalGeometry(bar, 0.25)!.Value.PlayheadX;
                    var onLane = dLane.PlayheadStripX - dLane.Offset;
                    worstDemo = Math.Max(worstDemo, Math.Abs(want - dLane.PlayheadStripX) + (onLane >= 0 && onLane <= dLane.ActualWidth ? 0 : 999));
                    logDemo += $" {bar}:{dLane.Offset:0}/{dLane.PlayheadStripX:0}/{want:0}";
                }
            Check($"band follow: long seeks on the {name} song land on the asked bar", worstDemo < 0.01, logDemo);
        }

        // Migration: a file saved with smooth follow off (no follow-score value) keeps page-by-page; the default follows the score.
        var old = JsonSerializer.Deserialize<BandSettings>("{\"SmoothFollow\":false}")!;
        var def = JsonSerializer.Deserialize<BandSettings>("{\"SmoothFollow\":true}")!;
        Check("band follow: an old saved smooth-follow off keeps the Band's own page-by-page follow", !old.FollowsScore && !old.SmoothFollow);
        Check("band follow: an old saved smooth-follow on follows the score", def.FollowsScore);
        Check("band follow: an explicit choice is kept", !JsonSerializer.Deserialize<BandSettings>("{\"FollowLikeScore\":false,\"SmoothFollow\":true}")!.FollowsScore);
    }
}
