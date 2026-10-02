using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;
using TabForge.Rendering;
using TabForge.Services;

namespace TabForge;

/// <summary>Render dialog "Custom bars" / "Custom sections": same bar-to-time mapping as "Selected bars", validation, section ranges.</summary>
public static partial class SelfTest
{
    private static void TestRenderBarRanges()
    {
        // 6 bars of quarters at 120 BPM; bar 2 (index 1) switches to 60 BPM, bar 5 (index 4) to 3/4. Sections start at bars 1, 3 and 5.
        var p = SingleTrack(6);
        for (var bar = 0; bar < 6; bar++)
            for (var beat = 0; beat < 4; beat++) Beat(p, 0, bar, beat * 4, 4, 60);
        p.Tracks[0].Measures[1].TempoChange = 60;
        p.Tracks[0].Measures[4].TimeSigNum = 3; p.Tracks[0].Measures[4].TimeSigDenom = 4;
        for (var beat = 3; beat < 4; beat++) { p.Tracks[0].Measures[4].Cells[beat * 4].Notes.Clear(); }
        p.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "Intro" });
        p.Markers.Add(new MarkerModel { MeasureIndex = 2, Title = "Verse 1" });
        p.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "Outro" });
        var tl = RenderSpecBuilder.Compile(p);
        Check("render bars: the test song has 6 bars with a tempo change and a 3/4 bar",
            tl.Bars.Count == 6 && tl.Bars[1].EndMs - tl.Bars[1].StartMs > 1.5 * (tl.Bars[0].EndMs - tl.Bars[0].StartMs)
            && tl.Bars[4].EndMs - tl.Bars[4].StartMs < tl.Bars[3].EndMs - tl.Bars[3].StartMs, string.Join(",", tl.Bars.Select(b => (b.EndMs - b.StartMs).ToString("0"))));

        (double, double) Selected(int a, int b) => RenderSpecBuilder.Bounds(tl, RenderBounds.Bars, a, 0, b, -1, 0, 0);
        (double, double) CustomBars(int a, int b) => RenderSpecBuilder.Bounds(tl, RenderBounds.CustomBars, a, 0, b, -1, 0, 0);
        foreach (var (a, b) in new[] { (0, 5), (1, 1), (1, 3), (3, 5), (4, 4), (2, 4) })
        {
            var (s, e) = CustomBars(a, b);
            Check($"render custom bars {a + 1}..{b + 1}: same start/end as Selected bars, bar start to bar end ({s:0}..{e:0} ms)",
                (s, e) == Selected(a, b) && Math.Abs(s - tl.Bars[a].StartMs) < 0.01 && Math.Abs(e - tl.Bars[b].EndMs) < 0.01);
        }
        var (fs, fe) = CustomBars(0, 5);
        Near("render custom bars 1..last covers the whole song", tl.TotalMs, fe - fs, 1);

        // Validation.
        Check("render bars: 1 to 6 is valid", RenderBarRange.Validate("1", "6", 6) is null);
        Check("render bars: 3 to 3 is valid", RenderBarRange.Validate("3", "3", 6) is null);
        Check("render bars: last before first is refused", RenderBarRange.Validate("4", "2", 6) is not null);
        Check("render bars: 0 and past the end are refused", RenderBarRange.Validate("0", "3", 6) is not null && RenderBarRange.Validate("1", "7", 6) is not null);
        Check("render bars: empty or non-numeric text is refused", RenderBarRange.Validate("", "3", 6) is not null && RenderBarRange.Validate("1", "x", 6) is not null);
        Check("render bars: a song without bars is refused", RenderBarRange.Validate("1", "1", 0) is not null);

        // Sections: bars (0-based, inclusive).
        Check("render sections: first section alone = bars 1-2", RenderBarRange.SectionBars(p, 0, 0) is (0, 1));
        Check("render sections: middle section alone = bars 3-4", RenderBarRange.SectionBars(p, 1, 1) is (2, 3));
        Check("render sections: last section ends at the song end = bars 5-6", RenderBarRange.SectionBars(p, 2, 2) is (4, 5));
        Check("render sections: first to last = the whole song", RenderBarRange.SectionBars(p, 0, 2) is (0, 5));
        Check("render sections: first to middle = bars 1-4", RenderBarRange.SectionBars(p, 0, 1) is (0, 3));
        Check("render sections: 'to' before 'from' or out of range is refused", RenderBarRange.SectionBars(p, 2, 1) is null && RenderBarRange.SectionBars(p, 0, 3) is null);
        Check("render sections: labelled with the bar", RenderBarRange.Label(p.Markers[1]) == "Verse 1 (bar 3)");
        var (ss, se) = RenderSpecBuilder.Bounds(tl, RenderBounds.CustomSections, 2, 0, 3, -1, 0, 0);
        Check("render sections: the middle section's time range is bars 3-4 of the timeline", Math.Abs(ss - tl.Bars[2].StartMs) < 0.01 && Math.Abs(se - tl.Bars[3].EndMs) < 0.01);
        var none = SingleTrack(3);
        Check("render sections: a song without sections has no range", RenderBarRange.SectionBars(none, 0, 0) is null);

        // Length: the rendered file is the range plus a fixed tail.
        var rate = 48000; var tail = RenderSpecBuilder.ToFrames(3000, rate);
        var (ms, me) = CustomBars(1, 3);
        var frames = RenderSpecBuilder.ToFrames(me, rate) - RenderSpecBuilder.ToFrames(ms, rate);
        Check("render custom bars: range length in frames matches the bars' duration", Math.Abs(frames - RenderSpecBuilder.ToFrames(tl.Bars[3].EndMs - tl.Bars[1].StartMs, rate)) <= 1);
        Check("render custom bars: expected file length = range + tail", Math.Abs(frames + tail - (RenderSpecBuilder.ToFrames(tl.Bars[3].EndMs - tl.Bars[1].StartMs, rate) + 144000)) <= 1);

        // Repeats: the same mapping as Selected bars (first pass start .. last pass end of the chosen bars).
        var rep = SingleTrack(3);
        for (var bar = 0; bar < 3; bar++) Beat(rep, 0, bar, 0, 4, 60);
        rep.Tracks[0].Measures[1].RepeatStart = true; rep.Tracks[0].Measures[1].RepeatEnd = true; rep.Tracks[0].Measures[1].RepeatCount = 2;
        var rtl = RenderSpecBuilder.Compile(rep);
        Check("render custom bars: with a repeat it matches Selected bars exactly",
            RenderSpecBuilder.Bounds(rtl, RenderBounds.CustomBars, 1, 0, 2, -1, 0, 0) == RenderSpecBuilder.Bounds(rtl, RenderBounds.Bars, 1, 0, 2, -1, 0, 0));
    }
}
