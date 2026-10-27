using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the score's system height in TAB-only notation: the standard staff's four-line block is not reserved above the TAB, so the TAB sits
//   closer to the system top than in the TAB-and-staff notation by at least that block, and the system is shorter.
// Does not own: the drawing (ScoreRenderer), the mark extents (ScoreLayoutEngine), the staff layout (StaffNotationLayoutBuilder).
// Tests: this file.
public class ScoreTabOnlyLayoutTests
{
    [AvaloniaFact]
    public void TabOnlyDropsTheStaffBlockAboveEachTabStaff()
    {
        var probe = new ScoreGeometryProbe(FullDemoSongFactory.Create(), new AvaloniaDrawContext());
        probe.Notation = NotationMode.TabAndStaff;
        var bothHeight = probe.ContentSize().Height;   // the measure builds the layout (its mark extents) before the TAB is placed
        var bothGap = probe.TabTop(0) - probe.StaffTop(0);

        probe.Notation = NotationMode.TabOnly;
        var tabOnlyHeight = probe.ContentSize().Height;
        var tabOnlyGap = probe.TabTop(0) - probe.StaffTop(0);

        Assert.True(tabOnlyGap <= bothGap - 4 * probe.StaffGap, $"tab only gap {tabOnlyGap:0.0} keeps the staff band (tab and staff gap {bothGap:0.0})");
        Assert.True(tabOnlyHeight < bothHeight, $"tab only {tabOnlyHeight:0.0} is not shorter than tab and staff {bothHeight:0.0}");
    }
}
