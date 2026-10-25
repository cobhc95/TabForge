namespace TabForge.Plugins;

// Owns: the reasons a plug-in of a chain is left out of (or only partly in) an FX chain file for other hosts, and the one plain line for each.
// Does not own: finding the reason (RfxChainExporter for what the engine said, the caller for installed / approved), showing the line.
// Tests: TabForge.Avalonia.Tests.RfxExportGapTests.
public enum RfxExportGap { NotInstalled, NotTrusted, NoAnswer, StateUnreadable, Unsupported }

public static class RfxExportGaps
{
    /// <summary>"Name: reason", for the export summary.</summary>
    public static string Line(string name, RfxExportGap gap) => name + ": " + gap switch
    {
        RfxExportGap.NotInstalled => "not installed on this PC",
        RfxExportGap.NotTrusted => "needs your approval in Plug-in safety",
        RfxExportGap.NoAnswer => "didn't answer in time",
        RfxExportGap.StateUnreadable => "couldn't read its settings (default settings written)",
        _ => "can't be exported (the audio engine could not load it)",
    };
}
