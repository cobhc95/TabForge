using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Capture;

namespace TabForge.Avalonia.Capture;

// Owns: the frames verb: shoot a sequence of states (a moving playhead, or a list of step groups) at a fixed frame rate and encode them as one looping GIF.
// Does not own: the encoding (GifEncoder), which control is shot (CaptureTargets), the step verbs a group runs (the other step classes).
// Tests: TabForge.Avalonia.Tests.CaptureSessionTests.
internal sealed class FrameSteps(CaptureSession session)
{
    /// <summary>Wait before each frame is read back, so the state a group set has drawn (ms).</summary>
    private const int FrameSettleMs = 80;

    private readonly ShotSteps _shots = new(session);

    public void Register(Dictionary<string, Func<CaptureStep, Task>> verbs) => verbs["frames"] = FramesAsync;

    /// <summary>
    /// <c>{"frames":"name","fps":10,"scale":1,"target":..,"region":[..],"hold":800, "playhead":[from,to],"count":30}</c> moves the held playback from performed bar <c>from</c>
    /// to <c>to</c> (1-based, fractions allowed, repeats counted) in <c>count</c> frames; <c>"sequence":[[step,..],[step,..]]</c> runs one group of steps before each frame instead.
    /// <c>hold</c> lengthens the last frame (ms).
    /// </summary>
    private async Task FramesAsync(CaptureStep step)
    {
        var fps = Math.Max(1, step.Option("fps", 10));
        var scale = step.Option("scale", 1.0);
        var groups = step.Groups("sequence");
        var range = step.NumbersOption("playhead");
        var count = groups?.Count ?? step.Option("count", 20);
        if (groups is null && range is null) throw new InvalidOperationException("frames needs a playhead range or a sequence.");
        var images = new List<(CaptureImage Image, int Delay)>();
        for (var i = 0; i < count; i++)
        {
            if (groups is not null) foreach (var inner in groups[i]) await session.RunStepAsync(inner);
            if (range is not null)
            {
                var at = range[0] + (range[1] - range[0]) * (count == 1 ? 0 : i / (double)(count - 1));
                session.HoldMoment((int)Math.Floor(at) - 1, at - Math.Floor(at));
            }
            await session.SettleAsync(FrameSettleMs);
            var image = _shots.Render(step, scale);
            images.Add((image, Math.Max(0, (int)Math.Round(1000.0 / fps))));
        }
        var across = images.Max(i => i.Image.Width);
        var down = images.Max(i => i.Image.Height);
        var frames = images.Select(i => new GifFrame(across, down, i.Image.PadTo(across, down).ToRgba(), i.Delay)).ToList();
        var last = frames[^1];
        frames[^1] = last with { DelayMs = last.DelayMs + Math.Max(0, step.Option("hold", 0)) };
        var file = step.Text + ".gif";
        var path = CaptureNames.PathUnder(session.OutputFolder, step.Text, ".gif");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var output = File.Create(path)) GifEncoder.Write(output, frames);
        session.Shots.Add(new CaptureShot(step.Text, file, last.Width, last.Height, session.Theme, 0));
    }
}
