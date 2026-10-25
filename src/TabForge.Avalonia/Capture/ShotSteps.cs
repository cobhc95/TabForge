using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TabForge.Capture;

namespace TabForge.Avalonia.Capture;

// Owns: the shot verb: render the target at the capture scale, crop to a region, write the PNG and record it in the report.
// Does not own: which control a target name means (CaptureTargets), the pixels (CaptureImage).
// Tests: TabForge.Avalonia.Tests.CaptureSessionTests.
internal sealed class ShotSteps(CaptureSession session)
{
    private readonly CaptureTargets _targets = new(session);

    public void Register(Dictionary<string, Func<CaptureStep, Task>> verbs) => verbs["shot"] = ShotAsync;

    private async Task ShotAsync(CaptureStep step)
    {
        await session.SettleAsync(step.Option("settle", CaptureSession.SettleMs));
        var image = Render(step, step.Option("scale", CaptureScript.RenderScale));
        var file = step.Text + ".png";
        image.SavePng(CaptureNames.PathUnder(session.OutputFolder, step.Text, ".png"));
        session.Shots.Add(new CaptureShot(step.Text, file, image.Width, image.Height, session.Theme, Math.Round(image.InkShare(), 4)));
    }

    /// <summary>Renders the step's target (and optional region [x, y, width, height] in DIPs) at <paramref name="scale"/>.</summary>
    public CaptureImage Render(CaptureStep step, double scale)
    {
        session.ScrubStatusBar();
        var image = CaptureImage.Render(_targets.Resolve(step.Option("target")), scale);
        if (step.NumbersOption("region") is not { Length: 4 } r) return image;
        return image.Crop((int)Math.Round(r[0] * scale), (int)Math.Round(r[1] * scale), (int)Math.Round(r[2] * scale), (int)Math.Round(r[3] * scale));
    }
}
