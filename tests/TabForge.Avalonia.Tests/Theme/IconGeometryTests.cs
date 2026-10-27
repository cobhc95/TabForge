using TabForge.Platform.Theme;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: pins the path data of each shared icon in IconGeometry, so a change to an icon's shape fails here under the icon's name.
// Does not own: how the icons are drawn (the render identity gate and the parity captures).
public class IconGeometryTests
{
    [Fact]
    public void MaximisePath() => Assert.Equal("M 3.5,3.5 L 12.5,3.5 L 12.5,12.5 L 3.5,12.5 Z", IconGeometry.Maximise);

    [Fact]
    public void RestorePath() => Assert.Equal("M 3,6 L 10,6 L 10,13 L 3,13 Z M 6,6 L 6,3 L 13,3 L 13,10 L 10,10", IconGeometry.Restore);

    [Fact]
    public void ClosePath() => Assert.Equal("M 4,4 L 12,12 M 12,4 L 4,12", IconGeometry.Close);

    [Fact]
    public void PlusPath() => Assert.Equal("M 8,3.5 L 8,12.5 M 3.5,8 L 12.5,8", IconGeometry.Plus);

    [Fact]
    public void SpeakerPath() => Assert.Equal("M 3,6 L 5.5,6 L 9,3.5 L 9,12.5 L 5.5,10 L 3,10 Z M 11,6 C 12.3,7 12.3,9 11,10 M 12.5,4.5 C 14.5,6.5 14.5,9.5 12.5,11.5", IconGeometry.Speaker);

    [Fact]
    public void CogPath() => Assert.Equal("F0 M 6.65,2.57 L 6.95,0.37 L 9.05,0.37 L 9.35,2.57 A 5.6,5.6 0 0 1 10.89,3.20 L 10.89,3.20 L 12.65,1.86 L 14.14,3.35 L 12.80,5.11 A 5.6,5.6 0 0 1 13.43,6.65 L 13.43,6.65 L 15.63,6.95 L 15.63,9.05 L 13.43,9.35 A 5.6,5.6 0 0 1 12.80,10.89 L 12.80,10.89 L 14.14,12.65 L 12.65,14.14 L 10.89,12.80 A 5.6,5.6 0 0 1 9.35,13.43 L 9.35,13.43 L 9.05,15.63 L 6.95,15.63 L 6.65,13.43 A 5.6,5.6 0 0 1 5.11,12.80 L 5.11,12.80 L 3.35,14.14 L 1.86,12.65 L 3.20,10.89 A 5.6,5.6 0 0 1 2.57,9.35 L 2.57,9.35 L 0.37,9.05 L 0.37,6.95 L 2.57,6.65 A 5.6,5.6 0 0 1 3.20,5.11 L 3.20,5.11 L 1.86,3.35 L 3.35,1.86 L 5.11,3.20 A 5.6,5.6 0 0 1 6.65,2.57 Z M 10.6,8 A 2.6,2.6 0 1 0 5.4,8 A 2.6,2.6 0 1 0 10.6,8 Z", IconGeometry.Cog);

    [Fact]
    public void FretboardPath() => Assert.Equal("M 2,8 L 6,6.5 L 6,17.5 L 2,16 Z M 6,6.5 L 22,6.5 L 22,17.5 L 6,17.5 M 11,6.5 L 11,17.5 M 16,6.5 L 16,17.5 M 6,10.2 L 22,10.2 M 6,13.8 L 22,13.8", IconGeometry.Fretboard);

    [Fact]
    public void PrefsPath() => Assert.Equal("M 3,5.5 L 13,5.5 M 3,10.5 L 13,10.5 M 5.5,4 L 8,4 L 8,7 L 5.5,7 Z M 9,9 L 11.5,9 L 11.5,12 L 9,12 Z", IconGeometry.Prefs);

    [Fact]
    public void SidePanelPath() => Assert.Equal("M3,4 H21 V20 H3 Z M15,4 V20", IconGeometry.SidePanel);

    [Fact]
    public void LearnModePath() => Assert.Equal("M3,5 H21 V19 H3 Z M9,12.5 V19 M15,12.5 V19 M7.5,5 V12.5 H10.5 V5 M13.5,5 V12.5 H16.5 V5", IconGeometry.LearnMode);

    [Fact]
    public void BandViewPath() => Assert.Equal("M9.3,7 a2.7,2.7 0 1 0 5.4,0 a2.7,2.7 0 1 0 -5.4,0 M7,19.5 c0,-3.4 2.1,-5.6 5,-5.6 s5,2.2 5,5.6 M2.8,8.8 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M1.8,18.5 c0,-2.4 1.1,-4 3,-4.4 M17.2,8.8 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M22.2,18.5 c0,-2.4 -1.1,-4 -3,-4.4", IconGeometry.BandView);

    [Fact]
    public void CompactModePath() => Assert.Equal("M12,2.5 V8.5 M9,5.5 L12,8.5 L15,5.5 M12,21.5 V15.5 M9,18.5 L12,15.5 L15,18.5 M3.5,12 H20.5", IconGeometry.CompactMode);

    [Fact]
    public void CameraPath() => Assert.Equal("M2,6 H15 V18 H2 Z M15,10 L22,6 V18 L15,14", IconGeometry.Camera);

    [Fact]
    public void ZoomGlassPath() => Assert.Equal("M7,4 A3,3 0 1 1 1,4 A3,3 0 1 1 7,4 M6.3,6.3 L9.5,9.5", IconGeometry.ZoomGlass);

    [Fact]
    public void ScaleStepsPath() => Assert.Equal("M0,11 L3,11 L3,8 L6,8 L6,5 L9,5 L9,2 L12,2", IconGeometry.ScaleSteps);

