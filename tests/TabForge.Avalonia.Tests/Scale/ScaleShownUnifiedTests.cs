using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TabForge.Services;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views.TransportPopups;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: that every way of showing or hiding the scale on the fretboard (the panel switch, Clear highlight, the editing settings' one method) reads and writes one state.
// Does not own: the drawing, the settings file.
// Tests: this class.
public sealed class ScaleShownUnifiedTests
{
    [AvaloniaFact]
    public void TheSwitchTheClearButtonAndTheSettingsMethodShareOneState()
    {
        using var settings = MainWindowSettings.Open(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tf-shown-" + System.Guid.NewGuid().ToString("N") + ".json"));
        using var rig = new StatusBarTransportTests.Rig(song: SectionScaleDetectorTests.TwoSections(), settings: settings);
        var scale = rig.Status.Scale;
        var editing = settings.Settings.Editing;
        var window = new global::Avalonia.Controls.Window { Width = 1200, Height = 700, Content = new global::Avalonia.Controls.Border() };
        window.Show();
        Assert.True(scale.OpenForFretboard(window));
        Dispatcher.UIThread.RunJobs();
        var toggle = scale.Popup!.Frame.Rows.GetLogicalDescendants().OfType<LargeSwitch>().Single();
        try
        {
            Assert.False(toggle.IsChecked);
            toggle.IsChecked = true;
            Assert.True(editing.ScaleShown);
            editing.SetScaleShown(false);                  // the menu, hotkey and Preferences path
            Dispatcher.UIThread.RunJobs();
            Assert.False(toggle.IsChecked);
            editing.ShowScale("D Dorian");                 // a scale picked from the fretboard menu
            Dispatcher.UIThread.RunJobs();
            Assert.True(toggle.IsChecked);
            scale.ClearHighlight();                        // the Clear highlight button
            Dispatcher.UIThread.RunJobs();
            Assert.False(toggle.IsChecked);
            Assert.Null(editing.ScaleHighlight);
        }
        finally { window.Close(); }
    }
}
