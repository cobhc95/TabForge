using TabForge.Documents;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: checking the Global tuning status line that the Tools command shows after a retune (GlobalRetune.StatusOf).
// Does not own: the retune and its undo step (GlobalRetune.Apply, DocumentEdits), the menu row (MainMenuTable, checked by MainWindowMenuTests).
// Tests: this file.
public class MainWindowGlobalTuningTests
{
    [Fact]
    public void GlobalTuningStatusNamesOriginalUniformAndCustomTunings()
    {
        Assert.Equal("Original tuning", GlobalRetune.StatusOf(new[] { 0, 0, 0, 0, 0, 0 }));
        Assert.Equal("All tracks retuned +2 semitones", GlobalRetune.StatusOf(new[] { 2, 2, 2, 2, 2, 2 }));
        Assert.Equal("All tracks retuned (custom per-string tuning)", GlobalRetune.StatusOf(new[] { 1, 0, 0, 0, 0, 0 }));
    }
}
