using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Capture;
using TabForge.Capture;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the capture run checks: the fixed window size and scale, a script of steps writing a PNG, a frame sequence writing a GIF, and a failed step being logged while the run goes on.
/// Does not own: the parser (CaptureScriptTests) or the encoder (GifEncoderTests).
/// Tests: this class.
/// </summary>
public class CaptureSessionTests
{
    private static string NewFolder() => DialogTestKit.RenderDirectory(nameof(CaptureSessionTests) + Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task ScriptWritesShotsAtTwiceTheWindowSizeAndKeepsGoingAfterAFailedStep()
    {
        var folder = NewFolder();
        var session = new CaptureSession(folder);
        await session.StartAsync();
        var failed = await session.RunAsync(CaptureScript.Parse("""
            [ {"nonsense":true}, {"shot":"main"}, {"shot":"crop","region":[0,0,100,50],"scale":1} ]
            """), "unit");
        session.Window.Close();
        Assert.Equal(1, failed);
        Assert.Contains("FAILED", File.ReadAllText(Path.Combine(folder, "unit.log")));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "unit.report.json")));
        var shots = report.RootElement.GetProperty("shots");
        Assert.Equal(CaptureScript.WindowDipsAcross * 2, shots[0].GetProperty("Width").GetInt32());
        Assert.Equal(CaptureScript.WindowDipsDown * 2, shots[0].GetProperty("Height").GetInt32());
        Assert.Equal(100, shots[1].GetProperty("Width").GetInt32());
        Assert.True(new FileInfo(Path.Combine(folder, "main.png")).Length > 1000);
    }

    [AvaloniaFact]
    public async Task FrameSequenceOverARepeatWritesALoopingGif()
    {
        var folder = NewFolder();
        var session = new CaptureSession(folder);
        await session.StartAsync();
        var failed = await session.RunAsync(CaptureScript.Parse("""
            [ {"frames":"walk","playhead":[1,5],"count":4,"fps":10,"scale":0.25,"target":"score"} ]
            """), "gif");
        session.Window.Close();
        Assert.Equal(0, failed);
        var bytes = File.ReadAllBytes(Path.Combine(folder, "walk.gif"));
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(bytes, 0, 6));
        Assert.Contains("NETSCAPE2.0", System.Text.Encoding.ASCII.GetString(bytes));
    }
}
