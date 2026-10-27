using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the saved arrangement lane modes (MainWindowArrangementModes.Restore): a new arrangement timeline takes the modes kept in the
//   Timeline settings.
// Does not own: the lane drawing (TimelineView), the menu table check (MainWindowMenuTests).
// Tests: this file.
public class MainWindowArrangementModesTests
{
    [AvaloniaFact]
    public void SavedLaneModesAreRestoredOnANewTimeline()
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-arrangement-modes-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using var settings = MainWindowSettings.Open(path);
            settings.Settings.Timeline.ShowContinuousLine = true;
            var timeline = new TimelineView(new SongProject());
            MainWindowArrangementModes.Restore(timeline, new Window(), settings);
            Assert.True(timeline.ShowContinuousBlocks);
            Assert.False(timeline.ShowIndividualNotes);
        }
        finally { File.Delete(path); }
    }
}