    [Fact]
    public void ToolSelectPath() => Assert.Equal("M4,2.5 L4,13.2 L6.7,10.6 L8.6,14.2 L10.4,13.4 L8.5,9.8 L12.2,9.6 Z", IconGeometry.ToolSelect);

    [Fact]
    public void ToolDrawPath() => Assert.Equal("M3,13 L3.7,10.2 L10.8,3.1 L12.9,5.2 L5.8,12.3 Z M9.5,4.4 L11.6,6.5", IconGeometry.ToolDraw);

    [Fact]
    public void ToolErasePath() => Assert.Equal("M2.8,10.2 L8.4,4.6 L13.4,9.6 L9.6,13.4 L6,13.4 Z M5.6,7.4 L10.6,12.4 M9.6,13.4 L14,13.4", IconGeometry.ToolErase);

    [Fact]
    public void ToolSplitPath() => Assert.Equal("M2.8,11.6 A2,2 0 1 1 6.8,11.6 A2,2 0 1 1 2.8,11.6 M9.2,11.6 A2,2 0 1 1 13.2,11.6 A2,2 0 1 1 9.2,11.6 M6.2,10.2 L11.5,2.8 M9.8,10.2 L4.5,2.8", IconGeometry.ToolSplit);

    [Fact]
    public void ToolMutePath() => Assert.Equal("M2.5,6 L10.5,6 L10.5,10.5 L2.5,10.5 Z M11.5,3.5 L14.5,7 M14.5,3.5 L11.5,7", IconGeometry.ToolMute);

    [Fact]
    public void QuantizePath() => Assert.Equal("M3,2.5 L3,13.5 M8,2.5 L8,13.5 M13,2.5 L13,13.5 M3,4.5 L7,4.5 L7,6.9 L3,6.9 Z M8,9.2 L11.2,9.2 L11.2,11.6 L8,11.6 Z", IconGeometry.Quantize);

    [Fact]
    public void ZoomInPath() => Assert.Equal("M2.7,7 A4.3,4.3 0 1 1 11.3,7 A4.3,4.3 0 1 1 2.7,7 M10.2,10.2 L13.8,13.8 M5,7 L9,7 M7,5 L7,9", IconGeometry.ZoomIn);

    [Fact]
    public void ZoomOutPath() => Assert.Equal("M2.7,7 A4.3,4.3 0 1 1 11.3,7 A4.3,4.3 0 1 1 2.7,7 M10.2,10.2 L13.8,13.8 M5,7 L9,7", IconGeometry.ZoomOut);

    [Fact]
    public void ZoomFitPath() => Assert.Equal("M2.5,5.5 L2.5,2.5 L5.5,2.5 M10.5,2.5 L13.5,2.5 L13.5,5.5 M13.5,10.5 L13.5,13.5 L10.5,13.5 M5.5,13.5 L2.5,13.5 L2.5,10.5 M5.5,6.5 L10.5,6.5 L10.5,9.5 L5.5,9.5 Z", IconGeometry.ZoomFit);

    [Fact]
    public void FollowPath() => Assert.Equal("M2.5,8 L11.5,8 M8.5,5 L11.5,8 L8.5,11 M13.5,3 L13.5,13", IconGeometry.Follow);

    [Fact]
    public void MagnetPath() => Assert.Equal("M3.5,2.5 L3.5,8.5 A4.5,4.5 0 0 0 12.5,8.5 L12.5,2.5 L9.8,2.5 L9.8,8.5 A1.8,1.8 0 0 1 6.2,8.5 L6.2,2.5 Z M3.5,5 L6.2,5 M9.8,5 L12.5,5", IconGeometry.Magnet);

    [Fact]
    public void WarningPath() => Assert.Equal("M8,2 L14.5,13.5 L1.5,13.5 Z M8,6.5 L8,9.5 M8,11.6 L8,11.7", IconGeometry.Warning);

    [Fact]
    public void ToolGluePath() => Assert.Equal("M2.5,8 L6.5,8 M9.5,8 L13.5,8 M6.5,5.5 L6.5,10.5 M9.5,5.5 L9.5,10.5 M7.2,8 L8.8,8", IconGeometry.ToolGlue);

    [Fact]
    public void ToolVelocityPath() => Assert.Equal("M3.5,13.5 L3.5,8 M8,13.5 L8,4.5 M12.5,13.5 L12.5,7 M2.2,7 A1.3,1.3 0 1 1 4.8,7 A1.3,1.3 0 1 1 2.2,7 M6.7,3.5 A1.3,1.3 0 1 1 9.3,3.5 A1.3,1.3 0 1 1 6.7,3.5 M11.2,6 A1.3,1.3 0 1 1 13.8,6 A1.3,1.3 0 1 1 11.2,6", IconGeometry.ToolVelocity);

    [Fact]
    public void ToolLegatoPath() => Assert.Equal("M2.5,5 L7,5 L7,7.5 L2.5,7.5 Z M7.5,9 L13.5,9 L13.5,11.5 L7.5,11.5 Z M7,6.2 L9.5,6.2", IconGeometry.ToolLegato);

    [Fact]
    public void ScaleRowsPath() => Assert.Equal("M2.5,4 L13.5,4 M2.5,8 L13.5,8 M2.5,12 L13.5,12 M2.5,5.6 L13.5,5.6 L13.5,6.6 L2.5,6.6 Z", IconGeometry.ScaleRows);

    [Fact]
    public void StepInputPath() => Assert.Equal("M2.5,6 L7,6 L7,9.5 L2.5,9.5 Z M9,7.75 L13.5,7.75 M11.5,5.5 L13.5,7.75 L11.5,10", IconGeometry.StepInput);
}
