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
    public void HeadphonesPath() => Assert.Equal("M 3,10.5 L 3,8 A 5,5 0 0 1 13,8 L 13,10.5 M 2,10 L 4.5,10 L 4.5,13.5 L 2,13.5 Z M 11.5,10 L 14,10 L 14,13.5 L 11.5,13.5 Z", IconGeometry.Headphones);

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

    [Fact]
    public void ChevronDownPath() => Assert.Equal("M 3.5,6 L 8,10.5 L 12.5,6", IconGeometry.ChevronDown);

    [Fact]
    public void DragHandlePath() => Assert.Equal("M6,4 L6.1,4 M10,4 L10.1,4 M6,8 L6.1,8 M10,8 L10.1,8 M6,12 L6.1,12 M10,12 L10.1,12", IconGeometry.DragHandle);

    [Fact]
    public void PowerPath() => Assert.Equal("M8,2 L8,7.5 M4.6,4.2 A5,5 0 1 0 11.4,4.2", IconGeometry.Power);

    [Fact]
    public void SaveDiskPath() => Assert.Equal("M2.5,2.5 L11.5,2.5 L13.5,4.5 L13.5,13.5 L2.5,13.5 Z M5,2.5 L5,6 L10.5,6 L10.5,2.5 M5,13.5 L5,9.5 L11,9.5 L11,13.5", IconGeometry.SaveDisk);

    [Fact]
    public void TrashPath() => Assert.Equal("M2.5,4 L13.5,4 M6,4 L6,2.5 L10,2.5 L10,4 M4,4 L4.8,13.5 L11.2,13.5 L12,4 M6.8,6.5 L6.8,11 M9.2,6.5 L9.2,11", IconGeometry.Trash);

    [Fact]
    public void TargetPath() => Assert.Equal("M2.5,8 A5.5,5.5 0 1 1 13.5,8 A5.5,5.5 0 1 1 2.5,8 M5.5,8 A2.5,2.5 0 1 1 10.5,8 A2.5,2.5 0 1 1 5.5,8", IconGeometry.Target);

    [Fact]
    public void CheckMarkPath() => Assert.Equal("M3,8.5 L6.5,12 L13,4.5", IconGeometry.CheckMark);

    [Fact]
    public void MidiGroupNotesPath() => Assert.Equal("M3.5,11.5 A2,1.6 0 1 1 7.5,11.5 A2,1.6 0 1 1 3.5,11.5 M7.5,11.5 L7.5,2.5 C9,4.5 12,4.5 12.5,7", IconGeometry.MidiGroupNotes);

    [Fact]
    public void MidiGroupVelocityPath() => Assert.Equal("M2,13.5 L14,13.5 M3.5,13.5 L3.5,11 M6.5,13.5 L6.5,8 M9.5,13.5 L9.5,5.5 M12.5,13.5 L12.5,3", IconGeometry.MidiGroupVelocity);

    [Fact]
    public void MidiGroupTimingPath() => Assert.Equal("M5,14 L11,14 L9.5,2 L6.5,2 Z M8,11 L11.5,4.5", IconGeometry.MidiGroupTiming);

    [Fact]
    public void MidiGroupDrumsPath() => Assert.Equal("M2.5,7 A5.5,2 0 1 1 13.5,7 A5.5,2 0 1 1 2.5,7 M2.5,7 L2.5,12 A5.5,2 0 0 0 13.5,12 L13.5,7 M3,1.5 L7,5.5 M13,1.5 L9,5.5", IconGeometry.MidiGroupDrums);

    [Fact]
    public void MidiGroupGeneratePath() => Assert.Equal("M8,1.5 L9.3,6.7 L14.5,8 L9.3,9.3 L8,14.5 L6.7,9.3 L1.5,8 L6.7,6.7 Z", IconGeometry.MidiGroupGenerate);

    [Fact]
    public void MidiGroupChannelsPath() => Assert.Equal("M4,2 L4,14 M8,2 L8,14 M12,2 L12,14 M2.5,10 L5.5,10 M6.5,5 L9.5,5 M10.5,8.5 L13.5,8.5", IconGeometry.MidiGroupChannels);

    [Fact]
    public void MidiGroupAudioPath() => Assert.Equal("M1.5,8 L3.5,8 L5,3 L7,13 L9,5 L10.5,10 L12,8 L14.5,8", IconGeometry.MidiGroupAudio);

    [Fact]
    public void MidiGroupUtilitiesPath() => Assert.Equal("M10.5,2 A3.5,3.5 0 0 0 7.2,6.7 L2.2,11.7 A1.4,1.4 0 0 0 4.3,13.8 L9.3,8.8 A3.5,3.5 0 0 0 14,5.5 L11.8,7.2 L9.5,6.5 L8.8,4.2 Z", IconGeometry.MidiGroupUtilities);

    [Fact]
    public void MidiTransposePath() => Assert.Equal("M5,13 L5,3 M2.5,5.5 L5,3 L7.5,5.5 M11,3 L11,13 M8.5,10.5 L11,13 L13.5,10.5", IconGeometry.MidiTranspose);

    [Fact]
    public void MidiNoteRangePath() => Assert.Equal("M3,3 L3,13 M13,3 L13,13 M3,8 L13,8 M5.5,5.5 L3,8 L5.5,10.5 M10.5,5.5 L13,8 L10.5,10.5", IconGeometry.MidiNoteRange);

    [Fact]
    public void MidiScaleSnapPath() => Assert.Equal("M2,13 L5,13 L5,10 L8,10 L8,7 L11,7 L11,4 L14,4 M9.5,12 A1.5,1.5 0 1 1 12.5,12 A1.5,1.5 0 1 1 9.5,12", IconGeometry.MidiScaleSnap);

    [Fact]
    public void MidiChordPath() => Assert.Equal("M4,12 A1.8,1.3 0 1 1 7.6,12 A1.8,1.3 0 1 1 4,12 M4,8.5 A1.8,1.3 0 1 1 7.6,8.5 A1.8,1.3 0 1 1 4,8.5 M4,5 A1.8,1.3 0 1 1 7.6,5 A1.8,1.3 0 1 1 4,5 M7.6,12 L7.6,2", IconGeometry.MidiChord);

    [Fact]
    public void MidiVelVariationPath() => Assert.Equal("M3,14 L3,10 M6,14 L6,7 M9,14 L9,9 M12,14 L12,6 M2,3.5 C4,1.5 6,5.5 8,3.5 C10,1.5 12,5.5 14,3.5", IconGeometry.MidiVelVariation);

    [Fact]
    public void MidiHumanizePath() => Assert.Equal("M6.2,5.3 A1.8,1.8 0 1 1 9.8,5.3 A1.8,1.8 0 1 1 6.2,5.3 M4,13.5 C4,10 6,8.8 8,8.8 C10,8.8 12,10 12,13.5", IconGeometry.MidiHumanize);

    [Fact]
    public void MidiDelayPath() => Assert.Equal("M2.5,8 A5.5,5.5 0 1 1 13.5,8 A5.5,5.5 0 1 1 2.5,8 M8,5 L8,8 L10.5,9.5", IconGeometry.MidiDelay);

    [Fact]
    public void MidiNoteHoldPath() => Assert.Equal("M2.5,6 L6.5,6 L6.5,10 L2.5,10 Z M6.5,8 L13.5,8 M13.5,5.5 L13.5,10.5", IconGeometry.MidiNoteHold);

    [Fact]
    public void MidiRepeaterPath() => Assert.Equal("M3,7 A4,4 0 0 1 12,5 M12,2.5 L12,5 L9.5,5 M13,9 A4,4 0 0 1 4,11 M4,13.5 L4,11 L6.5,11", IconGeometry.MidiRepeater);

    [Fact]
    public void MidiNoteMapPath() => Assert.Equal("M2.5,4.5 L6,4.5 M2.5,11.5 L6,11.5 M6,4.5 L10,11.5 M6,11.5 L10,4.5 M10,4.5 L13.5,4.5 M10,11.5 L13.5,11.5", IconGeometry.MidiNoteMap);

    [Fact]
    public void MidiChokePath() => Assert.Equal("M1.5,6 L14.5,6 M4,6 C5,3.5 11,3.5 12,6 M8,6 L8,14 M10,9.5 L14,13.5 M14,9.5 L10,13.5", IconGeometry.MidiChoke);

    [Fact]
    public void MidiChokeGroupPath() => Assert.Equal("M1.5,5 L7,5 M9,5 L14.5,5 M4.25,5 L4.25,13 M11.75,5 L11.75,13 M4.25,9 L11.75,9", IconGeometry.MidiChokeGroup);

    [Fact]
    public void MidiAudioDrumPath() => Assert.Equal("M1.5,8 L3,8 L4,4 L5.5,12 L6.5,8 L8,8 M9,6.5 A2.75,1.2 0 1 1 14.5,6.5 A2.75,1.2 0 1 1 9,6.5 M9,6.5 L9,11.5 M14.5,6.5 L14.5,11.5 M9,11.5 C10,12.5 13.5,12.5 14.5,11.5", IconGeometry.MidiAudioDrum);

    [Fact]
    public void MidiArpPath() => Assert.Equal("M2,13 L4.5,13 M5.5,10 L8,10 M9,7 L11.5,7 M12.5,4 L15,4", IconGeometry.MidiArp);

    [Fact]
    public void MidiStepSeqPath() => Assert.Equal("M2,4 L14,4 L14,12 L2,12 Z M5,4 L5,12 M8,4 L8,12 M11,4 L11,12 M2.8,9 L4.2,9 M8.8,6.5 L10.2,6.5", IconGeometry.MidiStepSeq);

    [Fact]
    public void MidiLfoPath() => Assert.Equal("M1.5,8 C3.5,2 5.5,2 8,8 C10.5,14 12.5,14 14.5,8", IconGeometry.MidiLfo);

    [Fact]
    public void MidiNoteRandomPath() => Assert.Equal("M2.5,2.5 L13.5,2.5 L13.5,13.5 L2.5,13.5 Z M5.5,5.5 L5.6,5.6 M10.5,10.5 L10.6,10.6 M8,8 L8.1,8.1", IconGeometry.MidiNoteRandom);

    [Fact]
    public void MidiModalRandomPath() => Assert.Equal("M2,7.5 L8.5,7.5 L8.5,14 L2,14 Z M4.3,9.8 L4.4,9.9 M6.2,11.7 L6.3,11.8 M10,11 L12,11 L12,8 L14,8 M10,5.5 L12,5.5 L12,2.5 L14,2.5", IconGeometry.MidiModalRandom);

    [Fact]
    public void MidiScaleVariationPath() => Assert.Equal("M2,4.5 L5,4.5 L11,11.5 L14,11.5 M2,11.5 L5,11.5 L11,4.5 L14,4.5 M12,2.5 L14,4.5 L12,6.5 M12,9.5 L14,11.5 L12,13.5", IconGeometry.MidiScaleVariation);

    [Fact]
    public void MidiChannelMapPath() => Assert.Equal("M2,3.5 L14,3.5 M2,6.5 L9,6.5 M11,6.5 L14,6.5 M2,9.5 L14,9.5 M2,12.5 L6,12.5 M9,12.5 L14,12.5", IconGeometry.MidiChannelMap);

    [Fact]
    public void MidiChannelRoutePath() => Assert.Equal("M2,8 L7,8 L12,3.5 M7,8 L12,12.5 M10,3.5 L12,3.5 L12,5.5 M10,12.5 L12,12.5 L12,10.5", IconGeometry.MidiChannelRoute);

    [Fact]
    public void MidiProgramBankPath() => Assert.Equal("M2,4.5 L6,4.5 L7.5,6 L14,6 L14,13 L2,13 Z M5,9.5 L11,9.5", IconGeometry.MidiProgramBank);

    [Fact]
    public void MidiCcSenderPath() => Assert.Equal("M2,8 A4,4 0 1 1 10,8 A4,4 0 1 1 2,8 M6,8 L6,4.5 M11,8 L14.5,8 M12.5,6 L14.5,8 L12.5,10", IconGeometry.MidiCcSender);

    [Fact]
    public void MidiCcMapperPath() => Assert.Equal("M1.5,8 A2.75,2.75 0 1 1 7,8 A2.75,2.75 0 1 1 1.5,8 M9,8 A2.75,2.75 0 1 1 14.5,8 A2.75,2.75 0 1 1 9,8 M4.25,8 L4.25,5.25 M11.75,8 L13.7,6.05", IconGeometry.MidiCcMapper);

    [Fact]
    public void MidiAudioDuckerPath() => Assert.Equal("M1.5,8 L3,8 L4,4 L5.5,12 L7,6 L8,8 M11.5,3 L11.5,12 M9.5,10 L11.5,12 L13.5,10", IconGeometry.MidiAudioDucker);

    [Fact]
    public void MidiLoopSamplerPath() => Assert.Equal("M2.5,4 L13.5,4 L13.5,12 L2.5,12 Z M4,8 L5,8 L6,5.5 L7.5,10.5 L9,6.5 L10,8 L12,8", IconGeometry.MidiLoopSampler);

    [Fact]
    public void MidiLooperPath() => Assert.Equal("M13,8 A5,5 0 1 1 11.5,4.5 M11.5,1.5 L11.5,4.5 L8.5,4.5", IconGeometry.MidiLooper);

    [Fact]
    public void MidiSanitizerPath() => Assert.Equal("M2,3 L14,3 L9.5,8.5 L9.5,13 L6.5,13 L6.5,8.5 Z", IconGeometry.MidiSanitizer);

    [Fact]
    public void MidiPanicPath() => Assert.Equal("M5.5,2 L10.5,2 L14,5.5 L14,10.5 L10.5,14 L5.5,14 L2,10.5 L2,5.5 Z M8,5 L8,9 M8,11.2 L8,11.3", IconGeometry.MidiPanic);

    [Fact]
    public void MidiLogPath() => Assert.Equal("M3.5,2.5 L12.5,2.5 L12.5,13.5 L3.5,13.5 Z M5.5,5.5 L10.5,5.5 M5.5,8 L10.5,8 M5.5,10.5 L10.5,10.5", IconGeometry.MidiLog);
}
